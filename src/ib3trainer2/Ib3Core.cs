// ============================================================================
// Ib3Core.cs — IB3 训练器 2.0 核心引擎（自包含）
// 移植自 ib3trainer_mem/Ib3MemTrainer.cs (ScanCore/TypeUtil/Win32) + 新原语。
// 纯内存读写；无控制台注入、无窗口激活 API。
//
// 编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//         -target:exe -codepage:65001 <文件表>   （见 build.sh）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Ib3Trainer2 {

// ---------- 扫描类型 ----------
enum ScanType { I8, I16, I32, I64, F32, F64 }

// ---------- 筛选条件 ----------
enum FilterKind { Increased, Decreased, Changed, Unchanged, Equal, Less, Greater }

// ---------- 扫描结果命中 ----------
class ScanHit { public long Addr; public byte[] Prev = new byte[8]; }

// ---------- 未知初始值模式：快照区域 ----------
class SnapRegion { public long Addr; public byte[] Data; }

// ---------- 修改表条目 ----------
class MemEntry {
  public string Desc = "";
  public long Addr;
  public ScanType Type = ScanType.I64;
  public string Val = "0";
  public byte[] Orig = null;   // 首次写入前的原值（撤销用）
}

static class TypeUtil {
  public static int Size(ScanType t) {
    switch (t) {
      case ScanType.I8: return 1;
      case ScanType.I16: return 2;
      case ScanType.I32: return 4;
      case ScanType.I64: return 8;
      case ScanType.F32: return 4;
      case ScanType.F64: return 8;
    }
    return 8;
  }
  public static bool IsFloat(ScanType t) { return t == ScanType.F32 || t == ScanType.F64; }
  public static string Name(ScanType t) {
    switch (t) {
      case ScanType.I8: return "Int8";
      case ScanType.I16: return "Int16";
      case ScanType.I32: return "Int32";
      case ScanType.I64: return "Int64";
      case ScanType.F32: return "Float";
      case ScanType.F64: return "Double";
    }
    return "?";
  }
  // 解析（支持 0x 十六进制前缀与负数）
  public static long ParseInt(string s) {
    s = s.Trim();
    if (s.Length == 0) throw new FormatException("空值");
    bool neg = false;
    if (s[0] == '-') { neg = true; s = s.Substring(1); }
    long v;
    if (s.StartsWith("0x") || s.StartsWith("0X")) v = Convert.ToInt64(s.Substring(2), 16);
    else v = long.Parse(s);
    return neg ? -v : v;
  }
  public static double ParseRef(ScanType t, string s) {
    if (IsFloat(t)) return double.Parse(s.Trim(), CultureInfo.InvariantCulture);
    return (double)ParseInt(s);
  }
  // 值文本 → 字节（小端）
  public static byte[] Encode(ScanType t, string s) {
    switch (t) {
      case ScanType.I8: return new byte[] { unchecked((byte)(sbyte)ParseInt(s)) };
      case ScanType.I16: return BitConverter.GetBytes((short)ParseInt(s));
      case ScanType.I32: return BitConverter.GetBytes((int)ParseInt(s));
      case ScanType.I64: return BitConverter.GetBytes(ParseInt(s));
      case ScanType.F32: return BitConverter.GetBytes(float.Parse(s.Trim(), CultureInfo.InvariantCulture));
      case ScanType.F64: return BitConverter.GetBytes(double.Parse(s.Trim(), CultureInfo.InvariantCulture));
    }
    throw new FormatException("未知类型");
  }
  // 字节 → 值文本
  public static string Decode(ScanType t, byte[] b) { return Decode(t, b, 0); }
  public static string Decode(ScanType t, byte[] b, int off) {
    switch (t) {
      case ScanType.I8: return ((sbyte)b[off]).ToString();
      case ScanType.I16: return BitConverter.ToInt16(b, off).ToString();
      case ScanType.I32: return BitConverter.ToInt32(b, off).ToString();
      case ScanType.I64: return BitConverter.ToInt64(b, off).ToString();
      case ScanType.F32: return BitConverter.ToSingle(b, off).ToString("R");
      case ScanType.F64: return BitConverter.ToDouble(b, off).ToString("R");
    }
    return "?";
  }
  // 字节 → 可比较数值
  public static double ToDouble(ScanType t, byte[] b) { return ToDouble(t, b, 0); }
  public static double ToDouble(ScanType t, byte[] b, int off) {
    switch (t) {
      case ScanType.I8: return (sbyte)b[off];
      case ScanType.I16: return BitConverter.ToInt16(b, off);
      case ScanType.I32: return BitConverter.ToInt32(b, off);
      case ScanType.I64: return BitConverter.ToInt64(b, off);
      case ScanType.F32: return BitConverter.ToSingle(b, off);
      case ScanType.F64: return BitConverter.ToDouble(b, off);
    }
    return 0;
  }
  // 字节 → 精确整数（整数类型无 double 2^53 截断；浮点则截断）
  public static long ToInt64(ScanType t, byte[] b) { return ToInt64(t, b, 0); }
  public static long ToInt64(ScanType t, byte[] b, int off) {
    switch (t) {
      case ScanType.I8: return (sbyte)b[off];
      case ScanType.I16: return BitConverter.ToInt16(b, off);
      case ScanType.I32: return BitConverter.ToInt32(b, off);
      case ScanType.I64: return BitConverter.ToInt64(b, off);
      case ScanType.F32: return (long)BitConverter.ToSingle(b, off);
      case ScanType.F64: return (long)BitConverter.ToDouble(b, off);
    }
    return 0;
  }
}

static class Win32 {
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
  [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
  public static extern bool ReadProcessMemoryPtr(IntPtr h, IntPtr addr, IntPtr buf, int size, out int read);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
  [DllImport("kernel32.dll")]
  public static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);
  [DllImport("kernel32.dll")]
  public static extern bool CloseHandle(IntPtr h);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

  // ---- 注入器用（阶段2；此处仅声明，暂不调用）----
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, IntPtr size, uint allocType, uint protect);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool VirtualFreeEx(IntPtr h, IntPtr addr, IntPtr size, uint freeType);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, IntPtr size, uint newProtect, out uint oldProtect);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool FlushInstructionCache(IntPtr h, IntPtr addr, IntPtr size);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attr, IntPtr stackSize, IntPtr start, IntPtr param, uint flags, out uint tid);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern uint WaitForSingleObject(IntPtr h, uint ms);
  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool GetExitCodeThread(IntPtr h, out uint code);

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int L, T, R, B; }

  // x64 MEMORY_BASIC_INFORMATION64 —— RegionSize 前有 PartitionId（4 字节对齐）
  [StructLayout(LayoutKind.Sequential)]
  public struct MBI {
    public IntPtr BaseAddress, AllocationBase;
    public uint AllocationProtect;
    public uint PartitionId;
    public IntPtr RegionSize;
    public uint State, Protect, Type;
  }
}

// ============================================================================
// 扫描引擎（GUI 与命令行实测工具共用）
// ============================================================================
static class ScanCore {
  public const long MIN_ADDR = 0x10000;
  public const long MAX_ADDR = 0x7FFFFFFEFFFF;
  public const int MAX_HITS = 30000;             // 已知值首扫结果上限
  public const int MATERIALIZE_CAP = 1000000;    // 快照筛选物化上限
  public const long SNAPSHOT_CAP = 2560L * 1024 * 1024; // 快照总量上限(字节)。实测本游戏可写私有区 1637MB，1400MB 会漏区，故上调至 2.5GB

  // 扫描区域过滤：已提交、可读、非 Guard（放宽过滤，防漏活值）
  public static bool RegionOk(uint state, uint protect) {
    return state == 0x1000 && (protect & 0xEE) != 0 && (protect & 0x100) == 0;
  }
  // 可写（快照模式进一步收窄到可写私有区）
  public static bool RegionWritable(uint protect) { return (protect & 0xCC) != 0; }

  // ---------- 已知值首次扫描 ----------
  public static List<ScanHit> FirstScanKnown(IntPtr h, ScanType t, byte[] pat, int maxHits, Func<bool> cancel, Action<long, long> progress) {
    var found = new List<ScanHit>();
    long addr = MIN_ADDR;
    int lastUi = Environment.TickCount;
    while (addr < MAX_ADDR) {
      if (cancel != null && cancel()) break;
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (RegionOk(m.State, m.Protect)) {
        long p = addr, end = addr + size;
        byte[] buf = new byte[0x100000 + pat.Length];
        while (p < end) {
          if (cancel != null && cancel()) break;
          int chunk = (int)Math.Min(0x100000, end - p);
          int r;
          if (Win32.ReadProcessMemory(h, (IntPtr)p, buf, chunk, out r) && r >= pat.Length) {
            for (int i = 0; i <= r - pat.Length; i++) {
              if (buf[i] == pat[0]) {
                bool ok = true;
                for (int k = 1; k < pat.Length; k++) if (buf[i + k] != pat[k]) { ok = false; break; }
                if (ok) {
                  var hit = new ScanHit { Addr = p + i };
                  Array.Copy(pat, hit.Prev, pat.Length);
                  found.Add(hit);
                  if (found.Count > maxHits) break;
                }
              }
            }
          }
          if (found.Count > maxHits) break;
          p += chunk;
        }
      }
      if (found.Count > maxHits) break;
      addr += size;
      if (progress != null && Environment.TickCount - lastUi > 80) {
        lastUi = Environment.TickCount;
        progress(addr, MAX_ADDR);
      }
    }
    return found;
  }

  // ---------- 未知初始值：快照 ----------
  public static List<SnapRegion> SnapshotUnknown(IntPtr h, ScanType t, Func<bool> cancel, Action<long, long> progress,
                                                 out long totalBytes, out bool capped, out int skippedRegions) {
    totalBytes = 0; capped = false; skippedRegions = 0;
    var list = new List<SnapRegion>();
    long addr = MIN_ADDR;
    int lastUi = Environment.TickCount;
    while (addr < MAX_ADDR) {
      if (cancel != null && cancel()) break;
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long rsize = m.RegionSize.ToInt64();
      if (rsize <= 0) break;
      // 快照只取：已提交 + 可读 + 可写 + 私有（数据堆）
      if (RegionOk(m.State, m.Protect) && RegionWritable(m.Protect) && m.Type == 0x20000) {
        if (totalBytes + rsize > SNAPSHOT_CAP) { capped = true; skippedRegions++; }
        else {
          byte[] data = null;
          try { data = new byte[rsize]; } catch (OutOfMemoryException) { capped = true; skippedRegions++; }
          if (data != null) {
            bool okAll = true;
            GCHandle gc = GCHandle.Alloc(data, GCHandleType.Pinned);
            try {
              IntPtr baseP = gc.AddrOfPinnedObject();
              long p = 0;
              while (p < rsize) {
                if (cancel != null && cancel()) { okAll = false; break; }
                int chunk = (int)Math.Min(0x400000, rsize - p);
                int r;
                if (!Win32.ReadProcessMemoryPtr(h, (IntPtr)(addr + p), (IntPtr)((long)baseP + p), chunk, out r) || r != chunk) {
                  okAll = false; break;
                }
                p += chunk;
              }
            } finally { gc.Free(); }
            if (okAll) {
              list.Add(new SnapRegion { Addr = addr, Data = data });
              totalBytes += rsize;
            } else skippedRegions++;
          }
        }
      }
      addr += rsize;
      if (progress != null && Environment.TickCount - lastUi > 80) {
        lastUi = Environment.TickCount;
        progress(totalBytes, SNAPSHOT_CAP);
      }
    }
    return list;
  }

  // ---------- 筛选：对已有命中列表（标准两遍扫描第二遍起） ----------
  public static List<ScanHit> FilterHits(IntPtr h, ScanType t, List<ScanHit> src, FilterKind f, double refVal,
                                         Func<bool> cancel, Action<int, int> progress) {
    int size = TypeUtil.Size(t);
    var keep = new List<ScanHit>();
    int done = 0;
    foreach (var hit in src) {
      if (cancel != null && cancel()) break;
      byte[] cur = new byte[size];
      int r;
      if (Win32.ReadProcessMemory(h, (IntPtr)hit.Addr, cur, size, out r) && r == size) {
        if (KeepBytes(t, cur, 0, hit.Prev, 0, f, refVal)) { hit.Prev = cur; keep.Add(hit); }
      }
      done++;
      if (progress != null && (done & 0x3FF) == 0) progress(done, src.Count);
    }
    return keep;
  }

  // ---------- 筛选（精确引用版）：整数引用值按文本全精度解析，浮点走容差 ----------
  public static List<ScanHit> FilterHitsExact(IntPtr h, ScanType t, List<ScanHit> src, FilterKind f, string refText,
                                              Func<bool> cancel, Action<int, int> progress) {
    long rvI = 0; double rvF = 0;
    if (TypeUtil.IsFloat(t)) rvF = TypeUtil.ParseRef(t, refText); else rvI = TypeUtil.ParseInt(refText);
    int size = TypeUtil.Size(t);
    var keep = new List<ScanHit>();
    int done = 0;
    foreach (var hit in src) {
      if (cancel != null && cancel()) break;
      byte[] cur = new byte[size];
      int r;
      if (Win32.ReadProcessMemory(h, (IntPtr)hit.Addr, cur, size, out r) && r == size) {
        if (KeepBytes(t, cur, 0, hit.Prev, 0, f, rvI, rvF)) { hit.Prev = cur; keep.Add(hit); }
      }
      done++;
      if (progress != null && (done & 0x3FF) == 0) progress(done, src.Count);
    }
    return keep;
  }

  // ---------- 筛选：对快照（未知初始值模式，首次筛选） ----------
  public static List<ScanHit> FilterSnapshot(IntPtr h, ScanType t, List<SnapRegion> snap, FilterKind f, double refVal,
                                             Func<bool> cancel, Action<long, long> progress, out bool capped) {
    capped = false;
    int size = TypeUtil.Size(t);
    var keep = new List<ScanHit>();
    byte[] buf = new byte[0x100000];
    long totalBytes = 0, totalAll = 0;
    foreach (var reg in snap) totalAll += reg.Data.Length;
    foreach (var reg in snap) {
      if (cancel != null && cancel()) break;
      long rsize = reg.Data.Length;
      long p = 0;
      while (p < rsize) {
        if (cancel != null && cancel()) break;
        int chunk = (int)Math.Min(buf.Length, rsize - p);
        int r;
        if (Win32.ReadProcessMemory(h, (IntPtr)(reg.Addr + p), buf, chunk, out r) && r >= size) {
          int usable = r - r % size;
          for (int i = 0; i + size <= usable; i += size) {
            if (KeepBytes(t, buf, i, reg.Data, (int)(p + i), f, refVal)) {
              var hit = new ScanHit { Addr = reg.Addr + p + i };
              Array.Copy(buf, i, hit.Prev, 0, size);
              keep.Add(hit);
              if (keep.Count > MATERIALIZE_CAP) { capped = true; return keep; }
            }
          }
        }
        p += chunk;
        totalBytes += chunk;
      }
      if (progress != null) progress(totalBytes, totalAll);
    }
    return keep;
  }

  // ---------- 快照筛选（精确引用版）：整数按文本全精度解析 ----------
  public static List<ScanHit> FilterSnapshotExact(IntPtr h, ScanType t, List<SnapRegion> snap, FilterKind f, string refText,
                                                  Func<bool> cancel, Action<long, long> progress, out bool capped) {
    capped = false;
    long rvI = 0; double rvF = 0;
    if (TypeUtil.IsFloat(t)) rvF = TypeUtil.ParseRef(t, refText); else rvI = TypeUtil.ParseInt(refText);
    int size = TypeUtil.Size(t);
    var keep = new List<ScanHit>();
    byte[] buf = new byte[0x100000];
    long totalBytes = 0, totalAll = 0;
    foreach (var reg in snap) totalAll += reg.Data.Length;
    foreach (var reg in snap) {
      if (cancel != null && cancel()) break;
      long rsize = reg.Data.Length;
      long p = 0;
      while (p < rsize) {
        if (cancel != null && cancel()) break;
        int chunk = (int)Math.Min(buf.Length, rsize - p);
        int r;
        if (Win32.ReadProcessMemory(h, (IntPtr)(reg.Addr + p), buf, chunk, out r) && r >= size) {
          int usable = r - r % size;
          for (int i = 0; i + size <= usable; i += size) {
            if (KeepBytes(t, buf, i, reg.Data, (int)(p + i), f, rvI, rvF)) {
              var hit = new ScanHit { Addr = reg.Addr + p + i };
              Array.Copy(buf, i, hit.Prev, 0, size);
              keep.Add(hit);
              if (keep.Count > MATERIALIZE_CAP) { capped = true; return keep; }
            }
          }
        }
        p += chunk;
        totalBytes += chunk;
      }
      if (progress != null) progress(totalBytes, totalAll);
    }
    return keep;
  }

  // ---------- 比较（Float/Double 带相对容差） ----------
  public static bool EqTol(ScanType t, double a, double b) {
    if (TypeUtil.IsFloat(t)) {
      if (double.IsNaN(a) || double.IsNaN(b)) return double.IsNaN(a) && double.IsNaN(b);
      double tol = 1e-4 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
      return Math.Abs(a - b) <= tol;
    }
    return a == b;
  }
  public static bool Keep(ScanType t, double cv, double pv, FilterKind f, double refVal) {
    switch (f) {
      case FilterKind.Increased: return cv > pv;
      case FilterKind.Decreased: return cv < pv;
      case FilterKind.Changed: return !EqTol(t, cv, pv);
      case FilterKind.Unchanged: return EqTol(t, cv, pv);
      case FilterKind.Equal: return EqTol(t, cv, refVal);
      case FilterKind.Less: return cv < refVal;
      case FilterKind.Greater: return cv > refVal;
    }
    return false;
  }
  // 字节级比较：整数类型走精确 long（避免 >2^53 的 I64 在 double 下截断而漏判）；
  // 浮点类型仍走 EqTol 相对容差。低于 2^53 的整数行为与 Keep(double) 完全一致。
  public static bool KeepBytes(ScanType t, byte[] a, int aOff, byte[] b, int bOff, FilterKind f, double refVal) {
    return KeepBytes(t, a, aOff, b, bOff, f, (long)refVal, refVal);
  }
  // refInt 供整数类型（精确 long），refFloat 供浮点类型
  public static bool KeepBytes(ScanType t, byte[] a, int aOff, byte[] b, int bOff, FilterKind f, long refInt, double refFloat) {
    if (TypeUtil.IsFloat(t)) return Keep(t, TypeUtil.ToDouble(t, a, aOff), TypeUtil.ToDouble(t, b, bOff), f, refFloat);
    long cv = TypeUtil.ToInt64(t, a, aOff), pv = TypeUtil.ToInt64(t, b, bOff);
    switch (f) {
      case FilterKind.Increased: return cv > pv;
      case FilterKind.Decreased: return cv < pv;
      case FilterKind.Changed: return cv != pv;
      case FilterKind.Unchanged: return cv == pv;
      case FilterKind.Equal: return cv == refInt;
      case FilterKind.Less: return cv < refInt;
      case FilterKind.Greater: return cv > refInt;
    }
    return false;
  }
}

} // namespace
