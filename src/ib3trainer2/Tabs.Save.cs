// ============================================================================
// Tabs.Save.cs — 存档导出/导入（.ib3save）：把某个槽位打包带走，导入前自动完整性校验
//
// 为什么需要这一页：换档失败的原因几乎从来不是"游戏不读 Cloud\"，而是
// "换进去的那套自己不自洽"。见 E:\ib3_re\宝石研究\03_存档迁移.md。三件事必须同时成立：
//   ① 文件内容对；② LocalFileHeaderCache 里的 40 位 SHA1 与 contentLen 对；
//   ③ _SwordSaveSlotX_N.bin 里的 CloudDocIndex 等于它在本机文档列表里的下标。
// 本页把这三条（外加解密、端到端结构解析、本机交叉核对）全部做成导入前的 PASS/FAIL 表，
// 任一"必需"项不过就不让导入；另留一个"强制导入"（先弹确认，再照常备份）。
//
// ---- 字节级格式（2026-10-07 对活档实测；勿凭记忆改）----
//
// 加密：Cloud\ 下每个文件 = "yeK "(4 字节 ASCII) + AES-256-ECB(明文零填充到 16 字节倍数)
//   密钥 hex = 366e486d6a643a6862574e663d397c554f323a3f3b4b30792b675a4c2d6a5035
//   填充是【零字节】不是 PKCS7；解密 = 去掉前 4 字节 → 逐 16 字节块 ECB 解密
//   （用 RijndaelManaged(KeySize=256,BlockSize=128) 就是 AES——它在 mscorlib 里，
//     AesCryptoServiceProvider 在 System.Core，本项目没引用它，故不用）
//
// LocalFileHeaderCache（解密后体长 1280）：
//   i32 ver(=2), i32 0, i32 0, i32 count(=11)
//   每条： i32 hashLen(=41) ; 40 个 hex 字符 + NUL
//          i32 pathLen     ; UTF-8 路径 + NUL  如 "..\..\SwordGame\Cloud\_SwordSaveX_0-0.bin"
//          i32 nameLen     ; UTF-8 文档名 + NUL 如 "SwordSaveX_0-0.bin"
//          i32 contentLen  ; ★ 权威明文长度——哈希只覆盖解密体的前 contentLen 字节
//          i32 0
//   哈希规则（实测）：sha1hex == SHA1( 解密体[0:contentLen] )，小写
//   ⚠ 文件名是 "_" + 文档名（缓存里存的是不带下划线的文档名）
//
// _CurrentSlot：明文 = i32 槽号（体 16 字节，后 12 字节零；磁盘 20 字节）
//
// _SwordSaveX_N-0.bin / _SwordSaveSlotX_N.bin：UE3 tagged property
//   整档 = [i32 -1 哨兵] + 属性列表，列表以名为 "None" 的属性结束，之后是零填充
//   一条属性 = [FString 名][FString 类型][i32 size][i32 数组下标] + 类型附加字段 + 值
//     StructProperty  : 附加 [FString 结构体名]，其 size 只算【内部属性列表】的字节数
//     ArrayProperty   : 无附加；值 = [i32 元素数] + 元素，size = 4 + 元素字节数；
//                       元素可能是"None 结尾的属性列表"（结构体数组）或裸 FString / 裸 i32 列表
//     ByteProperty    : 附加 [FString 枚举名]，然后走 size 字节（size=0 时走 1 字节）
//     BoolProperty    : 只走 1 字节（值写在标签里）
//     Int/Float       : 4 字节；Str/Name : FString
//   ★ 全档走完后【解析终点恰好等于 contentLen】——本页把它当作结构完好性的硬判据。
//
// ---- .ib3save 容器（本页定义，单文件、自带描述、无压缩无外部依赖）----
//   "IB3SAVE1"(8 字节 ASCII) | i32 版本(=1) | i32 槽号 | i32 条目数
//   每条目： i32 名长, 名(UTF-8 无 NUL), i32 数据长, 数据(磁盘原样字节，仍带 "yeK " 前缀)
//   条目 = 该槽的 4 个文件 + 来源机的 LocalFileHeaderCache + _CurrentSlot
//   （带上缓存是为了让导入方拿到每个文件的权威 contentLen）
//
// 本文件只做文件 IO，绝不调用 WriteProcessMemory。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

// ============================================================================
// 加密层：yeK + AES-256-ECB（零填充）
// ============================================================================
static class Ib3Crypt {
  public const string Magic = "yeK ";
  static readonly byte[] Key = HexToBytes(
    "366e486d6a643a6862574e663d397c554f323a3f3b4b30792b675a4c2d6a5035");

  static byte[] HexToBytes(string s) {
    byte[] o = new byte[s.Length / 2];
    for (int i = 0; i < o.Length; i++) o[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
    return o;
  }

  static byte[] Ecb(byte[] src, int off, int len, bool encrypt) {
    using (RijndaelManaged r = new RijndaelManaged()) {
      r.KeySize = 256; r.BlockSize = 128;
      r.Mode = CipherMode.ECB; r.Padding = PaddingMode.None;
      r.Key = Key; r.IV = new byte[16];
      ICryptoTransform t = encrypt ? r.CreateEncryptor() : r.CreateDecryptor();
      return t.TransformFinalBlock(src, off, len);
    }
  }

  public static bool HasMagic(byte[] raw) {
    if (raw == null || raw.Length < 4) return false;
    for (int i = 0; i < 4; i++) if (raw[i] != (byte)Magic[i]) return false;
    return true;
  }

  // 解密：raw = "yeK " + 加密体 → 返回【不含 "yeK " 前缀】的明文（尾部可能带零填充）
  public static bool TryDecrypt(byte[] raw, out byte[] body, out string err) {
    body = null; err = null;
    if (raw == null) { err = I18n.T("空数据"); return false; }
    if (!HasMagic(raw)) { err = I18n.T("缺少 \"yeK \" 魔数（未加密 / 文件损坏 / 不是 Cloud\\ 里的文件）"); return false; }
    int n = raw.Length - 4;
    if (n <= 0) { err = I18n.T("只有魔数没有内容"); return false; }
    if (n % 16 != 0) { err = I18n.T("加密体 ") + n + I18n.T(" 字节不是 16 的倍数"); return false; }
    try { body = Ecb(raw, 4, n, false); }
    catch (Exception ex) { err = I18n.T("解密失败: ") + ex.Message; return false; }
    return true;
  }

  // 加密：明文零填充到 16 的倍数 → 前面加 "yeK "（去尾零靠调用方按 contentLen 处理）
  public static byte[] Encrypt(byte[] body) {
    int pad = (16 - (body.Length % 16)) % 16;
    byte[] buf = new byte[body.Length + pad];
    Buffer.BlockCopy(body, 0, buf, 0, body.Length);
    byte[] enc = Ecb(buf, 0, buf.Length, true);
    byte[] outp = new byte[4 + enc.Length];
    for (int i = 0; i < 4; i++) outp[i] = (byte)Magic[i];
    Buffer.BlockCopy(enc, 0, outp, 4, enc.Length);
    return outp;
  }

  public static string Sha1Hex(byte[] b, int len) {
    using (SHA1 s = new SHA1Managed()) {
      byte[] h = s.ComputeHash(b, 0, len);
      StringBuilder sb = new StringBuilder(40);
      for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
      return sb.ToString();
    }
  }
}

// ============================================================================
// LocalFileHeaderCache
// ============================================================================
class CacheEntry {
  public int Index;
  public string Hash = "";      // 40 位小写 hex
  public int HashOff;           // 解密体偏移：40 个 hex 字符的起点（后面紧跟 NUL）
  public string Path = "";      // ..\..\SwordGame\Cloud\_X
  public string DocName = "";   // 不带下划线的文档名，如 SwordSaveX_0-0.bin
  public int ContentLen;        // ★ 权威明文长度
  public int ContentLenOff;     // 解密体偏移：contentLen 这个 i32 的起点
  public string DiskName { get { return "_" + DocName; } }
}

static class Ib3Cache {
  public const int HeaderLen = 16;   // ver + f1 + f2 + count

  static bool Str(byte[] b, ref int o, int end, out string s) {
    s = "";
    if (o + 4 > end) return false;
    int n = BitConverter.ToInt32(b, o);
    if (n == 0) { o += 4; return true; }
    if (n < 0 || o + 4 + n > end) return false;
    s = Encoding.UTF8.GetString(b, o + 4, n - 1);
    o += 4 + n;
    return true;
  }

  public static bool TryParse(byte[] body, out List<CacheEntry> ents, out int end, out string err) {
    ents = new List<CacheEntry>(); end = 0; err = null;
    if (body == null || body.Length < HeaderLen) { err = I18n.T("缓存体太短"); return false; }
    int o = 0;
    int ver = BitConverter.ToInt32(body, o); o += 4;
    int f1 = BitConverter.ToInt32(body, o); o += 4;
    int f2 = BitConverter.ToInt32(body, o); o += 4;
    int cnt = BitConverter.ToInt32(body, o); o += 4;
    if (ver != 2) { err = I18n.T("版本号不是 2（=") + ver + I18n.T("）"); return false; }
    if (cnt < 1 || cnt > 256) { err = I18n.T("条目数不合理（=") + cnt + I18n.T("）"); return false; }
    for (int i = 0; i < cnt; i++) {
      if (o + 4 > body.Length) { err = I18n.T("条目 ") + i + I18n.T(" 被截断"); return false; }
      int hl = BitConverter.ToInt32(body, o);
      if (hl < 1 || o + 4 + hl > body.Length) { err = I18n.T("条目 ") + i + I18n.T(" 哈希长度非法（") + hl + I18n.T("）"); return false; }
      CacheEntry e = new CacheEntry();
      e.Index = i;
      e.HashOff = o + 4;
      e.Hash = Encoding.ASCII.GetString(body, o + 4, hl - 1);
      o += 4 + hl;
      string path;
      if (!Str(body, ref o, body.Length, out path)) { err = I18n.T("条目 ") + i + I18n.T(" 路径越界"); return false; }
      e.Path = path;
      string name;
      if (!Str(body, ref o, body.Length, out name)) { err = I18n.T("条目 ") + i + I18n.T(" 文档名越界"); return false; }
      e.DocName = name;
      if (o + 8 > body.Length) { err = I18n.T("条目 ") + i + I18n.T(" contentLen 越界"); return false; }
      e.ContentLenOff = o;
      e.ContentLen = BitConverter.ToInt32(body, o); o += 4;
      int z = BitConverter.ToInt32(body, o); o += 4;
      if (e.ContentLen < 0 || e.ContentLen > 64 * 1024 * 1024) { err = I18n.T("条目 ") + i + I18n.T(" contentLen 不合理（") + e.ContentLen + I18n.T("）"); return false; }
      if (e.Hash.Length != 40) { err = I18n.T("条目 ") + i + I18n.T(" 哈希不是 40 位"); return false; }
      if (z != 0) { err = I18n.T("条目 ") + i + I18n.T(" 末位非 0"); return false; }
      ents.Add(e);
    }
    end = o;
    return true;
  }

  public static int FindByName(List<CacheEntry> ents, string docName) {
    for (int i = 0; i < ents.Count; i++)
      if (string.Equals(ents[i].DocName, docName, StringComparison.OrdinalIgnoreCase)) return i;
    return -1;
  }

  // 原地改写：40 位 hex + contentLen 都是定长字段，体长与后续偏移全不变
  public static bool SetInPlace(byte[] body, CacheEntry e, string hash40, int contentLen) {
    if (body == null || e == null) return false;
    if (hash40.Length != 40) return false;
    if (e.HashOff < 0 || e.HashOff + 40 > body.Length) return false;
    if (e.ContentLenOff < 0 || e.ContentLenOff + 4 > body.Length) return false;
    for (int i = 0; i < 40; i++) body[e.HashOff + i] = (byte)hash40[i];
    byte[] cl = BitConverter.GetBytes(contentLen);
    Buffer.BlockCopy(cl, 0, body, e.ContentLenOff, 4);
    return true;
  }
}

// ============================================================================
// UE3 tagged property：结构走查 + 展平收集
// ============================================================================
class Prop {
  public string Path = "";
  public string Type = "";
  public int ArrayIndex;
  public long I;
  public string S = "";
  public bool HasI, HasS;
}

static class Ib3Props {
  const int MAXDEPTH = 48;

  static bool Str(byte[] b, ref int o, int end, out string s, out string err) {
    s = ""; err = null;
    if (o + 4 > end) { err = I18n.T("字符串头越界 @") + o; return false; }
    int n = BitConverter.ToInt32(b, o);
    if (n == 0) { o += 4; return true; }
    if (n > 0) {
      if (o + 4 + n > end) { err = I18n.T("字符串越界（长 ") + n + " @" + o + I18n.T("）"); return false; }
      s = Encoding.UTF8.GetString(b, o + 4, n - 1);
      o += 4 + n;
      return true;
    }
    int m = -n;
    if (m > 4096 || o + 4 + 2 * m > end) { err = I18n.T("宽字符串越界 @") + o; return false; }
    s = Encoding.Unicode.GetString(b, o + 4, 2 * m).TrimEnd('\0');
    o += 4 + 2 * m;
    return true;
  }

  // 走查一个属性列表（不收集值）；成功时 endOff = 结束（"None" 之后）的偏移
  public static bool Walk(byte[] b, int start, int end, int depth, out int endOff, out string err) {
    endOff = start; err = null;
    if (depth > MAXDEPTH) { err = I18n.T("嵌套过深"); return false; }
    int o = start;
    while (true) {
      if (o + 4 > end) { err = I18n.T("属性越界 @") + o; return false; }
      string name;
      if (!Str(b, ref o, end, out name, out err)) return false;
      if (name == "None" || name.Length == 0) { endOff = o; return true; }
      string typ;
      if (!Str(b, ref o, end, out typ, out err)) return false;
      if (o + 8 > end) { err = I18n.T("标签头越界 @ ") + name; return false; }
      int size = BitConverter.ToInt32(b, o);
      int aidx = BitConverter.ToInt32(b, o + 4);
      o += 8;
      if (size < 0) { err = name + I18n.T(" 的 size<0"); return false; }

      if (typ == "StructProperty") {
        string sn;
        if (!Str(b, ref o, end, out sn, out err)) return false;
        if (sn.Length == 0) { err = I18n.T("StructProperty 缺结构体名 @ ") + name; return false; }
        int sub;
        if (!Walk(b, o, o + size, depth + 1, out sub, out err)) return false;
        o = sub;
      } else if (typ == "ArrayProperty") {
        if (o + 4 > end) { err = I18n.T("数组元素数越界 @ ") + name; return false; }
        int cnt = BitConverter.ToInt32(b, o); o += 4;
        if (cnt < 0 || cnt > 2000000) { err = I18n.T("数组元素数不合理（") + cnt + I18n.T("）@ ") + name; return false; }
        int rem = size - 4;
        if (rem < 0) { err = I18n.T("数组 size<4 @ ") + name; return false; }
        int hit = -1;
        // ① 结构体元素：cnt 个以 "None" 结尾的属性列表
        {
          int q = o; bool ok = true;
          for (int i = 0; i < cnt && ok; i++) {
            int q2; string e2;
            if (!Walk(b, q, end, depth + 1, out q2, out e2)) ok = false; else q = q2;
          }
          if (ok && q - o == rem) hit = q;
        }
        // ② 裸 FString 元素（Name/Str 数组）
        if (hit < 0) {
          int q = o; bool ok = true;
          for (int i = 0; i < cnt && ok; i++) {
            string s2; string e2;
            if (!Str(b, ref q, end, out s2, out e2)) ok = false;
          }
          if (ok && q - o == rem) hit = q;
        }
        // ③ 裸 i32 / f32 元素
        if (hit < 0 && (long)cnt * 4 == rem && o + rem <= end) hit = o + rem;
        if (hit < 0) { err = I18n.T("数组 ") + name + I18n.T(" 元素无法解析（rem=") + rem + " cnt=" + cnt + I18n.T("）"); return false; }
        o = hit;
      } else if (typ == "ByteProperty") {
        string en;
        if (!Str(b, ref o, end, out en, out err)) return false;
        o += (size > 0 ? size : 1);
        if (o > end) { err = I18n.T("ByteProperty 越界 @ ") + name; return false; }
      } else if (typ == "BoolProperty") {
        o += 1;
        if (o > end) { err = I18n.T("BoolProperty 越界 @ ") + name; return false; }
      } else if (typ == "IntProperty" || typ == "FloatProperty") {
        o += 4;
        if (o > end) { err = name + I18n.T(" 的 ") + typ + I18n.T(" 越界"); return false; }
      } else if (typ == "StrProperty" || typ == "NameProperty") {
        string v;
        if (!Str(b, ref o, end, out v, out err)) return false;
      } else {
        err = I18n.T("未知属性类型 ") + typ + " @ " + name;
        return false;
      }
    }
  }

  // 整档结构走查：首属性名必须是 firstTag，且解析终点必须恰好等于 contentLen、其后全零
  public static bool TryValidate(byte[] body, int contentLen, string firstTag,
                                 out string head, out int endOff, out string err) {
    head = null; endOff = 0; err = null;
    if (body == null || body.Length < 8) { err = I18n.T("明文太短"); return false; }
    if (BitConverter.ToInt32(body, 0) != -1) { err = I18n.T("开头不是 -1 哨兵（不是 UE3 tagged property 档）"); return false; }
    int o = 4;
    if (!Str(body, ref o, body.Length, out head, out err)) return false;
    if (head != firstTag) { err = I18n.T("首属性是 \"") + head + I18n.T("\"，期望 \"") + firstTag + "\""; return false; }
    int eo;
    if (!Walk(body, 4, body.Length, 0, out eo, out err)) return false;
    endOff = eo;
    if (eo != contentLen) { err = I18n.T("结构解析结束于 ") + eo + I18n.T("，与缓存里的 contentLen ") + contentLen + I18n.T(" 不符"); return false; }
    for (int i = eo; i < body.Length; i++)
      if (body[i] != 0) { err = I18n.T("尾部零填充区 @") + i + I18n.T(" 非零"); return false; }
    return true;
  }

  // 展平收集（struct 递归展开成点分路径；array 只记元素数，元素内部不再展开）
  public static bool Collect(byte[] b, string prefix, int start, int end, int depth,
                             List<Prop> outp, out int endOff, out string err) {
    endOff = start; err = null;
    if (depth > MAXDEPTH) { err = I18n.T("嵌套过深"); return false; }
    int o = start;
    while (true) {
      if (o + 4 > end) { err = I18n.T("属性越界 @") + o; return false; }
      string name;
      if (!Str(b, ref o, end, out name, out err)) return false;
      if (name == "None" || name.Length == 0) { endOff = o; return true; }
      string typ;
      if (!Str(b, ref o, end, out typ, out err)) return false;
      if (o + 8 > end) { err = I18n.T("标签头越界 @ ") + name; return false; }
      int size = BitConverter.ToInt32(b, o);
      int aidx = BitConverter.ToInt32(b, o + 4);
      o += 8;
      if (size < 0) { err = name + I18n.T(" 的 size<0"); return false; }
      string p = (prefix.Length == 0) ? name : prefix + "." + name;

      if (typ == "StructProperty") {
        string sn;
        if (!Str(b, ref o, end, out sn, out err)) return false;
        if (sn.Length == 0) { err = I18n.T("StructProperty 缺结构体名 @ ") + p; return false; }
        Prop sp = new Prop();
        sp.Path = p; sp.Type = typ; sp.ArrayIndex = aidx; sp.I = aidx; sp.HasI = true;
        outp.Add(sp);
        int sub;
        if (!Collect(b, p, o, o + size, depth + 1, outp, out sub, out err)) return false;
        o = sub;
      } else if (typ == "ArrayProperty") {
        if (o + 4 > end) { err = I18n.T("数组元素数越界 @ ") + p; return false; }
        int cnt = BitConverter.ToInt32(b, o); o += 4;
        if (cnt < 0 || cnt > 2000000) { err = I18n.T("数组元素数不合理 @ ") + p; return false; }
        int rem = size - 4;
        if (rem < 0) { err = I18n.T("数组 size<4 @ ") + p; return false; }
        int hit = -1;
        {
          int q = o; bool ok = true;
          for (int i = 0; i < cnt && ok; i++) {
            int q2; string e2;
            if (!Walk(b, q, end, depth + 1, out q2, out e2)) ok = false; else q = q2;
          }
          if (ok && q - o == rem) hit = q;
        }
        if (hit < 0) {
          int q = o; bool ok = true;
          for (int i = 0; i < cnt && ok; i++) {
            string s2; string e2;
            if (!Str(b, ref q, end, out s2, out e2)) ok = false;
          }
          if (ok && q - o == rem) hit = q;
        }
        if (hit < 0 && (long)cnt * 4 == rem && o + rem <= end) hit = o + rem;
        if (hit < 0) { err = I18n.T("数组 ") + p + I18n.T(" 元素无法解析"); return false; }
        Prop ap = new Prop();
        ap.Path = p; ap.Type = typ; ap.ArrayIndex = aidx; ap.I = cnt; ap.HasI = true;
        outp.Add(ap);
        o = hit;
      } else if (typ == "ByteProperty") {
        string en;
        if (!Str(b, ref o, end, out en, out err)) return false;
        Prop bp = new Prop();
        bp.Path = p; bp.Type = typ; bp.ArrayIndex = aidx;
        if (size <= 1) { bp.I = (o < b.Length) ? b[o] : 0; bp.HasI = true; }
        else { bp.S = en; bp.HasS = true; }
        outp.Add(bp);
        o += (size > 0 ? size : 1);
        if (o > end) { err = I18n.T("ByteProperty 越界 @ ") + p; return false; }
      } else if (typ == "BoolProperty") {
        Prop bp = new Prop();
        bp.Path = p; bp.Type = typ; bp.ArrayIndex = aidx;
        bp.I = (o < b.Length) ? b[o] : 0; bp.HasI = true;
        outp.Add(bp);
        o += 1;
        if (o > end) { err = I18n.T("BoolProperty 越界 @ ") + p; return false; }
      } else if (typ == "IntProperty") {
        Prop ip = new Prop();
        ip.Path = p; ip.Type = typ; ip.ArrayIndex = aidx;
        ip.I = (o + 4 <= b.Length) ? BitConverter.ToInt32(b, o) : 0; ip.HasI = true;
        outp.Add(ip);
        o += 4;
      } else if (typ == "FloatProperty") {
        Prop fp = new Prop();
        fp.Path = p; fp.Type = typ; fp.ArrayIndex = aidx; fp.HasI = false;
        outp.Add(fp);
        o += 4;
      } else if (typ == "StrProperty" || typ == "NameProperty") {
        string v;
        if (!Str(b, ref o, end, out v, out err)) return false;
        Prop sp2 = new Prop();
        sp2.Path = p; sp2.Type = typ; sp2.ArrayIndex = aidx; sp2.S = v; sp2.HasS = true;
        outp.Add(sp2);
      } else {
        err = I18n.T("未知属性类型 ") + typ + " @ " + p;
        return false;
      }
    }
  }

  public static Prop Find(List<Prop> ps, string path) {
    for (int i = 0; i < ps.Count; i++) if (ps[i].Path == path) return ps[i];
    return null;
  }
  public static Prop Find(List<Prop> ps, string path, int arrayIndex) {
    for (int i = 0; i < ps.Count; i++)
      if (ps[i].Path == path && ps[i].ArrayIndex == arrayIndex) return ps[i];
    return null;
  }
  // 取第一个数值型同名属性
  public static long Num(List<Prop> ps, string path, long dflt) {
    for (int i = 0; i < ps.Count; i++)
      if (ps[i].Path == path && ps[i].HasI) return ps[i].I;
    return dflt;
  }
  public static string Txt(List<Prop> ps, string path) {
    for (int i = 0; i < ps.Count; i++)
      if (ps[i].Path == path && ps[i].HasS) return ps[i].S;
    return null;
  }
}

// ============================================================================
// .ib3save 容器
// ============================================================================
class BundleEntry {
  public string Name = "";
  public byte[] Data;
}

class Bundle {
  public int Version;
  public int Slot;
  public List<BundleEntry> Entries = new List<BundleEntry>();
  public BundleEntry Get(string name) {
    for (int i = 0; i < Entries.Count; i++)
      if (string.Equals(Entries[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Entries[i];
    return null;
  }
}

static class Ib3Bundle {
  public const int FormatVersion = 1;
  static readonly byte[] Magic = Encoding.ASCII.GetBytes("IB3SAVE1");
  const int MAXENT = 64;
  const int MAXLEN = 64 * 1024 * 1024;

  public static bool Write(string path, int slot, List<BundleEntry> ents, out string err) {
    err = null;
    try {
      using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
        fs.Write(Magic, 0, Magic.Length);
        fs.Write(BitConverter.GetBytes(FormatVersion), 0, 4);
        fs.Write(BitConverter.GetBytes(slot), 0, 4);
        fs.Write(BitConverter.GetBytes(ents.Count), 0, 4);
        for (int i = 0; i < ents.Count; i++) {
          byte[] nb = Encoding.UTF8.GetBytes(ents[i].Name);
          byte[] db = ents[i].Data;
          fs.Write(BitConverter.GetBytes(nb.Length), 0, 4);
          fs.Write(nb, 0, nb.Length);
          fs.Write(BitConverter.GetBytes(db.Length), 0, 4);
          fs.Write(db, 0, db.Length);
        }
      }
      return true;
    } catch (Exception ex) { err = ex.Message; return false; }
  }

  public static bool Read(string path, out Bundle bd, out string err) {
    bd = null; err = null;
    byte[] z;
    try { z = File.ReadAllBytes(path); }
    catch (Exception ex) { err = I18n.T("读取失败: ") + ex.Message; return false; }
    if (z.Length < 20) { err = I18n.T("文件太小（") + z.Length + I18n.T(" 字节）"); return false; }
    for (int i = 0; i < 8; i++) if (z[i] != Magic[i]) { err = I18n.T("魔数不是 IB3SAVE1（不是本工具导出的存档包）"); return false; }
    int o = 8;
    int ver = BitConverter.ToInt32(z, o); o += 4;
    int slot = BitConverter.ToInt32(z, o); o += 4;
    int cnt = BitConverter.ToInt32(z, o); o += 4;
    if (ver != FormatVersion) { err = I18n.T("容器版本 ") + ver + I18n.T(" 不支持（本工具只认 ") + FormatVersion + I18n.T("）"); return false; }
    if (slot < 0 || slot > 2) { err = I18n.T("槽号 ") + slot + I18n.T(" 不合理"); return false; }
    if (cnt < 1 || cnt > MAXENT) { err = I18n.T("条目数 ") + cnt + I18n.T(" 不合理"); return false; }
    Bundle b = new Bundle();
    b.Version = ver; b.Slot = slot;
    for (int i = 0; i < cnt; i++) {
      if (o + 4 > z.Length) { err = I18n.T("条目 ") + i + I18n.T(" 名长被截断"); return false; }
      int nl = BitConverter.ToInt32(z, o); o += 4;
      if (nl <= 0 || nl > 260 || o + nl > z.Length) { err = I18n.T("条目 ") + i + I18n.T(" 名长非法（") + nl + I18n.T("）"); return false; }
      string nm = Encoding.UTF8.GetString(z, o, nl); o += nl;
      if (o + 4 > z.Length) { err = I18n.T("条目 ") + i + I18n.T(" 数据长被截断"); return false; }
      int dl = BitConverter.ToInt32(z, o); o += 4;
      if (dl <= 0 || dl > MAXLEN || o + dl > z.Length) { err = I18n.T("条目 ") + i + I18n.T("（") + nm + I18n.T("）数据长非法或被截断（") + dl + I18n.T("）"); return false; }
      BundleEntry e = new BundleEntry();
      e.Name = nm;
      e.Data = new byte[dl];
      Buffer.BlockCopy(z, o, e.Data, 0, dl);
      o += dl;
      // 同名重复 = 包被人拼过，拒绝
      if (b.Get(nm) != null) { err = I18n.T("条目名重复：") + nm; return false; }
      b.Entries.Add(e);
    }
    if (o != z.Length) { err = I18n.T("包尾部多出 ") + (z.Length - o) + I18n.T(" 字节（文件被追加/损坏）"); return false; }
    bd = b;
    return true;
  }
}

// ============================================================================
// 校验结果行
// ============================================================================
class CheckRow {
  // ★ 显示字段改成属性：set 时过一次 I18n.T()（键 = 中文原文；组合出来的整句查不到就原样保留）。
  //   全项目没有任何地方拿 Item/Result/Detail 做比较（只有 ShowChecks 显示与 Log），改属性安全。
  string _item = "", _result = "", _detail = "";
  public string Item { get { return _item; } set { _item = I18n.T(value); } }
  public string Result { get { return _result; } set { _result = I18n.T(value); } }
  public string Detail { get { return _detail; } set { _detail = I18n.T(value); } }
  public bool Mandatory;
  public bool Pass;
  public bool Warn;
}

partial class MainForm {

  // ---------------- 状态 ----------------
  ComboBox cboSaveSlot;
  ListView lvSaveChecks;
  Label lblSlotInfo, lblExportInfo, lblBundleInfo, lblSaveInfo, lblBackup;
  Button btnVerifyLocal, btnExport;
  Button btnPickBundle, btnRecheck, btnApply, btnForceImport, btnBackupDir;
  CheckBox chkSetSlot;   // 导入后是否把 _CurrentSlot（默认启动槽）切到该槽

  string backupRoot = "";
  string lastBundlePath = null;
  bool importReady = false;

  const string CACHE_FILE = "LocalFileHeaderCache";
  const string SLOT_CUR = "_CurrentSlot";
  static readonly string[] SLOT_FMT = { "_SwordSaveX_{0}-0.bin", "_BackupX_{0}-0.bin",
                                        "_SwordSaveSlotX_{0}.bin", "_BackupSlotX_{0}.bin" };

  // ================= 页签 =================
  TabPage BuildTabSave() {
    TabPage p = new TabPage("存档导出/导入");
    p.BackColor = Theme.BG; p.ForeColor = Theme.Text;

    // ---- 槽位行 ----
    p.Controls.Add(Theme.MkLabelInk("槽位", 14, 13, 40));
    cboSaveSlot = new ComboBox(); cboSaveSlot.SetBounds(56, 10, 200, 23);
    Theme.StyleCombo(cboSaveSlot);
    cboSaveSlot.SelectedIndexChanged += delegate { OnSlotChanged(); };
    p.Controls.Add(cboSaveSlot);
    p.Controls.Add(Theme.MkButton("扫描本地存档", 264, 8, 110, 26, delegate { ScanLocal(true); }));

    lblSlotInfo = Theme.MkHint("", 382, 6, 430);
    lblSlotInfo.Height = 32;
    lblSlotInfo.ForeColor = Theme.Ink;
    p.Controls.Add(lblSlotInfo);

    // ---- ① 导出 ----
    FlatGroupBox g1 = new FlatGroupBox();
    g1.Title = "① 导出：把某个槽位打包成一个 .ib3save（先校验本地缓存是否自洽）";
    g1.SetBounds(8, 40, 800, 86);
    g1.Fill = Theme.CardGold;
    btnVerifyLocal = Theme.MkButton("校验本地缓存", 16, 26, 128, 26, delegate { VerifyLocal(); });
    btnExport = Theme.MkButton("导出为 .ib3save", 152, 26, 148, 26, delegate { ExportSlot(); });
    g1.Controls.Add(btnVerifyLocal);
    g1.Controls.Add(btnExport);
    lblExportInfo = Theme.MkHint("导出会带上该槽的 4 个文件 + 本机的 LocalFileHeaderCache + _CurrentSlot，" +
                                 "导入方靠缓存里的 contentLen 做校验。", 308, 28, 478);
    lblExportInfo.Height = 46;
    lblExportInfo.ForeColor = Theme.Text;
    g1.Controls.Add(lblExportInfo);
    p.Controls.Add(g1);

    // ---- 校验表 ----
    lvSaveChecks = new DarkListView();
    Theme.ApplyDarkScroll(lvSaveChecks);
    lvSaveChecks.SetBounds(14, 132, 790, 166);
    Theme.StyleList(lvSaveChecks);
    lvSaveChecks.Columns.Add("检查项", 196);
    lvSaveChecks.Columns.Add("结果", 56);
    lvSaveChecks.Columns.Add("说明", 536);   // 说明较长，完整内容同时会写进下方日志（可滚动）
    p.Controls.Add(lvSaveChecks);
    lvSaveChecks.HandleCreated += delegate {
      SendMessage(lvSaveChecks.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)0, (IntPtr)LVS_EX_DOUBLEBUFFER);
    };

    // ---- ② 导入 ----
    FlatGroupBox g2 = new FlatGroupBox();
    g2.Title = "② 导入：选 .ib3save → 自动完整性校验 → 应用（会先把本机 Cloud\\ 整目录备份）";
    g2.SetBounds(8, 304, 800, 130);
    btnPickBundle = Theme.MkButton("选择存档文件…", 16, 26, 132, 26, delegate { PickBundle(); });
    btnRecheck = Theme.MkButton("重新校验", 156, 26, 96, 26, delegate { RecheckBundle(); });
    btnApply = Theme.MkButton("应用导入", 260, 26, 104, 26, delegate { ApplyImport(false); });
    btnForceImport = Theme.MkButton("强制导入", 372, 26, 104, 26, delegate { ApplyImport(true); });
    g2.Controls.Add(btnPickBundle);
    g2.Controls.Add(btnRecheck);
    g2.Controls.Add(btnApply);
    g2.Controls.Add(btnForceImport);
    lblBundleInfo = Theme.MkHint("尚未选择存档包。", 484, 28, 302);
    lblBundleInfo.Height = 48;
    lblBundleInfo.ForeColor = Theme.Text;
    g2.Controls.Add(lblBundleInfo);

    btnBackupDir = Theme.MkButton("备份位置…", 16, 62, 104, 24, delegate { PickBackupDir(); });
    g2.Controls.Add(btnBackupDir);
    // 导入是否顺带把「默认启动槽」切过去。迁移场景通常要（否则进游戏还是旧角色），
    // 但拿自己机器做往返测试时会把启动槽改掉 —— 所以做成显式勾选。
    chkSetSlot = new CheckBox();
    // ★ 用 Register 而不是"只 T() 一次"：这是直接 new 的 CheckBox，不走 Theme.Mk* 的内部登记。
    //   只 T() 一次的话，**运行期切换语言它不会跟着变**（用户实测报的就是这一条）。
    I18n.Register(chkSetSlot, delegate(string s) { chkSetSlot.Text = s; }, "导入后把默认启动槽切到该槽");
    chkSetSlot.SetBounds(126, 62, 240, 24);
    chkSetSlot.Checked = true;
    chkSetSlot.ForeColor = Theme.Text;
    chkSetSlot.BackColor = Color.Transparent;
    g2.Controls.Add(chkSetSlot);

    lblBackup = Theme.MkHint("", 374, 62, 412);
    lblBackup.Height = 24;
    lblBackup.ForeColor = Theme.Text;
    g2.Controls.Add(lblBackup);
    lblSaveInfo = Theme.MkHint("", 16, 92, 770);
    lblSaveInfo.Height = 34;
    lblSaveInfo.ForeColor = Theme.Text;
    g2.Controls.Add(lblSaveInfo);
    p.Controls.Add(g2);

    backupRoot = DefaultBackupRoot();
    SetImportControls(false, false);
    RefreshSlotCombo();
    ShowBackupRoot();
    return p;
  }

  // ================= 路径 =================
  // 真档目录：<文档>\My Games\Infinity Blade III\SwordGame\Cloud
  // 2026-10-10 起委托给 GameConfig（那套路径拼法现在两个功能共用，别再各写一份）。
  string CloudDir() { return GameConfig.CloudDir(); }

  static string DefaultBackupRoot() {
    try { if (Directory.Exists(@"E:\ib3_re\_tmp")) return @"E:\ib3_re\_tmp\"; } catch { }
    try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloud_backup"); } catch { }
    return "cloud_backup";
  }

  void ShowBackupRoot() {
    if (lblBackup != null) {
      lblBackup.Text = I18n.T("导入前整目录备份到：") + backupRoot;
      // 拼接文案（含路径变量）没法整串进字典 ⇒ 登记一个重画函数，切换语言时按新语言重拼。
      // key 固定 ⇒ 反复跑到这里也只留一条，不会累积。
      I18n.OnLang("save.backupdir", delegate { lblBackup.Text = I18n.T("导入前整目录备份到：") + backupRoot; });
    }
  }

  // ================= 本地槽位扫描（只做存在性，不算哈希） =================
  void RefreshSlotCombo() {
    if (cboSaveSlot == null) return;
    int keep = cboSaveSlot.SelectedIndex;
    cboSaveSlot.Items.Clear();
    string cloud = CloudDir();
    bool hasCloud = false;
    try { hasCloud = Directory.Exists(cloud); } catch { }
    for (int s = 0; s <= 2; s++) {
      int n = 0;
      if (hasCloud) {
        for (int k = 0; k < SLOT_FMT.Length; k++) {
          try { if (File.Exists(Path.Combine(cloud, string.Format(SLOT_FMT[k], s)))) n++; }
          catch { }
        }
      }
      // ★ 存**中文原文**，不要在这里翻：下拉项是 StyleCombo 自绘的，显示时才过 T()
      //   （Theme.cs 的 DrawItem）。这里若翻成英文存进去，切回中文时 T() 命不中 → 漏成英文。
      cboSaveSlot.Items.Add("槽 " + s + "（磁盘 " + n + "/4 文件）");
    }
    cboSaveSlot.SelectedIndex = (keep >= 0 && keep <= 2) ? keep : 0;
  }

  void OnSlotChanged() {
    if (lblSlotInfo == null) return;
    string cloud = CloudDir();
    string s = "";
    try {
      if (!Directory.Exists(cloud)) {
        s = I18n.T("找不到真档目录：") + cloud + I18n.T("（先确认游戏装过/进过一次存档）");
      } else {
        int slot = cboSaveSlot.SelectedIndex;
        if (slot < 0) slot = 0;
        s = I18n.T("目录 ") + cloud;
        for (int k = 0; k < SLOT_FMT.Length; k++) {
          string f = string.Format(SLOT_FMT[k], slot);
          long sz = -1;
          try { FileInfo fi = new FileInfo(Path.Combine(cloud, f)); if (fi.Exists) sz = fi.Length; } catch { }
          s += "   " + (sz < 0 ? I18n.T("缺 ") + f : f + "=" + sz + "B");
        }
      }
    } catch (Exception ex) { s = I18n.T("扫描失败: ") + ex.Message; }
    lblSlotInfo.Text = s;
  }

  void ScanLocal(bool toast) {
    RefreshSlotCombo();
    OnSlotChanged();
    if (toast) ToastMgr.Show("已重新扫描本地存档槽位");
  }

  void PickBackupDir() {
    FolderBrowserDialog fb = new FolderBrowserDialog();
    fb.Description = I18n.T("选择【导入前整目录备份】放到哪个文件夹（每次导入会在其下新建 cloud_backup_时间戳 子目录）");
    fb.ShowNewFolderButton = true;
    try { if (Directory.Exists(backupRoot)) fb.SelectedPath = backupRoot; } catch { }
    if (fb.ShowDialog(this) != DialogResult.OK) return;
    backupRoot = fb.SelectedPath;
    ShowBackupRoot();
    Log("备份位置已设为: " + backupRoot);
  }

  // ================= ① 本地缓存自洽性校验 =================
  void VerifyLocal() {
    string cloud = CloudDir();
    BusyShow("校验本地缓存");
    try {
      string msg;
      List<CheckRow> rows = CheckLocalCache(cloud, out msg);
      ShowChecks(rows);
      lblExportInfo.Text = msg;
      Log("本地缓存校验: " + msg.Replace("\r\n", " "));
      ToastMgr.Show(msg.Split('\n')[0]);
    } finally { BusyHide(); }
  }

  // 逐条比对 SHA1(明文[0:contentLen]) 与缓存里记的 hex。返回每个条目一行。
  List<CheckRow> CheckLocalCache(string cloud, out string summary) {
    List<CheckRow> rows = new List<CheckRow>();
    summary = "";
    byte[] craw;
    try { craw = File.ReadAllBytes(Path.Combine(cloud, CACHE_FILE)); }
    catch (Exception ex) { summary = I18n.T("读不到 LocalFileHeaderCache: ") + ex.Message; return rows; }

    byte[] cbody;
    string err;
    if (!Ib3Crypt.TryDecrypt(craw, out cbody, out err)) {
      summary = I18n.T("缓存解密失败: ") + err;
      return rows;
    }
    List<CacheEntry> ents;
    int cend;
    if (!Ib3Cache.TryParse(cbody, out ents, out cend, out err)) {
      summary = I18n.T("缓存解析失败: ") + err;
      return rows;
    }
    int bad = 0, ok = 0;
    for (int i = 0; i < ents.Count; i++) {
      CacheEntry e = ents[i];
      CheckRow r = new CheckRow();
      r.Item = "[" + i + "] " + e.DocName;
      r.Mandatory = true;
      string disk = Path.Combine(cloud, e.DiskName);
      byte[] raw;
      try { raw = File.ReadAllBytes(disk); }
      catch (Exception ex) {
        r.Pass = false; r.Result = "失败"; r.Detail = I18n.T("读不到 ") + e.DiskName + I18n.T("：") + ex.Message;
        bad++; rows.Add(r); continue;
      }
      byte[] body;
      if (!Ib3Crypt.TryDecrypt(raw, out body, out err)) {
        r.Pass = false; r.Result = "失败"; r.Detail = I18n.T("解密失败：") + err;
        bad++; rows.Add(r); continue;
      }
      if (e.ContentLen > body.Length) {
        r.Pass = false; r.Result = "失败";
        r.Detail = "contentLen " + e.ContentLen + I18n.T(" > 明文 ") + body.Length;
        bad++; rows.Add(r); continue;
      }
      string h = Ib3Crypt.Sha1Hex(body, e.ContentLen);
      r.Pass = (h == e.Hash);
      r.Result = r.Pass ? "通过" : "失败";
      r.Detail = I18n.T("磁盘 ") + raw.Length + I18n.T("B / 明文 ") + body.Length + "B / contentLen " + e.ContentLen +
                 (r.Pass ? I18n.T(" / SHA1 一致") : I18n.T(" / SHA1 不符（缓存 ") + e.Hash.Substring(0, 12) + I18n.T("… 实际 ") + h.Substring(0, 12) + I18n.T("…）"));
      if (r.Pass) ok++; else bad++;
      rows.Add(r);
    }
    summary = bad == 0
      ? I18n.T("本地缓存自洽：") + ok + " / " + ents.Count + I18n.T(" 条全部通过，可以放心导出。")
      : I18n.T("本地缓存有 ") + bad + I18n.T(" 条不一致（见下表）——先修好来源再导出，否则导出的包本身就有问题。");
    return rows;
  }

  // ================= ① 导出 =================
  void ExportSlot() {
    int slot = cboSaveSlot.SelectedIndex;
    if (slot < 0) slot = 0;
    string cloud = CloudDir();
    if (!Directory.Exists(cloud)) { ToastMgr.Warn(I18n.T("找不到真档目录：") + cloud); return; }

    BusyShow(I18n.T("校验并打包 槽") + slot);
    List<BundleEntry> ents = new List<BundleEntry>();
    StringBuilder log = new StringBuilder();
    try {
      // 1) 本地缓存必须自洽（整份都查：破掉的来源不该被打包带走）
      string csum;
      List<CheckRow> rows = CheckLocalCache(cloud, out csum);
      ShowChecks(rows);
      bool anyFail = false;
      for (int i = 0; i < rows.Count; i++) if (!rows[i].Pass) anyFail = true;
      if (anyFail) {
        lblExportInfo.Text = csum;
        ToastMgr.Warn("本地缓存不自洽，已拒绝导出（见下表）");
        Log("导出被拒绝：本地缓存不自洽。" + csum);
        return;
      }

      // 2) 取 4 个槽文件 + 缓存 + _CurrentSlot
      string[] names = new string[] {
        string.Format(SLOT_FMT[0], slot), string.Format(SLOT_FMT[1], slot),
        string.Format(SLOT_FMT[2], slot), string.Format(SLOT_FMT[3], slot),
        CACHE_FILE, SLOT_CUR };
      for (int i = 0; i < names.Length; i++) {
        string f = Path.Combine(cloud, names[i]);
        if (!File.Exists(f)) { ToastMgr.Warn(I18n.T("缺少文件：") + names[i]); Log("导出失败：缺 " + names[i]); return; }
        BundleEntry e = new BundleEntry();
        e.Name = names[i];
        e.Data = File.ReadAllBytes(f);
        ents.Add(e);
        log.Append(names[i]).Append("=").Append(e.Data.Length).Append("B  ");
      }

      // 3) 选路径并写盘（对话框必须在 UI 线程）
      BusyHide();
      SaveFileDialog sfd = new SaveFileDialog();
      sfd.Filter = I18n.T("IB3 存档包") + " (*.ib3save)|*.ib3save|" + I18n.T("所有文件") + " (*.*)|*.*";
      sfd.DefaultExt = "ib3save";
      sfd.FileName = "IB3存档_槽" + slot + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".ib3save";
      try { sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { }
      if (sfd.ShowDialog(this) != DialogResult.OK) { Log("导出已取消"); return; }
      BusyShow("写出存档包");

      string err;
      if (!Ib3Bundle.Write(sfd.FileName, slot, ents, out err)) {
        ToastMgr.Warn(I18n.T("写盘失败：") + err);
        Log("导出失败：" + err);
        return;
      }
      long sz = 0;
      try { sz = new FileInfo(sfd.FileName).Length; } catch { }
      string msg = I18n.T("已导出槽 ") + slot + " → " + Path.GetFileName(sfd.FileName) + I18n.T("（") + sz + I18n.T(" 字节，6 个条目）");
      lblExportInfo.Text = msg + "　" + log.ToString();
      ToastMgr.Show(I18n.T("导出成功：") + Path.GetFileName(sfd.FileName));
      Log(msg);
      Log("  条目: " + log.ToString().TrimEnd());
      Log("  本地缓存 11 条全部自洽（SHA1 == 缓存记录）");
    } catch (Exception ex) {
      ToastMgr.Warn(I18n.T("导出异常：") + ex.Message);
      Log("导出异常: " + ex);
    } finally { BusyHide(); }
  }

  // ================= ② 导入：选择 + 自动校验 =================
  void PickBundle() {
    OpenFileDialog ofd = new OpenFileDialog();
    ofd.Filter = I18n.T("IB3 存档包") + " (*.ib3save)|*.ib3save|" + I18n.T("所有文件") + " (*.*)|*.*";
    ofd.CheckFileExists = true;
    if (ofd.ShowDialog(this) != DialogResult.OK) return;
    VerifyBundle(ofd.FileName);
  }

  void RecheckBundle() {
    if (lastBundlePath == null || !File.Exists(lastBundlePath)) {
      ToastMgr.Warn("还没有选择存档包");
      return;
    }
    VerifyBundle(lastBundlePath);
  }

  void VerifyBundle(string path) {
    RunBackground("校验存档包", delegate {
      List<CheckRow> rows;
      bool ready;
      string summary;
      DoVerify(path, out rows, out ready, out summary);
      int nFail = 0;
      for (int i = 0; i < rows.Count; i++) if (rows[i].Mandatory && !rows[i].Pass) nFail++;
      BeginInvoke((MethodInvoker)delegate {
        lastBundlePath = path;
        ShowChecks(rows);
        // 表格列宽有限，完整说明同时落到日志区（可滚动），便于逐项核对
        for (int i = 0; i < rows.Count; i++) {
          CheckRow r = rows[i];
          string mark = r.Pass ? "✓" : (r.Warn ? "⚠" : "✗");
          Log("校验 " + mark + " " + r.Item + " — " + r.Detail);
        }
        lblSaveInfo.Text = summary;
        importReady = ready;
        SetImportControls(ready, !ready);
        lblBundleInfo.Text = Path.GetFileName(path) + "\r\n" +
          (ready ? I18n.T("✅ 必需检查全部通过，可以应用导入。")
                 : I18n.T("❌ 有 ") + nFail + I18n.T(" 项必需检查未通过——请勿直接导入。"));
      });
      return "存档包校验: " + Path.GetFileName(path) + " → " +
             (ready ? "通过（必需项全过）" : "未通过，必需项失败 " + nFail + " 条");
    });
  }

  void SetImportControls(bool canApply, bool canForce) {
    if (btnApply != null) btnApply.Enabled = canApply;
    if (btnForceImport != null) btnForceImport.Enabled = canForce;
  }

  // ---- 真正的校验清单（1..10） ----
  void DoVerify(string path, out List<CheckRow> rows, out bool ready, out string summary) {
    rows = new List<CheckRow>();
    ready = false;
    summary = "";

    // ① 容器
    Bundle bd;
    string err;
    CheckRow r1 = new CheckRow();
    r1.Item = "1. 容器结构（magic / 版本 / 条目数 / 无截断）";
    r1.Mandatory = true;
    if (!Ib3Bundle.Read(path, out bd, out err)) {
      r1.Pass = false; r1.Result = "失败"; r1.Detail = err;
      rows.Add(r1);
      summary = I18n.T("存档包读不出来，后面的检查无法进行。");
      return;
    }
    r1.Pass = true; r1.Result = "通过";
    r1.Detail = "IB3SAVE1 v" + bd.Version + I18n.T("，槽 ") + bd.Slot + I18n.T("，") + bd.Entries.Count + I18n.T(" 个条目，") +
                I18n.T("文件 ") + new FileInfo(path).Length + I18n.T(" 字节");
    rows.Add(r1);

    int slot = bd.Slot;
    string[] slotNames = new string[] {
      string.Format(SLOT_FMT[0], slot), string.Format(SLOT_FMT[1], slot),
      string.Format(SLOT_FMT[2], slot), string.Format(SLOT_FMT[3], slot) };

    // ② 四个槽文件齐全
    CheckRow r2 = new CheckRow();
    r2.Item = "2. 该槽 4 个文件齐全";
    r2.Mandatory = true;
    int have = 0;
    StringBuilder miss = new StringBuilder();
    for (int i = 0; i < 4; i++) {
      if (bd.Get(slotNames[i]) != null) have++;
      else miss.Append(slotNames[i]).Append(" ");
    }
    r2.Pass = (have == 4);
    r2.Result = r2.Pass ? "通过" : "失败";
    r2.Detail = r2.Pass ? ("主档 / 备份主档 / 槽元数据 / 备份槽元数据 4/4 都在") : (I18n.T("缺少：") + miss.ToString());
    rows.Add(r2);

    // ③ 每条能解密（"yeK " + 16 的倍数）
    CheckRow r3 = new CheckRow();
    r3.Item = "3. 每个条目都能解密";
    r3.Mandatory = true;
    int decOk = 0;
    StringBuilder decBad = new StringBuilder();
    for (int i = 0; i < bd.Entries.Count; i++) {
      byte[] body;
      string e2;
      if (Ib3Crypt.TryDecrypt(bd.Entries[i].Data, out body, out e2)) decOk++;
      else decBad.Append(bd.Entries[i].Name).Append("(").Append(e2).Append(") ");
    }
    r3.Pass = (decOk == bd.Entries.Count);
    r3.Result = r3.Pass ? "通过" : "失败";
    r3.Detail = r3.Pass
      ? ("6 个条目全部带 \"yeK \" 魔数且体长为 16 的倍数")
      : (I18n.T("解密失败：") + decBad.ToString());
    rows.Add(r3);

    // ④ 包内缓存可解析，且四个文件都有条目
    CheckRow r4 = new CheckRow();
    r4.Item = "4. 包内 LocalFileHeaderCache 可解析且含该槽 4 条";
    r4.Mandatory = true;
    BundleEntry ce = bd.Get(CACHE_FILE);
    byte[] cbody = null;
    List<CacheEntry> cents = null;
    if (ce == null) {
      r4.Pass = false; r4.Result = "失败"; r4.Detail = "包里没有 LocalFileHeaderCache（导入方拿不到权威 contentLen）";
      rows.Add(r4);
    } else if (!Ib3Crypt.TryDecrypt(ce.Data, out cbody, out err)) {
      r4.Pass = false; r4.Result = "失败"; r4.Detail = I18n.T("缓存解密失败：") + err;
      rows.Add(r4);
    } else {
      int cend;
      if (!Ib3Cache.TryParse(cbody, out cents, out cend, out err)) {
        r4.Pass = false; r4.Result = "失败"; r4.Detail = I18n.T("缓存解析失败：") + err;
        rows.Add(r4);
      } else {
        int found = 0;
        StringBuilder m2 = new StringBuilder();
        for (int i = 0; i < 4; i++) {
          string doc = slotNames[i].Substring(1);   // 去掉前导下划线 = 文档名
          if (Ib3Cache.FindByName(cents, doc) >= 0) found++;
          else m2.Append(doc).Append(" ");
        }
        bool curOk = (Ib3Cache.FindByName(cents, "CurrentSlot") >= 0);
        r4.Pass = (found == 4 && curOk);
        r4.Result = r4.Pass ? "通过" : "失败";
        r4.Detail = r4.Pass
          ? (I18n.T("来源机文档列表 ") + cents.Count + I18n.T(" 条，该槽 4 条 + CurrentSlot 都能对上"))
          : (I18n.T("缓存里缺：") + m2.ToString() + (curOk ? "" : "CurrentSlot "));
        rows.Add(r4);
      }
    }

    // ⑤ 哈希匹配（真正的完整性证明）
    CheckRow r5 = new CheckRow();
    r5.Item = "5. 哈希匹配 SHA1(明文[0:contentLen]) == 包内缓存记录";
    r5.Mandatory = true;
    if (cents == null) {
      r5.Pass = false; r5.Result = "失败"; r5.Detail = "依赖第 4 项：包内缓存不可用";
      rows.Add(r5);
    } else {
      int okc = 0;
      StringBuilder bad = new StringBuilder();
      string[] checkNames = new string[] { slotNames[0], slotNames[1], slotNames[2], slotNames[3], SLOT_CUR };
      for (int i = 0; i < checkNames.Length; i++) {
        BundleEntry be = bd.Get(checkNames[i]);
        if (be == null) { bad.Append(checkNames[i]).Append(I18n.T("(缺失) ")); continue; }
        string doc = checkNames[i].Length > 0 && checkNames[i][0] == '_' ? checkNames[i].Substring(1) : checkNames[i];
        int idx = Ib3Cache.FindByName(cents, doc);
        if (idx < 0) { bad.Append(doc).Append(I18n.T("(缓存无记录) ")); continue; }
        byte[] body;
        string e2;
        if (!Ib3Crypt.TryDecrypt(be.Data, out body, out e2)) { bad.Append(doc).Append(I18n.T("(解密失败) ")); continue; }
        CacheEntry ent = cents[idx];
        if (ent.ContentLen > body.Length) { bad.Append(doc).Append("(contentLen " + ent.ContentLen + I18n.T(" > 明文 ") + body.Length + ") "); continue; }
        string h = Ib3Crypt.Sha1Hex(body, ent.ContentLen);
        if (h == ent.Hash) okc++;
        else bad.Append(doc).Append(I18n.T("(SHA1 不符) "));
      }
      r5.Pass = (okc == checkNames.Length);
      r5.Result = r5.Pass ? "通过" : "失败";
      r5.Detail = r5.Pass
        ? "5 个文件（4 槽文件 + _CurrentSlot）的 SHA1 与包内缓存逐条一致"
        : (I18n.T("不一致/无法验证：") + bad.ToString());
      rows.Add(r5);
    }

    // ⑥ 主档：首属性 ValidSave + 端到端结构解析
    CheckRow r6 = new CheckRow();
    r6.Item = "6. 主档以 FString \"ValidSave\" 开头，且 tagged property 端到端可解析";
    r6.Mandatory = true;
    BundleEntry main = bd.Get(slotNames[0]);
    if (main == null || cents == null) {
      r6.Pass = false; r6.Result = "失败"; r6.Detail = "主档缺失或包内缓存不可用";
      rows.Add(r6);
    } else {
      byte[] body;
      string e2;
      if (!Ib3Crypt.TryDecrypt(main.Data, out body, out e2)) {
        r6.Pass = false; r6.Result = "失败"; r6.Detail = I18n.T("解密失败：") + e2;
        rows.Add(r6);
      } else {
        int idx = Ib3Cache.FindByName(cents, slotNames[0].Substring(1));
        int cl = idx >= 0 ? cents[idx].ContentLen : -1;
        string head;
        int eo;
        if (cl < 0 || cl > body.Length) {
          r6.Pass = false; r6.Result = "失败";
          r6.Detail = I18n.T("包内缓存里没有该主档的条目或 contentLen 越界（contentLen=") + cl + I18n.T("，明文 ") + body.Length + I18n.T("B）");
          rows.Add(r6);
        } else if (!Ib3Props.TryValidate(body, cl, "ValidSave", out head, out eo, out err)) {
          r6.Pass = false; r6.Result = "失败"; r6.Detail = err;
          rows.Add(r6);
        } else {
          r6.Pass = true; r6.Result = "通过";
          r6.Detail = I18n.T("首属性 ValidSave；整档 ") + cl + I18n.T(" 字节解析到底，终点 == contentLen，尾部全零");
          rows.Add(r6);
        }
      }
    }

    // ⑦ 槽元数据：CloudDocIndex + CharacterName
    CheckRow r7 = new CheckRow();
    r7.Item = "7. 槽元数据可解析，且含 CloudDocIndex + CharacterName";
    r7.Mandatory = true;
    int docIndex = -1;
    string charName = null;
    long pawn = -1, gold = -1;
    string cmap = null;
    BundleEntry meta = bd.Get(slotNames[2]);
    if (meta == null) {
      r7.Pass = false; r7.Result = "失败"; r7.Detail = I18n.T("缺 ") + slotNames[2];
      rows.Add(r7);
    } else {
      byte[] body;
      string e2;
      if (!Ib3Crypt.TryDecrypt(meta.Data, out body, out e2)) {
        r7.Pass = false; r7.Result = "失败"; r7.Detail = I18n.T("解密失败：") + e2;
        rows.Add(r7);
      } else {
        int idx = cents != null ? Ib3Cache.FindByName(cents, slotNames[2].Substring(1)) : -1;
        int cl = idx >= 0 ? cents[idx].ContentLen : body.Length;
        List<Prop> ps = new List<Prop>();
        int eo;
        if (!Ib3Props.Collect(body, "", 4, body.Length, 0, ps, out eo, out err)) {
          r7.Pass = false; r7.Result = "失败"; r7.Detail = I18n.T("属性解析失败：") + err;
          rows.Add(r7);
        } else {
          Prop pi = Ib3Props.Find(ps, "CloudDocIndex");
          charName = Ib3Props.Txt(ps, "CharacterName");
          if (pi != null && pi.HasI) docIndex = (int)pi.I;
          pawn = Ib3Props.Num(ps, "SaveFiles.PawnLevel", -1);
          gold = Ib3Props.Num(ps, "SaveFiles.CurrentGold", -1);
          cmap = Ib3Props.Txt(ps, "SaveFiles.CurrentMap");
          r7.Pass = (docIndex >= 0 && charName != null && charName.Length > 0);
          r7.Result = r7.Pass ? "通过" : "失败";
          r7.Detail = r7.Pass
            ? (I18n.T("角色名 \"") + charName + I18n.T("\"，CloudDocIndex=") + docIndex +
               (idx >= 0 ? (I18n.T("（在来源机列表里的下标也正好是 ") + idx + (idx == docIndex ? " ✅" : I18n.T(" ⚠ 与 CloudDocIndex 不符"))) : I18n.T("（来源机列表里没有这条）")))
            : (I18n.T("没读出 CloudDocIndex/CharacterName（CloudDocIndex=") + docIndex + I18n.T("，角色名=") + (charName == null ? I18n.T("无") : charName) + I18n.T("）"));
          rows.Add(r7);
        }
      }
    }

    // ⑧ _CurrentSlot 合理
    CheckRow r8 = new CheckRow();
    r8.Item = "8. _CurrentSlot 在 0..2 且是纯 i32";
    r8.Mandatory = true;
    BundleEntry cs = bd.Get(SLOT_CUR);
    int curVal = -1;
    if (cs == null) {
      r8.Pass = false; r8.Result = "失败"; r8.Detail = "包里缺 _CurrentSlot";
      rows.Add(r8);
    } else {
      byte[] body;
      string e2;
      if (!Ib3Crypt.TryDecrypt(cs.Data, out body, out e2)) {
        r8.Pass = false; r8.Result = "失败"; r8.Detail = I18n.T("解密失败：") + e2;
        rows.Add(r8);
      } else if (body.Length < 4) {
        r8.Pass = false; r8.Result = "失败"; r8.Detail = I18n.T("明文只有 ") + body.Length + I18n.T(" 字节");
        rows.Add(r8);
      } else {
        curVal = BitConverter.ToInt32(body, 0);
        bool tailZero = true;
        for (int i = 4; i < body.Length; i++) if (body[i] != 0) tailZero = false;
        r8.Pass = (curVal >= 0 && curVal <= 2);
        r8.Result = r8.Pass ? "通过" : "失败";
        r8.Detail = I18n.T("槽号 ") + curVal + I18n.T("，明文 ") + body.Length + I18n.T(" 字节，尾部") + (tailZero ? I18n.T("全零") : I18n.T("非零 ⚠")) +
                    (curVal != slot ? I18n.T("（与包的槽号 ") + slot + I18n.T(" 不同——应用时会按包的槽号改写）") : "");
        rows.Add(r8);
      }
    }

    // ⑨ 与本机列表交叉核对：本机该槽元数据的下标 vs 进来的 CloudDocIndex
    CheckRow r9 = new CheckRow();
    r9.Item = "9. 交叉核对：本机列表下标 vs 进来的 CloudDocIndex";
    r9.Mandatory = false;
    string cloud = CloudDir();
    int localIdx = -1;
    bool localCacheOk = false;
    if (!Directory.Exists(cloud)) {
      r9.Warn = true; r9.Result = "警告";
      r9.Detail = I18n.T("本机找不到 Cloud 目录（") + cloud + I18n.T("），无法交叉核对");
      rows.Add(r9);
    } else {
      byte[] lcraw = null;
      try { lcraw = File.ReadAllBytes(Path.Combine(cloud, CACHE_FILE)); } catch { }
      byte[] lcbody = null;
      List<CacheEntry> lcents = null;
      if (lcraw != null && Ib3Crypt.TryDecrypt(lcraw, out lcbody, out err)) {
        int cend;
        if (Ib3Cache.TryParse(lcbody, out lcents, out cend, out err)) localCacheOk = true;
      }
      if (!localCacheOk) {
        r9.Warn = true; r9.Result = "警告";
        r9.Detail = I18n.T("本机 LocalFileHeaderCache 读不出/解析失败（") + err + I18n.T("），无法交叉核对");
        rows.Add(r9);
      } else {
        localIdx = Ib3Cache.FindByName(lcents, slotNames[2].Substring(1));
        if (localIdx < 0) {
          r9.Warn = true; r9.Result = "警告";
          r9.Detail = I18n.T("本机文档列表里没有 ") + slotNames[2].Substring(1) +
                      I18n.T("（该槽在本机是孤儿槽，例如 2 号槽）——进来的 CloudDocIndex=") + docIndex;
          rows.Add(r9);
        } else if (docIndex < 0) {
          r9.Warn = true; r9.Result = "警告";
          r9.Detail = I18n.T("第 7 项没读出 CloudDocIndex，无法核对（本机下标 ") + localIdx + I18n.T("）");
          rows.Add(r9);
        } else if (docIndex == localIdx) {
          r9.Pass = true; r9.Result = "通过";
          r9.Detail = I18n.T("进来的 CloudDocIndex=") + docIndex + I18n.T(" 与本机下标一致 ✅（不会成为孤儿）");
          rows.Add(r9);
        } else {
          r9.Warn = true; r9.Result = "警告";
          r9.Detail = I18n.T("⚠ 孤儿陷阱：进来的 CloudDocIndex=") + docIndex + I18n.T("，但本机列表里的下标是 ") + localIdx +
                      I18n.T("——两侧文档列表顺序/条数不同。导入后该槽可能被当作孤儿（点了也读不到）。");
          rows.Add(r9);
        }
      }
    }

    // ⑩ 本机目标状态：会覆盖什么 / 缓存是否够改写
    CheckRow r10 = new CheckRow();
    r10.Item = "10. 本机目标状态（会被覆盖的文件 / 缓存够不够原地改写）";
    r10.Mandatory = true;
    if (!Directory.Exists(cloud)) {
      r10.Pass = false; r10.Result = "失败"; r10.Detail = I18n.T("本机没有 Cloud 目录：") + cloud;
      rows.Add(r10);
    } else {
      StringBuilder sb = new StringBuilder();
      int ex = 0;
      for (int i = 0; i < 4; i++) {
        try {
          FileInfo fi = new FileInfo(Path.Combine(cloud, slotNames[i]));
          if (fi.Exists) { ex++; sb.Append(slotNames[i]).Append("=").Append(fi.Length).Append(I18n.T("B(将被覆盖) ")); }
          else sb.Append(slotNames[i]).Append(I18n.T("(新建) "));
        } catch { }
      }
      // 本机缓存里必须已经有这 5 条，才能"原地"改写（不加条目、长度不变）
      string need = "";
      if (!localCacheOk) need = I18n.T("本机 LocalFileHeaderCache 不可用");
      else {
        List<CacheEntry> lcents;
        byte[] lcraw = null, lcbody = null;
        try { lcraw = File.ReadAllBytes(Path.Combine(cloud, CACHE_FILE)); } catch { }
        if (lcraw == null || !Ib3Crypt.TryDecrypt(lcraw, out lcbody, out err)) need = I18n.T("本机缓存读不出");
        else {
          int cend;
          if (!Ib3Cache.TryParse(lcbody, out lcents, out cend, out err)) need = I18n.T("本机缓存解析失败");
          else {
            for (int i = 0; i < 4; i++) {
              string doc = slotNames[i].Substring(1);
              if (Ib3Cache.FindByName(lcents, doc) < 0) need += doc + " ";
            }
            if (Ib3Cache.FindByName(lcents, "CurrentSlot") < 0) need += "CurrentSlot ";
          }
        }
      }
      r10.Pass = (need.Length == 0);
      r10.Result = r10.Pass ? "通过" : "失败";
      r10.Detail = r10.Pass
        ? (sb.ToString() + I18n.T("｜本机缓存 5 条都在，可原地改写 SHA1/contentLen（长度不变）"))
        : (I18n.T("本机缓存缺条目，无法原地改写：") + need);
      rows.Add(r10);
    }

    // ---- 结论 ----
    bool firstMandatoryFail = true;
    for (int i = 0; i < rows.Count; i++) {
      if (rows[i].Mandatory && !rows[i].Pass) { firstMandatoryFail = false; break; }
    }
    ready = firstMandatoryFail;

    // ---- 摘要 ----
    StringBuilder sum = new StringBuilder();
    sum.Append(I18n.T("入口存档：槽 ")).Append(slot);
    if (charName != null) sum.Append(I18n.T("　角色 ")).Append(charName);
    if (pawn >= 0) sum.Append(I18n.T("　等级 ")).Append(pawn);
    if (gold >= 0) sum.Append(I18n.T("　金币 ")).Append(gold);
    if (cmap != null) sum.Append(I18n.T("　地图 ")).Append(cmap);
    if (docIndex >= 0) sum.Append(I18n.T("　CloudDocIndex ")).Append(docIndex);
    sum.Append(I18n.T("　_CurrentSlot ")).Append(curVal);

    // 宝石数：从主档里数（能数出来才显示）
    if (main != null) {
      byte[] body;
      string e2;
      if (Ib3Crypt.TryDecrypt(main.Data, out body, out e2)) {
        List<Prop> ps = new List<Prop>();
        int eo;
        if (Ib3Props.Collect(body, "", 4, body.Length, 0, ps, out eo, out err)) {
          Prop bag = Ib3Props.Find(ps, "PlayerUnequippedGems");
          Prop store = Ib3Props.Find(ps, "CurrentStoreGems");
          long mv = Ib3Props.Num(ps, "PawnLevel", -1);
          if (mv >= 0 && pawn < 0) { pawn = mv; }
          sum.Append(I18n.T("\r\n主档：背包宝石 "));
          sum.Append(bag != null && bag.HasI ? bag.I.ToString() : "?");
          sum.Append(I18n.T(" 颗 / 随身商店 "));
          sum.Append(store != null && store.HasI ? store.I.ToString() : "?");
          sum.Append(I18n.T(" 颗"));
        } else {
          sum.Append(I18n.T("\r\n主档：宝石数未取到（")).Append(err).Append(I18n.T("）"));
        }
      }
    }
    sum.Append("\r\n");
    sum.Append(ready ? I18n.T("👉 必需检查全部通过，可以点「应用导入」。") : I18n.T("👉 有必需检查未通过，请勿直接导入（要硬来只能点「强制导入」，仍会先备份）。"));
    summary = sum.ToString();
  }

  // ================= ② 应用导入 =================
  void ApplyImport(bool force) {
    if (lastBundlePath == null) { ToastMgr.Warn("请先选择并校验一个 .ib3save"); return; }
    if (Launcher.FindGame() != null) {
      ToastMgr.Warn("游戏正在运行 —— 请先完全关闭 IB3.exe 再导入");
      Log("导入被拒绝：检测到 IB3 进程仍在运行。游戏退出/自动存档时会把 Cloud\\ 整个重写，这是换档失败的头号原因。");
      return;
    }
    if (!force && !importReady) {
      ToastMgr.Warn("必需检查未通过，已阻止导入（要硬来请点「强制导入」）");
      return;
    }
    if (force) {
      DialogResult dr = MessageBox.Show(this,
        I18n.T("强制导入会跳过「必需检查未通过」的阻拦，直接覆盖本机的存档文件。\n\n" +
               "虽然仍会先整目录备份，但强烈建议先看清校验表里的失败项。\n\n确定要继续吗？"),
        I18n.T("强制导入确认"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
      if (dr != DialogResult.Yes) { Log("强制导入已取消"); return; }
    }

    string cloud = CloudDir();
    if (!Directory.Exists(cloud)) { ToastMgr.Warn(I18n.T("找不到本机 Cloud 目录：") + cloud); return; }

    BusyShow("导入存档（备份 + 写入）");
    try {
      Bundle bd;
      string err;
      if (!Ib3Bundle.Read(lastBundlePath, out bd, out err)) { ToastMgr.Warn(I18n.T("读取存档包失败：") + err); return; }
      int slot = bd.Slot;
      string[] slotNames = new string[] {
        string.Format(SLOT_FMT[0], slot), string.Format(SLOT_FMT[1], slot),
        string.Format(SLOT_FMT[2], slot), string.Format(SLOT_FMT[3], slot) };

      // ---- 包内缓存（权威 contentLen）----
      BundleEntry ce = bd.Get(CACHE_FILE);
      if (ce == null) { ToastMgr.Warn("包里没有 LocalFileHeaderCache，无法同步 contentLen"); return; }
      byte[] cbody;
      if (!Ib3Crypt.TryDecrypt(ce.Data, out cbody, out err)) { ToastMgr.Warn(I18n.T("包内缓存解密失败：") + err); return; }
      List<CacheEntry> cents;
      int cend;
      if (!Ib3Cache.TryParse(cbody, out cents, out cend, out err)) { ToastMgr.Warn(I18n.T("包内缓存解析失败：") + err); return; }

      // ---- 本机缓存：必须"原地"改写，所以 5 条必须都已存在 ----
      byte[] lraw;
      try { lraw = File.ReadAllBytes(Path.Combine(cloud, CACHE_FILE)); }
      catch (Exception ex) { ToastMgr.Warn(I18n.T("读本机 LocalFileHeaderCache 失败：") + ex.Message); return; }
      byte[] lbody;
      if (!Ib3Crypt.TryDecrypt(lraw, out lbody, out err)) { ToastMgr.Warn(I18n.T("本机缓存解密失败：") + err); return; }
      List<CacheEntry> lents;
      int lend;
      if (!Ib3Cache.TryParse(lbody, out lents, out lend, out err)) { ToastMgr.Warn(I18n.T("本机缓存解析失败：") + err); return; }

      string[] needDocs = new string[] {
        slotNames[0].Substring(1), slotNames[1].Substring(1),
        slotNames[2].Substring(1), slotNames[3].Substring(1), "CurrentSlot" };
      for (int i = 0; i < needDocs.Length; i++) {
        if (Ib3Cache.FindByName(lents, needDocs[i]) < 0) {
          ToastMgr.Warn(I18n.T("本机缓存里没有 ") + needDocs[i] + I18n.T(" —— 无法原地改写（本工具不新增文档条目）。请先在游戏里存过一次该槽。"));
          Log("导入中止：本机缓存缺 " + needDocs[i] + "（新增条目会改变文档列表长度/顺序，超出本工具的原地改写范围）");
          return;
        }
      }

      // ---- 1) 备份整个 Cloud\ ----
      string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
      string bdir = Path.Combine(backupRoot, "cloud_backup_" + ts);
      try {
        Directory.CreateDirectory(bdir);
        string[] files = Directory.GetFiles(cloud);
        for (int i = 0; i < files.Length; i++)
          File.Copy(files[i], Path.Combine(bdir, Path.GetFileName(files[i])), true);
        Log("已备份本机 Cloud\\ 共 " + files.Length + " 个文件 → " + bdir);
      } catch (Exception ex) {
        ToastMgr.Warn(I18n.T("备份失败，已中止导入：") + ex.Message);
        Log("导入中止：备份失败 " + ex);
        return;
      }

      // ---- 2) 写 4 个槽文件，并记下各自的 (contentLen, 新 SHA1) ----
      StringBuilder wlog = new StringBuilder();
      string[] newHash = new string[5];
      int[] newLen = new int[5];
      for (int i = 0; i < 4; i++) {
        BundleEntry be = bd.Get(slotNames[i]);
        if (be == null) { ToastMgr.Warn(I18n.T("包里缺 ") + slotNames[i] + I18n.T("，已中止")); return; }
        string doc = slotNames[i].Substring(1);
        int idx = Ib3Cache.FindByName(cents, doc);
        int cl = cents[idx].ContentLen;
        byte[] body;
        if (!Ib3Crypt.TryDecrypt(be.Data, out body, out err)) { ToastMgr.Warn(slotNames[i] + I18n.T(" 解密失败：") + err); return; }
        if (cl > body.Length) { ToastMgr.Warn(slotNames[i] + I18n.T(" contentLen 超出明文长度")); return; }
        newLen[i] = cl;
        newHash[i] = Ib3Crypt.Sha1Hex(body, cl);
        try { File.WriteAllBytes(Path.Combine(cloud, slotNames[i]), be.Data); }
        catch (Exception ex) { ToastMgr.Warn(I18n.T("写 ") + slotNames[i] + I18n.T(" 失败：") + ex.Message); return; }
        wlog.Append(slotNames[i]).Append("=").Append(be.Data.Length).Append("B ");
      }

      // ---- 3) _CurrentSlot（默认启动槽）—— 可选，见界面上那个勾选框 ----
      // 不勾就完全不动它（连它的缓存条目也不改写，否则会把缓存写坏）
      int nRewrite = 4;                         // 要重写缓存的前几条：4=只槽文件，5=外加 _CurrentSlot
      if (chkSetSlot == null || chkSetSlot.Checked) {
        byte[] csBody = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(slot), 0, csBody, 0, 4);
        byte[] csEnc = Ib3Crypt.Encrypt(csBody);
        int csIdx = Ib3Cache.FindByName(cents, "CurrentSlot");
        int csLen = cents[csIdx].ContentLen;
        newLen[4] = csLen;
        newHash[4] = Ib3Crypt.Sha1Hex(csBody, csLen);
        try { File.WriteAllBytes(Path.Combine(cloud, SLOT_CUR), csEnc); }
        catch (Exception ex) { ToastMgr.Warn(I18n.T("写 ") + SLOT_CUR + I18n.T(" 失败：") + ex.Message); return; }
        wlog.Append(SLOT_CUR).Append("=").Append(csEnc.Length).Append("B(槽号→").Append(slot).Append(") ");
        nRewrite = 5;
      } else {
        wlog.Append(SLOT_CUR).Append("=保持不变(未勾选) ");
      }

      // ---- 4) 原地改写本机缓存里那 5 条的 SHA1 + contentLen（长度与偏移全不变）----
      int beforeLen = lbody.Length;
      int changed = 0;
      for (int i = 0; i < nRewrite; i++) {
        string doc = (i < 4) ? slotNames[i].Substring(1) : "CurrentSlot";
        int idx = Ib3Cache.FindByName(lents, doc);
        CacheEntry e = lents[idx];
        bool clChanged = (e.ContentLen != newLen[i]);
        if (!Ib3Cache.SetInPlace(lbody, e, newHash[i], newLen[i])) {
          ToastMgr.Warn(I18n.T("改写本机缓存条目 ") + doc + I18n.T(" 失败（偏移越界）"));
          return;
        }
        changed++;
        if (clChanged) Log("  缓存 " + doc + " 的 contentLen " + e.ContentLen + " → " + newLen[i]);
      }
      if (lbody.Length != beforeLen) {
        ToastMgr.Warn(I18n.T("内部错误：缓存体长被改变（") + beforeLen + " → " + lbody.Length + I18n.T("），已中止写盘"));
        return;
      }
      try { File.WriteAllBytes(Path.Combine(cloud, CACHE_FILE), Ib3Crypt.Encrypt(lbody)); }
      catch (Exception ex) { ToastMgr.Warn(I18n.T("写 LocalFileHeaderCache 失败：") + ex.Message); return; }

      // ---- 5) 写后复验：本机缓存与刚写下去的文件必须逐条自洽 ----
      string csum;
      List<CheckRow> rows = CheckLocalCache(cloud, out csum);
      ShowChecks(rows);
      int badn = 0;
      for (int i = 0; i < rows.Count; i++) if (!rows[i].Pass) badn++;

      Log("导入完成：槽 " + slot + "，写入 " + wlog.ToString().TrimEnd() + "，缓存改写 " + changed + " 条");
      Log("备份路径: " + bdir);
      if (badn == 0) {
        Log("写后复验：本机缓存 " + rows.Count + " 条全部自洽 ✅");
        ToastMgr.Show(I18n.T("导入完成（槽 ") + slot + I18n.T("）。备份在 ") + bdir + I18n.T(" —— 现在可以启动游戏了"));
        lblSaveInfo.Text = I18n.T("✅ 导入完成：槽 ") + slot + I18n.T("。备份：") + bdir + I18n.T("\r\n现在可以启动游戏（点底部「启动游戏」）。");
      } else {
        Log("写后复验：仍有 " + badn + " 条不一致（见下表）");
        ToastMgr.Warn(I18n.T("导入已写入，但复验有 ") + badn + I18n.T(" 条不一致 —— 见校验表；备份在 ") + bdir);
        lblSaveInfo.Text = I18n.T("⚠ 导入已写入，但复验有 ") + badn + I18n.T(" 条不一致（见校验表）。备份：") + bdir;
      }
      lblBundleInfo.Text = I18n.T("最近导入：") + Path.GetFileName(lastBundlePath) + I18n.T(" → 槽 ") + slot;
      importReady = false;
      SetImportControls(false, true);
      RefreshSlotCombo();
    } catch (Exception ex) {
      ToastMgr.Warn(I18n.T("导入异常：") + ex.Message);
      Log("导入异常: " + ex);
    } finally { BusyHide(); }
  }

  // ================= 校验表 =================
  void ShowChecks(List<CheckRow> rows) {
    if (lvSaveChecks == null) return;
    if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { ShowChecks(rows); }); return; }
    lvSaveChecks.BeginUpdate();
    lvSaveChecks.Items.Clear();
    for (int i = 0; i < rows.Count; i++) {
      CheckRow r = rows[i];
      ListViewItem it = new ListViewItem(r.Item);
      it.SubItems.Add(r.Result);
      it.SubItems.Add(r.Detail);
      it.Tag = r;
      lvSaveChecks.Items.Add(it);
    }
    lvSaveChecks.EndUpdate();
  }
}

} // namespace
