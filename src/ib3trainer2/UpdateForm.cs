// ============================================================================
// UpdateForm.cs — 更新提示弹窗（UpdateForm）+ 调度/检查/询问（UpdateUI）
//
// 为什么不用 Toast：ToastForm 结构上就是**单个 Label**（Toast.cs:54-62），挂不了按钮，
//   也表达不了"现在更新 / 跳过此版本 / 以后再说"三选一 + 多行更新说明。
// 为什么不用 MessageBox：它表达不了三个选项，样式也脱开了本项目自绘的暗金卡片风格
//   （AboutForm.cs:2-9 就是为逃离系统样式才写的）。
// BusyOverlay 只用于"正在检查 / 正在下载"两个瞬时态（它没有按钮，见 BusyOverlay.cs:29）。
//
// 配色沿用 Theme 的设计语言：浅色羊皮纸底（BG）上放**深色卡片**（CardSolid），
//   卡上写浅字（Text），纸上直接写深棕字（Ink）。
//   ⚠ 不要照抄 AboutForm 的 `BackColor=BG + ForeColor=Text` —— 那是浅底浅字，几乎看不见。
//
// 「绝不抢焦点」的约定只约束 ToastMgr（Toast.cs:86-94）；本框与 AboutForm 一样是
// **用户主动点击才打开**的模态窗，ShowDialog 是正当的（AboutForm.cs:6-9 已明确过这一点）。
//
// C# 5：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Ib3Trainer2 {

// ---------------------------------------------------------------------------
// UpdateUI — 调度 / 检查 / 询问 / 下载。可以碰 WinForms 与 I18n 的地方都放这里，
// 好让 Updater.cs 保持"纯逻辑、可单独编译自测"。
// ---------------------------------------------------------------------------
static class UpdateUI {
  static bool inFlight = false;
  static Timer oneShot = null;

  // 从 MainForm.OnShown 调用。用一次性 Timer（3 秒）而不是直接在 ctor 里起线程：
  // ctor 末尾还在 BusyShow 里（Ib3Trainer2.cs:101-152），此刻弹任何东西都会叠在浮窗上。
  public static void ScheduleAutoCheck(MainForm f) {
    if (f == null) return;
    try {
      oneShot = new Timer();
      oneShot.Interval = 3000;
      oneShot.Tick += delegate {
        try { oneShot.Stop(); oneShot.Dispose(); oneShot = null; } catch { }
        CheckNow(f, true);
      };
      oneShot.Start();
    } catch { }
  }

  public static void StopTimer() {
    try { if (oneShot != null) { oneShot.Stop(); oneShot.Dispose(); oneShot = null; } } catch { }
  }

  public static void CheckNow(MainForm f, bool auto) {
    if (f == null || inFlight) return;
    inFlight = true;
    string dir = AppDomain.CurrentDomain.BaseDirectory;
    UpdateState st = Updater.LoadState(dir);

    // 自动检查受 6h/1h 节流；手动检查永远真查（用户点了就得给结果）
    if (auto && !Updater.ShouldAutoCheck(st)) { inFlight = false; return; }

    System.Threading.Thread th = new System.Threading.Thread(delegate() {
      try { DoCheck(f, dir, st, auto); }
      catch (Exception ex) { f.Log("更新检查失败: " + ex.Message); }
      finally { inFlight = false; }
    });
    th.IsBackground = true;
    th.Start();
  }

  static void DoCheck(MainForm f, string dir, UpdateState st, bool auto) {
    string newEtag, err;
    UpdateInfo info = Updater.Fetch(st, out newEtag, out err);

    if (info == null) {
      // 304 / 404 / 限流 都算"这次检查本身是成功的"（不是故障）⇒ ok=1，回到 6h 节奏
      if (err == Updater.E_NOTMODIFIED) {
        st.LastCheck = Updater.NowIso(); st.Ok = 1; Updater.SaveState(dir, st);
        f.Log("更新检查：内容未变（304）");
        return;
      }
      if (err == Updater.E_NOREPO) {
        st.LastCheck = Updater.NowIso(); st.Ok = 1; Updater.SaveState(dir, st);
        f.Log("更新检查：仓库尚未发布任何正式版本");
        if (!auto) f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Show("已是最新版本"); });
        return;
      }
      if (err == Updater.E_RATELIMIT) {
        st.LastCheck = Updater.NowIso(); st.Ok = 1; Updater.SaveState(dir, st);
        f.Log("更新检查：已触发 GitHub 速率限制，本次跳过");
        if (!auto) f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("检查更新失败"); });
        return;
      }
      st.LastCheck = Updater.NowIso(); st.Ok = 0; Updater.SaveState(dir, st);
      f.Log("更新检查失败: " + err);
      if (!auto) f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("检查更新失败"); });
      return;
    }

    st.LastCheck = Updater.NowIso(); st.Ok = 1;
    if (!string.IsNullOrEmpty(newEtag)) st.ETag = newEtag;
    Updater.SaveState(dir, st);

    if (st.DevDump == 1) {
      f.Log("更新检查[devdump]：tag=" + info.Tag + " 资产=" + (info.AssetName == null ? "(无)" : info.AssetName) +
            " 大小=" + info.AssetSize + " sha256=" + (info.AssetSha256 == null ? "(无)" : info.AssetSha256));
    }

    // 旧标签（r13 这类）解析不出来 ⇒ 静默放弃，**永不提示**（防止把旧包当新版本推）
    int maj, min, pat;
    if (!Updater.TryParseTag(info.Tag, out maj, out min, out pat)) {
      f.Log("更新检查：远端 tag「" + info.Tag + "」不是语义化版本，已跳过");
      if (!auto) f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("检查更新失败"); });
      return;
    }

    if (!Updater.IsNewer(maj, min, pat)) {
      f.Log("更新检查：已是最新（远端 " + info.Tag + "，本地 v" + Updater.LocalSemVer + "）");
      if (!auto) f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Show("已是最新版本"); });
      return;
    }

    // ★ 防"无限更新循环"。走到这里说明远端 tag 比本地自称的版本新，但**若这个 tag 其实已经装过**，
    //   那根因就是"二进制自称的版本与 tag 不一致"（v1.1.1 就发生过：tag=v1.1.1 而 exe 里
    //   BuildInfo 还是 1.1.0）。此时继续提示只会让用户反复下载同一份 exe。
    //   有了 applied 记录，无论二进制怎么自称，同一个 tag 只装一次。
    if (!string.IsNullOrEmpty(st.Applied) && st.Applied == info.Tag) {
      f.Log("更新检查：远端 " + info.Tag + " 本机已经装过（本地自称 v" + Updater.LocalSemVer +
            "，与 tag 不符——那份二进制里的版本号可能写错了）——已跳过，不再反复提示");
      return;
    }

    // 只有"下载+替换"这条路才需要这些前置条件；不满足就退化成"打开下载页"
    bool manualOnly = false;
    string why = null;
    if (string.IsNullOrEmpty(info.AssetUrl)) { manualOnly = true; why = "无 .exe 资产"; }
    else if (Updater.HasDataRevBump(info.Notes)) { manualOnly = true; why = "数据文件也有更新"; }
    else if (!Updater.CanWriteDir(dir)) { manualOnly = true; why = "目录只读"; }
    else {
      string other = Updater.SameDirOtherInstance(Updater.RealExePath);
      if (other != null) { manualOnly = true; why = "另有实例在运行"; }
    }

    if (auto && Updater.IsSkipped(st, info.Tag)) {
      f.Log("更新检查：已被用户跳过（" + info.Tag + "）");
      return;
    }

    f.Log("更新检查：发现新版本 " + info.Tag + "（本地 v" + Updater.LocalSemVer + "）" +
          (manualOnly ? "，需手动下载" : ""));

    UpdateInfo fi = info;
    bool fm = manualOnly;
    string fw = why;
    f.BeginInvoke((MethodInvoker)delegate { Ask(f, fi, fm, fw); });
  }

  // UI 线程：弹窗三选一
  public static void Ask(MainForm f, UpdateInfo info, bool manualOnly, string why) {
    if (f == null || info == null) return;
    UpdateForm.Choice ch;
    try {
      using (UpdateForm d = new UpdateForm(f, info, manualOnly)) {
        d.ShowDialog(f);
        ch = d.Result;
      }
    } catch (Exception ex) {
      f.Log("更新弹窗打开失败: " + ex.Message);
      return;
    }

    string dir = AppDomain.CurrentDomain.BaseDirectory;
    if (ch == UpdateForm.Choice.Later) { f.Log("更新：用户选择稍后"); return; }
    if (ch == UpdateForm.Choice.Skip) {
      UpdateState st = Updater.LoadState(dir);
      st.Skip = info.Tag;
      Updater.SaveState(dir, st);
      f.Log("更新：用户跳过 " + info.Tag);
      return;
    }
    if (ch == UpdateForm.Choice.OpenPage) { OpenUrl(f, info.HtmlUrl); return; }

    if (manualOnly) { f.Log("更新：此版本需手动下载（" + why + "）"); OpenUrl(f, info.HtmlUrl); return; }
    StartDownload(f, dir, info);
  }

  static void StartDownload(MainForm f, string dir, UpdateInfo info) {
    UpdateState st = Updater.LoadState(dir);

    // swaptest=1：不下载，直接用程序目录里已有的 .new.exe 走一遍替换流程 ——
    // 用于在本地验证"改名替换 + 重启"这段，不需要真的去发一个新版本。
    if (st.SwapTest == 1) { StartSwapTest(f, dir, info); return; }

    f.BusyShow("正在下载新版本");
    f.Log("更新：开始下载 " + info.AssetName + "（" + info.AssetSize + " 字节）");

    System.Threading.Thread th = new System.Threading.Thread(delegate() {
      string err = null;
      byte[] data = null;
      string staged = null;
      try {
        data = Updater.DownloadAsset(info, out err);
        if (data == null) {
          f.BusyHide();
          f.Log("更新：下载失败 — " + err);
          f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("新版本下载失败"); });
          return;
        }

        string reason; bool hashSkipped;
        if (!Updater.VerifyPayload(data, info, out reason, out hashSkipped)) {
          f.BusyHide();
          f.Log("更新：校验失败 — " + reason + "（未写入任何文件）");
          f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("下载的文件校验失败，已放弃更新"); });
          return;
        }
        if (hashSkipped) f.Log("更新：⚠ 远端未提供 sha256 摘要（旧资产），已跳过哈希校验");

        // devnodl=1：走完下载与校验但不落盘 —— 用来验证"下载 + 校验"链路而不真的替换
        if (st.DevNoDownload == 1) {
          f.BusyHide();
          f.Log("更新[devnodl]：下载与校验均通过（" + data.Length + " 字节），按开关未写入");
          return;
        }

        staged = Updater.StagePayload(dir, data, out err);
        if (staged == null) {
          f.BusyHide();
          f.Log("更新：写入暂存文件失败 — " + err);
          f.BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn("新版本下载失败"); });
          return;
        }
        f.BusyHide();
        f.Log("更新：已校验并暂存 " + staged + "，准备替换");
      } catch (Exception ex) {
        try { f.BusyHide(); } catch { }
        f.Log("更新：异常 — " + ex.Message);
        return;
      }

      string sp = staged;
      f.BeginInvoke((MethodInvoker)delegate { Handoff(f, dir, info, sp); });
    });
    th.IsBackground = true;
    th.Start();
  }

  // swaptest=1 专用：把程序目录里**已经存在**的载荷当作"已校验的下载结果"，直接走替换。
  // 用途 = 在本地验证 .old 改名 / 助手等待父进程 / 重启这一段，不需要真去发一个新版本。
  // 仍然要求 MZ 头与最小体积 —— 免得拿一个垃圾文件把正在运行的自己换掉。
  static void StartSwapTest(MainForm f, string dir, UpdateInfo info) {
    string staged = Path.Combine(dir, Updater.NEW_NAME);
    if (!File.Exists(staged)) {
      f.Log("更新[swaptest]：程序目录里没有 " + Updater.NEW_NAME + "，请先放一份 exe 进去");
      ToastMgr.Warn("更新助手启动失败");
      return;
    }
    byte[] d;
    try { d = File.ReadAllBytes(staged); }
    catch (Exception ex) { f.Log("更新[swaptest]：读取载荷失败 — " + ex.Message); return; }
    if (d.Length < 100 * 1024 || d[0] != (byte)'M' || d[1] != (byte)'Z') {
      f.Log("更新[swaptest]：载荷不是可执行文件（" + d.Length + " 字节），已拒绝");
      ToastMgr.Warn("下载的文件校验失败，已放弃更新");
      return;
    }
    f.Log("更新[swaptest]：用本地载荷走替换流程（" + d.Length + " 字节；跳过下载与 sha256 核对）");
    Handoff(f, dir, info, staged);
  }

  // UI 线程：起助手 → 本程序退出 → 助手替换并重启
  static void Handoff(MainForm f, string dir, UpdateInfo info, string staged) {
    string real = Updater.RealExePath;
    if (string.IsNullOrEmpty(real)) {
      f.Log("更新：取不到自身路径，已放弃");
      ToastMgr.Warn("更新助手启动失败");
      return;
    }
    string err;
    if (!Updater.StartHelper(staged, real, info.Tag, out err)) {
      f.Log("更新：更新助手启动失败 — " + err + "（暂存文件已保留，下次可重试）");
      ToastMgr.Warn("更新助手启动失败");
      return;
    }
    f.Log("更新：已启动更新助手，本程序即将退出并替换文件…");
    f.Close();
  }

  public static void OpenUrl(MainForm f, string url) {
    if (!Updater.IsSafeUrl(url)) {
      f.Log("更新：拒绝打开非 https 链接（" + (url == null ? "空" : url) + "）");
      ToastMgr.Warn("打开浏览器失败");
      return;
    }
    try {
      ProcessStartInfo psi = new ProcessStartInfo(url);
      psi.UseShellExecute = true;      // URL 只能交给 ShellExecute → 默认浏览器（同 Launcher.cs:124-127）
      Process.Start(psi);
      f.Log("更新：已在浏览器打开 " + url);
    } catch (Exception ex) {
      f.Log("更新：打开浏览器失败 — " + ex.Message);
      ToastMgr.Warn("打开浏览器失败");
    }
  }
}

// ---------------------------------------------------------------------------
// UpdateForm — 460×250，与 AboutForm 同尺寸同画法（无边框 + 金线 + 左侧金条 + WS_EX_TOOLWINDOW）
// ---------------------------------------------------------------------------
class UpdateForm : Form {
  public enum Choice { Later, Skip, Go, OpenPage }
  public Choice Result = Choice.Later;

  const int PAD = 20;
  const int Y_TITLE = 16, Y_CUR = 50, Y_NEW = 74, Y_HINT = 98, Y_NOTES = 116, Y_BOTTOM = 192, Y_BTN = 206;

  public UpdateForm(Form owner, UpdateInfo info, bool manualOnly) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    KeyPreview = true;
    BackColor = Theme.BG;
    ForeColor = Theme.Ink;          // ★ 浅底要配深字（不是 Theme.Text）
    Font = Theme.UI;
    ClientSize = new Size(460, 250);

    Label t = new Label();
    t.Text = I18n.T("发现新版本");
    t.Font = FontBank.Get("Microsoft YaHei", FontStyle.Bold, 12f);
    t.ForeColor = Theme.Gold;
    t.BackColor = Color.Transparent;
    t.SetBounds(PAD, Y_TITLE, 420, 26);
    Controls.Add(t);

    Controls.Add(Row(I18n.T("当前版本"), "v" + Updater.LocalSemVer, Y_CUR));
    Controls.Add(Row(I18n.T("最新版本"), info.Tag, Y_NEW));

    Label nl = new Label();
    nl.Text = I18n.T("更新说明");
    nl.ForeColor = Theme.Ink;
    nl.BackColor = Color.Transparent;
    nl.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    nl.SetBounds(PAD, Y_HINT, 420, 16);
    Controls.Add(nl);

    // 说明区：**深色卡片 + 浅字**（Theme 的正牌配对），不是浅底浅字
    TextBox notes = new TextBox();
    notes.Multiline = true;
    notes.ReadOnly = true;
    notes.ScrollBars = ScrollBars.Vertical;
    notes.BorderStyle = BorderStyle.None;
    notes.BackColor = Theme.CardSolid;
    notes.ForeColor = Theme.Text;
    notes.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    notes.SetBounds(PAD, Y_NOTES, 420, 74);
    notes.Text = (info.Notes == null || info.Notes.Length == 0) ? I18n.T("（此版本没有写更新说明）") : info.Notes;
    notes.Select(0, 0);
    Controls.Add(notes);

    Label hint = new Label();
    hint.Text = manualOnly
      ? I18n.T("此版本无法自动更新，将为你打开下载页")
      : I18n.T("更新会重启修改器，当前锁定与地址表会丢失（游戏本身不受影响）");
    hint.ForeColor = Theme.Ink;
    hint.BackColor = Color.Transparent;
    hint.Font = FontBank.Get("Microsoft YaHei", FontStyle.Regular, 8.25f);
    hint.SetBounds(PAD, Y_BOTTOM, 420, 16);
    Controls.Add(hint);

    if (manualOnly) {
      Controls.Add(Theme.MkButton(I18n.T("打开下载页"), 30, Y_BTN, 100, 28, delegate {
        Result = Choice.OpenPage; Close();
      }));
      Controls.Add(Theme.MkButton(I18n.T("以后再说"), 138, Y_BTN, 100, 28, delegate {
        Result = Choice.Later; Close();
      }));
    } else {
      Controls.Add(Theme.MkButton(I18n.T("现在更新"), 30, Y_BTN, 100, 28, delegate {
        Result = Choice.Go; Close();
      }));
      Controls.Add(Theme.MkButton(I18n.T("跳过此版本"), 138, Y_BTN, 100, 28, delegate {
        Result = Choice.Skip; Close();
      }));
      Controls.Add(Theme.MkButton(I18n.T("以后再说"), 246, Y_BTN, 100, 28, delegate {
        Result = Choice.Later; Close();
      }));
    }

    // Esc / 点空白 = 以后再说。**绝不默认更新。**
    KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Result = Choice.Later; Close(); } };
    MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Result = Choice.Later; Close(); } };

    if (owner != null) {
      Location = new Point(owner.Left + (owner.Width - Width) / 2,
                           owner.Top + (owner.Height - Height) / 2);
    }
  }

  Label Row(string k, string v, int y) {
    Label l = new Label();
    l.Text = k + "：" + (v == null ? "" : v);
    l.ForeColor = Theme.Ink;         // 浅底深字
    l.BackColor = Color.Transparent;
    l.Font = Theme.UI;
    l.SetBounds(PAD, y, 420, 20);
    return l;
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    using (Pen p = new Pen(Theme.Line, 1f)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    using (SolidBrush b = new SolidBrush(Theme.Gold)) e.Graphics.FillRectangle(b, 0, 0, 4, Height);
  }

  // 无边框窗不进 Alt-Tab（与 AboutForm / ToastForm / BusyOverlay 一致）
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
      return cp;
    }
  }
}

} // namespace
