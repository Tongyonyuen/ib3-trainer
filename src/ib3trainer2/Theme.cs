// ============================================================================
// Theme.cs v2 — 扁平圆角 · 烟熏玻璃卡片（80% 不透明）· 纯白底 · 金主题保留
//   - 全局圆角：按钮/卡片/输入/页签 均为圆角扁平
//   - 卡片 = 80% 不透明度深色块（浮于白底/横幅渐隐之上）
//   - 无边框窗体的自绘标题栏在 Ib3Trainer2.cs
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ib3Trainer2 {

static class Theme {
  public static readonly Color Gold = Color.FromArgb(212, 175, 55);
  public static readonly Color GoldDim = Color.FromArgb(120, 100, 44);
  public static readonly Color BG = Color.FromArgb(244, 238, 223);     // 应用底色 = 暖象牙/羊皮纸色
  public static readonly Color Ink = Color.FromArgb(90, 78, 58);       // 底色上的文字（深棕）
  public static readonly Color Card = Color.FromArgb(255, 41, 35, 29);  // 卡片（实色：保证浅色文字可读）
  public static readonly Color CardSolid = Color.FromArgb(255, 38, 33, 28);
  public static readonly Color CardLite = Color.FromArgb(255, 56, 48, 40);   // 按钮/输入
  public static readonly Color CardLiteSolid = Color.FromArgb(255, 56, 48, 40);
  public static readonly Color CardGold = Color.FromArgb(255, 74, 58, 27);   // 暗金主区块（首页专用）
  public static readonly Color Line = Color.FromArgb(90, 150, 124, 60);      // 金线
  public static readonly Color Text = Color.FromArgb(232, 222, 202);
  public static readonly Color TextDim = Color.FromArgb(160, 150, 134);
  public static readonly Color Warn = Color.FromArgb(206, 92, 58);
  public static readonly Color Ok = Color.FromArgb(122, 190, 120);

  // 兼容旧常量名（其余代码仍引用 Panel/PanelLight）
  public static readonly Color Panel = CardSolid;
  public static readonly Color PanelLight = CardLiteSolid;

  // 字体一律经 FontBank 取：随缩放换代缓存，拖动窗口时不会每轮泄漏一批 GDI 字体对象。
  // 注意：FontBank 返回的是共享实例，任何地方都不得 Dispose 它。
  public static Font UI { get { return FontBank.Get("Microsoft YaHei", FontStyle.Regular, 9.75f); } }
  public static Font Mono { get { return FontBank.Get("Consolas", FontStyle.Regular, 9f); } }

  // 卡片顶部标题留白（设计值 20）。原为 FlatGroupBox.TITLE_H 常量 —— const 会被内联进
  // 调用方 IL，运行时改不了，故改为随缩放同步的静态量，由 Layout.cs 每轮赋值。
  public static int TitleH = 20;

  // ---- 圆角矩形路径 ----
  public static GraphicsPath Round(Rectangle r, int rad) {
    GraphicsPath p = new GraphicsPath();
    if (rad < 2) { p.AddRectangle(r); return p; }
    int d = rad * 2;
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
  }

  // ---- 输入框占位提示（EM_SETCUEBANNER）----
  [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
  static extern IntPtr SendMessageCue(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
  public static void SetCue(TextBox t, string cue) {
    try {
      t.HandleCreated += delegate { SendMessageCue(t.Handle, 0x1501, (IntPtr)1, cue); };
      if (t.IsHandleCreated) SendMessageCue(t.Handle, 0x1501, (IntPtr)1, cue);
    } catch { }
  }

  // 深色滚动条（DarkMode_Explorer）
  [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
  static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
  public static void ApplyDarkScroll(Control c) {
    EventHandler apply = delegate {
      try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    };
    c.HandleCreated += apply;
    if (c.IsHandleCreated) apply(c, EventArgs.Empty);
  }

  // 把原生 ProgressBar 画成我们的深色风格。
  //
  // ★ 为什么需要专门一招：**开着视觉样式时原生 ProgressBar 会忽略 BackColor/ForeColor**
  //   （主题引擎自己画那条灰槽）。必须先 `SetWindowTheme(h, " ", " ")` 去掉视觉样式，
  //   颜色才会生效 —— 与 StyleTab（Theme.cs:176）用的是同一招。
  //   去掉视觉样式后必须保持 Continuous：分段式布局在深色底上很难看。
  //
  // 2026-10-08：发现模式那条"空白灰条"就是它 —— 原生灰槽(230,230,230)压在象牙底色上，
  //   与整体手绘风格不搭（作者反馈"看起来没有实际意义"，但它其实是扫描进度条，真在用）。
  public static void StyleProgress(ProgressBar pb, Color track, Color fill) {
    if (pb == null) return;
    try { pb.Style = ProgressBarStyle.Continuous; } catch { }
    EventHandler apply = delegate {
      try { SetWindowTheme(pb.Handle, " ", " "); } catch { }
      try { pb.BackColor = track; pb.ForeColor = fill; } catch { }
    };
    pb.HandleCreated += apply;                 // 句柄重建（换 DPI/缩放）后要重新去样式
    if (pb.IsHandleCreated) apply(pb, EventArgs.Empty);
  }

  public static Button MkButton(string text, int x, int y, int w, int h, EventHandler onClick) {
    RButton b = new RButton();
    // 文案走 I18n：建的时候按当前语言贴一次，并登记下来供切换时重设（key = 控件本身）
    I18n.Register(b, delegate(string s) { b.Text = s; }, text);
    b.SetBounds(x, y, w, h);
    b.Font = UI;
    if (onClick != null) b.Click += onClick;
    return b;
  }
  public static void StyleButton(Button b) { /* RButton 自绘；保留兼容空实现 */ }

  // ★★ 浅底还是深底，不能看父控件的 BackColor 属性 ★★
  //   MkLabel 会设 l.BackColor = Color.Transparent，而透明控件显示的是**父控件实际画出来的
  //   像素**，不是父控件的 BackColor 属性值。FlatGroupBox 的 BackColor 写着 Theme.BG（浅），
  //   但它的 OnPaint 把卡片区域整块填成深色（Card / CardGold），只有顶部 Theme.TitleH
  //   那一条是浅的。所以「这个标签该用浅字还是深字」取决于**它被 Add 到哪个容器**：
  //     页面 / 窗体 / 标题条（真浅底）      → 用 MkLabelInk（Ink）
  //     FlatGroupBox 内部（y > TitleH）     → 用 MkLabel 默认（Text）
  //   2026-10-08 逐像素审计 42 个调用点的结果：9 个在浅底、12 个在深卡、5 个已显式覆盖。
  //   正因如此**默认值不能改**——改了会坏 12 处、只修 9 处。
  public static Label MkLabel(string text, int x, int y, int w) {
    Label l = new Label();
    // MkHint 走的就是这里，所以两处只需改这一处
    I18n.Register(l, delegate(string s) { l.Text = s; }, text);
    l.SetBounds(x, y, w, 18);
    l.ForeColor = Text;
    l.Font = UI;
    l.BackColor = Color.Transparent;
    return l;
  }

  // 放在**真·浅色背景**上的字段标题（TabPage 页首那排「主分类 / 搜索 / 槽位…」、
  // 窗体级别的标题条署名）。默认的 Theme.Text 在这个背景上只有 **1.15:1**，等于看不见。
  //
  // 需要它而不是 MkLabel 的场合很具体：**直接 Add 到 TabPage / Form / TitleBar**。
  // 只要标签是 Add 进 FlatGroupBox（深卡）的，就用 MkLabel 默认值，别用这个。
  public static Label MkLabelInk(string text, int x, int y, int w) {
    Label l = MkLabel(text, x, y, w);
    l.ForeColor = Ink;
    return l;
  }

  public static Label MkHint(string text, int x, int y, int w) {
    Label l = MkLabel(text, x, y, w);
    l.ForeColor = TextDim;
    l.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    l.Height = 32;
    return l;
  }

  public static TextBox MkText(int x, int y, int w, string init) {
    TextBox t = new TextBox();
    t.SetBounds(x, y, w, 23);
    t.Text = init;
    t.BackColor = CardLiteSolid;      // 扁平：无边框
    t.ForeColor = Text;
    t.BorderStyle = BorderStyle.None;
    t.Font = UI;
    return t;
  }

  public static void StyleCombo(ComboBox c) {
    c.BackColor = CardLiteSolid;
    c.ForeColor = Text;
    c.FlatStyle = FlatStyle.Flat;
    c.Font = UI;
    c.DropDownStyle = ComboBoxStyle.DropDownList;
    c.DrawMode = DrawMode.OwnerDrawFixed;
    c.ItemHeight = 20;
    c.DrawItem += delegate(object s, DrawItemEventArgs e) {
      if (e.Index < 0) return;
      bool sel = (e.State & DrawItemState.Selected) != 0;
      using (SolidBrush b = new SolidBrush(sel ? Color.FromArgb(74, 64, 48) : CardLiteSolid))
        e.Graphics.FillRectangle(b, e.Bounds);
      using (SolidBrush fg = new SolidBrush(Text))
        // ★ 在这里过 T()：所有下拉项（含 ItemDb 注入的 "全部"）一次覆盖。
        //   注意只翻"显示"，**存储值不变** —— ItemDb.cs 拿 "全部" 做比较，改了会坏逻辑。
        e.Graphics.DrawString(I18n.T(c.Items[e.Index].ToString()), UI, fg, e.Bounds.X + 3, e.Bounds.Y + 2);
    };
  }

  public static void StyleList(ListView lv) {
    lv.BackColor = CardSolid;
    lv.ForeColor = Text;
    lv.Font = UI;
    lv.BorderStyle = BorderStyle.None;
    lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
    lv.FullRowSelect = true;
    lv.View = View.Details;
  }

  public static void StyleTab(TabControl t) {
    t.Font = UI;
    t.SizeMode = TabSizeMode.Fixed;
    t.ItemSize = new Size(120, 36);
    t.Appearance = TabAppearance.FlatButtons;               // 去系统 3D 边框
    t.BackColor = Color.FromArgb(58, 47, 36);
    EventHandler untheme = delegate {
      try { SetWindowTheme(t.Handle, " ", " "); } catch { }
    };
    t.HandleCreated += untheme;
    if (t.IsHandleCreated) untheme(t, EventArgs.Empty);
    // 自绘交给 FlatTab 的 WM_PAINT 后覆盖（OwnerDrawFixed 且无 DrawItem 处理器 = 系统不画项目）
    t.DrawMode = TabDrawMode.OwnerDrawFixed;
  }
}

// ============================================================================
// 扁平圆角 TabControl：WM_PAINT 后整条覆盖（未选中页签 + 无页签空白区 = 深棕）
// ============================================================================
class FlatTab : TabControl {
  public static readonly Color Strip = Color.FromArgb(58, 47, 36);
  protected override void WndProc(ref Message m) {
    base.WndProc(ref m);
    if (m.Msg == 0x000F) {   // WM_PAINT 之后覆盖
      try {
        using (Graphics g = Graphics.FromHwnd(Handle)) {
          int stripH = TabCount > 0 ? GetTabRect(0).Bottom + 2 : 38;
          using (SolidBrush b = new SolidBrush(Strip))
            g.FillRectangle(b, 0, 0, Width, stripH);
          for (int i = 0; i < TabCount; i++) {
            Rectangle r = GetTabRect(i);
            bool sel = (i == SelectedIndex);
            if (sel) {
              g.SmoothingMode = SmoothingMode.AntiAlias;
              int in4 = Math.Max(1, UiScale.Px(4));
              Rectangle pill = new Rectangle(r.X + in4, r.Y + in4, r.Width - in4 * 2, r.Height - in4 * 2);
              int rad = Math.Max(2, UiScale.Px(9));
              using (GraphicsPath p = Theme.Round(pill, rad))
              using (SolidBrush b2 = new SolidBrush(Theme.CardGold)) g.FillPath(b2, p);
              using (GraphicsPath p = Theme.Round(pill, rad))
              using (Pen pen = new Pen(Theme.Gold, 1f)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, I18n.T(TabPages[i].Text), Font, r,
              sel ? Theme.Gold : Theme.Text,
              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
          }
        }
      } catch { }
    }
  }
}

// ============================================================================
// 烟熏玻璃卡片（80% 不透明 + 圆角 + 金线；Title 绘于卡片上沿留白处）
// 兼容旧用法：Title 属性 + 作为 Panel 容器
// ============================================================================
class FlatGroupBox : Panel {
  public string Title = "";
  public Color Fill = Theme.Card;   // 可逐组覆盖（如首页主区块的暗金色）
  // 顶部标题留白改为随缩放同步的 Theme.TitleH（原 const 会被内联进调用方 IL，运行时改不了）
  public FlatGroupBox() {
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    BackColor = Theme.BG;
  }
  protected override void OnPaint(PaintEventArgs e) {
    Graphics g = e.Graphics;
    g.SmoothingMode = SmoothingMode.AntiAlias;
    int th = Theme.TitleH;
    int ch = Height - 1 - th;
    if (ch < 1) ch = 1;                       // 极端小尺寸下别让卡片高度变负
    int cw = Width - 1;
    if (cw < 1) cw = 1;
    Rectangle card = new Rectangle(0, th, cw, ch);
    int rad = Math.Max(2, UiScale.Px(10));
    using (GraphicsPath p = Theme.Round(card, rad))
    using (SolidBrush b = new SolidBrush(Fill)) g.FillPath(b, p);
    using (GraphicsPath p = Theme.Round(card, rad))
    using (Pen pen = new Pen(Theme.Line, 1f)) g.DrawPath(pen, p);
    if (Title.Length > 0) {
      using (SolidBrush fg = new SolidBrush(Theme.Gold))
        g.DrawString(I18n.T(Title), FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f), fg,
                     UiScale.Px(14), UiScale.Px(2));
    }
  }
}

// ============================================================================
// 扁平圆角按钮（FlatStyle.Flat 属性渲染 + Region 圆角裁剪：稳定可靠）
// ============================================================================
class RButton : Button {
  public RButton() {
    FlatStyle = FlatStyle.Flat;
    FlatAppearance.BorderSize = 0;
    FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 74, 63, 50);
    FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 96, 82, 58);
    BackColor = Theme.CardLiteSolid;
    ForeColor = Theme.Text;
    UseVisualStyleBackColor = false;
    TabStop = false;
  }
  int _rw = -1, _rh = -1, _rr = -1;

  // 圆角半径随缩放；并在尺寸/半径未变时早退 —— 否则拖动窗口时每帧给几十个按钮
  // 新建 GraphicsPath+Region，GDI+ 会明显抖动。
  public void RefreshCorners() {
    int rad = Math.Max(2, UiScale.Px(8));
    int lim = Math.Min(Width, Height) / 2;
    // 半径超过半边长时四个圆弧互相穿插、路径退化，Region 可能为空 → 按钮整块消失且点不到
    if (rad > lim) rad = lim;
    if (rad < 2) rad = 2;
    if (Width == _rw && Height == _rh && rad == _rr) return;
    _rw = Width; _rh = Height; _rr = rad;
    try {
      using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width, Height), rad))
        Region = new Region(p);
    } catch { }
  }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshCorners(); }
  protected override void OnResize(EventArgs e) { base.OnResize(e); RefreshCorners(); }
}

// ============================================================================
// 深色 ListView：自绘深色表头（金线 + 金字）
// ============================================================================
class DarkListView : ListView {
  [StructLayout(LayoutKind.Sequential)]
  struct NMHDR { public IntPtr hwndFrom; public IntPtr idFrom; public int code; }
  [StructLayout(LayoutKind.Sequential)]
  struct RECTS { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)]
  struct NMCUSTOMDRAW { public NMHDR hdr; public uint dwDrawStage; public IntPtr hdc; public RECTS rc; public IntPtr dwItemSpec; public uint uItemState; public IntPtr lItemlParam; }
  [StructLayout(LayoutKind.Sequential)]
  struct HDITEM { public uint mask; public int cxy; public IntPtr pszText; public IntPtr hbm; public int cchTextMax; public int fmt; public IntPtr lParam; public int iImage; public int iOrder; public uint type; public IntPtr pvFilter; public uint state; }
  [StructLayout(LayoutKind.Sequential)]
  struct DRAWITEMSTRUCT { public uint CtlType; public uint CtlID; public uint itemID; public uint itemAction; public uint itemState; public IntPtr hwndItem; public IntPtr hDC; public RECTS rcItem; public IntPtr itemData; }

  [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
  [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendMessageItem(IntPtr h, uint msg, IntPtr wp, ref HDITEM lp);

  const uint LVM_GETHEADER = 0x1000 + 31;
  const uint HDM_GETITEMW = 0x1200 + 11;
  const uint HDM_SETITEMW = 0x1200 + 12;
  const int WM_NOTIFY = 0x4E;
  const int WM_DRAWITEM = 0x2B;
  const int NM_CUSTOMDRAW = -12;
  const uint CDDS_PREPAINT = 0x1;
  const uint CDDS_ITEMPREPAINT = 0x10001;
  const int CDRF_NOTIFYITEMDRAW = 0x20;
  const int CDRF_SKIPDEFAULT = 0x4;
  const int HDF_OWNERDRAW = 0x8000;

  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    ApplyOwnerHeader();
  }
  void ApplyOwnerHeader() {
    try {
      IntPtr hdr = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
      if (hdr == IntPtr.Zero) return;
      for (int i = 0; i < Columns.Count; i++) {
        HDITEM hd = new HDITEM();
        hd.mask = 0x4;
        SendMessageItem(hdr, HDM_GETITEMW, (IntPtr)i, ref hd);
        hd.fmt |= HDF_OWNERDRAW;
        SendMessageItem(hdr, HDM_SETITEMW, (IntPtr)i, ref hd);
      }
    } catch { }
  }

  protected override void WndProc(ref Message m) {
    if (m.Msg == WM_DRAWITEM) {
      try {
        DRAWITEMSTRUCT dis = (DRAWITEMSTRUCT)Marshal.PtrToStructure(m.LParam, typeof(DRAWITEMSTRUCT));
        if (dis.CtlType == 100) {
          IntPtr buf = Marshal.AllocHGlobal(512);
          try {
            HDITEM hd = new HDITEM();
            hd.mask = 0x2;
            hd.pszText = buf;
            hd.cchTextMax = 255;
            SendMessageItem(dis.hwndItem, HDM_GETITEMW, (IntPtr)dis.itemID, ref hd);
            string text = Marshal.PtrToStringUni(buf);
            using (Graphics g = Graphics.FromHdc(dis.hDC)) {
              int w = dis.rcItem.R - dis.rcItem.L + 1, h = dis.rcItem.B - dis.rcItem.T + 1;
              using (SolidBrush bg = new SolidBrush(Theme.CardLiteSolid)) g.FillRectangle(bg, dis.rcItem.L, dis.rcItem.T, w, h);
              using (Pen p = new Pen(Theme.Line)) {
                g.DrawLine(p, dis.rcItem.L, dis.rcItem.B - 1, dis.rcItem.R, dis.rcItem.B - 1);
                g.DrawLine(p, dis.rcItem.R - 1, dis.rcItem.T, dis.rcItem.R, dis.rcItem.B);
              }
              TextRenderer.DrawText(g, I18n.T(text == null ? "" : text), Theme.UI,
                new Rectangle(dis.rcItem.L + 6, dis.rcItem.T, w - 8, h), Theme.Gold,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            m.Result = (IntPtr)1;
            return;
          } finally { Marshal.FreeHGlobal(buf); }
        }
      } catch { }
    }
    if (m.Msg == WM_NOTIFY) {
      try {
        IntPtr hdr = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        NMHDR nh = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR));
        if (hdr != IntPtr.Zero && nh.hwndFrom == hdr && nh.code == NM_CUSTOMDRAW) {
          NMCUSTOMDRAW cd = (NMCUSTOMDRAW)Marshal.PtrToStructure(m.LParam, typeof(NMCUSTOMDRAW));
          if (cd.dwDrawStage == CDDS_PREPAINT) { m.Result = (IntPtr)CDRF_NOTIFYITEMDRAW; return; }
          if (cd.dwDrawStage == CDDS_ITEMPREPAINT) {
            IntPtr buf = Marshal.AllocHGlobal(512);
            try {
              HDITEM hd = new HDITEM();
              hd.mask = 0x2 | 0x4;
              hd.pszText = buf;
              hd.cchTextMax = 255;
              SendMessageItem(hdr, HDM_GETITEMW, cd.dwItemSpec, ref hd);
              string text = Marshal.PtrToStringUni(buf);
              using (Graphics g = Graphics.FromHdc(cd.hdc)) {
                int w = cd.rc.R - cd.rc.L + 1, h = cd.rc.B - cd.rc.T + 1;
                using (SolidBrush bg = new SolidBrush(Theme.CardLiteSolid)) g.FillRectangle(bg, cd.rc.L, cd.rc.T, w, h);
                using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, cd.rc.L, cd.rc.B, cd.rc.R, cd.rc.B);
                TextRenderer.DrawText(g, I18n.T(text == null ? "" : text), Theme.UI,
                  new Rectangle(cd.rc.L + 6, cd.rc.T + 1, w - 8, h - 2), Theme.Gold,
                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
              }
              m.Result = (IntPtr)CDRF_SKIPDEFAULT;
              return;
            } finally { Marshal.FreeHGlobal(buf); }
          }
        }
      } catch { }
    }
    base.WndProc(ref m);
  }
}

} // namespace
