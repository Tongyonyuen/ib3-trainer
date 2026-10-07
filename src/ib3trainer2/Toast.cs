// ============================================================================
// Toast.cs — 零焦点浮窗提示（WS_EX_NOACTIVATE + ShowWithoutActivation）
// 规范：绝不调用 Activate/SetForegroundWindow/BringWindowToTop/ShowDialog。
// 最多 3 条垂直堆叠；指引 3.2s / 警告 4.5s；位置=游戏窗口底边居中（或屏幕右下）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Ib3Trainer2 {

static class ToastMgr {
  public static IntPtr GameHwnd = IntPtr.Zero;   // 由主窗体维护
  public static Form TrainerForm = null;         // 定位兜底
  public static int PosMode = 0;                 // 0=游戏窗口内底部居中 1=屏幕右下
  static readonly List<ToastForm> live = new List<ToastForm>();

  public static void Show(string msg) { Show(msg, false); }
  public static void Warn(string msg) { Show(msg, true); }

  static void Show(string msg, bool warn) {
    try {
      // 只在入口翻一次：Show/Warn 是全部 124 处提示的唯一收口。
      // 浮窗自己是按 MeasureString 自适应宽高的（见下方 ToastForm），所以英文变长不会截断。
      // 字典未命中 → 原样返回（组合出来的动态句子就保持中文，不再深挖）。
      msg = I18n.T(msg);
      for (int i = live.Count - 1; i >= 0; i--) { if (!live[i].Visible) live.RemoveAt(i); }
      if (live.Count >= 3) { try { live[0].Close(); } catch { } live.RemoveAt(0); }
      ToastForm t = new ToastForm(msg, warn);
      t.PositionAt(live.Count);
      live.Add(t);
      t.Show();
    } catch { }
  }
}

class ToastForm : Form {
  readonly Timer life;

  public ToastForm(string msg, bool warn) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    TopMost = true;
    BackColor = Color.FromArgb(26, 23, 20);
    if (warn) Name = "warn";
    Font font = new Font("Microsoft YaHei", 10.5f);
    Size textSize;
    using (Graphics g = CreateGraphics()) { textSize = Size.Ceiling(g.MeasureString(msg, font, 520)); }
    int w = Math.Min(560, Math.Max(300, textSize.Width + 58));
    int h = Math.Max(58, textSize.Height + 26);
    Size = new Size(w, h);
    Label l = new Label();
    l.Dock = DockStyle.Fill;
    l.Text = msg;
    l.ForeColor = Color.FromArgb(242, 232, 212);
    l.TextAlign = ContentAlignment.MiddleLeft;
    l.Font = font;
    l.BackColor = Color.Transparent;
    l.Padding = new Padding(22, 8, 16, 8);
    Controls.Add(l);
    life = new Timer();
    life.Interval = warn ? 4500 : 3200;
    life.Tick += delegate { Close(); };
    life.Start();
  }

  public void PositionAt(int slot) {
    int w = Width, h = Height;
    int x, y;
    if (ToastMgr.PosMode == 0 && ToastMgr.GameHwnd != IntPtr.Zero && Win32.IsWindow(ToastMgr.GameHwnd)) {
      Win32.RECT r;
      if (Win32.GetWindowRect(ToastMgr.GameHwnd, out r) && r.R > r.L && r.B > r.T) {
        x = r.L + (r.R - r.L) / 2 - w / 2;
        y = r.B - 130 - h - slot * (h + 8);
      } else { x = 40; y = 40 + slot * (h + 8); }
    } else {
      Rectangle wa = Screen.PrimaryScreen.WorkingArea;
      x = wa.Right - w - 24;
      y = wa.Bottom - h - 24 - slot * (h + 8) - 60;
    }
    Location = new Point(x, y);
  }

  protected override bool ShowWithoutActivation { get { return true; } }
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE：弹出时不夺游戏焦点
      cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW：不进 Alt-Tab
      return cp;
    }
  }
  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    using (Pen p = new Pen(Color.FromArgb(80, 206, 172, 96)))
      e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    Color accent = (Name == "warn") ? Color.FromArgb(206, 92, 58) : Color.FromArgb(212, 175, 55);
    using (SolidBrush b = new SolidBrush(accent))
      e.Graphics.FillRectangle(b, 0, 0, 4, Height);
  }
}

} // namespace
