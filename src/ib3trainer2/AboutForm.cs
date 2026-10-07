// ============================================================================
// AboutForm.cs — 「关于」弹窗（2026-10-07 新增）
//
// 作者信息有两处：标题条右侧一行署名（Ib3Trainer2.cs 的 BuildChrome），点它开这个框。
// 样式沿用本项目的扁平自绘风格（无边框 + 深色卡片 + 金线，见 Toast.cs / BusyOverlay.cs）。
//
// 注意：本框是**用户主动点击才打开**的窗口，与既有 FolderBrowserDialog/SaveFileDialog 同类；
// 项目里"绝不抢焦点"的约定只约束 ToastMgr 浮窗，不约束这里。
//
// 文案在**打开时**按当前语言取（每次 new 一个，所以切换语言后重开会跟着变）。
//
// ★ 配色修正（2026-10-08）：本框原来是 BackColor=Theme.BG（浅羊皮纸 244,238,223）
//   配 ForeColor=Theme.Text（浅 232,222,202）—— 对比度只有 **1.15:1**，文字几乎不可见
//   （WCAG 正文要求 4.5:1）。改成 Theme.Ink（深棕 90,78,58）= **7.02:1**。
//   规律：**浅底(BG)配深字(Ink)，深卡(CardSolid)配浅字(Text)**。
//
// C# 5：不能用字符串插值 / ?. / out var / 表达式体成员。
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Ib3Trainer2 {

class AboutForm : Form {
  // 「检查更新」只置位，由调用方（MainForm.ShowAbout）去跑 —— 这样 BusyShow / ToastMgr /
  // UpdateForm 都还在主窗口的上下文里，不会出现"子窗已关、父窗句柄没了"的时序问题。
  public bool CheckUpdateRequested;

  public AboutForm(Form owner) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    KeyPreview = true;
    BackColor = Theme.BG;
    ForeColor = Theme.Ink;          // ★ 浅底配深字
    Font = Theme.UI;
    ClientSize = new Size(460, 250);

    int y = 16;
    Label t = new Label();
    t.Text = I18n.T("无尽之剑Ⅲ修改器");
    t.Font = FontBank.Get("Microsoft YaHei", FontStyle.Bold, 12f);
    t.ForeColor = Theme.Gold;
    t.BackColor = Color.Transparent;
    t.SetBounds(20, y, 420, 26);
    Controls.Add(t);

    y += 34;
    Controls.Add(Row(I18n.T("作者"), "Andrew Tong", y));
    y += 24;
    Controls.Add(Row(I18n.T("版本"), BuildInfo.Version, y));
    y += 24;
    Controls.Add(Row(I18n.T("界面语言"), I18n.IsEn ? "English" : "中文", y));
    y += 24;
    // 项目主页：金色 = 可点（与正文的深棕区分）。点它开浏览器。
    Controls.Add(LinkRow(I18n.T("项目主页"), "github.com/Tongyonyuen/ib3-trainer", y,
      delegate { OpenUrl(Updater.REPO_URL); }));

    y += 30;
    Label hint = new Label();
    hint.Text = I18n.T("单机游戏修改器，仅供个人离线使用。改动会写进存档，请先自行备份。");
    hint.ForeColor = Theme.Ink;
    hint.BackColor = Color.Transparent;
    hint.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    hint.SetBounds(20, y, 420, 40);
    Controls.Add(hint);

    // 检查更新（100 宽，右缘 340）+ 关闭（350 起）：10px 间隙，右侧留 20px
    Button chk = Theme.MkButton(I18n.T("检查更新"), 240, 206, 100, 28,
      delegate { CheckUpdateRequested = true; Close(); });
    Controls.Add(chk);

    Button close = Theme.MkButton(I18n.T("关闭"), 350, 206, 90, 28,
      delegate { Close(); });
    Controls.Add(close);

    KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
    // 点空白处也关（与浮窗习惯一致）。子控件自己处理鼠标消息、不会冒泡到这里，
    // 所以点"项目主页"只会开浏览器，不会顺手把窗口关掉。
    MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Close(); };

    if (owner != null) {
      Location = new Point(owner.Left + (owner.Width - Width) / 2,
                           owner.Top + (owner.Height - Height) / 2);
    }
  }

  // 一行 "标签：值"
  Label Row(string k, string v, int y) {
    Label l = new Label();
    l.Text = k + "：" + v;
    l.ForeColor = Theme.Ink;        // 浅底深字
    l.BackColor = Color.Transparent;
    l.Font = Theme.UI;
    l.SetBounds(20, y, 420, 20);
    return l;
  }

  // 与 Row 同排版，但金色 + 手型光标 + 可点
  Label LinkRow(string k, string v, int y, EventHandler onClick) {
    Label l = Row(k, v, y);
    l.ForeColor = Theme.Gold;
    l.Cursor = Cursors.Hand;
    if (onClick != null) l.Click += onClick;
    return l;
  }

  // 只放行 https（URL 只能交给 ShellExecute → 默认浏览器，同 Launcher.cs:124-127）
  static void OpenUrl(string url) {
    if (url == null || url.IndexOf("https://", StringComparison.OrdinalIgnoreCase) != 0) return;
    try {
      ProcessStartInfo psi = new ProcessStartInfo(url);
      psi.UseShellExecute = true;
      Process.Start(psi);
    } catch { }
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    using (Pen p = new Pen(Theme.Line, 1f)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    using (SolidBrush b = new SolidBrush(Theme.Gold)) e.Graphics.FillRectangle(b, 0, 0, 4, Height);
  }

  // 无边框窗不进 Alt-Tab（与 ToastForm / BusyOverlay 一致）
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
      return cp;
    }
  }
}

} // namespace
