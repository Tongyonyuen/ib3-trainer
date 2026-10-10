// ============================================================================
// GameConfig.cs — 游戏 ini 的「段落级、键级」读改写（可复用底座）
//
// 为什么需要它：IB3 的宝石模板是一堆 [模板名 类] 段落，我们要改的往往只是段内
//   **某一个键**。全局字符串替换在这里是**错的** —— 例如 `BE_Time_ParryAllAttacks`
//   在 DefaultGems.ini 里出现 2 次（全招架宝石段 + Potion_ParryAll 药水段），
//   全局替换会把药水段一起写坏，"还原"时更会把药水段写反。
//
// 三条铁律（都由实测逼出来，见 docs/使用说明.md 的「全招架宝石」一节）：
//   ① **按段落定位**再改键，绝不全局替换。
//   ② **字节级定点替换**，不按行读写。实测：同一台机器上，游戏侧 DefaultGems.ini 是
//      **裸 LF**、用户侧 SwordGems.ini 是**纯 CRLF**（各自内部一致）。任何
//      ReadAllLines/WriteAllLines 都会把换行统一掉 ⇒ 等于把整个文件重写一遍。
//      字节替换则保证**除值区间外一个字节都不动**。
//   ③ **写后回读复核**（照 MemIO.SafeWrite 的"文件版"）：核不上就报错，绝不报假成功。
//
// ★ 本文件**刻意不引用** WinForms / I18n / MainForm / Theme / Launcher：
//   路径一律由调用方传入，这样离线自测（ConfigSelfTest.cs）能传临时夹具、绝不碰真实文件。
//
// C# 5（csc v4.0.30319，无 /langversion）：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

static class GameConfig {

  // ================= 路径（纯字符串，不读全局状态） =================

  // <我的文档>\My Games\Infinity Blade III\SwordGame
  public static string UserGameDir() {
    string doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    return Path.Combine(Path.Combine(Path.Combine(doc, "My Games"),
                       "Infinity Blade III"), "SwordGame");
  }
  // 真档目录（存档所在）。Tabs.Save.cs 的 CloudDir() 已改为委托到这里。
  public static string CloudDir() { return Path.Combine(UserGameDir(), "Cloud"); }

  // 用户侧宝石模板表：UE3 分层加载里**用户侧在默认配置之后**，会覆盖游戏侧。
  // 实测（2026-10-10）：只改用户侧这一份，游戏内行为就已改变。
  public static string UserGemsPath() {
    return Path.Combine(Path.Combine(UserGameDir(), "Config"), "SwordGems.ini");
  }

  // 游戏侧默认模板表：<游戏根>\SwordGame\Config\DefaultGems.ini
  // ★ 必须用「启动器所在文件夹」上探得到的游戏根，不能假定某个固定盘符 ——
  //   本机就有两份安装，改错那一份会让人以为"改了没生效"。
  public static string DefaultGemsPath(string gameRoot) {
    if (string.IsNullOrEmpty(gameRoot)) return null;
    return Path.Combine(Path.Combine(Path.Combine(gameRoot, "SwordGame"), "Config"),
                        "DefaultGems.ini");
  }

  // ================= 结果码 =================

  public const int RC_OK = 3;
  public const int RC_FILE_MISSING = 0;
  public const int RC_SECTION_MISSING = 1;
  public const int RC_KEY_MISSING = 2;
  public const int RC_IO_ERROR = 4;

  // ================= 纯字节原语（自测直接打这些） =================

  static int IndexOf(byte[] hay, byte[] needle, int from) {
    if (hay == null || needle == null || needle.Length == 0) return -1;
    int last = hay.Length - needle.Length;
    for (int i = (from < 0 ? 0 : from); i <= last; i++) {
      int k = 0;
      while (k < needle.Length && hay[i + k] == needle[k]) k++;
      if (k == needle.Length) return i;
    }
    return -1;
  }

  // 行首判定：文件开头，或前一字节是 '\n'（对 LF 与 CRLF 都成立）
  public static bool IsLineStart(byte[] b, int i) {
    return i == 0 || (i > 0 && b[i - 1] == (byte)'\n');
  }

  static bool HasNul(byte[] b) {
    if (b == null) return false;
    for (int i = 0; i < b.Length; i++) if (b[i] == 0) return true;
    return false;
  }

  // 定位段头行。name 是**不含方括号**的完整段头文本（例如
  // "GreatParryAllGem SwordInventoryItemGem"）。三重约束防误命中：
  //   行首 + '[' + 全名 —— 因为同一文件里还有 `MPParent=GreatParryAllGem` 这类出现。
  // 输出：bodyStart = 头行之后（含换行）的偏移；bodyEnd = 下一个**行首** '[' 的偏移（或文件尾）。
  public static bool FindSection(byte[] b, string name, out int bodyStart, out int bodyEnd) {
    bodyStart = -1; bodyEnd = -1;
    if (b == null || name == null || name.Length == 0) return false;
    byte[] hdr = Encoding.ASCII.GetBytes("[" + name + "]");
    int hit = -1, from = 0;
    while (true) {
      int j = IndexOf(b, hdr, from);
      if (j < 0) return false;
      if (IsLineStart(b, j)) { hit = j; break; }
      from = j + 1;
    }
    int eol = -1;
    for (int k = hit; k < b.Length; k++) { if (b[k] == (byte)'\n') { eol = k; break; } }
    bodyStart = (eol < 0) ? b.Length : eol + 1;
    bodyEnd = b.Length;
    int m = bodyStart;
    while (m < b.Length) {
      int lb = -1;
      for (int q = m; q < b.Length; q++) { if (b[q] == (byte)'[') { lb = q; break; } }
      if (lb < 0) { bodyEnd = b.Length; break; }
      if (IsLineStart(b, lb)) { bodyEnd = lb; break; }
      m = lb + 1;
    }
    return true;
  }

  // 段内按「行首 key=」取值。★ 必须整体比较 "key=" —— 段内紧邻就是
  // `BattleEffectValue=1`，任何 StartsWith(key) 的写法都会抓错行。
  // 值区间已剔除尾部空格/制表符，**不含行尾符**（写入时只替换这一段，行尾符原样保留）。
  public static bool FindKey(byte[] b, int bodyStart, int bodyEnd, string key,
                             out int valStart, out int valEnd) {
    valStart = -1; valEnd = -1;
    if (b == null || key == null || key.Length == 0) return false;
    if (bodyStart < 0) bodyStart = 0;
    if (bodyEnd > b.Length) bodyEnd = b.Length;
    byte[] pat = Encoding.ASCII.GetBytes(key + "=");
    int i = bodyStart;
    while (i < bodyEnd) {
      int e = i;
      while (e < bodyEnd && b[e] != (byte)'\r' && b[e] != (byte)'\n') e++;
      if (e - i >= pat.Length) {
        int k = 0;
        while (k < pat.Length && b[i + k] == pat[k]) k++;
        if (k == pat.Length) {
          int vs = i + pat.Length;
          int ve = e;
          while (ve > vs && (b[ve - 1] == (byte)' ' || b[ve - 1] == (byte)'\t')) ve--;
          valStart = vs; valEnd = ve;
          return true;
        }
      }
      i = e;
      while (i < bodyEnd && (b[i] == (byte)'\r' || b[i] == (byte)'\n')) i++;
    }
    return false;
  }

  // 段内该键出现几次。!= 1 时调用方一律拒绝（歧义段不猜）。
  public static int CountKey(byte[] b, int bodyStart, int bodyEnd, string key) {
    int n = 0, i = bodyStart;
    if (bodyStart < 0) i = 0;
    if (b == null || key == null) return 0;
    int limit = (bodyEnd > b.Length || bodyEnd < 0) ? b.Length : bodyEnd;
    byte[] pat = Encoding.ASCII.GetBytes(key + "=");
    while (i < limit) {
      int e = i;
      while (e < limit && b[e] != (byte)'\r' && b[e] != (byte)'\n') e++;
      if (e - i >= pat.Length) {
        int k = 0;
        while (k < pat.Length && b[i + k] == pat[k]) k++;
        if (k == pat.Length) n++;
      }
      i = e;
      while (i < limit && (b[i] == (byte)'\r' || b[i] == (byte)'\n')) i++;
    }
    return n;
  }

  public static string Ascii(byte[] b, int s, int e) {
    if (b == null || s < 0 || e > b.Length || e < s) return null;
    char[] c = new char[e - s];
    for (int i = 0; i < c.Length; i++) c[i] = (char)b[s + i];
    return new string(c);
  }

  // 用 repl 替换 [from,to)，长度可不同
  public static byte[] Splice(byte[] b, int from, int to, byte[] repl) {
    if (b == null || repl == null) return b;
    byte[] nb = new byte[b.Length - (to - from) + repl.Length];
    Array.Copy(b, 0, nb, 0, from);
    Array.Copy(repl, 0, nb, from, repl.Length);
    Array.Copy(b, to, nb, from + repl.Length, b.Length - to);
    return nb;
  }

  // ================= 备份 =================

  // ★ 固定名、**只在首次创建**：绝不覆盖用户已有的备份（包括手工留下的 *.bak_*）。
  public static string BackupPath(string path) { return path + ".ib3bak"; }

  public static bool EnsureBackup(string path, out string err) {
    err = null;
    try {
      string bak = BackupPath(path);
      if (File.Exists(bak)) return true;
      if (!File.Exists(path)) { err = "源文件不存在"; return false; }
      File.Copy(path, bak, false);
      return true;
    } catch (Exception ex) { err = ex.Message; return false; }
  }

  // ================= 读一个键 =================

  public static int ReadKey(string path, string section, string key, out string value, out string err) {
    value = null; err = null;
    if (string.IsNullOrEmpty(path)) { err = "路径为空"; return RC_FILE_MISSING; }
    if (!File.Exists(path)) { err = "文件不存在：" + path; return RC_FILE_MISSING; }
    byte[] b;
    try { b = File.ReadAllBytes(path); }
    catch (Exception ex) { err = "读取失败：" + ex.Message; return RC_IO_ERROR; }
    if (HasNul(b)) { err = "文件含 NUL 字节（疑似 UTF-16），拒绝按 ASCII 解析"; return RC_IO_ERROR; }

    int bs, be;
    if (!FindSection(b, section, out bs, out be)) {
      err = "文件里没有 [" + section + "] 段"; return RC_SECTION_MISSING;
    }
    int vs, ve;
    if (!FindKey(b, bs, be, key, out vs, out ve)) {
      err = "[" + section + "] 段内没有 " + key + " 键"; return RC_KEY_MISSING;
    }
    int n = CountKey(b, bs, be, key);
    if (n != 1) { err = "段内 " + key + " 键出现 " + n + " 次，拒绝处理"; return RC_KEY_MISSING; }
    value = Ascii(b, vs, ve);
    return RC_OK;
  }

  // ================= 写一个键 =================
  // 幂等：值已等于目标 → 直接 RC_OK，**不写盘、不动 mtime**。
  // 任何一步不满足就**不碰文件**地返回失败（绝不留半个状态）。
  public static int WriteKey(string path, string section, string key, string value, out string err) {
    err = null;
    if (string.IsNullOrEmpty(path)) { err = "路径为空"; return RC_FILE_MISSING; }
    if (!File.Exists(path)) { err = "文件不存在：" + path; return RC_FILE_MISSING; }
    byte[] b;
    try { b = File.ReadAllBytes(path); }
    catch (Exception ex) { err = "读取失败：" + ex.Message; return RC_IO_ERROR; }
    if (HasNul(b)) { err = "文件含 NUL 字节（疑似 UTF-16），拒绝改写"; return RC_IO_ERROR; }

    int bs, be;
    if (!FindSection(b, section, out bs, out be)) {
      err = "文件里没有 [" + section + "] 段 —— 未做任何改动"; return RC_SECTION_MISSING;
    }
    int n = CountKey(b, bs, be, key);
    if (n != 1) {
      err = "[" + section + "] 段内 " + key + " 键出现 " + n + " 次 —— 未做任何改动";
      return RC_KEY_MISSING;
    }
    int vs, ve;
    if (!FindKey(b, bs, be, key, out vs, out ve)) {
      err = "[" + section + "] 段内没有 " + key + " 键 —— 未做任何改动"; return RC_KEY_MISSING;
    }
    string cur = Ascii(b, vs, ve);
    if (cur == value) return RC_OK;                       // ★ 幂等

    try {
      FileAttributes at = File.GetAttributes(path);
      if ((at & FileAttributes.ReadOnly) != 0) { err = "文件被设为只读：" + path; return RC_IO_ERROR; }
    } catch (Exception ex) { err = "检查文件属性失败：" + ex.Message; return RC_IO_ERROR; }

    string berr;
    if (!EnsureBackup(path, out berr)) { err = "备份失败：" + berr + "（未做任何改动）"; return RC_IO_ERROR; }

    byte[] nb = Splice(b, vs, ve, Encoding.ASCII.GetBytes(value));
    try { File.WriteAllBytes(path, nb); }
    catch (Exception ex) { err = "写入失败：" + ex.Message; return RC_IO_ERROR; }

    // ★ 写后回读复核（照 MemIO.SafeWrite 的"文件版"）
    string back, rerr;
    int rc = ReadKey(path, section, key, out back, out rerr);
    if (rc != RC_OK) { err = "写入后回读失败：" + rerr; return RC_IO_ERROR; }
    if (back != value) {
      err = "写入后回读不一致（期望 " + value + "，实得 " + back + "）"; return RC_IO_ERROR;
    }
    return RC_OK;
  }
}

// ============================================================================
// ParryGem — 本期唯一的那颗宝石。[GreatParryAllGem] 段的 BattleEffect：
//   原版 BE_VarAdd_GreatParryAll   = 写 ModVars[81] ⇒ NoParry 把 MinParryTypeValid
//                                    抬到 2（高水平招架）⇒ 只有高水平招架以上才判定成功
//   改为 BE_Time_ParryAllAttacks   = IB2「完全招架宝石」的效果，**普通招架即可**
//                                    （2026-10-10 游戏内实测通过，且跨场常驻）
// 以后要扩成「任意宝石 → 任意效果」的通用改写器时，把这三个常量参数化即可；
// 底下的 GameConfig 那一层不用动（这就是"先做窄的、留扩展口"的落点）。
// ============================================================================
static class ParryGem {
  public const string SECTION = "GreatParryAllGem SwordInventoryItemGem";
  public const string KEY = "BattleEffect";
  public const string VAL_ON = "BE_Time_ParryAllAttacks";
  public const string VAL_OFF = "BE_VarAdd_GreatParryAll";

  // 单文件状态
  public const int ST_FILE_MISSING = 0;   // 文件不存在
  public const int ST_SEC_MISSING = 1;    // 文件里没有该段
  public const int ST_KEY_MISSING = 2;    // 段内没有该键（或出现多次）
  public const int ST_ON = 3;             // 已是常规招架效果
  public const int ST_OFF = 4;            // 仍为原版效果
  public const int ST_OTHER = 5;          // 第三种值（别的工具/手工改过）—— 一律不覆盖
  public const int ST_IO_ERR = 6;         // 读不了（只读/编码/异常）

  public static int FileState(string path, out string value, out string err) {
    value = null; err = null;
    int rc = GameConfig.ReadKey(path, SECTION, KEY, out value, out err);
    if (rc == GameConfig.RC_FILE_MISSING) return ST_FILE_MISSING;
    if (rc == GameConfig.RC_SECTION_MISSING) return ST_SEC_MISSING;
    if (rc == GameConfig.RC_KEY_MISSING) return ST_KEY_MISSING;
    if (rc != GameConfig.RC_OK) return ST_IO_ERR;
    if (value == VAL_ON) return ST_ON;
    if (value == VAL_OFF) return ST_OFF;
    return ST_OTHER;
  }

  // 生效态 = **用户侧优先，游戏侧回退**。
  // ★ 为什么不是"两个都是目标值才算开启"：本机现状就是反例 —— 用户侧 ON、游戏侧 OFF
  //   （游戏侧那份 DefaultGems.ini 是原版），而游戏内行为**已经**是开启的。
  //   按"两个都要"判会在 UI 上显示假状态，还会诱导用户去点一次"开启"而误写。
  public static int EffectiveState(string userPath, string defPath,
                                   out string userVal, out int userSt, out string userErr,
                                   out string defVal, out int defSt, out string defErr) {
    userSt = FileState(userPath, out userVal, out userErr);
    defSt = FileState(defPath, out defVal, out defErr);
    if (userSt == ST_ON || userSt == ST_OFF || userSt == ST_OTHER) return userSt;
    if (defSt == ST_ON || defSt == ST_OFF || defSt == ST_OTHER) return defSt;
    return userSt;   // 两边都读不到 → 返回用户侧状态，供 UI 报原因
  }

  // 应用/还原。返回 null = 成功；否则错误文案（可直接进 Toast）。
  // detail = 逐文件结果串（进日志），**含两条完整路径** —— 本机有两份安装，
  // "改了哪一份"必须一眼可见（这个坑已经踩过一次）。
  // 用户侧**必须成功**；游戏侧**尽力而为**，失败只记录、不判整体失败。
  public static string Apply(bool on, string userPath, string defPath, out string detail) {
    string val = on ? VAL_ON : VAL_OFF;
    StringBuilder sb = new StringBuilder();

    // ---- 1) 用户侧预检：不满足就**不碰任何文件**地拒绝 ----
    string uv, uerr;
    int ust = FileState(userPath, out uv, out uerr);
    if (ust == ST_FILE_MISSING) {
      detail = "用户配置不存在：" + userPath;
      return "用户配置文件不存在（请先启动一次游戏再试）：" + userPath;
    }
    if (ust == ST_SEC_MISSING) {
      detail = "用户配置缺少段：" + uerr;
      return "用户配置文件里找不到全招架宝石段（游戏版本不符？）：" + uerr;
    }
    if (ust == ST_KEY_MISSING) {
      detail = "用户配置缺少键：" + uerr;
      return "用户配置文件里找不到 BattleEffect 键（游戏版本不符？）：" + uerr;
    }
    if (ust == ST_IO_ERR) {
      detail = "用户配置读取失败：" + uerr;
      return "读取用户配置文件失败：" + uerr;
    }
    if (ust == ST_OTHER) {
      detail = "用户配置的效果是第三种值：" + uv;
      return "该宝石的效果已被改成 " + uv + "（可能是别的工具或手工改的）—— 为避免覆盖，已拒绝改写";
    }

    // ---- 2) 用户侧写入（必须成功）----
    string werr;
    int rc = GameConfig.WriteKey(userPath, SECTION, KEY, val, out werr);
    if (rc != GameConfig.RC_OK) {
      detail = "用户配置写入失败：" + werr;
      return "写入用户配置失败：" + werr;
    }
    sb.Append("用户配置 ").Append(userPath).Append(" = ").Append(val);

    // ---- 3) 游戏侧（尽力而为）----
    if (string.IsNullOrEmpty(defPath)) {
      sb.Append(" ｜ 游戏默认：未设置游戏目录，跳过");
    } else {
      string dv, derr;
      int dst = FileState(defPath, out dv, out derr);
      if (dst == ST_FILE_MISSING) {
        sb.Append(" ｜ 游戏默认 ").Append(defPath).Append("：不存在，跳过");
      } else if (dst == ST_SEC_MISSING || dst == ST_KEY_MISSING) {
        sb.Append(" ｜ 游戏默认 ").Append(defPath).Append("：无该段/键，跳过");
      } else if (dst == ST_OTHER) {
        sb.Append(" ｜ 游戏默认 ").Append(defPath).Append("：效果为 ").Append(dv).Append("，为免覆盖已跳过");
      } else if (dst == ST_IO_ERR) {
        sb.Append(" ｜ 游戏默认 ").Append(defPath).Append("：读取失败（").Append(derr).Append("），跳过");
      } else {
        string derr2;
        int rc2 = GameConfig.WriteKey(defPath, SECTION, KEY, val, out derr2);
        if (rc2 == GameConfig.RC_OK) {
          sb.Append(" ｜ 游戏默认 ").Append(defPath).Append(" = ").Append(val);
        } else {
          sb.Append(" ｜ 游戏默认 ").Append(defPath).Append("：写入失败（").Append(derr2)
            .Append("）—— 不影响用户配置生效");
        }
      }
    }
    detail = sb.ToString();
    return null;
  }

  // 供 UI 用：把状态码翻成中文片段（拼接句，调用方自行再拼路径/值）
  public static string StateText(int st) {
    if (st == ST_ON) return "已开启";
    if (st == ST_OFF) return "已关闭";
    if (st == ST_OTHER) return "效果被改成其它值";
    if (st == ST_FILE_MISSING) return "配置文件不存在";
    if (st == ST_SEC_MISSING) return "配置里没有该宝石段";
    if (st == ST_KEY_MISSING) return "配置里没有该键";
    return "读不到配置";
  }
}

} // namespace
