// ============================================================================
// BusyOverlay.cs — 无感浮动进度窗
//
// 用途：初始化 / 扫描 / 读取背包这类耗时操作期间，在主窗中央浮一个小窗，
//       避免用户以为「卡了」而反复点击。（日志区在下方，操作时看不到。）
//
// 设计要点：
//   - 无边框 + TopMost + ShowWithoutActivation：不抢焦点、不打断游戏
//   - 居中于训练器主窗；主窗还没显示时退化为屏幕居中
//   - 用计数器支持嵌套调用（BusyShow/BusyHide 成对出现，内层不会提前关掉外层）
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Ib3Trainer2 {

class BusyOverlay : Form {
  Label lbl;
  ProgressBar bar;

  public BusyOverlay() {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    TopMost = true;
    // ★ 配色（2026-10-08 修正）：原来是 BackColor=Theme.BG（浅羊皮纸 244,238,223）配
    //   ForeColor=Theme.Text（浅 232,222,202）—— 对比度只有 **1.15:1**。而这是用户启动时
    //   第一眼看到的东西（"正在初始化…"），文字几乎不可见。改为 Theme.Ink（深棕）= **7.02:1**。
    //   规律：**浅底(BG)配深字(Ink)，深卡(CardSolid)配浅字(Text)**。
    BackColor = Theme.BG;
    ForeColor = Theme.Ink;
    Size = new Size(380, 98);
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

    lbl = new Label();
    lbl.SetBounds(18, 16, 344, 40);
    lbl.ForeColor = Theme.Ink;
    lbl.BackColor = Color.Transparent;
    lbl.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
    lbl.TextAlign = ContentAlignment.MiddleLeft;
    lbl.Text = "处理中…";
    Controls.Add(lbl);

    bar = new ProgressBar();
    bar.SetBounds(18, 62, 344, 16);
    bar.Style = ProgressBarStyle.Marquee;
    bar.MarqueeAnimationSpeed = 30;
    Controls.Add(bar);
  }

  protected override bool ShowWithoutActivation { get { return true; } }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    using (Pen p = new Pen(Theme.Gold, 2)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
  }

  public void SetText(string s) {
    try { lbl.Text = s; lbl.Refresh(); } catch { }
  }

  public void CenterOn(Form owner) {
    try {
      if (owner != null && owner.IsHandleCreated && owner.Visible) {
        Point sc = owner.PointToScreen(new Point(0, 0));
        Location = new Point(sc.X + (owner.Width - Width) / 2, sc.Y + (owner.Height - Height) / 2);
      } else {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
      }
    } catch { }
  }
}

partial class MainForm {
  BusyOverlay busyOverlay;
  int busyDepth = 0;

  // 显示/更新浮窗。可嵌套，必须与 BusyHide 成对。
  public void BusyShow(string what) {
    try {
      if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { BusyShow(what); }); return; }
      busyDepth++;
      if (busyOverlay == null) busyOverlay = new BusyOverlay();
      // ⚠ 这个浮窗是**固定 380×98、标签 344px**（不像浮窗提示会自适应）⇒ 英文要短，
      //   字典里这几条（校验/打包/注入/初始化…）都压到很短
      busyOverlay.SetText(I18n.T(what) + "…");
      busyOverlay.CenterOn(this);
      if (!busyOverlay.Visible) busyOverlay.Show();
      busyOverlay.BringToFront();
      Application.DoEvents();      // 让浮窗立刻画出来，别排在消息队列后面
    } catch { }
  }

  public void BusyHide() {
    try {
      if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { BusyHide(); }); return; }
      busyDepth--;
      if (busyDepth <= 0) {
        busyDepth = 0;
        if (busyOverlay != null && busyOverlay.Visible) busyOverlay.Hide();
      }
    } catch { }
  }

  // 强制关闭（异常兜底 / 窗体关闭时）
  public void BusyHideAll() {
    try {
      busyDepth = 0;
      if (busyOverlay != null && busyOverlay.Visible) busyOverlay.Hide();
    } catch { }
  }
}

} // namespace
