// ============================================================================
// WhatsNewForm.cs — 「开发者信息 + 本版更新」弹窗（2026-10-09 新增）
//
// 触发时机：**每个版本第一次运行**弹一次（MainForm.OnShown → MaybeShowWhatsNew）。
//   "看过没有"记在 ib3_update.ini 的 notesver= —— 与更新器共用同一个状态文件（它本来
//   就是"非破坏式 key=value + SaveState 自带全套键"的写法，见 Update.cs:109-166），
//   不为这一条另开第 7 个 ini。
//
// ★ 只显示**一句话摘要**，不显示整段 changelog（作者 2026-10-09 定：弹窗要短）。
//   摘要的来源仍是仓库根的 CHANGELOG.md（build.sh 把它内嵌进 exe：
//   -resource:../../CHANGELOG.md,changelog），**约定**：每个版本段的正文第一行写成
//   > 新增…；修改…；删除…
//   本框只取这一行。这样"发版写说明"依旧只有 CHANGELOG.md 一处，不会两处说法不一致；
//   想看细节点「完整更新日志」（开浏览器）。
//   老版本段没有这行就显示一句明确提示，不编内容。
//
// ★ 不默认全选：多行只读 TextBox 拿到焦点时会**自动全选**，一进来整片高亮很难看
//   （作者反馈）。这里 TabStop=false + OnShown 里把选区清零并把焦点交给「知道了」按钮。
//
// 排版沿用 AboutForm：无边框 + 浅底(BG)配深字(Ink) + 左侧金线；摘要区是
//   **深卡(CardSolid)配浅字(Text)** —— 对比度规律见 AboutForm 头部注释的实测数据。
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

  TextBox box;          // 摘要区（只读）
  Button ok;

  public WhatsNewForm(Form owner, string versionLabel, string summary) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    KeyPreview = true;
    BackColor = Theme.BG;
    ForeColor = Theme.Ink;              // 浅底配深字
    Font = Theme.UI;
    ClientSize = new Size(600, 256);

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
    // 项目主页：金色 = 可点（与正文的深棕区分）。点它开浏览器。
    Controls.Add(LinkRow(I18n.T("项目主页"), "github.com/Tongyonyuen/ib3-trainer", y,
      delegate { OpenUrl(Updater.REPO_URL); }));

    y += 30;
    Label cap = new Label();
    cap.Text = I18n.T("本版更新说明");
    cap.ForeColor = Theme.Ink;
    cap.BackColor = Color.Transparent;
    cap.Font = Theme.UI;
    cap.SetBounds(20, y, 560, 18);
    Controls.Add(cap);

    box = new TextBox();
    box.Multiline = true;
    box.ReadOnly = true;
    box.WordWrap = true;
    box.ScrollBars = ScrollBars.None;    // 只放一句话，不需要滚动条
    box.TabStop = false;                 // ★ 别让它拿焦点（见文件头：多行框获焦会全选）
    box.BorderStyle = BorderStyle.None;
    box.BackColor = Theme.CardSolid;     // 深卡配浅字
    box.ForeColor = Theme.Text;
    box.Font = Theme.UI;
    box.SetBounds(20, y + 20, 560, 62);
    box.Text = string.IsNullOrEmpty(summary)
      ? I18n.T("（本版没写一句话摘要——详情见仓库 CHANGELOG.md）")
      : summary;
    box.SelectionStart = 0;              // 光标/选区归零，进来就是干净的一片
    box.SelectionLength = 0;
    Controls.Add(box);

    Label hint = new Label();
    hint.Text = I18n.T("每版首次运行提示一次");
    hint.ForeColor = Theme.TextDim;
    hint.BackColor = Color.Transparent;
    hint.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    hint.SetBounds(20, 196, 560, 18);
    Controls.Add(hint);

    Button all = Theme.MkButton(I18n.T("完整更新日志"), 320, 214, 140, 30,
      delegate { OpenUrl(Updater.REPO_URL + "/blob/main/CHANGELOG.md"); });
    Controls.Add(all);

    ok = Theme.MkButton(I18n.T("知道了"), 470, 214, 110, 30, delegate { Close(); });
    Controls.Add(ok);

    KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
    // 点空白处也关（与浮窗/关于框习惯一致）。子控件自己处理鼠标消息，不会冒泡到这里。
    MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Close(); };

    if (owner != null) {
      Location = new Point(owner.Left + (owner.Width - Width) / 2,
                           owner.Top + (owner.Height - Height) / 2);
    }
  }

  // 焦点给按钮、摘要区选区清零 —— 双保险，防止"一进来整片全选"（TabStop 只管 Tab 路径）
  protected override void OnShown(EventArgs e) {
    base.OnShown(e);
    try {
      if (ok != null && ok.CanSelect) ok.Select();
      if (box != null) { box.SelectionStart = 0; box.SelectionLength = 0; }
    } catch { }
  }

  // ---------- 内嵌 CHANGELOG.md → 本版那一句话摘要 ----------

  // 找不到内嵌资源 / 找不到本版段 / 本版段没写 "> " 摘要 ⇒ 返回 null（调用方给明确提示，不编内容）
  public static string LoadSummary(string semver) {
    try {
      string[] names = typeof(WhatsNewForm).Assembly.GetManifestResourceNames();
      for (int i = 0; i < names.Length; i++) {
        if (names[i].IndexOf("changelog", StringComparison.OrdinalIgnoreCase) < 0) continue;
        using (Stream st = typeof(WhatsNewForm).Assembly.GetManifestResourceStream(names[i])) {
          if (st == null) continue;
          using (StreamReader sr = new StreamReader(st, Encoding.UTF8)) {
            return ExtractSummary(sr.ReadToEnd(), semver);
          }
        }
      }
    } catch { }
    return null;
  }

  // 在 "## [v1.1.5] — 日期" 段的正文里取第一行 "> …"（约定：一句话写清新增/修改/删除）。
  // 版本匹配容忍开头的 v（CHANGELOG 一律写 vX.Y.Z，而传进来的是 SemVer）。
  static string ExtractSummary(string md, string semver) {
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

    // ★ 摘要可能被排版成**连续多行**引用块（作者写起来更好读），所以第一行 ">" 只是开始：
    //   把紧随其后的 ">" 行一路拼起来，遇到非空非 ">" 行就收工。
    StringBuilder sb = new StringBuilder();
    for (int i = start; i < lines.Length; i++) {
      string s = lines[i].Trim();
      if (s.StartsWith("## [", StringComparison.Ordinal)) break;         // 到下一版了
      if (s.StartsWith(">", StringComparison.Ordinal)) {
        string part = s.TrimStart('>').Trim().Replace("**", "");
        if (part.Length > 0) sb.Append(part);
        continue;
      }
      if (s.Length == 0) continue;      // 引用块内部的空行/行尾空行：跳过
      break;                            // 摘要后面的正文（"- …"）→ 收工
    }
    string sum = sb.ToString().Trim();
    return sum.Length == 0 ? null : sum;
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
