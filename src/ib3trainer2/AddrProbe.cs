// ============================================================================
// AddrProbe.cs — 只读地址探针（诊断工具：不注入、不写目标进程内存、不碰窗口）
//
// 用途：验证「四项基本属性是否就是玩家真身对象里的固定偏移」这一推断。
//   ib3_addrs.ini 里记的是堆地址（会搬家），而文档 2026-10-06 记的真身是
//   0x7FF4F65F0040、当时四项属性在 0x7FF4F65F1F50 起 —— 恰好是 真身+0x1F10。
//   若该偏移成立，四项属性就能像金币一样「每次附着自动绑定」，不必依赖地址簿，
//   这才真正可迁移到别人的机器。
//
// 用法：
//   addrprobe.exe                          列出真身候选 + 打印 +0x1F10 附近 Int32
//   addrprobe.exe <体力> <护盾> <攻击> <魔法>
//        → 在全部可写私有区搜这 4 个连续 Int32，直接告出真实地址与相对真身的偏移
//
// 编译：sh build.sh probe     输出 addrprobe.exe（控制台）
// 结果同时打印到控制台并写入 addrprobe.txt（UTF-8），便于直接发回分析。
//
// 权限：只用 PROCESS_QUERY_INFORMATION | PROCESS_VM_READ（0x410），
//       不含 VM_WRITE —— 本工具在操作系统权限层面也不可能改写游戏内存。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ib3Trainer2 {

static class AddrProbe {
  const uint ACCESS_READONLY = 0x0410;   // QUERY_INFORMATION | VM_READ（故意不含 VM_WRITE）
  const long IMG_PREF = 0x140000000L;
  // 与 EngineCall 一致的类 vtable（映像内地址 → 基址 + RVA）
  const long RVA_VT_PLAYER = 0xB66BE0;   // 玩家对象系（金币 +0x2070 / 筹码 +0x2094 所在）
  const long RVA_VT_SWORDPC = 0xB5FF70;

  const long OFF_GOLD = 0x2070;          // I64
  const long OFF_CHIP = 0x2094;          // I32
  const long OFF_STATS = 0x1F10;         // 待验证：体力 / 护盾 / 攻击 / 魔法 四个连续 I32

  static readonly StringBuilder OUT = new StringBuilder();
  static void W(string s) { try { Console.WriteLine(s); } catch { } OUT.AppendLine(s); }

  static void Main(string[] args) {
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    try { Run(args); } catch (Exception ex) { W("异常: " + ex); }
    try {
      File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addrprobe.txt"),
                        OUT.ToString(), new UTF8Encoding(false));
    } catch { }
  }

  static void Run(string[] args) {
    Process proc = null;
    foreach (Process p in Process.GetProcesses()) {
      try { if (string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) { proc = p; break; } } catch { }
    }
    if (proc == null) { W("未找到 IB3 进程 —— 请先启动游戏并进入存档。"); return; }

    IntPtr h = Win32.OpenProcess(ACCESS_READONLY, false, (uint)proc.Id);
    if (h == IntPtr.Zero) { W("OpenProcess(只读) 失败 err=" + Marshal.GetLastWin32Error()); return; }

    long imgBase = IMG_PREF;
    try { imgBase = proc.MainModule.BaseAddress.ToInt64(); } catch { }
    W("IB3 PID=" + proc.Id + "  映像基址=0x" + imgBase.ToString("X") +
      (imgBase != IMG_PREF ? "  (已重定位)" : "  (与首选一致)"));

    long vtPlayer = imgBase + RVA_VT_PLAYER;
    long vtSwordPC = imgBase + RVA_VT_SWORDPC;

    // ---- ① 定位玩家真身候选 ----
    List<long> cand = new List<long>();
    ScanVtable(h, vtPlayer, cand, 64);
    W("玩家类 vtable 0x" + vtPlayer.ToString("X") + " 命中实例 " + cand.Count + " 个");

    List<long> ranked = new List<long>(cand);
    ranked.Sort(delegate(long a, long b) {
      int sa = Score(h, a), sb = Score(h, b);
      if (sa != sb) return sb - sa;
      return a < b ? -1 : (a > b ? 1 : 0);
    });

    long best = 0;
    W("");
    W("---- 候选（按 金币/筹码 真身分降序）----");
    for (int i = 0; i < ranked.Count && i < 6; i++) {
      long o = ranked[i];
      W(string.Format("  {0}. 0x{1:X}  金币={2}  筹码={3}  分={4}",
                      i + 1, o, ReadI64(h, o + OFF_GOLD), ReadI32(h, o + OFF_CHIP), Score(h, o)));
      if (i == 0) best = o;
    }

    List<long> spc = new List<long>();
    ScanVtable(h, vtSwordPC, spc, 32);
    W("（参考）SwordPC 系 vtable 命中 " + spc.Count + " 个");

    if (best == 0) { W("没有可用的真身候选 —— 请确认游戏已进入存档、不是停在标题/加载界面。"); return; }

    // ---- 前后快照对比（加点追踪）----
    if (args != null && args.Length >= 1 && args[0] == "snap") { DoSnap(h, best); return; }
    if (args != null && args.Length >= 1 && args[0] == "cmp") { DoCmp(h, best); return; }
    if (args != null && args.Length >= 2 && args[0] == "near") { DoNear(h, best, args); return; }
    if (args != null && args.Length >= 2 && args[0] == "ctx") { DoCtx(h, best, args); return; }

    // ---- ② 打印真身里 +0x1EE0 附近，肉眼核对 ----
    W("");
    W("---- 真身 0x" + best.ToString("X") + " 内 +0x1EE0 .. +0x1F40 的 Int32 ----");
    for (long off = 0x1EE0; off <= 0x1F40; off += 0x10) {
      StringBuilder sb = new StringBuilder();
      sb.Append(string.Format("  +0x{0:X4}: ", off));
      for (int k = 0; k < 4; k++) sb.Append(string.Format("{0,12}", ReadI32(h, best + off + k * 4)));
      W(sb.ToString());
    }
    W("");
    W(string.Format("  按推断偏移 +0x{0:X} 读四维 = {1} / {2} / {3} / {4}",
        OFF_STATS, ReadI32(h, best + OFF_STATS), ReadI32(h, best + OFF_STATS + 4),
        ReadI32(h, best + OFF_STATS + 8), ReadI32(h, best + OFF_STATS + 12)));

    // ---- ③ 已知值搜索：直接找出真实位置 ----
    long[] want = ParseFour(args);
    if (want == null) {
      W("");
      W("提示：带上游戏内的四维数值重跑，可直接定位真实偏移，例如");
      W("      addrprobe.exe 60 45 50 20      （体力 护盾 攻击 魔法）");
      return;
    }

    byte[] pat = new byte[16];
    for (int i = 0; i < 4; i++) BitConverter.GetBytes((int)want[i]).CopyTo(pat, i * 4);
    W("");
    W(string.Format("---- 全内存搜索连续 Int32 [{0}, {1}, {2}, {3}] ----", want[0], want[1], want[2], want[3]));
    List<long> hits = ScanBytes(h, pat, 64);
    if (hits.Count == 0) {
      W("  0 条命中。可能原因：数值在读档/换装后被游戏重算（请用界面上刚显示的值），");
      W("  或四项并非连续排列（那就需要逐项搜）。");
      return;
    }
    W("  命中 " + hits.Count + " 条：");
    for (int i = 0; i < hits.Count; i++) {
      long a = hits[i];
      W(string.Format("    0x{0:X}   相对真身 {1}0x{2:X}",
                      a, (a >= best ? "+" : "-"), (a >= best ? a - best : best - a)));
    }
    W("");
    W("判读：若命中里出现「真身+0x1F10」，四项属性即为真身内的固定偏移 —— 推断成立，");
    W("      可改成随金币一起自动绑定，新用户不再需要地址簿。");
  }

  // ============ 前后快照对比：加点追踪 ============
  // 快照范围取「真身之前 0x20000 ~ 之后 0x80000」：四项属性若确实是真身内字段，
  // 或只是紧邻的同批分配（UE3 堆块常连号），都能覆盖到。
  const long SNAP_BACK = 0x20000;
  const long SNAP_FWD = 0x80000;

  static string SnapPath {
    get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addrprobe_snap.bin"); }
  }

  static void DoSnap(IntPtr h, long best) {
    long start = best - SNAP_BACK;
    int len = (int)(SNAP_BACK + SNAP_FWD);
    byte[] buf = new byte[len];
    int got = ReadChunked(h, start, buf, len);
    // 头部记录基准地址，供 cmp 校验是同一次会话（真身搬家后对比就没意义了）
    byte[] head = BitConverter.GetBytes(best);
    byte[] outp = new byte[8 + len];
    Array.Copy(head, 0, outp, 0, 8);
    Array.Copy(buf, 0, outp, 8, len);
    File.WriteAllBytes(SnapPath, outp);
    W("");
    W(string.Format("已快照：真身 0x{0:X} 范围 -0x{1:X} .. +0x{2:X}（读到 {3}/{4} 字节）",
        best, SNAP_BACK, SNAP_FWD, got, len));
    W(string.Format("  当前 +0x{0:X} 四维 = {1} / {2} / {3} / {4}",
        OFF_STATS, ReadI32(h, best + OFF_STATS), ReadI32(h, best + OFF_STATS + 4),
        ReadI32(h, best + OFF_STATS + 8), ReadI32(h, best + OFF_STATS + 12)));
    W("  → 现在去游戏里加点，完了运行：  addrprobe.exe cmp");
  }

  static void DoCmp(IntPtr h, long best) {
    if (!File.Exists(SnapPath)) { W("没有快照文件，请先运行： addrprobe.exe snap"); return; }
    byte[] outp = File.ReadAllBytes(SnapPath);
    long baseAddr = BitConverter.ToInt64(outp, 0);
    int len = outp.Length - 8;
    W("");
    if (baseAddr != best) {
      W(string.Format("注意：真身已从 0x{0:X} 变为 0x{1:X}（换了存档/重开过游戏）—— 本次对比无效，请重新 snap。",
          baseAddr, best));
      return;
    }
    byte[] now = new byte[len];
    ReadChunked(h, baseAddr - SNAP_BACK, now, len);

    W(string.Format("对比基准 0x{0:X}（-0x{1:X} .. +0x{2:X}）", baseAddr, SNAP_BACK, SNAP_FWD));
    W(string.Format("  现在 +0x{0:X} 四维 = {1} / {2} / {3} / {4}",
        OFF_STATS, ReadI32(h, best + OFF_STATS), ReadI32(h, best + OFF_STATS + 4),
        ReadI32(h, best + OFF_STATS + 8), ReadI32(h, best + OFF_STATS + 12)));
    W("");
    W("---- 发生变化的 Int32（偏移相对真身）----");
    int shown = 0, total = 0;
    for (int i = 0; i + 4 <= len; i += 4) {
      long o = baseAddr - SNAP_BACK + i;
      int a = BitConverter.ToInt32(outp, 8 + i), b = BitConverter.ToInt32(now, i);
      if (a == b) continue;
      total++;
      // 只报「数值发生实质变化」的项：排除只读到一半导致的 0 抖动
      if (a == 0 && b == 0) continue;
      if (shown < 120) {
        long rel = o - baseAddr;
        W(string.Format("  {0}0x{1:X}   {2}  ->  {3}",
            (rel >= 0 ? "+" : "-"), (rel >= 0 ? rel : -rel), a, b));
        shown++;
      }
    }
    W("  共 " + total + " 处变化" + (total > shown ? "（只列了前 " + shown + " 条）" : ""));
    W("");
    W("判读：加了 1 点技能点后，应能看到「技能点 -1」和「某一维 +1」两处变化。");
    W(string.Format("      若其中一条落在 +0x{0:X}（四维区），则「四维 = 真身 + 0x{0:X} 起的连续 Int32」成立。",
        OFF_STATS));
  }

  // ============ 真身对象内定向查找 ============
  // 四项属性已验证就在真身对象里（+0x1F10）。等级/技能点/生命若也在同一对象内，
  // 只需在对象本体（512KB）里搜一次就能得到固定偏移 —— 比全内存搜索精确得多。
  static void DoNear(IntPtr h, long best, string[] args) {
    int len = (int)SNAP_FWD;
    byte[] buf = new byte[len];
    int got = ReadChunked(h, best, buf, len);
    W("");
    W(string.Format("---- 在真身对象 0x{0:X} 内查找（读到 {1}/{2} 字节）----", best, got, len));
    // 诊断：分块缓冲 vs 直接读，定位「读到的数据是否整体偏移/缺失」
    W(string.Format("  诊断 buf[0]=0x{0:X8}(直接读 best=0x{1:X8})  buf[0x1F10]={2}(直接读={3})  buf[0x20000]={4}(直接读={5})",
        BitConverter.ToInt32(buf, 0), ReadI32(h, best),
        BitConverter.ToInt32(buf, 0x1F10), ReadI32(h, best + 0x1F10),
        BitConverter.ToInt32(buf, 0x20000), ReadI32(h, best + 0x20000)));
    for (int ai = 1; ai < args.Length; ai++) {
      long v;
      if (!long.TryParse(args[ai].Trim(), out v)) continue;
      byte[] pat = BitConverter.GetBytes((int)v);
      StringBuilder sb = new StringBuilder();
      int n = 0;
      for (int i = 0; i + 4 <= len; i += 4) {
        if (buf[i] != pat[0] || buf[i + 1] != pat[1] || buf[i + 2] != pat[2] || buf[i + 3] != pat[3]) continue;
        n++;
        if (n <= 20) sb.Append(string.Format("  +0x{0:X}", i));
      }
      string extra = "";
      if (n == 0) {
        // 对象内没有 → 顺便报一下全内存有多少处，判断「是不是压根不在玩家对象里」
        int all = ScanBytes(h, pat, 4000).Count;
        extra = "   全内存命中 " + all + (all >= 4000 ? "+（值太常见，需换更独特的值）" : "");
      }
      W(string.Format("  值 {0,-12} 真身内命中 {1} 处{2}{3}",
          v, n, n > 0 ? ":" + sb.ToString() : "", extra));
    }
    W("  判读：命中数少且偏移稳定 → 该字段可作为固定偏移接入自动绑定。");
  }

  // ============ 命中处上下文 ============
  // 同一数值在对象内常有多处命中，靠「左右邻居长什么样」判断哪个才是真字段
  // （例如等级旁边常跟着经验值，技能点旁边常跟着已用点数/属性数组）。
  static void DoCtx(IntPtr h, long best, string[] args) {
    int len = (int)SNAP_FWD;
    byte[] buf = new byte[len];
    ReadChunked(h, best, buf, len);
    for (int ai = 1; ai < args.Length; ai++) {
      long v;
      if (!long.TryParse(args[ai].Trim(), out v)) continue;
      byte[] pat = BitConverter.GetBytes((int)v);
      W("");
      W("值 " + v + " 的命中上下文（[] 内为命中项，左右各 4 个 Int32）：");
      for (int i = 0; i + 4 <= len; i += 4) {
        if (buf[i] != pat[0] || buf[i + 1] != pat[1] || buf[i + 2] != pat[2] || buf[i + 3] != pat[3]) continue;
        StringBuilder sb = new StringBuilder();
        sb.Append(string.Format("  +0x{0:X6}:", i));
        for (int k = -4; k <= 4; k++) {
          int o = i + k * 4;
          if (o < 0 || o + 4 > len) continue;
          sb.Append(k == 0
            ? string.Format("[{0,11}]", BitConverter.ToInt32(buf, o))
            : string.Format("{0,12}", BitConverter.ToInt32(buf, o)));
        }
        W(sb.ToString());
      }
    }
  }

  // 分块读。
  //
  // 刻意用 byte[] 版 ReadProcessMemory + 临时缓冲 + Array.Copy：
  // 之前用固定指针版（ReadProcessMemoryPtr）时，第一块恒定读失败被填零，
  // 导致缓冲区开头 64KB 全是 0 —— 「真身内查找」因此漏掉了 +0x1F10（它在 7952 处），
  // 快照对比也没能列出该偏移的变化。byte[] 版与全内存扫描（ScanBytes）走的是同一条
  // 已验证可用的路径，故改用它，避免再被同一个坑绊住。
  // 块大小取 4KB：ReadProcessMemory 只要请求区间内【任意一页】不可读就整块失败，
  // 64KB 的块会让开头那段（含 +0x1F10）被整体填零，从而漏掉真实存在的字段。
  // 4KB 块与页面对齐，跨区时最多损失一页，不会吞掉整段。
  static readonly byte[] RCBuf = new byte[0x1000];
  static int ReadChunked(IntPtr h, long start, byte[] buf, int len) {
    int total = 0, done = 0;
    while (done < len) {
      int chunk = Math.Min(RCBuf.Length, len - done);
      int r;
      if (Win32.ReadProcessMemory(h, (IntPtr)(start + done), RCBuf, chunk, out r) && r > 0) {
        Array.Copy(RCBuf, 0, buf, done, r);
        if (r < chunk) Array.Clear(buf, done + r, chunk - r);   // 撞到区域边界
        total += r;
      } else {
        Array.Clear(buf, done, chunk);
      }
      done += chunk;
    }
    return total;
  }

  static long[] ParseFour(string[] args) {
    if (args == null || args.Length < 4) return null;
    long[] r = new long[4];
    for (int i = 0; i < 4; i++) {
      long v;
      if (!long.TryParse(args[i].Trim(), out v)) return null;
      r[i] = v;
    }
    return r;
  }

  // ---- 真身分（本工具自己的简化版）：金币/筹码非 0（影子实例为 0）。
     //      训练器主体已改为更强的一套（EngineCall.BodyScore：背包装载 4 / 商店装载 4 / 金币 2 / 筹码 1），
     //      本探针保持简单版即可 —— 它只做只读比对，不参与选真身。 ----
  static int Score(IntPtr h, long o) {
    int s = 0;
    long g = ReadI64(h, o + OFF_GOLD);
    long c = ReadI32(h, o + OFF_CHIP);
    if (g > 0 && g < 1000000000000000L) s += 2;
    if (c > 0 && c < 1000000000000000L) s += 1;
    return s;
  }

  static long ReadI32(IntPtr h, long a) {
    byte[] b = new byte[4]; int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)a, b, 4, out r) && r == 4) return BitConverter.ToInt32(b, 0);
    return long.MinValue;
  }
  static long ReadI64(IntPtr h, long a) {
    byte[] b = new byte[8]; int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)a, b, 8, out r) && r == 8) return BitConverter.ToInt64(b, 0);
    return long.MinValue;
  }
  static bool ShapeOk(IntPtr h, long vt) {
    for (int k = 0; k < 8; k++) {
      long v = ReadI64(h, vt + k * 8);
      if (v < 0x140001000L || v >= 0x1408DC000L) return false;
    }
    return true;
  }

  // 在可写私有区按 8 字节对齐扫 vtable 指针
  static void ScanVtable(IntPtr h, long vt, List<long> into, int cap) {
    long addr = ScanCore.MIN_ADDR;
    while (addr < ScanCore.MAX_ADDR && into.Count < cap) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long p = addr, end = addr + size;
        byte[] buf = new byte[0x100000];
        while (p < end && into.Count < cap) {
          int chunk = (int)Math.Min(0x100000, end - p);
          int r;
          if (Win32.ReadProcessMemory(h, (IntPtr)p, buf, chunk, out r) && r >= 8) {
            for (int i = 0; i + 8 <= r; i += 8) {
              if (BitConverter.ToInt64(buf, i) == vt && ShapeOk(h, vt)) {
                if (!into.Contains(p + i)) into.Add(p + i);
                if (into.Count >= cap) break;
              }
            }
          }
          p += chunk;
        }
      }
      addr += size;
    }
  }

  // 全可写私有区搜字节模式（按 Int32 对齐）
  static List<long> ScanBytes(IntPtr h, byte[] pat, int cap) {
    List<long> hits = new List<long>();
    long addr = ScanCore.MIN_ADDR;
    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long p = addr, end = addr + size;
        byte[] buf = new byte[0x100000 + pat.Length];
        while (p < end) {
          int chunk = (int)Math.Min(0x100000, end - p);
          int r;
          if (Win32.ReadProcessMemory(h, (IntPtr)p, buf, chunk, out r) && r >= pat.Length) {
            for (int i = 0; i + pat.Length <= r; i += 4) {
              if (buf[i] != pat[0]) continue;
              bool ok = true;
              for (int k = 1; k < pat.Length; k++) if (buf[i + k] != pat[k]) { ok = false; break; }
              if (ok) { hits.Add(p + i); if (hits.Count >= cap) return hits; }
            }
          }
          p += chunk;
        }
      }
      addr += size;
    }
    return hits;
  }
}

} // namespace
