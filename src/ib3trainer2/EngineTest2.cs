// ============================================================================
// EngineTest2.cs — 训练器2 引擎自检（阶段0 门禁，9/9 才许开发新功能）
// 8 项确定性自检（以本进程为靶，验证扫描/写入引擎正确性）
// + 1 项实况冒烟（游戏运行时：区域量级 / 无 ASLR 断言；未运行则记 SKIP）
//
// 编译: csc -target:exe -main:Ib3Trainer2.EngineTest2 -codepage:65001 -out:enginetest2.exe Ib3Core.cs MemIO.cs EngineTest2.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ib3Trainer2 {

static class EngineTest2 {
  static int passed = 0, failed = 0;

  static int Main(string[] args) {
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    Console.WriteLine("== IB3 训练器2 引擎自检 ==");
    T("typeutil.roundtrip", TypeRoundtrip);
    T("typeutil.parse", ParseTests);
    T("eq.tolerance", EqTests);
    T("region.flags", RegionFlags);
    T("scan.known", ScanKnown);
    T("scan.filter", ScanFilter);
    T("snapshot.diff", SnapshotDiff);
    T("memio.safewrite", SafeWriteTests);
    T("live.smoke", LiveSmoke);
    Console.WriteLine("RESULT enginetest2 = " + passed + "/" + (passed + failed) + (failed == 0 ? " PASS" : " FAIL"));
    return failed == 0 ? 0 : 1;
  }

  static void T(string name, Func<string> f) {
    try {
      string note = f();
      if (note == null) { passed++; Console.WriteLine("PASS " + name); }
      else if (note.StartsWith("SKIP")) { passed++; Console.WriteLine("PASS " + name + " (" + note + ")"); }
      else { failed++; Console.WriteLine("FAIL " + name + " — " + note); }
    } catch (Exception ex) {
      failed++; Console.WriteLine("FAIL " + name + " — 异常: " + ex.Message);
    }
  }

  static IntPtr SelfH() {
    return Win32.OpenProcess(0x438, false, (uint)Process.GetCurrentProcess().Id);
  }

  // 1 — 类型编解码往返
  static string TypeRoundtrip() {
    long v64 = 0x1122334455667788L;
    if (TypeUtil.Decode(ScanType.I64, TypeUtil.Encode(ScanType.I64, "0x1122334455667788")) != v64.ToString()) return "I64 往返失败";
    if (TypeUtil.Decode(ScanType.I32, TypeUtil.Encode(ScanType.I32, "-12345")) != "-12345") return "I32 往返失败";
    if (TypeUtil.Decode(ScanType.I16, TypeUtil.Encode(ScanType.I16, "30000")) != "30000") return "I16 往返失败";
    if (TypeUtil.Decode(ScanType.I8, TypeUtil.Encode(ScanType.I8, "-5")) != "-5") return "I8 往返失败";
    if (TypeUtil.Decode(ScanType.F32, TypeUtil.Encode(ScanType.F32, "3.5")) != "3.5") return "F32 往返失败";
    if (TypeUtil.Decode(ScanType.F64, TypeUtil.Encode(ScanType.F64, "-0.125")) != "-0.125") return "F64 往返失败";
    return null;
  }

  // 2 — 解析（十六进制前缀/负数/空串报错）
  static string ParseTests() {
    if (TypeUtil.ParseInt("0x1F") != 31) return "0x1F != 31";
    if (TypeUtil.ParseInt("-7") != -7) return "-7 解析错";
    if (TypeUtil.ParseInt("42") != 42) return "42 解析错";
    bool threw = false;
    try { TypeUtil.ParseInt(""); } catch (FormatException) { threw = true; }
    if (!threw) return "空串未报错";
    return null;
  }

  // 3 — 浮点相对容差
  static string EqTests() {
    if (!ScanCore.EqTol(ScanType.F32, 1.0, 1.00005)) return "1.0≈1.00005 应相等";
    if (ScanCore.EqTol(ScanType.F32, 1.0, 1.01)) return "1.0 vs 1.01 应不等";
    if (!ScanCore.EqTol(ScanType.I32, 5, 5)) return "整数相等判定错";
    if (!ScanCore.EqTol(ScanType.F64, double.NaN, double.NaN)) return "NaN≈NaN 应相等";
    if (ScanCore.EqTol(ScanType.F32, double.NaN, 1.0)) return "NaN vs 1.0 应不等";
    return null;
  }

  // 4 — 区域标志语义
  static string RegionFlags() {
    if (!ScanCore.RegionOk(0x1000, 0x04)) return "提交+可读 应为可扫";
    if (ScanCore.RegionOk(0x1000, 0x100)) return "Guard 页不应可扫";
    if (ScanCore.RegionOk(0x2000, 0x04)) return "未提交不应可扫";
    if (!ScanCore.RegionWritable(0x04)) return "PAGE_READWRITE 应可写";
    if (ScanCore.RegionWritable(0x02)) return "PAGE_READONLY 不应可写";
    if (!ScanCore.RegionWritable(0x40)) return "PAGE_EXECUTE_READWRITE 应可写";
    // MemIO.WritablePage：同一语义 + 拒绝 MEM_IMAGE
    Win32.MBI m = new Win32.MBI();
    m.State = 0x1000; m.Protect = 0x04; m.Type = 0x20000;
    if (!MemIO.WritablePage(m)) return "私有RW页 应可写";
    m.Type = 0x1000000;
    if (MemIO.WritablePage(m)) return "MEM_IMAGE 不应可写";
    m.Type = 0x20000; m.Protect = 0x104;
    if (MemIO.WritablePage(m)) return "Guard+RW 不应可写";
    return null;
  }

  // 5 — 已知值扫描（本进程固定缓冲区中的独特魔法值）
  static string ScanKnown() {
    IntPtr h = SelfH();
    if (h == IntPtr.Zero) return "OpenProcess 自身失败";
    byte[] buf = null;
    GCHandle gc = GCHandle.Alloc(buf = new byte[8192], GCHandleType.Pinned);
    try {
      long baseA = gc.AddrOfPinnedObject().ToInt64();
      Marshal.WriteInt64((IntPtr)baseA, 0x4C3B2A1908F7E6D5L);
      List<ScanHit> hits = ScanCore.FirstScanKnown(h, ScanType.I64, TypeUtil.Encode(ScanType.I64, "0x4C3B2A1908F7E6D5"), ScanCore.MAX_HITS, null, null);
      bool foundAt = false;
      for (int i = 0; i < hits.Count; i++) if (hits[i].Addr == baseA) foundAt = true;
      if (!foundAt) return "未命中预期地址（hits=" + hits.Count + "）";
    } finally { gc.Free(); Win32.CloseHandle(h); }
    return null;
  }

  // 6 — 命中列表筛选（等于/增大）
  static string ScanFilter() {
    IntPtr h = SelfH();
    if (h == IntPtr.Zero) return "OpenProcess 自身失败";
    GCHandle gc = GCHandle.Alloc(new byte[8192], GCHandleType.Pinned);
    try {
      long baseA = gc.AddrOfPinnedObject().ToInt64();
      Marshal.WriteInt64((IntPtr)baseA, 0x4C3B2A1908F7E6D5L);
      List<ScanHit> hits = ScanCore.FirstScanKnown(h, ScanType.I64, TypeUtil.Encode(ScanType.I64, "0x4C3B2A1908F7E6D5"), ScanCore.MAX_HITS, null, null);
      Marshal.WriteInt64((IntPtr)baseA, 0x4C3B2A1908F7E6D6L);
      List<ScanHit> eq = ScanCore.FilterHitsExact(h, ScanType.I64, hits, FilterKind.Equal, "0x4C3B2A1908F7E6D6", null, null);
      bool has = false; for (int i = 0; i < eq.Count; i++) if (eq[i].Addr == baseA) has = true;
      if (!has) return "Equal 筛选丢失目标（keep=" + eq.Count + "）";
      Marshal.WriteInt64((IntPtr)baseA, 0x4C3B2A1908F7E6D7L);
      List<ScanHit> inc = ScanCore.FilterHitsExact(h, ScanType.I64, eq, FilterKind.Increased, "0", null, null);
      has = false; for (int i = 0; i < inc.Count; i++) if (inc[i].Addr == baseA) has = true;
      if (!has) return "Increased 筛选丢失目标（keep=" + inc.Count + "）";
    } finally { gc.Free(); Win32.CloseHandle(h); }
    return null;
  }

  // 7 — 快照差分（改变检测 + 等于定位）
  static string SnapshotDiff() {
    IntPtr h = SelfH();
    if (h == IntPtr.Zero) return "OpenProcess 自身失败";
    GCHandle gc = GCHandle.Alloc(new byte[8192], GCHandleType.Pinned);
    try {
      long baseA = gc.AddrOfPinnedObject().ToInt64();
      long addrA = baseA + 0x100, addrB = baseA + 0x104;
      Marshal.WriteInt32((IntPtr)addrA, 111);
      Marshal.WriteInt32((IntPtr)addrB, 222);
      long total; bool capped; int skipped;
      var snap = ScanCore.SnapshotUnknown(h, ScanType.I32, null, null, out total, out capped, out skipped);
      if (total < 1048576) return "快照过小 total=" + total;
      Marshal.WriteInt32((IntPtr)addrB, 333);
      bool cap2;
      var eq = ScanCore.FilterSnapshot(h, ScanType.I32, snap, FilterKind.Equal, 333, null, null, out cap2);
      bool hasB = false; for (int i = 0; i < eq.Count; i++) if (eq[i].Addr == addrB) hasB = true;
      if (!hasB) return "快照 Equal 未命中改动槽（eq=" + eq.Count + "）";
      var eq2 = ScanCore.FilterSnapshot(h, ScanType.I32, snap, FilterKind.Equal, 111, null, null, out cap2);
      bool hasA = false; for (int i = 0; i < eq2.Count; i++) if (eq2[i].Addr == addrA) hasA = true;
      if (!hasA) return "快照 Equal 未命中未动槽";
      var chg = ScanCore.FilterSnapshot(h, ScanType.I32, snap, FilterKind.Changed, 0, null, null, out cap2);
      hasB = false; hasA = false;
      for (int i = 0; i < chg.Count; i++) { if (chg[i].Addr == addrB) hasB = true; if (chg[i].Addr == addrA) hasA = true; }
      if (!hasB || hasA) return "Changed 集合判定错（B=" + hasB + " A=" + hasA + "）";
    } finally { gc.Free(); Win32.CloseHandle(h); }
    return null;
  }

  // 8 — SafeWrite：可写页成功 + 映像页拒绝
  static string SafeWriteTests() {
    IntPtr h = SelfH();
    if (h == IntPtr.Zero) return "OpenProcess 自身失败";
    GCHandle gc = GCHandle.Alloc(new byte[8192], GCHandleType.Pinned);
    try {
      long baseA = gc.AddrOfPinnedObject().ToInt64();
      string err;
      if (!MemIO.SafeWrite(h, baseA + 0x300, TypeUtil.Encode(ScanType.I32, "777"), out err)) return "可写页写入失败: " + err;
      string val; MemIO.ReadValue(h, baseA + 0x300, ScanType.I32, out val, out err);
      if (val != "777") return "回读值错: " + val;
      long imgBase = Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64();
      if (MemIO.SafeWrite(h, imgBase, new byte[] { 1, 2, 3, 4 }, out err)) return "映像页写入未被拒绝！（不应发生）";
    } finally { gc.Free(); Win32.CloseHandle(h); }
    return null;
  }

  // 9 — 实况冒烟（游戏运行时的关键前提断言）
  static string LiveSmoke() {
    Process game = null;
    foreach (Process p in Process.GetProcesses()) {
      try { if (string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) { game = p; break; } } catch { }
    }
    if (game == null) return "SKIP 游戏未运行";
    IntPtr h = Win32.OpenProcess(0x438, false, (uint)game.Id);
    if (h == IntPtr.Zero) return "OpenProcess 失败";
    try {
      // 无 ASLR 断言（注入器静态地址的根基）
      long imgBase = game.MainModule.BaseAddress.ToInt64();
      if (imgBase != 0x140000000L) return "映像基址异常 0x" + imgBase.ToString("X") + "（预期 0x140000000，注入器前提被破坏）";
      long addr = ScanCore.MIN_ADDR;
      int regionsAll = 0, regionsWP = 0; long bytesAll = 0, bytesWP = 0;
      long firstWP = 0;
      while (addr < ScanCore.MAX_ADDR) {
        Win32.MBI m;
        if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
        long size = m.RegionSize.ToInt64();
        if (size <= 0) break;
        if (ScanCore.RegionOk(m.State, m.Protect)) { regionsAll++; bytesAll += size; }
        if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
          regionsWP++; bytesWP += size; if (firstWP == 0) firstWP = addr;
        }
        addr += size;
      }
      if (regionsAll < 100) return "可扫区域过少 " + regionsAll;
      if (bytesAll < 300L * 1048576) return "可扫字节过少 " + (bytesAll / 1048576) + "MB";
      if (bytesWP < 100L * 1048576) return "可写私有过少 " + (bytesWP / 1048576) + "MB";
      byte[] probe = new byte[64]; int r;
      if (!Win32.ReadProcessMemory(h, (IntPtr)firstWP, probe, 64, out r) || r != 64) return "首可写区读取失败";
      Console.WriteLine("  live: 可扫 " + (bytesAll / 1048576) + "MB / 可写私有 " + (bytesWP / 1048576) + "MB / 区域 " + regionsAll);
    } finally { Win32.CloseHandle(h); }
    return null;
  }
}

} // namespace
