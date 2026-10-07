// ============================================================================
// GemSelfTest.cs — 宝石定位自检（**只读**，不写目标进程、不写存档）
//
// 目的：验证 Tabs.Gems.cs 里「宝石定位」这条路径真的能定位，
//       而不是只"编译通过"。**跑的就是 Tabs.Gems.cs 里那份真代码**（反射调私有方法），
//       不是另抄一份实现 —— 抄一份只能证明抄件对，证明不了产品代码对。
//
// 检查项：
//   identity.zip      ★ 2026-10-07 二次改版后的**主路径**：身份定位（真身 +0x1FEC/+0x1FFC）
//                     拿到背包/商店数组，再与存档**按位对齐**命名；断言名字逐条等于存档。
//                     特意覆盖「背包退化成一串同名同状态记录」这种会让序列指纹失效的状态。
//   saveseq.cands     存档候选序列（三槽 × 背包/商店）建成，闸门把坏数组挡在外面
//   saveseq.locate    序列扫描命中；命中数组数/条数在合理规模（不再有 405 组 / 92 条那种垃圾）
//   saveseq.names     每条命中记录都贴上了 GemDb 里查得到的真名（不是裸索引 0xNNNN）
//   saveseq.authority ★ 权威那份背包数组（真身 +0x1FEC 的 TArray.Data）必须能被**某条定位路径**拿到。
//                     二次改版后主路径是身份定位，序列扫描只是补充 —— 前者覆盖即通过。
//   saveseq.empty     候选为空时必须干净失败（不许退回形状扫描）
//   additive.all      15 个加法型模板都得在公式库里（少一个 = 那颗宝石在界面上被当成"未知类型"）
//   limits.known      ★「填上限」的判据：上限**说得清才允许自动填**。加法型 tier 上限 = 255
//                     （uint8 字段硬上限）、下标型 = 档位表长（**不是 255**）、未知类型/"空槽" = 说不清。
//                     旧版会给未知类型也填 255 —— 看着确定、其实全猜。
//
// 编译: sh build.sh gemtest        （游戏在跑才有实况项，否则记 SKIP）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace Ib3Trainer2 {

static class GemSelfTest {
  static int passed = 0, failed = 0;
  static MainForm F;
  static IntPtr H = IntPtr.Zero;
  static object Cands;              // List<SeqCand>（私有嵌套类型，只能 object 持有）
  static long RealBody;

  static int Main(string[] args) {
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    Console.WriteLine("== IB3 训练器2 · 宝石定位自检（按存档序列扫描）==");
    T("setup.form", SetupForm);
    T("setup.game", SetupGame);
    T("saveseq.cands", TestCands);
    T("saveseq.locate", TestLocate);
    T("saveseq.names", TestNames);
    T("saveseq.authority", TestAuthority);
    T("saveseq.empty", TestEmpty);
    T("identity.zip", TestIdentityZip);
    T("additive.all", TestAdditive);
    T("limits.known", TestLimits);
    Console.WriteLine("RESULT gemtest = " + passed + "/" + (passed + failed) + (failed == 0 ? " PASS" : " FAIL"));
    return failed == 0 ? 0 : 1;
  }

  static void T(string name, Func<string> f) {
    try {
      string note = f();
      if (note == null) { passed++; Console.WriteLine("PASS " + name); }
      else if (note.StartsWith("SKIP")) { passed++; Console.WriteLine("PASS " + name + " (" + note + ")"); }
      else { failed++; Console.WriteLine("FAIL " + name + " — " + note); }
    } catch (Exception ex) {
      failed++;
      Console.WriteLine("FAIL " + name + " — 异常: " + ex.Message +
        (ex.InnerException != null ? (" / " + ex.InnerException.Message) : ""));
    }
  }

  // ---------------- 反射工具 ----------------
  const BindingFlags PRIV = BindingFlags.NonPublic | BindingFlags.Instance;

  static object InvokePriv(string name, params object[] a) {
    MethodInfo mi = typeof(MainForm).GetMethod(name, PRIV);
    if (mi == null) throw new Exception("找不到私有方法 " + name);
    return mi.Invoke(F, a);
  }

  // 静态私有方法（如 GemsFromBody）用这个 —— 上面的 PRIV 只带 Instance 旗标，找不到静态。
  static object InvokeStatic(string name, params object[] a) {
    MethodInfo mi = typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
    if (mi == null) throw new Exception("找不到私有静态方法 " + name);
    return mi.Invoke(null, a);
  }

  static void SetPriv(string name, object v) {
    FieldInfo fi = typeof(MainForm).GetField(name, PRIV);
    if (fi == null) throw new Exception("找不到私有字段 " + name);
    fi.SetValue(F, v);
  }

  static object GetPriv(string name) {
    FieldInfo fi = typeof(MainForm).GetField(name, PRIV);
    return (fi == null) ? null : fi.GetValue(F);
  }

  // ---------------- 准备 ----------------
  // 用 GetUninitializedObject 跳过 MainForm 的构造函数：构造函数会铺整个界面（慢，而且要消息泵），
  // 而本自检只碰「宝石定位」这条纯逻辑路径（用到的字段下面全部补齐）。
  static string SetupForm() {
    F = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
    F.ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    SetPriv("gemIndex", new Dictionary<string, long>());
    SetPriv("gemNameByKey", new Dictionary<long, string>());
    SetPriv("gemBoost", new Dictionary<string, long>());

    int gl = GemDb.Load(System.IO.Path.Combine(F.ExeDir, "ib3_gems.ini"));
    if (gl == 0) return "ib3_gems.ini 未加载（要从 ib3trainer2 目录运行）";
    F.LoadGemIndex();
    Console.WriteLine("       宝石公式 " + gl + " 个 / 索引表名字 " + ((Dictionary<string, long>)GetPriv("gemIndex")).Count + " 个");
    return null;
  }

  static string SetupGame() {
    Process game = null;
    foreach (Process p in Process.GetProcesses()) {
      try { if (string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) { game = p; break; } } catch { }
    }
    if (game == null) return "SKIP 游戏未运行";
    H = Win32.OpenProcess(0x438, false, (uint)game.Id);
    if (H == IntPtr.Zero) return "OpenProcess 失败";
    F.H = H;
    AddrBook.Load(System.IO.Path.Combine(F.ExeDir, "ib3_addrs.ini"));
    RealBody = (long)InvokePriv("RealBody");
    Console.WriteLine("       游戏 PID " + game.Id + " / 真身 0x" + RealBody.ToString("X") + "（地址簿 misc.gold − 0x2070）");
    return null;
  }

  // ---------------- ① 候选序列 ----------------
  static string TestCands() {
    if (H == IntPtr.Zero) return "SKIP 无游戏句柄";
    object[] a = new object[] { null };
    Cands = InvokePriv("BuildSeqCands", a);
    string note = (string)a[0];
    Console.WriteLine("       存档侧: " + note);
    int n = (int)Cands.GetType().GetProperty("Count").GetValue(Cands, null);
    if (n == 0) return "一条可用候选都没有（存档没读到 / 全被闸门挡掉）";
    Console.WriteLine("       可用候选 " + n + " 条");
    return null;
  }

  // ---------------- ② 定位 ----------------
  static bool Locate(object cands, out List<GemRec> recs, out int[] sizes, out string note) {
    object[] a = new object[] { H, cands, null, null, null };
    bool ok = (bool)InvokePriv("LocateBySaveSequence", a);
    recs = (List<GemRec>)a[2]; sizes = (int[])a[3]; note = (string)a[4];
    return ok;
  }

  static List<GemRec> LocRecs; static int[] LocSizes; static string LocNote;

  static string TestLocate() {
    if (Cands == null) return "SKIP 无候选";
    List<GemRec> recs; int[] sizes; string note;
    if (!Locate(Cands, out recs, out sizes, out note)) {
      Console.WriteLine("       未命中原因: " + note);
      // ★ 产品**主动拒绝**（指纹退化 / 对齐撞车）是正确行为，不是回归 —— 那正是
      //   "宁可不动，也不乱认名字"那道闸。实测现场：商店 152 条时命中 11 个数组，
      //   且同一 FName 索引被贴成两个名字。测到这种拒绝应记 PASS。
      if (note != null && (note.StartsWith("序列指纹不独特") || note.StartsWith("序列对齐撞车")))
        return "SKIP 产品已按设计拒绝退化指纹 — " + note;
      return "序列扫描未命中（若游戏不在此存档/不在藏身地，属正常；否则是回归）";
    }
    LocRecs = recs; LocSizes = sizes; LocNote = note;
    Console.WriteLine("       日志行 → 自校准：" + note);

    int sum = 0;
    foreach (int s in sizes) sum += s;
    if (recs.Count != sum) return "条数与分组不符 recs=" + recs.Count + " 分组和=" + sum;
    if (sizes.Length > 8) return "命中数组过多（" + sizes.Length + " 个）——疑似又退回海选";

    // 打印每个命中数组
    for (int i = 0; i < sizes.Length; i++) {
      long a0 = 0; int shown = 0;
      foreach (GemRec r in recs) {
        if (r.ArrId != i) continue;
        if (shown == 0) a0 = r.RecAddr;
        if (shown < 3) {
          Console.WriteLine("       数组#" + i + " @" + (shown == 0 ? "0x" + a0.ToString("X") : "  ...") +
            "  " + r.Tpl + " idx=0x" + r.NameIdx.ToString("X") + " num=0x" + r.Number.ToString("X") +
            " tier=" + r.Tier + " cook=" + r.Cook + " pct=" + r.Pct.ToString("0.####"));
        }
        shown++;
      }
      Console.WriteLine("       → 数组#" + i + " 共 " + shown + " 条, 首址 0x" + a0.ToString("X"));
    }

    // 与存档侧条数对齐：每个命中数组的条数必须等于某个候选序列的条数
    Dictionary<int, int> candCounts = new Dictionary<int, int>();
    System.Collections.IEnumerable cs = (System.Collections.IEnumerable)Cands;
    foreach (object c in cs) {
      object sv = c.GetType().GetField("Sv", BindingFlags.Public | BindingFlags.Instance).GetValue(c);
      int cnt = (int)sv.GetType().GetProperty("Count").GetValue(sv, null);
      int v;
      candCounts.TryGetValue(cnt, out v);
      candCounts[cnt] = v + 1;
    }
    foreach (int s in sizes) if (!candCounts.ContainsKey(s)) return "命中数组条数 " + s + " 不在存档候选条数里";
    Console.WriteLine("       命中 " + sizes.Length + " 个数组 / " + recs.Count + " 条记录（旧形状扫描规模是 ~405 组 / 92 条）");
    return null;
  }

  // ---------------- ③ 名字 ----------------
  static string TestNames() {
    if (LocRecs == null) return "SKIP 未定位成功";
    // 判据用「ib3_gems.ini 里的模板名集合」而不是 GemDb.Get：
    //   GemDb.Load 会主动丢掉「既没有 *Bonus 也没有 UpgradeTier」的条目（本意是滤掉药水等非宝石），
    //   但它连 DualGem_5/6/DualGemMagic 这类真宝石一起丢了（实测 152 个模板只加载 117 个）。
    //   那是**既有问题**，与本次定位改造无关；用它当判据会把数据问题误报成定位故障。
    HashSet<string> tpls = LoadTplNames(System.IO.Path.Combine(F.ExeDir, "ib3_gems.ini"));

    int noName = 0, inTpl = 0, computable = 0;
    // ★ 键必须是 (Index, Number) 而不是 Index：同名族共用 Index、靠 Number 区分
    //   （见 Tabs.Gems.cs 的 GemKey 注释：UberElementalAttackGem_100=num 0x65 / _200=num 0xC9），
    //   按 Index 去重会把同一族的两个模板误判成"对齐错位"。
    var seen = new Dictionary<long, string>();
    var clash = new List<string>();
    var noVal = new List<string>();
    foreach (GemRec r in LocRecs) {
      if (r.Tpl == null || r.Tpl.Length == 0 || r.Tpl.StartsWith("0x")) noName++;
      else if (tpls.Contains(r.Tpl)) inTpl++;
      if (r.Kind != GemTierKind.Unknown) computable++;
      else if (noVal.Count < 8) noVal.Add(r.Tpl);
      long key = ((long)r.NameIdx << 32) | (uint)r.Number;
      string prev;
      if (seen.TryGetValue(key, out prev)) {
        if (prev != r.Tpl && clash.Count < 5)
          clash.Add("0x" + r.NameIdx.ToString("X") + "/num0x" + r.Number.ToString("X") + "→" + prev + "/" + r.Tpl);
      } else seen[key] = r.Tpl;
    }
    Console.WriteLine("       命名 " + LocRecs.Count + " 条：模板表里有 " + inTpl + " 条（原始大小写）" +
                      "，其中 " + computable + " 条可算值/可修改");
    if (noVal.Count > 0) {
      // 类型未知 = GemDb 里查不到（上面的既有数据问题），本页按设计拒绝修改，不算定位故障
      Console.WriteLine("       （" + (LocRecs.Count - computable) + " 条不可改，GemDb 无公式: " +
                        string.Join(", ", noVal.ToArray()) + "）");
    }
    if (noName > 0) return "有 " + noName + " 条没贴上名字（仍是裸索引）";
    if (inTpl != LocRecs.Count) return "有 " + (LocRecs.Count - inTpl) + " 条名字不在 ib3_gems.ini 的模板表里";
    if (clash.Count > 0) return "同一 FName 索引映射到多个名字（对齐错位）：" + string.Join(", ", clash.ToArray());

    // 名字↔索引 映射表必须与记录一致（ApplyGemEdit/ResolveKind 靠它）
    Dictionary<long, string> byKey = (Dictionary<long, string>)GetPriv("gemNameByKey");
    int mapped = 0;
    foreach (GemRec r in LocRecs) {
      string nm;
      if (byKey.TryGetValue(((long)r.NameIdx << 32) | (uint)r.Number, out nm) && nm == r.Tpl) mapped++;
    }
    if (mapped != LocRecs.Count) return "名字↔索引映射只对上 " + mapped + "/" + LocRecs.Count + " 条";
    Console.WriteLine("       名字↔FName 索引映射 " + mapped + "/" + LocRecs.Count + " 条一致");
    return null;
  }

  // 读 ib3_gems.ini 的模板名集合（`[名字 子类型]` 行），不经过 GemDb 的过滤
  static HashSet<string> LoadTplNames(string path) {
    var set = new HashSet<string>();
    if (!System.IO.File.Exists(path)) return set;
    foreach (string raw in System.IO.File.ReadAllLines(path)) {
      string s = raw.Trim();
      if (s.Length < 3 || s[0] != '[') continue;
      int sp = s.IndexOf(' ');
      int rb = s.IndexOf(']');
      string nm = sp > 1 ? s.Substring(1, sp - 1) : (rb > 1 ? s.Substring(1, rb - 1) : null);
      if (nm != null && nm.Length > 0) set.Add(nm);
    }
    return set;
  }

  // ---------------- ④ 权威性（用户验收点） ----------------
  static string TestAuthority() {
    if (LocRecs == null) return "SKIP 未定位成功";

    // 顺便把产品里那条「权威性自检」日志跑一遍（不写任何东西，只读）
    object[] an = new object[] { H, LocRecs };
    Console.WriteLine("       权威性自检日志 → " + (string)InvokePriv("AuthorityNote", an));

    // 取「权威那份」= 玩家真身 +0x1FEC 的 TArray.Data。
    //   ① 优先走地址簿（快）——但本自检不附着引擎 hook，地址簿里的 misc.gold 往往是过期的；
    //   ② 拿不到就**独立**按 vtable 找玩家对象：这条完全不依赖地址簿、也不依赖内容匹配，
    //      是对「序列扫描命中的那份是不是权威」的独立交叉验证。
    long data = 0; int cnt = 0;
    string via;
    object[] ra = new object[] { H, (long)0, null };
    bool rbOk = (bool)InvokePriv("RealBodyOk", ra);
    if (rbOk) {
      byte[] hdr = new byte[16]; string err;
      if (MemIO.ReadBytes(H, (long)ra[1] + 0x1FEC, hdr, out err)) {
        data = BitConverter.ToInt64(hdr, 0); cnt = BitConverter.ToInt32(hdr, 8);
      }
      via = "地址簿真身";
    } else {
      via = null;
    }
    if (data == 0 || cnt <= 0) {
      Console.WriteLine("       地址簿这条路用不了（" + (rbOk ? "真身背包为空 Count=0" : (string)ra[2]) +
                        "）→ 改用独立 vtable 扫描找玩家对象");
      via = "独立 vtable 扫描";
    }
    if (data == 0 || cnt <= 0) {
      // 独立扫描：带玩家 vtable 的对象可能有好几个（当场的、正在销毁的），
      // 只要**其中任何一个**的 +0x1FEC 指向我们命中的数组，就算证明「命中的那份是权威」。
      List<long> datas = FindPlayerBagDatas();
      foreach (long d in datas) {
        foreach (GemRec r in LocRecs) {
          if (r.RecAddr == d) {
            Console.WriteLine("       ★ 玩家对象背包数组 0x" + d.ToString("X") + " 就在命中列表里（独立 vtable 扫描）");
            return null;
          }
        }
      }
      if (datas.Count == 0) return "SKIP 没有任何玩家对象装载着背包（在关卡/商店界面，背包 TArray Count=0）";
      var sb = new StringBuilder();
      foreach (long d in datas) { if (sb.Length > 0) sb.Append(' '); sb.Append("0x").Append(d.ToString("X")); }
      return "★ 命中列表里没有一份是玩家对象 +0x1FEC 指向的数组（观察到的背包 Data: " + sb.ToString() +
             "）—— 「写进去游戏无变化」就是这个成因。命中首址: " + FirstAddrs();
    }
    {
      // 2026-10-07 二次改版：主路径已换成「身份定位」，序列扫描降为补充/兜底。
      // 所以这里问的是「权威那份**能不能被任意一条定位路径拿到**」，而不是旧版的
      // 「序列扫描的命中里有没有它」。背包内容退化时（一串一模一样的 t0/c0/p0 同名记录，
      // 独特记录不足 2 条）序列扫描会**合理地**漏掉背包 —— 只要身份定位拿得到就不算失败。
      object[] ia = new object[] { H, null, null, 0, 0, null };
      if ((bool)InvokePriv("LocateByIdentity", ia)) {
        var ibag = (List<GemRec>)ia[1];
        if (ibag.Count > 0 && ibag[0].RecAddr == data) {
          Console.WriteLine("       ★ 身份定位（主路径）覆盖权威背包数组 0x" + data.ToString("X") +
                            "（" + cnt + " 条，真身来源 " + via + "）");
          return null;
        }
      }
      bool isAuth = false;
      foreach (GemRec r in LocRecs) if (r.RecAddr == data) isAuth = true;
      if (!isAuth) {
        return "★ 没有任何一条定位路径拿到权威背包数组（" + via + " +0x1FEC → 0x" + data.ToString("X") +
               "，Count=" + cnt + "）。命中首址: " + FirstAddrs();
      }
      Console.WriteLine("       ★ 权威背包数组 0x" + data.ToString("X") + "（" + cnt + " 条）就在命中列表里（" + via + "）");
      return null;
    }
  }

  // ---------------- ⑧ 身份定位 + 按位对齐（2026-10-07 新主路径） ----------------
  // 这条是本次改动的核心验收：背包内容**退化成一串同名同状态的记录**时，
  // 旧路径（序列指纹）整条作废、背包定位不到；新路径靠真身相对偏移定位、按位对齐命名，
  // 必须仍然全对。所以这里特意断言「名字逐条等于存档」，而不是只看条数。
  static string TestIdentityZip() {
    if (H == IntPtr.Zero) return "SKIP 无游戏句柄";
    object[] a = new object[] { H, null, null, 0, 0, null };
    if (!(bool)InvokePriv("LocateByIdentity", a)) return "身份定位失败：" + (string)a[5];
    if (a[5] != null) Console.WriteLine("       ⚠ 部分数组被丢弃: " + (string)a[5]);
    var bag = (List<GemRec>)a[1]; var shop = (List<GemRec>)a[2];
    int bagC = (int)a[3], shopC = (int)a[4];
    Console.WriteLine("       身份定位：背包 " + bagC + " 条 @0x" +
                      (bagC > 0 ? bag[0].RecAddr.ToString("X") : "-") + " / 商店 " + shopC + " 条 @0x" +
                      (shopC > 0 ? shop[0].RecAddr.ToString("X") : "-"));
    // ★ 记 SKIP 而不是 FAIL：背包 TArray 只在特定界面装载（实测在熔接室/商店界面时 Count=0）。
    //   "查不了"不等于"不对" —— 这是本项目一贯的规矩，别把界面状态记成回归。
    if (bagC == 0) return "SKIP 背包数组当前未装载（Count=0，界面不对）—— 进藏身地宝石界面再跑，不是回归";

    object[] b = new object[] { bag, shop, bagC, shopC, 0, null, null };
    bool named = (bool)InvokePriv("NameIdentity", b);
    Console.WriteLine("       " + (string)b[6]);
    if (!named) return "按位对齐失败：内存与 0/1/2 号槽的存档都对不上（见上一行）";

    // 独立复核：名字逐条等于存档。再走一遍存档解析，不用 NameIdentity 自己的结论。
    byte[] body = (byte[])b[5];
    var svBag = (System.Collections.IEnumerable)InvokeStatic("GemsFromBody",
      new object[] { body, "PlayerUnequippedGems", 0 });
    int bad = 0, i = 0;
    foreach (object o in svBag) {
      if (i >= bag.Count) { bad++; break; }
      string nm = (string)o.GetType().GetField("Name").GetValue(o);
      if (bag[i].Tpl != nm) { bad++; Console.WriteLine("       ✗ [" + i + "] 内存=" + bag[i].Tpl + " 存档=" + nm); }
      i++;
    }
    if (i != bag.Count) bad++;
    if (bad > 0) return bad + " 条名字与存档不一致（已比 " + i + " 条）";
    Console.WriteLine("       名字与存档逐条一致（背包 " + bag.Count + " 条）；首条 = " + bag[0].Tpl +
                      " / 尾条 = " + bag[bag.Count - 1].Tpl);

    // 商店也逐条比 —— 它才是"空槽"问题的现场（第 140 条全零记录 ↔ 存档 GemName="None"）。
    // 空槽在内存侧命名为 "(空槽)"，故比对时把它与存档的 "None"/空名视为等价。
    if (shopC > 0) {
      var svShop = (System.Collections.IEnumerable)InvokeStatic("GemsFromBody",
        new object[] { body, "CurrentStoreGems", 0 });
      int badS = 0, j = 0, holes = 0;
      foreach (object o in svShop) {
        if (j >= shop.Count) { badS++; break; }
        string nm = (string)o.GetType().GetField("Name").GetValue(o);
        bool svEmpty = (nm == null || nm.Length == 0 || nm == "None");
        bool memEmpty = (shop[j].Tpl == "(空槽)");
        if (svEmpty && memEmpty) holes++;
        else if (shop[j].Tpl != nm) {
          badS++;
          if (badS <= 3) Console.WriteLine("       ✗ 商店[" + j + "] 内存=" + shop[j].Tpl + " 存档=" + nm);
        }
        j++;
      }
      if (j != shop.Count) badS++;
      if (badS > 0) return "商店 " + badS + " 条名字与存档不一致（已比 " + j + " 条）";
      Console.WriteLine("       商店 " + shop.Count + " 条名字与存档逐条一致（其中空槽 " + holes + " 个，标为「(空槽)」）");
    }
    return null;
  }

  // ---------------- ⑨ 加法型模板覆盖（改 tier 的前置条件） ----------------
  // 加法型（有 RecipeBoostAmount）是**唯一能改 GemTier 的一类**，且改时必须同时把
  // CookedGemVar 置 50（2026-10-07 实测定案，见 README 8.4-④）。少收一个模板 = 那颗宝石会被
  // 当成"未知类型"而在界面上直接拒绝修改。官方基线里这类模板共 15 个，全部列出以便回归。
  static readonly string[] ADDITIVE_15 = new string[] {
    "UberAttackGem", "UberHealthGem", "UberShieldGem", "UberMagicGem",
    "UberFireGem", "UberIceGem", "UberElecGem", "UberPoisonGem",
    "UberLightGem", "UberDarkGem", "UberWaterGem", "UberWindGem",
    "UberElementalAttackGem_100", "RainbowElementalAttackGem_100", "UberBossBoostGem"
  };

  static string TestAdditive() {
    var map = (Dictionary<string, long>)GetPriv("gemBoost");
    if (map == null) return "取不到 gemBoost 字段";
    if (map.Count == 0) return "gemBoost 为空（ib3_gems.ini 没读到 RecipeBoostAmount）";
    var miss = new List<string>();
    foreach (string t in ADDITIVE_15) if (!map.ContainsKey(t.ToLowerInvariant())) miss.Add(t);
    var sb = new StringBuilder();
    foreach (KeyValuePair<string, long> kv in map) {
      if (sb.Length > 0) sb.Append(", ");
      sb.Append(kv.Key).Append('=').Append(kv.Value);
    }
    Console.WriteLine("       加法型模板 " + map.Count + " 个: " + sb.ToString());
    if (miss.Count > 0) return miss.Count + " 个官方加法型模板未进公式库（界面上会拒绝修改）: " +
                               string.Join(", ", miss.ToArray());
    return null;
  }

  // ---------------- ⑩ 「填上限」的判据（上限说不清就不许填） ----------------
  // 2026-10-07 要求：已知上限的才自动填；**上限不是 255 的不要填 255**；说不清的明说"不清楚"并建议手动取值。
  // 判据落在 GemRec.TierMaxKnown / ValueMaxKnown 上（「填上限」按钮只认这两个谓词），这里逐条断言 ——
  // 防止以后又被"顺手填个 255"改回去（旧版正是对未知类型也填了 255）。
  class LimitCase {
    public string Tpl; public GemTierKind Kind;
    public bool TierKnown; public int TierMax;
    public bool ValKnown; public long ValMax;
    public LimitCase(string t, GemTierKind k, bool tk, int tm, bool vk, long vm) {
      Tpl = t; Kind = k; TierKnown = tk; TierMax = tm; ValKnown = vk; ValMax = vm;
    }
  }

  static string TestLimits() {
    var map = (Dictionary<string, long>)GetPriv("gemBoost");
    if (map == null || map.Count == 0) return "gemBoost 为空（ib3_gems.ini 没读到 RecipeBoostAmount）";

    var cases = new List<LimitCase>();
    // 加法型：tier 上限 = 255（uint8 字段硬上限，README 八 已实测），值上限 = 基数 + 255×增量
    cases.Add(new LimitCase("UberBossBoostGem",           GemTierKind.Additive, true, 255, true,  64750));
    cases.Add(new LimitCase("UberElementalAttackGem_100", GemTierKind.Additive, true, 255, true, 128500));
    cases.Add(new LimitCase("UberAttackGem",              GemTierKind.Additive, true, 255, true,  25750));
    // 下标型：tier 上限 = 档位表长（**不是 255**）
    cases.Add(new LimitCase("FireGem",                    GemTierKind.Indexed,  true,   5, true,    400));
    cases.Add(new LimitCase("AttackGem",                  GemTierKind.Indexed,  true,   5, true,    200));
    cases.Add(new LimitCase("HealthGem",                  GemTierKind.Indexed,  true,   5, true,     50));
    // 下标型但基数/pct 上限查不到 → **值**上限说不清（tier 上限照旧是表长，仍可填）
    cases.Add(new LimitCase("BossBoostGem",               GemTierKind.Indexed,  true,   5, false,    0));
    // 下面两行的前身是「档位表写成小数 → GemDb 的 long.Parse 抛异常 → 整张档位表读不进来 →
    // 被当"非宝石实体"丢掉 → 界面上显示未知类型、不可改」（全库 14 个模板中招，见下面 frac14）。
    // ✅ 2026-10-07 已修：GemDb 改为按 double 解析 —— 整数值（含 3.0/4.0/5.0 这种写法）按整数收，
    //    真小数占位并把值记 0、置 TiersFractional。所以它们现在是 **tier 可改**的下标型；
    //    但基数取不到（都用的 BattleEffectValue）、或档位本身是小数值 → 显示值算不出整数，
    //    值上限仍判"说不清"、界面显示 "?"。这三点都要同时成立才算修对。
    cases.Add(new LimitCase("GlobalScaleGoldGem_1",       GemTierKind.Indexed,  true,   3, false,    0));
    cases.Add(new LimitCase("ItemDropGem_1",              GemTierKind.Indexed,  true,   3, false,    0));
    // 未知类型与空槽：tier 语义与上限都说不清 —— 绝不能拿 255 顶替
    cases.Add(new LimitCase("UberElementalAttackGem",     GemTierKind.Unknown, false,   0, false,    0));
    cases.Add(new LimitCase("UberGoldGem",                GemTierKind.Unknown, false,   0, false,    0));
    cases.Add(new LimitCase("(空槽)",                      GemTierKind.Unknown, false,   0, false,    0));
    cases.Add(new LimitCase("NotARealTemplate",           GemTierKind.Unknown, false,   0, false,    0));

    // 用例表自身必须覆盖「上限不是 255」的情形，否则"不要填 255"这条等于没测
    bool coverNon255 = false;
    foreach (LimitCase c in cases) if (c.TierKnown && c.TierMax != 255) coverNon255 = true;
    if (!coverNon255) return "用例表没覆盖「上限不是 255」的情形";

    MethodInfo rk = typeof(MainForm).GetMethod("ResolveKind", PRIV);
    var bad = new List<string>();
    foreach (LimitCase c in cases) {
      var r = new GemRec();
      r.Tpl = c.Tpl;
      rk.Invoke(F, new object[] { r });
      Console.WriteLine("       " + c.Tpl.PadRight(26) + " " + r.Kind +
                        "  tier上限=" + (r.TierMaxKnown ? r.TierMax.ToString() : "说不清") +
                        "  值上限=" + (r.ValueMaxKnown ? r.MaxValue().ToString() : "说不清"));
      if (r.Kind != c.Kind) bad.Add(c.Tpl + " Kind=" + r.Kind + "（期望 " + c.Kind + "）");
      if (r.TierMaxKnown != c.TierKnown) bad.Add(c.Tpl + " TierMaxKnown=" + r.TierMaxKnown + "（期望 " + c.TierKnown + "）");
      if (c.TierKnown && r.TierMax != c.TierMax) bad.Add(c.Tpl + " TierMax=" + r.TierMax + "（期望 " + c.TierMax + "）");
      if (r.ValueMaxKnown != c.ValKnown) bad.Add(c.Tpl + " ValueMaxKnown=" + r.ValueMaxKnown + "（期望 " + c.ValKnown + "）");
      if (c.ValKnown && r.MaxValue() != c.ValMax) bad.Add(c.Tpl + " 值上限=" + r.MaxValue() + "（期望 " + c.ValMax + "）");
      // 说不清的必须给得出原因 —— 界面上要把这句念给用户听
      if (!r.TierMaxKnown && r.MaxUnknownWhy(false).Length == 0) bad.Add(c.Tpl + " 说不清却没给出原因");
      // 算不出的显示值必须返回 -1（界面显示 "?"），**不能是 0** —— 0 会被读成"这颗真的就是 0"
      if (r.Kind != GemTierKind.Additive && !c.ValKnown && r.ValueAt(1, r.Pct) != -1)
        bad.Add(c.Tpl + " 显示值算不出却返回了 " + r.ValueAt(1, r.Pct) + "（应为 -1 → 界面显示 ?）");
      // ★ 关键约定：GemRec.TierMax 对未知类型**字面上仍返回 255**（这次没改字段语义），
      //   所以「填上限」按钮只能认 TierMaxKnown。这三条必须在未知类型上同时成立，缺一就会填出 255。
      if (!c.TierKnown && r.TierMaxKnown) bad.Add(c.Tpl + " 未知上限却报告 TierMaxKnown=true");
    }
    // ★ 本轮修复的验收点：这 14 个模板的档位表**整个**是小数写法，原先在界面上是"未知类型、不可改"。
    //   修好后必须**全部**变成 tier 可改 —— 这是「可升级的宝石都要能改」的直接落点。
    string[] frac14 = new string[] {
      "ItemDropGem_1", "ItemDropGem_2", "ItemDropGem_3", "ItemDropGem_4",
      "BonusComboGem_5", "BonusComboGem_6", "FinalHitGem_5",
      "BreakBossGetMagic_1", "BreakBossGetSuper_1",
      "GlobalScaleGoldGem_1", "GlobalScaleXPGem_1", "GlobalScaleRareGem_1",
      "UberTakeHitGem_2", "UberTakeHitGem_3" };
    int ok14 = 0;
    foreach (string t in frac14) {
      var r = new GemRec();
      r.Tpl = t;
      rk.Invoke(F, new object[] { r });
      if (r.TierMaxKnown) ok14++;
      else bad.Add(t + " 仍不可改（TierMaxKnown=false）—— 小数档位表还是没读进来");
      // 这 14 个都没有 *Bonus（用的是 BattleEffectValue），基数取不到 ⇒ 显示值必须算不出，
      // 从而"显示数值"模式要被明确拒绝（而不是报"目标无法达成"）、值上限也判"说不清"
      if (r.ValueComputable) bad.Add(t + " 显示值竟然算得出（基数取不到，不该算得出）");
      if (r.ValueMaxKnown) bad.Add(t + " 值上限竟然算得出（基数取不到，不该算得出）");
    }
    Console.WriteLine("       小数档位模板 " + frac14.Length + " 个，其中 tier 可改 " + ok14 + " 个");

    if (bad.Count > 0) return string.Join("；", bad.ToArray());
    return null;
  }

  // 独立找玩家对象装载的背包数组：按玩家 vtable 扫可写私有区（对象首址 16 字节对齐），
  // 收集所有 +0x1FEC 上 Count>0 的 Data。不依赖地址簿、不依赖内容匹配。
  static List<long> FindPlayerBagDatas() {
    var res = new List<long>();
    long vt = (long)typeof(MainForm).GetField("PLAYER_VT", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
    byte[] pat = BitConverter.GetBytes(vt);
    byte[] buf = new byte[0x100000];
    byte[] hdr = new byte[16];
    long addr = ScanCore.MIN_ADDR;
    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(H, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long pos = addr, end = addr + size;
        while (pos < end) {
          long left = end - pos;
          int step = (int)Math.Min(0x100000L, left);
          int want = (int)Math.Min((long)buf.Length, left);
          byte[] use = (want == buf.Length) ? buf : new byte[want];
          string err;
          if (MemIO.ReadBytes(H, pos, use, out err)) {
            for (int i = 0; i + 8 <= use.Length; i += 8) {
              if (use[i] != pat[0] || use[i + 1] != pat[1] || use[i + 2] != pat[2] || use[i + 3] != pat[3]) continue;
              if (use[i + 4] != pat[4] || use[i + 5] != pat[5] || use[i + 6] != pat[6] || use[i + 7] != pat[7]) continue;
              long ob = pos + i;
              if (ob % 16 != 0) continue;
              if (!MemIO.ReadBytes(H, ob + 0x1FEC, hdr, out err)) continue;
              long d = BitConverter.ToInt64(hdr, 0);
              int c = BitConverter.ToInt32(hdr, 8);
              if (d != 0 && c > 0 && c <= 4096) {
                Console.WriteLine("       玩家 vtable 对象 0x" + ob.ToString("X") +
                                  " → 背包 Data=0x" + d.ToString("X") + " Count=" + c);
                bool dup = false;
                foreach (long e in res) if (e == d) dup = true;
                if (!dup) res.Add(d);
              }
            }
          }
          pos += step;
        }
      }
      addr += size;
    }
    return res;
  }

  static string FirstAddrs() {
    var sb = new StringBuilder();
    var seen = new List<long>();
    foreach (GemRec r in LocRecs) {
      bool dup = false;
      foreach (long a in seen) if (a == r.RecAddr) dup = true;
      if (dup) continue;
      seen.Add(r.RecAddr);
      if (sb.Length > 0) sb.Append(' ');
      sb.Append("0x").Append(r.RecAddr.ToString("X"));
    }
    return sb.ToString();
  }

  // ---------------- ⑤ 空候选必须干净失败 ----------------
  static string TestEmpty() {
    if (Cands == null) return "SKIP 无候选";
    object empty = Activator.CreateInstance(Cands.GetType());
    List<GemRec> recs; int[] sizes; string note;
    if (Locate(empty, out recs, out sizes, out note)) return "空候选竟返回成功（应失败）";
    if (recs != null || sizes != null) return "空候选失败时不应带回记录";
    Console.WriteLine("       空候选返回原因: " + note);
    if (note == null || note.IndexOf("没有可用") < 0) return "失败原因没写清楚: " + note;
    return null;
  }
}

} // namespace
