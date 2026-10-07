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
// C# 5：不能用字符串插值 / ?. / out var / 表达式体成员。
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Ib3Trainer2 {

class AboutForm : Form {
  public AboutForm(Form owner) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    KeyPreview = true;
    BackColor = Theme.BG;
    ForeColor = Theme.Text;
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

    y += 30;
    Label hint = new Label();
    hint.Text = I18n.T("单机游戏修改器，仅供个人离线使用。改动会写进存档，请先自行备份。");
    hint.ForeColor = Theme.TextDim;
    hint.BackColor = Color.Transparent;
    hint.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    hint.SetBounds(20, y, 420, 40);
    Controls.Add(hint);

    Button close = Theme.MkButton(I18n.T("关闭"), 350, 206, 90, 28,
      delegate { Close(); });
    Controls.Add(close);

    KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
    // 点空白处也关（与浮窗习惯一致）
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
    l.ForeColor = Theme.Text;
    l.BackColor = Color.Transparent;
    l.Font = Theme.UI;
    l.SetBounds(20, y, 420, 20);
    return l;
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
