// ============================================================================
// MemTestCli.cs — IB3 内存修改器 命令行实测工具
// 与 GUI（Ib3MemTrainer.cs）共用同一份 ScanCore 扫描引擎 —— 实测的就是交付算法。
//
// 编译（Git Bash）:
//   C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -target:exe -codepage:65001 \
//     -main:Ib3MemTrainer.MemTestCli -r:System.Windows.Forms.dll -r:System.Drawing.dll \
//     -out:memtest.exe Ib3MemTrainer.cs MemTestCli.cs
//
// 用法:
//   memtest.exe info
//   memtest.exe scan <Type> <value> [maxHits]        — 一次性已知值扫描
//   memtest.exe read <hexAddr> <Type>
//   memtest.exe write <hexAddr> <Type> <value>
//   memtest.exe gold <v1> <v2> [writeVal]            — 金币两遍扫描实测（setplayergold 改值）
//   memtest.exe stats <f|i> <s1> <m1> <h1> <a1> <s2> <m2> <h2> <a2>
//                                                    — 属性四维两遍扫描实测
//   memtest.exe snapstats <s1> <m1> <h1> <a1> <s2> <m2> <h2> <a2>
//                                                    — 属性四维 未知初始值(快照)实测
//   memtest.exe level <v1> <v2>                      — 玩家等级两遍扫描实测
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ib3MemTrainer {

static class MemTestCli {
  static IntPtr H = IntPtr.Zero;
  const string SENDCMD = @"E:\ib3_re\sendcmd.exe";

  static void Main(string[] a) {
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    if (a.Length == 0) { Usage(); return; }
    if (!Connect()) { Console.WriteLine("ERR 未找到 IB3 进程或打开句柄失败"); Environment.Exit(1); }
    switch (a[0]) {
      case "info": Info(); break;
      case "scan": Scan(a); break;
      case "check": CheckCmd(a); break;
      case "read": ReadCmd(a); break;
      case "write": WriteCmd(a); break;
      case "dump": Dump(a); break;
      case "scanpat": ScanPatCmd(a); break;
      case "records": Records(a); break;
      case "gold": Gold(a); break;
      case "stats": Stats(a); break;
      case "snapstats": SnapStats(a); break;
      case "snapcmd": SnapCmd(a); break;
      case "level": Level(a); break;
      default: Usage(); break;
    }
  }

  static void Usage() {
    Console.WriteLine("用法:");
    Console.WriteLine("  memtest info");
    Console.WriteLine("  memtest scan <Type> <value> [maxHits]");
    Console.WriteLine("  memtest check <Type> <hexAddr,hexAddr,...>");
    Console.WriteLine("  memtest read <hexAddr> <Type>");
    Console.WriteLine("  memtest write <hexAddr> <Type> <value>");
    Console.WriteLine("  memtest gold <v1> <v2> [writeVal]");
    Console.WriteLine("  memtest stats <f|i> <s1> <m1> <h1> <a1> <s2> <m2> <h2> <a2>");
    Console.WriteLine("  memtest snapstats <s1> <m1> <h1> <a1> <s2> <m2> <h2> <a2>");
    Console.WriteLine("  memtest snapcmd <Type> \"<cmdA>\" \"<cmdB>\" [refs|–]   refs=逗号分隔参照值(对快照筛等于)");
    Console.WriteLine("  memtest level <v1> <v2>");
    Console.WriteLine("  memtest dump <hexAddr> <len>            — hex+ASCII 转储");
    Console.WriteLine("  memtest scanpat <pattern> [maxHits] [maxPrint]");
    Console.WriteLine("      pattern = 十六进制字节，空白可省，?? 为通配字节");
    Console.WriteLine("      例: \"???????? 00000000 05000000 00000000 00000000 00000000\"");
    Console.WriteLine("  memtest records <hexPctAddr> [count] [strideHex]  — 按 24 字节记录回读游走");
    Console.WriteLine("Type = Int8/Int16/Int32/Int64/Float/Double");
  }

  // ---------- dump <hexAddr> <len> ----------
  static void Dump(string[] a) {
    long addr = TypeUtil.ParseInt(a[1]);
    int len = (int)TypeUtil.ParseInt(a[2]);
    byte[] buf = new byte[len];
    int r;
    if (!Win32.ReadProcessMemory(H, (IntPtr)addr, buf, len, out r)) {
      Console.WriteLine("ERR 读取失败 err=" + Marshal.GetLastWin32Error());
      return;
    }
    for (int i = 0; i < r; i += 16) {
      var sb = new StringBuilder();
      sb.Append((addr + i).ToString("X").PadLeft(16, '0')).Append("  ");
      for (int k = 0; k < 16; k++) {
        if (i + k < r) sb.Append(buf[i + k].ToString("X2")).Append(' ');
        else sb.Append("   ");
        if (k == 7) sb.Append(' ');
      }
      sb.Append(" |");
      for (int k = 0; k < 16 && i + k < r; k++) {
        byte c = buf[i + k];
        sb.Append(c >= 32 && c < 127 ? (char)c : '.');
      }
      sb.Append('|');
      Console.WriteLine(sb.ToString());
    }
    Console.Out.Flush();
  }

  // ---------- scanpat <pattern> [maxHits] [maxPrint] ----------
  // pattern: hex bytes, whitespace optional, "??" = wildcard byte
  static void ScanPatCmd(string[] a) {
    string s = a[1].Replace(" ", "").Replace("\t", "").Replace("\n", "");
    int n = s.Length / 2;
    if (s.Length % 2 != 0) { Console.WriteLine("ERR 模式长度须为偶数"); return; }
    byte[] pat = new byte[n];
    bool[] wild = new bool[n];
    for (int i = 0; i < n; i++) {
      string h = s.Substring(i * 2, 2);
      if (h == "??") { wild[i] = true; continue; }
      pat[i] = Convert.ToByte(h, 16);
    }
    int maxHits = a.Length > 2 ? int.Parse(a[2]) : 200;
    int maxPrint = a.Length > 3 ? int.Parse(a[3]) : 60;
    var found = new List<long>();
    long addr = ScanCore.MIN_ADDR;
    var t0 = Environment.TickCount;
    while (addr < ScanCore.MAX_ADDR && found.Count <= maxHits) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(H, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect)) {
        long p = addr, end = addr + size;
        var buf = new byte[0x100000 + n];
        while (p < end && found.Count <= maxHits) {
          int chunk = (int)Math.Min(0x100000, end - p);
          int r;
          if (Win32.ReadProcessMemory(H, (IntPtr)p, buf, chunk, out r) && r >= n) {
            for (int i = 0; i <= r - n; i++) {
              if (!wild[0] && buf[i] != pat[0]) continue;
              bool ok = true;
              for (int k = 1; k < n; k++) { if (!wild[k] && buf[i + k] != pat[k]) { ok = false; break; } }
              if (ok) { found.Add(p + i); if (found.Count > maxHits) break; }
            }
          }
          p += chunk;
        }
      }
      addr += size;
    }
    Console.WriteLine("RESULT scanpat[len=" + n + "] = " + found.Count + " (耗时 " + (Environment.TickCount - t0) + " ms)");
    for (int i = 0; i < Math.Min(maxPrint, found.Count); i++)
      Console.WriteLine("  [" + (i + 1) + "] 0x" + found[i].ToString("X"));
    Console.Out.Flush();
  }

  // ---------- records <hexPctAddr> [count] [strideHex] ----------
  // 宝石记录 = 24 字节：+0x00 FName索引/+0x04 0/+0x08 tier/+0x0C pct/+0x10 0/+0x14 0
  static void Records(string[] a) {
    long pct = TypeUtil.ParseInt(a[1]);
    int count = a.Length > 2 ? int.Parse(a[2]) : 8;
    int stride = a.Length > 3 ? (int)TypeUtil.ParseInt(a[3]) : 0x18;
    long first = pct - 0x0C - (long)count / 2 * stride;
    for (int i = 0; i < count; i++) {
      long rec = first + (long)i * stride;
      byte[] b = new byte[24];
      int r;
      if (!Win32.ReadProcessMemory(H, (IntPtr)rec, b, 24, out r) || r != 24) {
        Console.WriteLine("  0x" + rec.ToString("X") + "  <读取失败>");
        continue;
      }
      long idx = BitConverter.ToInt32(b, 0);
      int tier = BitConverter.ToInt32(b, 4 + 4);
      float p = BitConverter.ToSingle(b, 0x0C);
      Console.WriteLine("  0x" + rec.ToString("X") + (rec + 0x0C == pct ? " *" : "  ")
        + "  nameIdx=0x" + idx.ToString("X") + " tier=" + tier + " pct=" + p.ToString("R")
        + " [+0x10]=" + BitConverter.ToInt32(b, 0x10) + " [+0x14]=" + BitConverter.ToInt32(b, 0x14));
    }
    Console.Out.Flush();
  }

  static bool Connect() {
    uint pid = 0;
    // 优先精确名 IB3（游戏 IB3.exe）——避免匹配到"IB3内存修改器"等其他 IB3* 进程
    foreach (var p in Process.GetProcesses()) {
      try { if (string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) { pid = (uint)p.Id; break; } } catch { }
    }
    if (pid == 0) {
      foreach (var p in Process.GetProcesses()) {
        try {
          // 排除训练器/修改器类进程，否则会 attach 到自己人身上（"IB3训练器2" 也以 IB3 开头）
          string nm = p.ProcessName;
          if (!nm.StartsWith("IB3", StringComparison.OrdinalIgnoreCase)) continue;
          if (nm.IndexOf("修改器", StringComparison.Ordinal) >= 0) continue;
          if (nm.IndexOf("训练器", StringComparison.Ordinal) >= 0) continue;
          pid = (uint)p.Id; break;
        } catch { }
      }
    }
    if (pid == 0) return false;
    H = Win32.OpenProcess(0x438, false, pid);
    Console.WriteLine("PID=" + pid + " 句柄=" + (H != IntPtr.Zero ? "OK" : "失败"));
    return H != IntPtr.Zero;
  }

  static ScanType PT(string s) {
    switch (s.Trim().ToLowerInvariant()) {
      case "int8": case "i8": return ScanType.I8;
      case "int16": case "i16": return ScanType.I16;
      case "int32": case "i32": return ScanType.I32;
      case "int64": case "i64": return ScanType.I64;
      case "float": case "f32": return ScanType.F32;
      case "double": case "f64": return ScanType.F64;
    }
    throw new ArgumentException("未知类型 " + s);
  }

  // 注入一条控制台命令（sendcmd.exe 每个参数 = 一条完整命令）
  static void Cmd(string cmd) {
    Console.WriteLine("CMD> " + cmd + " …");
    try {
      var psi = new ProcessStartInfo(SENDCMD, "\"" + cmd + "\"");
      psi.UseShellExecute = false;
      psi.CreateNoWindow = true;
      psi.RedirectStandardOutput = true;
      var pr = Process.Start(psi);
      string outp = pr.StandardOutput.ReadToEnd();
      pr.WaitForExit(20000);
      Console.WriteLine("CMD< " + outp.Trim());
    } catch (Exception ex) { Console.WriteLine("CMD 失败: " + ex.Message); }
    Thread.Sleep(1500);
  }

  static void PrintHits(string tag, ScanType t, List<ScanHit> hs, int max) {
    Console.WriteLine("RESULT " + tag + " = " + hs.Count);
    for (int i = 0; i < Math.Min(max, hs.Count); i++) {
      Console.WriteLine("  [" + (i + 1) + "] 0x" + hs[i].Addr.ToString("X") + " = " + TypeUtil.Decode(t, hs[i].Prev));
    }
    Console.Out.Flush();
  }

  static void Info() {
    long addr = ScanCore.MIN_ADDR;
    int regionsAll = 0, regionsWP = 0;
    long bytesAll = 0, bytesWP = 0;
    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(H, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect)) { regionsAll++; bytesAll += size; }
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) { regionsWP++; bytesWP += size; }
      addr += size;
    }
    Console.WriteLine("RESULT regions.scannable = " + regionsAll + " (" + (bytesAll / 1048576) + " MB)");
    Console.WriteLine("RESULT regions.writablePrivate = " + regionsWP + " (" + (bytesWP / 1048576) + " MB)");
    Console.WriteLine("RESULT snapshotCap = " + (ScanCore.SNAPSHOT_CAP / 1048576) + " MB");
  }

  static void Scan(string[] a) {
    ScanType t = PT(a[1]);
    byte[] pat = TypeUtil.Encode(t, a[2]);
    int maxHits = a.Length > 3 ? int.Parse(a[3]) : ScanCore.MAX_HITS;
    var t0 = Environment.TickCount;
    var found = ScanCore.FirstScanKnown(H, t, pat, maxHits, delegate { return false; }, null);
    Console.WriteLine("RESULT scan[" + a[1] + " " + a[2] + "] = " + found.Count + " (耗时 " + (Environment.TickCount - t0) + " ms, 上限 " + maxHits + ")");
    int maxPrint = a.Length > 4 ? int.Parse(a[4]) : 60;
    for (int i = 0; i < Math.Min(maxPrint, found.Count); i++) {
      Console.WriteLine("  [" + (i + 1) + "] 0x" + found[i].Addr.ToString("X"));
    }
    Console.Out.Flush();
  }

  // check <Type> <hexAddr,hexAddr,...> — 批量读取指定地址的当前值（诊断/验证用）
  static void CheckCmd(string[] a) {
    ScanType t = PT(a[1]);
    string[] addrs = a[2].Split(',');
    int size = TypeUtil.Size(t);
    foreach (string s in addrs) {
      string h = s.Trim();
      if (h.Length == 0) continue;
      long addr = TypeUtil.ParseInt(h);
      byte[] buf = new byte[size];
      int r;
      if (Win32.ReadProcessMemory(H, (IntPtr)addr, buf, size, out r) && r == size) {
        Console.WriteLine("  " + h + " = " + TypeUtil.Decode(t, buf));
      } else {
        Console.WriteLine("  " + h + " = <读取失败>");
      }
    }
    Console.Out.Flush();
  }

  static void ReadCmd(string[] a) {
    ScanType t = PT(a[2]);
    long addr = TypeUtil.ParseInt(a[1]);
    byte[] buf = new byte[TypeUtil.Size(t)];
    int r;
    if (Win32.ReadProcessMemory(H, (IntPtr)addr, buf, buf.Length, out r) && r == buf.Length) {
      Console.WriteLine("RESULT read 0x" + addr.ToString("X") + " = " + TypeUtil.Decode(t, buf));
    } else {
      Console.WriteLine("ERR 读取失败 err=" + Marshal.GetLastWin32Error());
    }
  }

  static void WriteCmd(string[] a) {
    ScanType t = PT(a[2]);
    long addr = TypeUtil.ParseInt(a[1]);
    byte[] data = TypeUtil.Encode(t, a[3]);
    byte[] before = new byte[data.Length];
    int r;
    bool hasBefore = Win32.ReadProcessMemory(H, (IntPtr)addr, before, before.Length, out r) && r == before.Length;
    int w;
    bool ok = Win32.WriteProcessMemory(H, (IntPtr)addr, data, data.Length, out w);
    Console.WriteLine("RESULT write 0x" + addr.ToString("X") + " ok=" + ok + " 原值=" + (hasBefore ? TypeUtil.Decode(t, before) : "?") + " 新值=" + a[3]);
    byte[] after = new byte[data.Length];
    if (Win32.ReadProcessMemory(H, (IntPtr)addr, after, after.Length, out r) && r == after.Length)
      Console.WriteLine("RESULT readback = " + TypeUtil.Decode(t, after));
  }

  static void Gold(string[] a) {
    string v1 = a[1], v2 = a[2];
    // 第一遍：设独特值 → 全内存扫描
    Cmd("setplayergold " + v1);
    var t0 = Environment.TickCount;
    var first = ScanCore.FirstScanKnown(H, ScanType.I64, TypeUtil.Encode(ScanType.I64, v1), ScanCore.MAX_HITS, delegate { return false; }, null);
    Console.WriteLine("耗时 " + (Environment.TickCount - t0) + " ms");
    PrintHits("gold.first[" + v1 + "]", ScanType.I64, first, 12);
    // 第二遍：改值 → 筛选等于新值
    Cmd("setplayergold " + v2);
    var keep = ScanCore.FilterHits(H, ScanType.I64, first, FilterKind.Equal, TypeUtil.ParseInt(v2), delegate { return false; }, null);
    PrintHits("gold.filtered[" + v2 + "]", ScanType.I64, keep, 12);
    // 可选：写入并回读
    if (a.Length > 3) {
      byte[] nv = TypeUtil.Encode(ScanType.I64, a[3]);
      foreach (var hit in keep) {
        int w;
        Win32.WriteProcessMemory(H, (IntPtr)hit.Addr, nv, nv.Length, out w);
      }
      Thread.Sleep(300);
      foreach (var hit in keep) {
        byte[] b = new byte[8]; int r;
        Win32.ReadProcessMemory(H, (IntPtr)hit.Addr, b, 8, out r);
        Console.WriteLine("  写回验证 0x" + hit.Addr.ToString("X") + " = " + TypeUtil.Decode(ScanType.I64, b));
      }
    }
  }

  static readonly string[] STAT_NAMES = { "攻击", "体力", "护盾", "魔法" }; // 2026-10-06 内存实证：setplayerstats 参数顺序

  static void Stats(string[] a) {
    // a[1]=f|i, a[2..5]=第一组, a[6..9]=第二组
    ScanType t = (a[1] == "i") ? ScanType.I32 : ScanType.F32;
    Cmd("setplayerstats " + a[2] + " " + a[3] + " " + a[4] + " " + a[5]);
    var firsts = new List<ScanHit>[4];
    for (int k = 0; k < 4; k++) {
      var t0 = Environment.TickCount;
      firsts[k] = ScanCore.FirstScanKnown(H, t, TypeUtil.Encode(t, a[2 + k]), ScanCore.MAX_HITS, delegate { return false; }, null);
      Console.WriteLine("耗时 " + (Environment.TickCount - t0) + " ms");
      PrintHits("stats." + STAT_NAMES[k] + ".first[" + a[2 + k] + "]", t, firsts[k], 8);
    }
    Cmd("setplayerstats " + a[6] + " " + a[7] + " " + a[8] + " " + a[9]);
    for (int k = 0; k < 4; k++) {
      var keep = ScanCore.FilterHits(H, t, firsts[k], FilterKind.Equal, TypeUtil.ParseRef(t, a[6 + k]), delegate { return false; }, null);
      PrintHits("stats." + STAT_NAMES[k] + ".filtered[" + a[6 + k] + "]", t, keep, 8);
    }
  }

  static void SnapStats(string[] a) {
    // a[1..4]=旧值组(快照时), a[5..8]=新值组(变化后)；属性=Int32（2026-10-06 实证）
    Cmd("setplayerstats " + a[1] + " " + a[2] + " " + a[3] + " " + a[4]);
    Console.WriteLine("开始快照 (Int32)…");
    long total; bool capped; int skipped;
    var t0 = Environment.TickCount;
    var snap = ScanCore.SnapshotUnknown(H, ScanType.I32, delegate { return false; }, null, out total, out capped, out skipped);
    Console.WriteLine("RESULT snapshot.regions = " + snap.Count + " bytes = " + total + " (" + (total / 1048576) + " MB) capped = " + capped + " skipped = " + skipped + " ms = " + (Environment.TickCount - t0));
    Cmd("setplayerstats " + a[5] + " " + a[6] + " " + a[7] + " " + a[8]);
    for (int k = 0; k < 4; k++) {
      bool cap2;
      var keep = ScanCore.FilterSnapshot(H, ScanType.I32, snap, FilterKind.Equal, TypeUtil.ParseRef(ScanType.I32, a[5 + k]), delegate { return false; }, null, out cap2);
      PrintHits("snapstats." + STAT_NAMES[k] + ".equal[" + a[5 + k] + "]" + (cap2 ? " (CAPPED)" : ""), ScanType.I32, keep, 10);
    }
  }

  // snapcmd <Type> <cmdA> <cmdB> [maxPrint] — 执行cmdA→快照→执行cmdB→列出所有变化地址
  static void SnapCmd(string[] a) {
    ScanType t = PT(a[1]);
    Cmd(a[2]);
    long total; bool capped; int skipped;
    var t0 = Environment.TickCount;
    var snap = ScanCore.SnapshotUnknown(H, t, delegate { return false; }, null, out total, out capped, out skipped);
    Console.WriteLine("RESULT snapshot.regions = " + snap.Count + " bytes = " + total + " (" + (total / 1048576) + " MB) capped = " + capped + " skipped = " + skipped + " ms = " + (Environment.TickCount - t0));
    Console.Out.Flush();
    Cmd(a[3]);
    bool cap2;
    var chg = ScanCore.FilterSnapshot(H, t, snap, FilterKind.Changed, 0, delegate { return false; }, null, out cap2);
    Console.WriteLine("RESULT snapcmd.changed = " + chg.Count + (cap2 ? " (CAPPED)" : ""));
    int np = 30;
    string all = "";
    for (int i = 0; i < Math.Min(np, chg.Count); i++) {
      Console.WriteLine("  [" + (i + 1) + "] 0x" + chg[i].Addr.ToString("X") + " = " + TypeUtil.Decode(t, chg[i].Prev));
      all += "0x" + chg[i].Addr.ToString("X") + ",";
    }
    if (all.Length > 0) Console.WriteLine("ADDRS=" + all.TrimEnd(','));
    // 二次筛选：等 1.5 秒后「未变化」——游戏自身计时器/指针会继续变，被淘汰；
    // 命令触发后「变了一次就稳定」的字段（经验/掌握/技能点等）会留在 stable 集
    Thread.Sleep(1500);
    var stable = ScanCore.FilterHits(H, t, chg, FilterKind.Unchanged, 0, delegate { return false; }, null);
    Console.WriteLine("RESULT snapcmd.stable(1.5s未变化) = " + stable.Count);
    string ad2 = "";
    for (int i = 0; i < Math.Min(60, stable.Count); i++) {
      Console.WriteLine("  [" + (i + 1) + "] 0x" + stable[i].Addr.ToString("X") + " = " + TypeUtil.Decode(t, stable[i].Prev));
      if (ad2.Length < 4000) ad2 += "0x" + stable[i].Addr.ToString("X") + ",";
    }
    if (ad2.Length > 0) Console.WriteLine("STABLEADDRS=" + ad2.TrimEnd(','));
    // 可选：对快照做「等于参照值」筛选（refs 逗号分隔，如 "150,2"；"-" 表示跳过）
    if (a.Length > 4 && a[4] != "-") {
      foreach (string rs in a[4].Split(',')) {
        string r = rs.Trim();
        if (r.Length == 0) continue;
        bool cap3;
        var eq = ScanCore.FilterSnapshot(H, t, snap, FilterKind.Equal, TypeUtil.ParseRef(t, r), delegate { return false; }, null, out cap3);
        PrintHits("snapcmd.equal[" + r + "]" + (cap3 ? " (CAPPED)" : ""), t, eq, 20);
      }
    }
    Console.Out.Flush();
  }

  static void Level(string[] a) {
    Cmd("setplayerlevel " + a[1]);
    var first = ScanCore.FirstScanKnown(H, ScanType.I32, TypeUtil.Encode(ScanType.I32, a[1]), 300000, delegate { return false; }, null);
    PrintHits("level.first[" + a[1] + "]", ScanType.I32, first, 8);
    Cmd("setplayerlevel " + a[2]);
    var keep = ScanCore.FilterHits(H, ScanType.I32, first, FilterKind.Equal, TypeUtil.ParseRef(ScanType.I32, a[2]), delegate { return false; }, null);
    PrintHits("level.filtered[" + a[2] + "]", ScanType.I32, keep, 12);
  }
}

} // namespace
