// ============================================================================
// IB3 内存修改器 v2 — Infinity Blade III PC 移植版 直接读写游戏内存
// 独立于控制台注入版（IB3快捷修改器.exe），不改动控制台、不抢焦点、即时生效。
//
// v2 新增（2026-10-06）：
//   - ScanCore 扫描引擎抽离（GUI 与命令行实测工具 memtest.exe 共用同一份代码）
//   - 未知初始值(快照)扫描：数值留空 = 全内存快照 → 改变后按 增大/减小/变化/等于 筛选
//     （适用 HP / 掌握经验等不直接显示数值的目标）
//   - 目标向导下拉框：金币/属性四维/等级/属性点/掌握经验/HP 的定位流程提示
//   - Float/Double 比较带相对容差（等于/未变化 不再因浮点误差漏值）
//   - 撤销写入（恢复原值）
//
// 已验证主线（来自 ZCode 记忆）：
//   - 金币 = 64 位整数，属性名 "Current"（999999999 全链路成功案例）
//   - OpenProcess 用 0x438（QUERY|VM_READ|VM_WRITE|VM_OPERATION）
//   - x64 MBI 结构 RegionSize 前有 4 字节 PartitionId 对齐（错位 = 扫描全 0）
//   - 放宽 Protect 过滤：State==MEM_COMMIT && (Protect & 0xEE)!=0 && !PAGE_GUARD
//   - IB3.exe 未开 ASLR（DllCharacteristics=0x8100），但堆地址每次运行仍会变
//
// 编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//         /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll Ib3MemTrainer.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ib3MemTrainer {

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
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

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
        double cv = TypeUtil.ToDouble(t, cur), pv = TypeUtil.ToDouble(t, hit.Prev);
        if (Keep(t, cv, pv, f, refVal)) { hit.Prev = cur; keep.Add(hit); }
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
            double cv = TypeUtil.ToDouble(t, buf, i);
            double pv = TypeUtil.ToDouble(t, reg.Data, (int)(p + i));
            if (Keep(t, cv, pv, f, refVal)) {
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
}

// ============================================================================
// 目标向导
// ============================================================================
class Guide {
  public string Name, Hint, Log;
  public ScanType Type;
  public bool Snapshot;   // true=引导用未知初始值(快照)模式
  public Guide(string n, ScanType t, bool snap, string hint, string log) { Name = n; Type = t; Snapshot = snap; Hint = hint; Log = log; }
}

// ============================================================================
// 主窗口
// ============================================================================
class MainForm : Form {
  // ---------- 进程 ----------
  uint _pid = 0;
  string _procName = "";
  IntPtr _h = IntPtr.Zero;

  // ---------- 扫描状态 ----------
  List<ScanHit> hits = new List<ScanHit>();
  ScanType _hitsType = ScanType.I64;        // 本次结果对应的类型（修 v1 切换类型导致解码错位）
  List<SnapRegion> _snap = null;            // 非空 = 未知初始值模式快照待筛选
  ScanType _snapType = ScanType.F32;
  volatile bool _cancel = false;
  bool _scanning = false;

  // ---------- 控件 ----------
  Label lblProc; Button btnConnect;
  ComboBox cboGuide; Label lblGuideHint;
  ComboBox cboType, cboFilter; TextBox txtValue, txtRef;
  Button btnFirst, btnNext, btnReset, btnStop;
  ProgressBar prog; Label lblProg, lblCount;
  ListView lvRes; Button btnAddSel;
  ListView lvMod;
  TextBox txtDesc, txtAddr, txtModVal; ComboBox cboModType;
  Button btnWrite, btnReadCur, btnDel, btnRevert, btnSavePreset, btnLoadPreset;
  System.Windows.Forms.Timer freezeTimer;
  TextBox txtLog;

  static readonly Guide[] GUIDES = new Guide[] {
    new Guide("自定义（手动）", ScanType.I64, false,
      "自行选择类型并填当前值 → 首次扫描",
      "自定义模式：选类型 + 填当前值 → 首次扫描；游戏内改变它后 → 再次扫描（变化了/等于）收敛。"),
    new Guide("金币（Int64，已验证）", ScanType.I64, false,
      "改变方式：控制台 setplayergold <数额> 或买卖",
      "金币=Int64 已验证全链路。填当前金币 → 首次扫描 → 游戏内改金币（控制台 setplayergold <值>）→ 再次扫描选「等于 新值」→ 加入修改列表写 999999999。"),
    new Guide("属性四维（Int32，已验证）", ScanType.I32, false,
      "改变方式：控制台 setplayerstats <攻击> <体力> <护盾> <魔法>",
      "属性四维=PawnStat，Int32 存储（2026-10-06 内存实证）。★控制台参数顺序=攻击、体力、护盾、魔法；内存里四字段连续排列（体力,护盾,攻击,魔法）。流程：setplayerstats 设 4 个独特整数值 → 首次扫描某个值 → 再设新值 → 「等于 新值」收敛（一般剩 1~2 个地址）。★用整数参数（小数会被截断）；附近还有一组「加成后」副本（如 F40），写基础值那组最稳。"),
    new Guide("玩家等级（Int32，已验证）", ScanType.I32, false,
      "改变方式：控制台 setplayerlevel <等级>；或直接改内存",
      "玩家等级=Int32 直接存储（2026-10-06 实证：字段在 0x7FF4E8F6xxxx 族，写 42 → 界面显示 42 ✓）。定位法：setplayerlevel 大值（如 334）→ 扫该值 → 换值（335）→ 差分。小值难扫，务必用大而独特的等级做探针。"),
    new Guide("属性点/技能点（Int32，已验证）", ScanType.I32, false,
      "改变方式：升级/掌握获得；或直接改内存",
      "技能点=Int32（2026-10-06 实证：写 8888 → 界面显示 8888 ✓；字段在等级字段同族、相距 0x178C）。定位法：setplayerlevel 造大数（如 2000→3794 点）→ 扫该数 → 再升一级 → 差分找「变成0或新值」的那个。注意：升级命令会重算覆盖该字段，改完勿再触发升级/掌握事件。"),
    new Guide("部位掌握经验（Int32，已验证）", ScanType.I32, false,
      "读界面经验上限 R(x/R) → 扫 R → 灌经验 → 重扫差分",
      "部位掌握经验=Int32（2026-10-06 全链路实证）。★giveweaponxp 类只填满当前锻造上限不能突破（实证）；提上限需游戏内锻造或 masterallowneditems。定位流：界面读该部位经验上限 R（如 0/240）→ 首次扫描 Int32 R → 灌该部位经验（givehelmetxp 等）→ 再次扫描 → 新增地址（0x7FF4 玩家堆族）= 经验字段。物品结构：间距 0x2C，[+8]=当前经验（可直写：满=已掌握、小=显示分数）、[+0xC]=数量、**[+0x10]=等级（0 基！显示=值+1，免锻造提级且属性真实生效）、[+0x14]=已掌握标志（0/2）**。战斗场景噪声大，优先本流程而非快照。"),
    new Guide("物品等级（免锻造提级，已验证）", ScanType.I32, false,
      "物品结构 +0x10（0基：显示等级=值+1）直接写",
      "物品等级=Int32 @物品结构+0x10，0 基存储（2026-10-06 实证：写 7 → 显示\"等级8\"，且装备属性真实生效！）。快速定位：先用「部位掌握经验」流程找到该物品经验字段（+8），等级字段= 经验地址 + 8。同结构：[+0xC]=数量、[+0x14]=已掌握标志(0/2)。同一运行内物品结构以 0x2C 间距连续排列（装备在栈：武器→盾→盔甲→头盔→戒指→仓库）。"),
    new Guide("玩家 HP（Int32，已验证）", ScanType.I32, false,
      "改变方式：控制台 setplayerhealth <值>，或战斗中挨打",
      "玩家 HP=Int32（2026-10-06 实证：写入 2222222 游戏内显示确认）。控制台 setplayerhealth <值> 改当前 HP、setplayermaxhealth 改上限；两遍扫描「等于新值」收敛（活地址示例 0x7FF4E8DD03B8，另有一份副本）。战斗场景快照噪声大，优先用已知值两遍扫描。"),
  };

  const int SNAPSHOT_MATERIALIZE_CAP = ScanCore.MATERIALIZE_CAP;

  public MainForm() {
    Text = "IB3 内存修改器 v2";
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(960, 800);

    lblProc = new Label();
    lblProc.SetBounds(12, 12, 600, 20);
    lblProc.Text = "进程: 未连接";
    lblProc.ForeColor = Color.Firebrick;
    Controls.Add(lblProc);

    btnConnect = new Button();
    btnConnect.SetBounds(750, 9, 150, 26);
    btnConnect.Text = "连接 IB3 进程";
    btnConnect.Click += delegate { ConnectProc(); };
    Controls.Add(btnConnect);

    BuildScanBox();
    BuildResultBox();
    BuildModBox();

    txtLog = new TextBox();
    txtLog.SetBounds(12, 694, 936, 96);
    txtLog.Multiline = true;
    txtLog.ReadOnly = true;
    txtLog.ScrollBars = ScrollBars.Vertical;
    txtLog.Font = new Font("Consolas", 9);
    Controls.Add(txtLog);

    freezeTimer = new System.Windows.Forms.Timer();
    freezeTimer.Interval = 500;
    freezeTimer.Tick += delegate { FreezeTick(); };
    freezeTimer.Start();

    Load += delegate {
      LoadPresetSilent(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mem_presets.csv"));
      ConnectProc();
      Log("v2：目标向导 + 未知初始值(快照)扫描 + Float 容差 + 撤销写入。两种模式：①已知当前值→两遍扫描；②数值留空→快照→变化后筛选。");
    };
  }

  // ================= 控件构建 =================
  Label MkLabel(string t, int x, int y, int w) { Label l = new Label(); l.SetBounds(x, y, w, 20); l.Text = t; return l; }
  Label MkGray(string t, int x, int y, int w) { Label l = MkLabel(t, x, y, w); l.ForeColor = Color.Gray; return l; }
  TextBox MkText(int x, int y, int w, string init) { TextBox t = new TextBox(); t.SetBounds(x, y, w, 23); t.Text = init; return t; }
  Button MkBtn(string t, int x, int y, int w, EventHandler h) { Button b = new Button(); b.SetBounds(x, y, w, 30); b.Text = t; b.Click += h; return b; }
  Button MkBtn26(string t, int x, int y, int w, EventHandler h) { Button b = new Button(); b.SetBounds(x, y, w, 26); b.Text = t; b.Click += h; return b; }

  void BuildScanBox() {
    GroupBox g = new GroupBox(); g.SetBounds(12, 42, 936, 134); g.Text = "内存扫描";

    g.Controls.Add(MkLabel("目标", 15, 26, 36));
    cboGuide = new ComboBox(); cboGuide.SetBounds(55, 22, 300, 23); cboGuide.DropDownStyle = ComboBoxStyle.DropDownList;
    foreach (var gd in GUIDES) cboGuide.Items.Add(gd.Name);
    cboGuide.SelectedIndex = 0;
    cboGuide.SelectedIndexChanged += delegate { ApplyGuide(); };
    g.Controls.Add(cboGuide);
    lblGuideHint = MkGray("", 365, 26, 558);
    lblGuideHint.AutoEllipsis = true;
    lblGuideHint.Text = GUIDES[0].Hint;
    g.Controls.Add(lblGuideHint);

    g.Controls.Add(MkLabel("类型", 15, 58, 40));
    cboType = new ComboBox(); cboType.SetBounds(55, 54, 90, 23); cboType.DropDownStyle = ComboBoxStyle.DropDownList;
    cboType.Items.AddRange(new object[] { "Int64(8B)", "Int32(4B)", "Int16(2B)", "Int8(1B)", "Float(4B)", "Double(8B)" });
    cboType.SelectedIndex = 0;
    g.Controls.Add(cboType);

    g.Controls.Add(MkLabel("数值", 155, 58, 40));
    txtValue = MkText(195, 54, 110, "999999999");
    g.Controls.Add(txtValue);
    btnFirst = MkBtn26("首次扫描", 310, 52, 90, delegate { FirstScan(); });
    g.Controls.Add(btnFirst);
    g.Controls.Add(MkGray("留空 = 未知初始值(快照)模式", 405, 58, 190));

    cboFilter = new ComboBox(); cboFilter.SetBounds(15, 86, 120, 23); cboFilter.DropDownStyle = ComboBoxStyle.DropDownList;
    cboFilter.Items.AddRange(new object[] { "增大了", "减小了", "变化了", "未变化", "等于", "小于", "大于" });
    cboFilter.SelectedIndex = 2;
    g.Controls.Add(cboFilter);
    txtRef = MkText(145, 86, 130, "0");
    g.Controls.Add(txtRef);
    btnNext = MkBtn26("再次扫描", 285, 84, 100, delegate { NextScan(); });
    g.Controls.Add(btnNext);
    btnReset = MkBtn26("新扫描", 395, 84, 90, delegate { ResetScan(); });
    g.Controls.Add(btnReset);
    btnStop = MkBtn26("停止", 495, 84, 80, delegate { _cancel = true; Log("已请求停止…"); });
    g.Controls.Add(btnStop);

    prog = new ProgressBar(); prog.SetBounds(15, 118, 620, 14); prog.Minimum = 0; prog.Maximum = 1000;
    g.Controls.Add(prog);
    lblProg = MkLabel("空闲", 645, 116, 280);
    g.Controls.Add(lblProg);

    Controls.Add(g);
  }

  void BuildResultBox() {
    GroupBox g = new GroupBox(); g.SetBounds(12, 182, 936, 236); g.Text = "扫描结果（双击行或选中后点按钮加入修改列表）";
    lvRes = new ListView();
    lvRes.SetBounds(15, 24, 660, 178);
    lvRes.View = View.Details; lvRes.FullRowSelect = true; lvRes.MultiSelect = true;
    lvRes.Columns.Add("地址", 180);
    lvRes.Columns.Add("当前值", 220);
    lvRes.Columns.Add("类型", 120);
    lvRes.DoubleClick += delegate { AddSelToMod(); };
    g.Controls.Add(lvRes);

    btnAddSel = MkBtn("添加到修改列表", 690, 30, 150, delegate { AddSelToMod(); });
    g.Controls.Add(btnAddSel);
    lblCount = MkLabel("结果: 0", 690, 70, 200);
    g.Controls.Add(lblCount);
    Label tip = MkLabel("提示: 首次扫描结果过多属正常，改值后\n用「再次扫描」筛到个位数再添加。\n快照模式: 数值留空→首次扫描→\n游戏内改变→再次扫描筛选。", 690, 96, 230);
    tip.ForeColor = Color.Gray;
    tip.AutoSize = true;
    tip.MaximumSize = new Size(230, 0);
    g.Controls.Add(tip);
    Controls.Add(g);
  }

  void BuildModBox() {
    GroupBox g = new GroupBox(); g.SetBounds(12, 424, 936, 264); g.Text = "修改与锁定（勾选行的复选框 = 每 0.5s 回写一次实现冻结）";
    lvMod = new ListView();
    lvMod.SetBounds(15, 24, 560, 200);
    lvMod.View = View.Details; lvMod.FullRowSelect = true; lvMod.MultiSelect = false;
    lvMod.CheckBoxes = true;
    lvMod.Columns.Add("描述", 150);
    lvMod.Columns.Add("地址", 120);
    lvMod.Columns.Add("类型", 70);
    lvMod.Columns.Add("写入值", 100);
    lvMod.Columns.Add("当前值", 100);
    lvMod.SelectedIndexChanged += delegate { LoadEntryToEditor(); };
    g.Controls.Add(lvMod);

    g.Controls.Add(MkLabel("描述", 590, 24, 40));
    txtDesc = MkText(630, 21, 150, "");
    g.Controls.Add(txtDesc);
    g.Controls.Add(MkLabel("地址(hex)", 590, 54, 60));
    txtAddr = MkText(650, 51, 130, "");
    g.Controls.Add(txtAddr);
    g.Controls.Add(MkLabel("类型", 590, 84, 40));
    cboModType = new ComboBox(); cboModType.SetBounds(630, 80, 90, 23); cboModType.DropDownStyle = ComboBoxStyle.DropDownList;
    cboModType.Items.AddRange(new object[] { "Int64", "Int32", "Int16", "Int8", "Float", "Double" });
    cboModType.SelectedIndex = 0;
    g.Controls.Add(cboModType);
    g.Controls.Add(MkLabel("写入值", 730, 84, 50));
    txtModVal = MkText(780, 80, 90, "");
    g.Controls.Add(txtModVal);

    btnWrite = MkBtn("更新并写入", 590, 114, 130, delegate { ApplyEntry(); });
    g.Controls.Add(btnWrite);
    btnReadCur = MkBtn("读取当前值", 725, 114, 130, delegate { ReadCurValues(); });
    g.Controls.Add(btnReadCur);
    btnDel = MkBtn("删除选中", 590, 150, 130, delegate { DeleteEntry(); });
    g.Controls.Add(btnDel);
    btnRevert = MkBtn("撤销写入(恢复原值)", 725, 150, 130, delegate { RevertEntry(); });
    g.Controls.Add(btnRevert);

    btnSavePreset = MkBtn("保存预设", 590, 196, 130, delegate { SavePreset(); });
    g.Controls.Add(btnSavePreset);
    btnLoadPreset = MkBtn("载入预设", 725, 196, 130, delegate { LoadPreset(); });
    g.Controls.Add(btnLoadPreset);
    Controls.Add(g);
  }

  // ================= 目标向导 =================
  void ApplyGuide() {
    int i = cboGuide.SelectedIndex;
    if (i < 0 || i >= GUIDES.Length) return;
    Guide g = GUIDES[i];
    cboType.SelectedIndex = TypeIndex(g.Type);
    lblGuideHint.Text = g.Hint;
    if (g.Snapshot) {
      txtValue.Text = "";
      Log("[向导] " + g.Name + " → 未知初始值(快照)模式（数值留空）。");
    } else {
      if (txtValue.Text.Trim().Length == 0) txtValue.Text = "0";
      Log("[向导] " + g.Name + " → 类型=" + TypeUtil.Name(g.Type) + "。");
    }
    Log("[向导] " + g.Log);
  }

  static int TypeIndex(ScanType t) {
    switch (t) {
      case ScanType.I64: return 0;
      case ScanType.I32: return 1;
      case ScanType.I16: return 2;
      case ScanType.I8: return 3;
      case ScanType.F32: return 4;
      default: return 5;
    }
  }

  // ================= 进程连接 =================
  void ConnectProc() {
    CloseHandleIfAny();
    _pid = 0; _procName = "";
    uint me = (uint)Process.GetCurrentProcess().Id;
    // 优先精确名 IB3（游戏 IB3.exe）；必须排除自身（本工具名 IB3内存修改器，否则会连上自己）
    foreach (var p in Process.GetProcesses()) {
      try {
        if (p.Id != me && string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) { _pid = (uint)p.Id; _procName = p.ProcessName; break; }
      } catch { }
    }
    if (_pid == 0) {
      foreach (var p in Process.GetProcesses()) {
        try {
          if (p.Id != me && p.ProcessName.StartsWith("IB3", StringComparison.OrdinalIgnoreCase) && p.ProcessName.IndexOf("修改器", StringComparison.Ordinal) < 0) {
            _pid = (uint)p.Id; _procName = p.ProcessName; break;
          }
        } catch { }
      }
    }
    if (_pid == 0) {
      // 回退：窗口标题
      Win32.EnumWindows(delegate(IntPtr h, IntPtr l) {
        if (!Win32.IsWindowVisible(h)) return true;
        StringBuilder sb = new StringBuilder(256);
        Win32.GetWindowText(h, sb, 256);
        if (sb.ToString().StartsWith("Infinity Blade III")) {
          uint p; Win32.GetWindowThreadProcessId(h, out p);
          _pid = p; _procName = "IB3(窗口)"; return false;
        }
        return true;
      }, IntPtr.Zero);
    }
    if (_pid != 0) {
      _h = Win32.OpenProcess(0x438, false, _pid);
      if (_h != IntPtr.Zero) {
        lblProc.Text = "进程: " + _procName + " (PID " + _pid + ")  [已连接]";
        lblProc.ForeColor = Color.SeaGreen;
        Log("已连接 " + _procName + " PID=" + _pid);
      } else {
        lblProc.Text = "进程: " + _procName + " (PID " + _pid + ")  打开句柄失败";
        lblProc.ForeColor = Color.Firebrick;
        Log("OpenProcess 失败 err=" + Marshal.GetLastWin32Error());
      }
    } else {
      lblProc.Text = "进程: 未找到（请先启动 IB3.exe）";
      lblProc.ForeColor = Color.Firebrick;
      Log("未找到 IB3 进程");
    }
  }

  void CloseHandleIfAny() { if (_h != IntPtr.Zero) { Win32.CloseHandle(_h); _h = IntPtr.Zero; } }

  bool EnsureHandle() {
    if (_pid == 0 || _h == IntPtr.Zero) { ConnectProc(); }
    return _pid != 0 && _h != IntPtr.Zero;
  }

  // ================= 扫描 =================
  ScanType CurrentType() {
    switch (cboType.SelectedIndex) {
      case 0: return ScanType.I64;
      case 1: return ScanType.I32;
      case 2: return ScanType.I16;
      case 3: return ScanType.I8;
      case 4: return ScanType.F32;
      default: return ScanType.F64;
    }
  }

  void FirstScan() {
    if (!EnsureHandle()) { Log("未连接进程"); return; }
    if (_scanning) { Log("正在扫描中…"); return; }
    ScanType t = CurrentType();
    if (txtValue.Text.Trim().Length == 0) { SnapshotScan(t); return; }

    byte[] pat;
    try { pat = TypeUtil.Encode(t, txtValue.Text); }
    catch (Exception ex) { Log("数值解析失败: " + ex.Message); return; }
    _snap = null; // 已知值模式，丢弃旧快照
    string desc = TypeUtil.Name(t) + " = " + txtValue.Text.Trim();
    SetScanning(true);
    _cancel = false;
    IntPtr h = _h;
    new Thread(delegate() {
      var found = ScanCore.FirstScanKnown(h, t, pat, ScanCore.MAX_HITS, delegate { return _cancel; },
        delegate(long cur, long all) {
          BeginInvoke((MethodInvoker)delegate { prog.Value = (int)(cur * 1000 / all); });
        });
      BeginInvoke((MethodInvoker)delegate {
        hits = found;
        _hitsType = t;
        RenderResults();
        SetScanning(false);
        string note = hits.Count > ScanCore.MAX_HITS ? "（已达上限，请用更大的特征值或继续筛选）" : "";
        Log("首次扫描 [" + desc + "] → " + hits.Count + " 处" + note);
      });
    }).Start();
  }

  void SnapshotScan(ScanType t) {
    SetScanning(true);
    _cancel = false;
    IntPtr h = _h;
    Log("未知初始值(快照)模式：类型=" + TypeUtil.Name(t) + "，按 " + TypeUtil.Size(t) + " 字节对齐，只快照可写私有内存…");
    new Thread(delegate() {
      long total; bool capped; int skipped;
      var snap = ScanCore.SnapshotUnknown(h, t, delegate { return _cancel; },
        delegate(long cur, long all) {
          BeginInvoke((MethodInvoker)delegate { prog.Value = (int)Math.Min(1000, cur * 1000 / Math.Max(1, all)); lblProg.Text = "快照 " + (cur / 1048576) + " MB"; });
        }, out total, out capped, out skipped);
      BeginInvoke((MethodInvoker)delegate {
        _snap = snap; _snapType = t; _hitsType = t;
        hits = new List<ScanHit>();
        RenderResults();
        SetScanning(false);
        string note = capped ? "（快照总量超上限，" + skipped + " 个大区域未捕获！）" : (skipped > 0 ? "（" + skipped + " 个区域读取失败已跳过）" : "");
        Log("快照完成：" + snap.Count + " 个区域 / " + (total / 1048576) + " MB" + note);
        Log("→ 现在去游戏里让该数值变化，回来选筛选条件点「再次扫描」。");
      });
    }).Start();
  }

  FilterKind CurrentFilter(out string fname, out bool needRef) {
    fname = cboFilter.SelectedItem.ToString();
    needRef = (fname == "等于" || fname == "小于" || fname == "大于");
    switch (cboFilter.SelectedIndex) {
      case 0: return FilterKind.Increased;
      case 1: return FilterKind.Decreased;
      case 2: return FilterKind.Changed;
      case 3: return FilterKind.Unchanged;
      case 4: return FilterKind.Equal;
      case 5: return FilterKind.Less;
      default: return FilterKind.Greater;
    }
  }

  void NextScan() {
    if (!EnsureHandle()) { Log("未连接进程"); return; }
    if (_scanning) { Log("正在扫描中…"); return; }
    string fname; bool needRef;
    FilterKind f = CurrentFilter(out fname, out needRef);
    ScanType t = _hitsType;
    double refVal = 0;
    if (needRef) {
      try { refVal = TypeUtil.ParseRef(t, txtRef.Text); }
      catch (Exception ex) { Log("参考值解析失败: " + ex.Message); return; }
    }
    string fdesc = fname + (needRef ? " " + txtRef.Text.Trim() : "");

    if (_snap != null) {
      // ---------- 快照模式筛选 ----------
      var snap = _snap;
      ScanType st = _snapType;
      SetScanning(true);
      _cancel = false;
      IntPtr h = _h;
      new Thread(delegate() {
        bool capped;
        var keep = ScanCore.FilterSnapshot(h, st, snap, f, refVal, delegate { return _cancel; },
          delegate(long cur, long all) {
            BeginInvoke((MethodInvoker)delegate { prog.Value = (int)Math.Min(1000, cur * 1000 / Math.Max(1, all)); lblProg.Text = "快照筛选 " + (cur / 1048576) + "/" + (all / 1048576) + " MB"; });
          }, out capped);
        BeginInvoke((MethodInvoker)delegate {
          SetScanning(false);
          if (capped) {
            Log("快照筛选 [" + fdesc + "] 结果超过 " + SNAPSHOT_MATERIALIZE_CAP + " 条上限，未生成结果（快照保留）。请换更严格条件（等于/小于/大于）再筛。");
            return;
          }
          hits = keep;
          _snap = null; // 物化成功，快照释放；后续筛选走命中列表
          RenderResults();
          Log("快照筛选 [" + fdesc + "] → " + hits.Count + " 处");
          if (hits.Count == 0) Log("0 结果：确认数值确实变化过、类型是否正确，或重做快照。");
        });
      }).Start();
      return;
    }

    // ---------- 命中列表筛选（标准两遍扫描） ----------
    if (hits.Count == 0) { Log("请先做首次扫描"); return; }
    var src = hits;
    SetScanning(true);
    _cancel = false;
    IntPtr hh = _h;
    new Thread(delegate() {
      var keep = ScanCore.FilterHits(hh, t, src, f, refVal, delegate { return _cancel; },
        delegate(int done, int all) {
          BeginInvoke((MethodInvoker)delegate { lblProg.Text = "筛选 " + done + "/" + all; });
        });
      BeginInvoke((MethodInvoker)delegate {
        hits = keep;
        RenderResults();
        SetScanning(false);
        Log("再次扫描 [" + fdesc + "] → " + hits.Count + " 处");
      });
    }).Start();
  }

  void ResetScan() {
    hits.Clear();
    if (_snap != null) { _snap = null; Log("已清空快照"); }
    RenderResults();
    Log("已清空扫描结果");
  }

  void SetScanning(bool on) {
    _scanning = on;
    btnFirst.Enabled = !on; btnNext.Enabled = !on; btnReset.Enabled = !on;
    if (!on) { lblProg.Text = "空闲"; prog.Value = 0; }
  }

  void RenderResults() {
    ScanType t = _hitsType;
    lvRes.BeginUpdate();
    lvRes.Items.Clear();
    int n = Math.Min(hits.Count, ScanCore.MAX_HITS);
    for (int i = 0; i < n; i++) {
      ListViewItem it = new ListViewItem("0x" + hits[i].Addr.ToString("X"));
      it.SubItems.Add(TypeUtil.Decode(t, hits[i].Prev));
      it.SubItems.Add(TypeUtil.Name(t));
      it.Tag = hits[i];
      lvRes.Items.Add(it);
    }
    lvRes.EndUpdate();
    lblCount.Text = "结果: " + hits.Count + (hits.Count > ScanCore.MAX_HITS ? " (显示前" + ScanCore.MAX_HITS + ")" : "");
  }

  // ================= 修改表 =================
  ScanType ModTypeFromCombo() {
    switch (cboModType.SelectedIndex) {
      case 0: return ScanType.I64;
      case 1: return ScanType.I32;
      case 2: return ScanType.I16;
      case 3: return ScanType.I8;
      case 4: return ScanType.F32;
      default: return ScanType.F64;
    }
  }
  static int ModTypeToIndex(ScanType t) {
    switch (t) {
      case ScanType.I64: return 0;
      case ScanType.I32: return 1;
      case ScanType.I16: return 2;
      case ScanType.I8: return 3;
      case ScanType.F32: return 4;
      default: return 5;
    }
  }

  void AddSelToMod() {
    if (lvRes.SelectedItems.Count == 0) { Log("请先在扫描结果里选中行"); return; }
    ScanType t = _hitsType;
    int added = 0;
    foreach (ListViewItem it in lvRes.SelectedItems) {
      ScanHit h = (ScanHit)it.Tag;
      int sz = TypeUtil.Size(t);
      byte[] orig = new byte[sz];
      Array.Copy(h.Prev, orig, sz);
      MemEntry e = new MemEntry { Desc = "地址 " + it.Text, Addr = h.Addr, Type = t, Val = TypeUtil.Decode(t, h.Prev), Orig = orig };
      AddEntryRow(e);
      added++;
    }
    Log("已添加 " + added + " 条到修改列表");
  }

  void AddEntryRow(MemEntry e) {
    ListViewItem it = new ListViewItem(e.Desc);
    it.SubItems.Add("0x" + e.Addr.ToString("X"));
    it.SubItems.Add(TypeUtil.Name(e.Type));
    it.SubItems.Add(e.Val);
    it.SubItems.Add("");
    it.Tag = e;
    it.Checked = false;
    lvMod.Items.Add(it);
  }

  void LoadEntryToEditor() {
    if (lvMod.SelectedItems.Count == 0) return;
    MemEntry e = (MemEntry)lvMod.SelectedItems[0].Tag;
    txtDesc.Text = e.Desc;
    txtAddr.Text = e.Addr.ToString("X");
    cboModType.SelectedIndex = ModTypeToIndex(e.Type);
    txtModVal.Text = e.Val;
  }

  void ApplyEntry() {
    if (!EnsureHandle()) { Log("未连接进程"); return; }
    if (lvMod.SelectedItems.Count == 0) { Log("请先在修改列表选中行"); return; }
    ListViewItem it = lvMod.SelectedItems[0];
    MemEntry e = (MemEntry)it.Tag;
    long addr;
    try { addr = TypeUtil.ParseInt(txtAddr.Text); }
    catch (Exception ex) { Log("地址解析失败: " + ex.Message); return; }
    ScanType t = ModTypeFromCombo();
    byte[] data;
    try { data = TypeUtil.Encode(t, txtModVal.Text); }
    catch (Exception ex) { Log("数值解析失败: " + ex.Message); return; }
    // 首次写入前记录原值（撤销用）
    if (e.Orig == null) {
      byte[] cur = new byte[data.Length];
      int rr;
      if (Win32.ReadProcessMemory(_h, (IntPtr)addr, cur, cur.Length, out rr) && rr == cur.Length) e.Orig = cur;
    }
    e.Desc = txtDesc.Text.Trim().Length > 0 ? txtDesc.Text.Trim() : "地址 0x" + addr.ToString("X");
    e.Addr = addr; e.Type = t; e.Val = txtModVal.Text.Trim();
    int written;
    bool ok = Win32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out written);
    if (ok && written == data.Length) {
      Log("写入 0x" + addr.ToString("X") + " = " + e.Val + " (" + TypeUtil.Name(t) + ") 成功");
    } else {
      Log("写入失败 err=" + Marshal.GetLastWin32Error());
      return;
    }
    it.SubItems[0].Text = e.Desc;
    it.SubItems[1].Text = "0x" + addr.ToString("X");
    it.SubItems[2].Text = TypeUtil.Name(t);
    it.SubItems[3].Text = e.Val;
    it.Tag = e;
    ReadCurValues();
  }

  void RevertEntry() {
    if (!EnsureHandle()) { Log("未连接进程"); return; }
    if (lvMod.SelectedItems.Count == 0) { Log("请先在修改列表选中行"); return; }
    MemEntry e = (MemEntry)lvMod.SelectedItems[0].Tag;
    if (e.Orig == null) { Log("该条目没有记录原值（载入的预设条目在首次写入时才会记录）"); return; }
    int w;
    if (Win32.WriteProcessMemory(_h, (IntPtr)e.Addr, e.Orig, e.Orig.Length, out w) && w == e.Orig.Length) {
      Log("已恢复原值 0x" + e.Addr.ToString("X") + " = " + TypeUtil.Decode(e.Type, e.Orig));
      ReadCurValues();
    } else {
      Log("恢复失败 err=" + Marshal.GetLastWin32Error());
    }
  }

  void DeleteEntry() {
    if (lvMod.SelectedItems.Count == 0) return;
    lvMod.Items.Remove(lvMod.SelectedItems[0]);
  }

  void ReadCurValues() {
    if (!EnsureHandle()) { Log("未连接进程"); return; }
    int n = 0;
    foreach (ListViewItem it in lvMod.Items) {
      MemEntry e = (MemEntry)it.Tag;
      byte[] buf = new byte[TypeUtil.Size(e.Type)];
      int r;
      if (Win32.ReadProcessMemory(_h, (IntPtr)e.Addr, buf, buf.Length, out r) && r == buf.Length) {
        it.SubItems[4].Text = TypeUtil.Decode(e.Type, buf);
        n++;
      } else {
        it.SubItems[4].Text = "?";
      }
    }
    Log("已刷新 " + n + " 条当前值");
  }

  void FreezeTick() {
    if (_h == IntPtr.Zero || _pid == 0) return;
    foreach (ListViewItem it in lvMod.Items) {
      if (!it.Checked) continue;
      MemEntry e = (MemEntry)it.Tag;
      byte[] data;
      try { data = TypeUtil.Encode(e.Type, e.Val); } catch { continue; }
      int written;
      Win32.WriteProcessMemory(_h, (IntPtr)e.Addr, data, data.Length, out written);
    }
  }

  // ================= 预设 =================
  string PresetPath() {
    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mem_presets.csv");
  }
  void SavePreset() {
    using (SaveFileDialog d = new SaveFileDialog()) {
      d.FileName = "mem_presets.csv"; d.Filter = "CSV 预设|*.csv";
      if (d.ShowDialog() != DialogResult.OK) return;
      try {
        var sb = new StringBuilder();
        sb.AppendLine("描述,地址hex,类型,写入值,锁定");
        foreach (ListViewItem it in lvMod.Items) {
          MemEntry e = (MemEntry)it.Tag;
          sb.AppendLine(CSVEscape(e.Desc) + "," + e.Addr.ToString("X") + "," + TypeUtil.Name(e.Type) + "," + CSVEscape(e.Val) + "," + (it.Checked ? "1" : "0"));
        }
        File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
        Log("预设已保存: " + d.FileName);
      } catch (Exception ex) { Log("保存失败: " + ex.Message); }
    }
  }
  static string CSVEscape(string s) {
    if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
    return s;
  }
  void LoadPreset() {
    using (OpenFileDialog d = new OpenFileDialog()) {
      d.FileName = "mem_presets.csv"; d.Filter = "CSV 预设|*.csv";
      if (d.ShowDialog() != DialogResult.OK) return;
      LoadPresetSilent(d.FileName);
    }
  }
  void LoadPresetSilent(string path) {
    if (!File.Exists(path)) return;
    try {
      string[] lines = File.ReadAllLines(path, Encoding.UTF8);
      int n = 0;
      for (int i = 1; i < lines.Length; i++) {
        string line = lines[i].TrimEnd('\r');
        if (line.Length == 0) continue;
        string[] f = ParseCsv(line);
        if (f.Length < 5) continue;
        MemEntry e = new MemEntry { Desc = f[0], Type = ParseTypeName(f[2]), Val = f[3] };
        e.Addr = TypeUtil.ParseInt(f[1]);
        AddEntryRow(e);
        if (f[4] == "1") lvMod.Items[lvMod.Items.Count - 1].Checked = true;
        n++;
      }
      if (n > 0) Log("已载入预设 " + n + " 条（地址为上次运行值，重启后需重新扫描定位）");
    } catch (Exception ex) { Log("载入预设失败: " + ex.Message); }
  }
  static ScanType ParseTypeName(string s) {
    switch (s.Trim()) {
      case "Int8": return ScanType.I8;
      case "Int16": return ScanType.I16;
      case "Int32": return ScanType.I32;
      case "Float": return ScanType.F32;
      case "Double": return ScanType.F64;
      default: return ScanType.I64;
    }
  }
  static string[] ParseCsv(string line) {
    var fields = new List<string>();
    var cur = new StringBuilder();
    bool inQ = false;
    for (int i = 0; i < line.Length; i++) {
      char c = line[i];
      if (inQ) {
        if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else inQ = false; }
        else cur.Append(c);
      } else {
        if (c == '"') inQ = true;
        else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
        else cur.Append(c);
      }
    }
    fields.Add(cur.ToString());
    return fields.ToArray();
  }

  // ================= 日志 =================
  void Log(string s) {
    string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine;
    if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { txtLog.AppendText(line); }); return; }
    txtLog.AppendText(line);
  }

  [STAThread]
  static void Main() {
    Application.EnableVisualStyles();
    Application.Run(new MainForm());
  }
}

} // namespace
