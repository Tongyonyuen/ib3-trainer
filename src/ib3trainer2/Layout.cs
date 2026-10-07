// ============================================================================
// Layout.cs — 整界面等比缩放自适应（设计稿 1200×1000 绝对坐标 → 按屏幕/DPI 缩放）
//
// 为什么这么做：本窗体的全部控件都用绝对坐标 SetBounds 摆放，没有任何 Dock/Anchor。
// 在这种排版下，「整界面乘以同一个缩放比 s」是唯一能保证【元素绝不移位】的办法 ——
// 所有控件同乘一个 s，相对位置天然不变。
//
// 设计坐标模型：构造完成后把每个控件的设计 Bounds / 设计字号记录一次（CaptureDesign），
// 之后每轮布局都做 bounds = design * s —— 幂等、可反复调用、不累积舍入误差。
// 所以 Apply(1.0) → Apply(0.7) → Apply(1.0) 能逐像素回到原样。
//
// 纵向空间紧张时：先把横幅（设计高 300 → 最小 60）压缩，省下的 delta 让横幅以下的
// 顶层控件整体上移，设计总高由 1000 变成 700 + bannerH，用更小的压缩代价换更大的 s。
//
// 注意：本文件是 MainForm 的 partial（要访问 picBanner/tabs 等私有字段）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ib3Trainer2 {

// ============================================================================
// 缩放状态与小工具
// ============================================================================
static class UiScale {
  public const int DESIGN_W = 1200;      // 设计稿宽
  public const int TOP_FIXED = 700;      // 横幅以外的设计高度（1000 - 300）
  public const int BANNER_FULL = 300;    // 横幅设计高
  public const int BANNER_MIN = 60;      // 横幅可压缩到的下限
  public const int CONTENT_TOP = 340;    // 横幅下沿：设计 Y ≥ 此值的顶层控件随横幅压缩上移
  public const float READABLE = 0.85f;   // 可读下限：低于此值才启用横幅压缩
  public const float S_MIN_USER = 0.60f; // 用户拖拽的缩放下限

  public static float S = 1f;            // 当前缩放比
  public static float Dpi = 96f;         // 当前系统 DPI（SetProcessDPIAware 后为真实值）

  public static int Px(float designPx) { return (int)Math.Round(designPx * S); }

  // 字号换算：设计按 96dpi 定义。DPI 感知后 Font 的 pt 会按 dpi/96 放大，
  // 所以要反向折算，才能让文字渲染尺寸恰好等于「设计字号 × s」。
  // s == dpi/96 时结果等于设计字号（作者机器 96dpi、s=1 时行为与改动前完全一致）。
  public static float Pt(float designPt) {
    float pt = designPt * S * 96f / Dpi;
    return pt < 1f ? 1f : pt;
  }

  public static float ReadDpi() {
    try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; }
    catch { return 96f; }
  }
}

// ============================================================================
// 字体库：按「代」缓存，换代后统一释放上一代
//
// 为什么需要：每轮布局要给上百个控件换上按新 s 算出的字体。若每次都 new Font 而不释放，
// 用户拖拽几秒就会耗尽 GDI 句柄（进程上限约 1 万）。两代模型保证：
// 上一代字体在被全量替换之后才释放，期间不会有控件还引用着已释放的字体。
// ============================================================================
static class FontBank {
  static Dictionary<string, Font> gen = new Dictionary<string, Font>();
  static Dictionary<string, Font> dead = null;
  static float lastS = float.NaN;

  public static void BeginPass() {
    if (UiScale.S == lastS) return;      // s 未变 → 原地复用，不产生新对象
    lastS = UiScale.S;
    // 释放【上上一代】：此刻控件已全部换成上一代字体，再上一代确定无人引用。
    // 比「本代用完立刻释放」晚一整轮 —— 万一某一轮 ApplyRec 中途抛异常，也不会留下
    // 「控件还挂着已释放字体」的雷（那会导致之后创建句柄时 Font.ToHfont() 抛异常）。
    if (dead != null) {
      foreach (Font f in dead.Values) { try { f.Dispose(); } catch { } }
    }
    dead = gen;
    gen = new Dictionary<string, Font>();
  }
  // 保留为空实现：布局收尾无需额外动作（释放见 BeginPass 注释）
  public static void EndPass() { }
  public static Font Get(string family, FontStyle style, float designPt) {
    float pt = UiScale.Pt(designPt);
    string k = family + "|" + pt.ToString("0.###", CultureInfo.InvariantCulture) + "|" + (int)style;
    Font f;
    if (gen.TryGetValue(k, out f)) return f;
    if (dead != null && dead.TryGetValue(k, out f)) {
      // ★ 关键：算出同样字号时必须沿用上一代的【同一个对象】，并从待释放表里摘掉。
      //
      // 不能直接 new 一个「值相等的新实例」：Control.Font 的 setter 是按【值】比较的
      // （oldFont != value），判定相等就根本不把新对象存进属性包 —— 控件仍然挂着上一代的
      // 实例。等 EndPass 把上一代释放掉，控件稍后再创建句柄时 Font.ToHfont() 就会抛
      // ArgumentException「参数无效」（实机复现：TabControl/ListView 建句柄时必崩）。
      dead.Remove(k);
      gen[k] = f;
      return f;
    }
    f = new Font(family, pt, style, GraphicsUnit.Point);
    gen[k] = f;
    return f;
  }
}

// ============================================================================
// MainForm 的缩放部分
// ============================================================================
partial class MainForm {

  // ---- 单个控件的设计快照 ----
  class DesignInfo {
    public Rectangle D;              // 设计 Bounds
    public string Fam = null;        // 设计字体族
    public float Pt = 0f;            // 设计字号
    public FontStyle St = FontStyle.Regular;
    public bool Skip = false;        // TabPage：Bounds 归 TabControl 管，不记录也不设置
    public bool Banner = false;      // 横幅：高度由 bannerH 决定
    public int[] ColCum = null;      // ListView 列宽前缀和（保证缩放后总宽精确、不冒横向滚动条）
  }

  readonly Dictionary<Control, DesignInfo> _design = new Dictionary<Control, DesignInfo>();
  bool _ready = false;               // 首次 FitToScreen 之前不响应尺寸事件
  bool _inLayout = false;            // 重入闸：挡住 SetClientSizeCore 引发的 OnResize 回环
  float _bannerH = UiScale.BANNER_FULL;
  float _sMin = UiScale.S_MIN_USER;
  bool _sizing = false;              // 拖拽中：横幅位图用低质量插值重绘，保拖拽手感

  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", EntryPoint = "SendMessageW")]
  static extern IntPtr SendMsg(IntPtr h, uint m, IntPtr w, IntPtr l);

  [StructLayout(LayoutKind.Sequential)]
  struct LRECT { public int L, T, R, B; }

  const int WM_NCHITTEST = 0x0084;
  const int WM_SIZING = 0x0214;
  const int WM_ENTERSIZEMOVE = 0x0231;
  const int WM_EXITSIZEMOVE = 0x0232;
  const uint WM_SETREDRAW = 0x000B;
  const int HTBOTTOMRIGHT = 17;

  // ---------------- 采集设计坐标（构造完成后调用一次） ----------------
  void CaptureDesign() {
    _design.Clear();
    CaptureRec(this);
  }
  void CaptureRec(Control parent) {
    foreach (Control c in parent.Controls) {
      DesignInfo m = new DesignInfo();
      m.D = c.Bounds;
      m.Skip = (c is TabPage);
      m.Banner = (c == picBanner);
      Font f = c.Font;
      if (f != null) { m.Fam = f.FontFamily.Name; m.Pt = f.SizeInPoints; m.St = f.Style; }
      ListView lv = c as ListView;
      if (lv != null && lv.Columns.Count > 0) {
        m.ColCum = new int[lv.Columns.Count];
        int sum = 0;
        for (int i = 0; i < lv.Columns.Count; i++) { sum += lv.Columns[i].Width; m.ColCum[i] = sum; }
      }
      _design[c] = m;
      CaptureRec(c);
    }
  }

  // ---------------- 启动自适应 ----------------
  void FitToScreen() {
    UiScale.Dpi = UiScale.ReadDpi();
    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
    float s, bh;
    SolveLayout(wa.Width, wa.Height, UiScale.Dpi, out s, out bh);
    // 小屏时不得把下限设得比适配值还大，否则窗口反被撑出屏幕（底部按钮再度丢失）
    _sMin = Math.Min(UiScale.S_MIN_USER, s);
    SetSizeLimits(wa);
    ApplyLayout(s, bh);
    Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
    _ready = true;
  }

  // 已知可用空间，联合求解缩放比 s 与横幅高度 bannerH
  static void SolveLayout(float availW, float availH, float dpi, out float s, out float bannerH) {
    float sCap = Math.Min(dpi / 96f, availW / (float)UiScale.DESIGN_W);
    float sFull = Math.Min(sCap, availH / (float)(UiScale.TOP_FIXED + UiScale.BANNER_FULL));
    if (sFull >= UiScale.READABLE) { s = sFull; bannerH = UiScale.BANNER_FULL; return; }
    // 纵向紧张：压横幅换缩放，把 s 抬到可读下限为止
    float sTarget = Math.Min(sCap, UiScale.READABLE);
    float need = availH / sTarget - UiScale.TOP_FIXED;
    if (need > UiScale.BANNER_FULL) need = UiScale.BANNER_FULL;
    if (need < UiScale.BANNER_MIN) need = UiScale.BANNER_MIN;
    bannerH = need;
    s = Math.Min(sCap, availH / (UiScale.TOP_FIXED + bannerH));
  }

  void SetSizeLimits(Rectangle wa) {
    float sMax = Math.Min(UiScale.Dpi / 96f,
                 Math.Min(wa.Width / (float)UiScale.DESIGN_W,
                          wa.Height / (float)(UiScale.TOP_FIXED + _bannerH)));
    MinimumSize = new Size((int)Math.Round(UiScale.DESIGN_W * _sMin),
                           (int)Math.Round((UiScale.TOP_FIXED + _bannerH) * _sMin));
    MaximumSize = new Size((int)Math.Round(UiScale.DESIGN_W * sMax),
                           (int)Math.Round((UiScale.TOP_FIXED + _bannerH) * sMax));
  }

  float ClampScale(float s) {
    Rectangle wa = Screen.FromControl(this).WorkingArea;
    float sMax = Math.Min(UiScale.Dpi / 96f,
                 Math.Min(wa.Width / (float)UiScale.DESIGN_W,
                          wa.Height / (float)(UiScale.TOP_FIXED + _bannerH)));
    if (s > sMax) s = sMax;
    if (s < _sMin) s = _sMin;
    return s;
  }

  // ---------------- 应用布局（幂等） ----------------
  void ApplyLayout(float s, float bannerH) {
    if (_design.Count == 0 || _inLayout) return;
    _inLayout = true;
    UiScale.S = s;
    _bannerH = bannerH;
    FontBank.BeginPass();
    LockRedraw(false);
    try {
      int w = UiScale.Px(UiScale.DESIGN_W);
      int h = UiScale.Px(UiScale.TOP_FIXED + bannerH);
      if (ClientSize.Width != w || ClientSize.Height != h) SetClientSizeCore(w, h);
      Theme.TitleH = UiScale.Px(20);
      ApplyRec(this, true, UiScale.BANNER_FULL - bannerH);
      // 横幅位图必须按新尺寸重画 —— PictureBox 的 SizeMode 是 Normal（只裁剪不拉伸），
      // 沿用旧位图会看到「图只铺到左边一部分、右边是底色」。
      RenderBanner();
    } finally {
      LockRedraw(true);
      FontBank.EndPass();          // 此刻上一代字体已无控件引用，可安全释放
      _inLayout = false;
    }
  }

  // delta 只作用于顶层（子控件的 Bounds 是父容器相对坐标，绝不能叠加偏移）
  void ApplyRec(Control parent, bool topLevel, float delta) {
    foreach (Control c in parent.Controls) {
      DesignInfo m;
      if (!_design.TryGetValue(c, out m)) continue;   // 未采集（运行期新增）→ 不动
      if (!m.Skip) {
        Rectangle d = m.D;
        if (topLevel) {
          if (m.Banner) d.Height = (int)Math.Round(_bannerH);
          else if (d.Y >= UiScale.CONTENT_TOP) d.Y -= (int)Math.Round(delta);
        }
        // 先换字体再设边界：设 Bounds 可能在父句柄已建时顺带创建本控件句柄，
        // 那一刻就会用控件当前的 Font 去 SetWindowFont，先换好才不会用到旧字体。
        if (m.Fam != null) {
          // 每个控件都要显式写字体：绝不留下仍持有旧代字体的控件
          Font nf = FontBank.Get(m.Fam, m.St, m.Pt);
          if (!object.ReferenceEquals(c.Font, nf)) c.Font = nf;
        }
        c.Bounds = new Rectangle(UiScale.Px(d.X), UiScale.Px(d.Y),
                                 UiScale.Px(d.Width), UiScale.Px(d.Height));
        ApplySpecials(c, m);
      }
      ApplyRec(c, false, 0f);
    }
  }

  // 不走 SetBounds 的尺寸：列宽、页签尺寸、下拉行高、按钮圆角
  void ApplySpecials(Control c, DesignInfo m) {
    ListView lv = c as ListView;
    if (lv != null && m.ColCum != null) {
      int n = Math.Min(lv.Columns.Count, m.ColCum.Length);
      for (int i = 0; i < n; i++) {
        int hi = UiScale.Px(m.ColCum[i]);
        int lo = (i > 0) ? UiScale.Px(m.ColCum[i - 1]) : 0;
        int w = hi - lo;
        if (w < 1) w = 1;
        if (lv.Columns[i].Width != w) lv.Columns[i].Width = w;
      }
    }
    TabControl tc = c as TabControl;
    if (tc != null) {
      Size want = new Size(Math.Max(24, UiScale.Px(120)), Math.Max(12, UiScale.Px(36)));
      try { if (tc.ItemSize != want) tc.ItemSize = want; } catch { }
    }
    ComboBox cb = c as ComboBox;
    if (cb != null && cb.DrawMode == DrawMode.OwnerDrawFixed) {
      int ih = Math.Max(8, UiScale.Px(20));
      if (cb.ItemHeight != ih) cb.ItemHeight = ih;
    }
    RButton rb = c as RButton;
    if (rb != null) rb.RefreshCorners();
    FlatGroupBox gb = c as FlatGroupBox;
    if (gb != null) gb.Invalidate();
  }

  // 整轮布局包在 WM_SETREDRAW(0) 里，避免上百次 SetWindowPos 引发重绘闪烁
  void LockRedraw(bool on) {
    try {
      if (!IsHandleCreated) return;
      SendMsg(Handle, WM_SETREDRAW, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
      if (on) { Invalidate(true); Update(); }
    } catch { }
  }

  // ---------------- 窗口外框：右下角拖拽 + 宽高比吸附 ----------------
  protected override void WndProc(ref Message m) {
    if (m.Msg == WM_NCHITTEST && _ready) {
      int lp = unchecked((int)(long)m.LParam);
      // lParam 是屏幕坐标的有符号短整型；多显示器负坐标下直接 ToInt32 会拿到垃圾值
      Point scr = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
      Point p = PointToClient(scr);
      int g = Math.Max(12, UiScale.Px(16));
      if (p.X >= ClientSize.Width - g && p.Y >= ClientSize.Height - g) {
        m.Result = (IntPtr)HTBOTTOMRIGHT;   // 右下角留白由设计保证（见文件末尾注释）
        return;
      }
    } else if (m.Msg == WM_SIZING && _ready) {
      // 直接改写系统给的 RECT = 尺寸循环里就吸附，不会「改 ClientSize → 触发 OnResize」来回抖
      LRECT r = (LRECT)Marshal.PtrToStructure(m.LParam, typeof(LRECT));
      float s = ClampScale((r.R - r.L) / (float)UiScale.DESIGN_W);
      r.R = r.L + (int)Math.Round(UiScale.DESIGN_W * s);
      r.B = r.T + (int)Math.Round((UiScale.TOP_FIXED + _bannerH) * s);
      Marshal.StructureToPtr(r, m.LParam, false);   // 第三参数必须 false
      m.Result = (IntPtr)1;
      return;
    } else if (m.Msg == WM_ENTERSIZEMOVE && _ready) {
      _sizing = true;   // 拖动期间用低质量插值，别让高质量重绘拖慢拖拽手感
    } else if (m.Msg == WM_EXITSIZEMOVE && _ready) {
      _sizing = false;
      SetSizeLimits(Screen.FromControl(this).WorkingArea);   // 拖到别的显示器后放宽上限
      RenderBanner();                                        // 松手补一次高质量重绘
    }
    base.WndProc(ref m);
  }

  // 兜底路径（程序化改尺寸 / 系统改尺寸）。自己触发的会被 _inLayout 挡回。
  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (!_ready || _inLayout || _design.Count == 0) return;
    // ⚠ 最小化/最大化途中会带着 0 或异常尺寸进来：ClampScale(0) 被夹到下限 _sMin(0.60)，
    //   于是 ApplyLayout 把窗口永久缩到 60%，唤出后也回不来。
    //   （2026-10-07 用户实测：点最小化再唤出，缩放倍率变了、窗口变小。）
    if (WindowState != FormWindowState.Normal) return;
    if (!Visible || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
    ApplyLayout(ClampScale(ClientSize.Width / (float)UiScale.DESIGN_W), _bannerH);
  }
}

// 说明（拖拽命中区）：窗体设计尺寸 1200×1000，而内容右下角最远到
//   日志框   y 止于 950
//   状态标签 y 止于 981、x 止于 1188
// 因此右下角 (1184..1200, 984..1000) 恰好是裸窗体 —— WM_NCHITTEST 才能问到顶层窗体。
// 若日后加长日志框或状态栏，请保留这块留白，否则右下角拖拽会失效。

} // namespace
