// ============================================================================
// EngTest.cs — ScanCore 引擎自检（不依赖游戏）
// 对 _selftest\memprobe_target.exe（持有已知 Int64 的探针进程）全流程验证：
//   连接 → 已知值首扫 → 未变化筛选 → 写入 → 等于筛选 → 快照 → 改值 → 快照筛选 → 容差
// 编译: csc -target:exe -codepage:65001 -main:Ib3MemTrainer.EngTest -out:enginetest.exe Ib3MemTrainer.cs EngTest.cs
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Ib3MemTrainer {

static class EngTest {
  static int failures = 0;
  static void Check(string name, bool ok, string detail) {
    Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name + "  " + detail);
    Console.Out.Flush();
    if (!ok) failures++;
  }

  static void Main() {
    try { MainInner(); }
    catch (Exception ex) {
      try { Console.WriteLine("EXCEPTION: " + ex.ToString()); } catch { Console.WriteLine("EXCEPTION(无法格式化)"); }
      Environment.Exit(9);
    }
  }

  static void MainInner() {
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    Console.WriteLine("BOOT enginetest");
    string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_selftest\\memprobe_target.exe");
    Console.WriteLine("target exe = " + exe + " exists=" + File.Exists(exe));
    if (!File.Exists(exe)) { Console.WriteLine("未找到 " + exe); Environment.Exit(2); }

    var psi = new ProcessStartInfo(exe);
    psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
    var proc = Process.Start(psi);
    string pidLine = proc.StandardOutput.ReadLine();
    // 之后用后台线程持续排空输出，避免管道写满阻塞目标进程
    new Thread(delegate () { try { while (proc.StandardOutput.ReadLine() != null) { } } catch { } }) { IsBackground = true }.Start();
    int pid = int.Parse(pidLine.Substring(4).Trim());
    Thread.Sleep(400);
    Console.WriteLine("探针进程 pid=" + pid);

    IntPtr h = Win32.OpenProcess(0x438, false, (uint)pid);
    Check("connect", h != IntPtr.Zero, "handle=" + (h != IntPtr.Zero));
    if (h == IntPtr.Zero) { proc.Kill(); Environment.Exit(1); }

    // 1) 已知值首扫
    var first = ScanCore.FirstScanKnown(h, ScanType.I64, TypeUtil.Encode(ScanType.I64, "999999999"), 30000, delegate { return false; }, null);
    Check("first-scan-999999999", first.Count >= 1, "hits=" + first.Count + " first=0x" + (first.Count > 0 ? first[0].Addr.ToString("X") : "-"));

    // 2) 未变化筛选（此间目标值不变）
    var unchanged = ScanCore.FilterHits(h, ScanType.I64, first, FilterKind.Unchanged, 0, delegate { return false; }, null);
    Check("filter-unchanged", unchanged.Count == first.Count, "hits=" + unchanged.Count);

    // 3) 写新值 123456789 → 等于筛选（应恰好剩写入的那一个）
    long addr = unchanged[0].Addr;
    int w;
    byte[] nv = TypeUtil.Encode(ScanType.I64, "123456789");
    bool wrote = Win32.WriteProcessMemory(h, (IntPtr)addr, nv, nv.Length, out w);
    Thread.Sleep(150);
    var eq = ScanCore.FilterHits(h, ScanType.I64, unchanged, FilterKind.Equal, 123456789, delegate { return false; }, null);
    Check("write+filter-equal", wrote && eq.Count == 1 && eq[0].Addr == addr, "wrote=" + wrote + " hits=" + eq.Count + " addr=0x" + addr.ToString("X"));

    // 4) 快照 → 改值 555555555 → 快照筛选「等于」
    long total; bool capped; int skipped;
    var t0 = Environment.TickCount;
    var snap = ScanCore.SnapshotUnknown(h, ScanType.I64, delegate { return false; }, null, out total, out capped, out skipped);
    Check("snapshot", snap.Count >= 1 && total > 0, "regions=" + snap.Count + " MB=" + (total / 1048576) + " ms=" + (Environment.TickCount - t0) + " capped=" + capped + " skipped=" + skipped);
    byte[] nv2 = TypeUtil.Encode(ScanType.I64, "555555555");
    Win32.WriteProcessMemory(h, (IntPtr)addr, nv2, nv2.Length, out w);
    Thread.Sleep(150);
    bool cap2;
    var feq = ScanCore.FilterSnapshot(h, ScanType.I64, snap, FilterKind.Equal, 555555555, delegate { return false; }, null, out cap2);
    bool found = false;
    foreach (var hit in feq) if (hit.Addr == addr) found = true;
    Check("snapshot-filter-equal", found, "hits=" + feq.Count + " 目标命中=" + found + " capped=" + cap2);

    // 5) 同一快照再筛「变化了」（快照=123456789 时代，当前=555555555）
    var fchg = ScanCore.FilterSnapshot(h, ScanType.I64, snap, FilterKind.Changed, 0, delegate { return false; }, null, out cap2);
    found = false;
    foreach (var hit in fchg) if (hit.Addr == addr) found = true;
    Check("snapshot-filter-changed", found, "hits=" + fchg.Count + " 目标命中=" + found);

    // 6) Float 容差
    Check("float-tolerance", ScanCore.EqTol(ScanType.F32, 1000.00001, 1000.0) && !ScanCore.EqTol(ScanType.F32, 1001.0, 1000.0), "ok");

    // 7) 读回
    byte[] rd = new byte[8]; int r;
    Win32.ReadProcessMemory(h, (IntPtr)addr, rd, 8, out r);
    Check("readback", TypeUtil.Decode(ScanType.I64, rd) == "555555555", "val=" + TypeUtil.Decode(ScanType.I64, rd));

    try { proc.Kill(); } catch { }
    Console.WriteLine(failures == 0 ? "ALL PASS" : (failures + " FAILURES"));
    Environment.Exit(failures == 0 ? 0 : 1);
  }
}

} // namespace
