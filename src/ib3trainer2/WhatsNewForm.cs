// ============================================================================
// WhatsNewForm.cs — 「开发者信息 + 本版更新说明」弹窗（2026-10-09 新增）
//
// 触发时机：**每个版本第一次运行**弹一次（MainForm.OnShown → MaybeShowWhatsNew）。
//   "看过没有"记在 ib3_update.ini 的 notesver= —— 与更新器共用同一个状态文件（它本来
//   就是"非破坏式 key=value + SaveState 自带全套键"的写法，见 Update.cs:109-166），
//   不为这一条另开第 7 个 ini。
//
// 更新说明**不另写一份**：把仓库根的 CHANGELOG.md 内嵌进 exe（build.sh 的
//   -resource:../../CHANGELOG.md,changelog），运行时切出本版那一段。这样"发版写更新
//   说明"仍然只有**一处**（CHANGELOG.md），不会出现 exe 内文案与仓库里不一致。
//   切不出来（例如单独拷一份 exe 出来、或版本段还没写）就退回一句明确提示 —— 不装懂。
//
// 排版沿用 AboutForm：无边框 + 浅底(BG)配深字(Ink) + 左侧金线；说明区是
//   **深卡(CardSolid)配浅字(Text)** —— 这条对比度规律见 AboutForm 头部注释的实测数据。
//
// C# 5：不能用字符串插值 / ?. / out var / 表达式体成员。
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

class WhatsNewForm : Form {

  public WhatsNewForm(Form owner, string versionLabel, string notes) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    KeyPreview = true;
    BackColor = Theme.BG;
    ForeColor = Theme.Ink;              // 浅底配深字
    Font = Theme.UI;
    ClientSize = new Size(600, 540);

    Label t = new Label();
    t.Text = I18n.T("开发者信息") + " · " + versionLabel;
    t.Font = FontBank.Get("Microsoft YaHei", FontStyle.Bold, 12f);
    t.ForeColor = Theme.Gold;
    t.BackColor = Color.Transparent;
    t.SetBounds(20, 16, 560, 26);
    Controls.Add(t);

    int y = 52;
    Controls.Add(Row(I18n.T("作者"), "Andrew Tong", y));
    y += 24;
    Controls.Add(Row(I18n.T("本版更新说明"), I18n.T("每版首次运行提示一次"), y));
    y += 24;
    // 项目主页：金色 = 可点（与正文的深棕区分）。点它开浏览器。
    Controls.Add(LinkRow(I18n.T("项目主页"), "github.com/Tongyonyuen/ib3-trainer", y,
      delegate { OpenUrl(Updater.REPO_URL); }));
    y += 30;

    // 说明区：只读多行 + 纵向滚动条 + 自动换行（滚动条 + WordWrap 同时成立，超长行会折）
    TextBox box = new TextBox();
    box.Multiline = true;
    box.ReadOnly = true;
    box.ScrollBars = ScrollBars.Vertical;
    box.WordWrap = true;
    box.BorderStyle = BorderStyle.None;
    box.BackColor = Theme.CardSolid;     // 深卡配浅字
    box.ForeColor = Theme.Text;
    box.Font = Theme.UI;
    box.SetBounds(20, y, 560, 306);
    box.Text = string.IsNullOrEmpty(notes)
      ? I18n.T("（这一版没有内嵌更新说明——见仓库根目录的 CHANGELOG.md）")
      : notes;
    Controls.Add(box);

    Button all = Theme.MkButton(I18n.T("完整更新日志"), 320, 496, 140, 30,
      delegate { OpenUrl(Updater.REPO_URL + "/blob/main/CHANGELOG.md"); });
    Controls.Add(all);

    Button ok = Theme.MkButton(I18n.T("知道了"), 470, 496, 110, 30,
      delegate { Close(); });
    Controls.Add(ok);

    KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
    // 点空白处也关（与浮窗/关于框习惯一致）。子控件自己处理鼠标消息，不会冒泡到这里。
    MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Close(); };

    if (owner != null) {
      Location = new Point(owner.Left + (owner.Width - Width) / 2,
                           owner.Top + (owner.Height - Height) / 2);
    }
  }

  // ---------- 内嵌 CHANGELOG.md → 本版那一段 ----------

  // 找不到内嵌资源 / 找不到本版段 ⇒ 返回 null（调用方给一句明确提示，不编内容）
  public static string LoadNotes(string semver) {
    try {
      string[] names = typeof(WhatsNewForm).Assembly.GetManifestResourceNames();
      for (int i = 0; i < names.Length; i++) {
        if (names[i].IndexOf("changelog", StringComparison.OrdinalIgnoreCase) < 0) continue;
        using (Stream st = typeof(WhatsNewForm).Assembly.GetManifestResourceStream(names[i])) {
          if (st == null) continue;
          using (StreamReader sr = new StreamReader(st, Encoding.UTF8)) {
            return ExtractSection(sr.ReadToEnd(), semver);
          }
        }
      }
    } catch { }
    return null;
  }

  // 取 "## [v1.1.5] — 日期" 到下一个 "## [" 之间的正文。
  // 版本匹配容忍开头的 v（CHANGELOG 一律写 vX.Y.Z，而传进来的是 SemVer）。
  static string ExtractSection(string md, string semver) {
    if (md == null || semver == null) return null;
    string want = "## [v" + semver;
    string want2 = "## [" + semver;
    string[] lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    int start = -1;
    for (int i = 0; i < lines.Length; i++) {
      string s = lines[i].Trim();
      if (s.StartsWith(want, StringComparison.OrdinalIgnoreCase) ||
          s.StartsWith(want2, StringComparison.OrdinalIgnoreCase)) { start = i + 1; break; }
    }
    if (start < 0) return null;

    StringBuilder sb = new StringBuilder();
    for (int i = start; i < lines.Length; i++) {
      string s = lines[i];
      if (s.TrimStart().StartsWith("## [", StringComparison.Ordinal)) break;   // 下一版开始
      sb.AppendLine(Clean(s));
    }
    string body = sb.ToString().Trim();
    return body.Length == 0 ? null : body;
  }

  // 极简 markdown 去噪：只去掉 ** 强调（本项目 CHANGELOG 只用到这一种行内标记），
  // 其余（"- " 列表、缩进、空行）原样保留 —— 不追求渲染，只求读得顺、不显示星号。
  static string Clean(string s) {
    if (s == null) return "";
    return s.Replace("**", "");
  }

  // ---------- 排版小工具（与 AboutForm 同款） ----------

  Label Row(string k, string v, int y) {
    Label l = new Label();
    l.Text = k + "：" + v;
    l.ForeColor = Theme.Ink;
    l.BackColor = Color.Transparent;
    l.Font = Theme.UI;
    l.SetBounds(20, y, 560, 20);
    return l;
  }

  Label LinkRow(string k, string v, int y, EventHandler onClick) {
    Label l = Row(k, v, y);
    l.ForeColor = Theme.Gold;
    l.Cursor = Cursors.Hand;
    if (onClick != null) l.Click += onClick;
    return l;
  }

  // 只放行 https（URL 只交给 ShellExecute → 默认浏览器，同 AboutForm.cs / Launcher.cs）
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

  // 无边框窗不进 Alt-Tab（与 ToastForm / BusyOverlay / AboutForm 一致）
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
      return cp;
    }
  }
}

} // namespace
