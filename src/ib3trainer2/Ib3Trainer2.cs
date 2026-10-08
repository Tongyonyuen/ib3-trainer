// ============================================================================
// Ib3Trainer2.cs — IB3 训练器 2.0 主窗体（partial）
// 布局: 顶部官方横幅 / 左侧 5 Tab / 右侧常驻地址表 / 底部日志
// 冻结引擎: 读后写 + 500ms(常规)/100ms(快速) 双定时器 + 无激活 API
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm : Form {
  // ---- 附着状态 ----
  public IntPtr H = IntPtr.Zero;
  public Process GameProc = null;
  public uint GamePid = 0;
  public IntPtr GameHwnd = IntPtr.Zero;
  public string ExeDir = "";
  public string AddrIni = "";

  // ---- 数据库 ----
  public ItemDb Items = new ItemDb();

  // ---- 冻结引擎 ----
  public class LockEntry {
    public string Key = "";
    public string Desc = "";
    public long Addr;
    public ScanType Type = ScanType.I32;
    public string Val = "0";
    public string Note = "";
    public bool Fast;
    public bool Active = true;
    public byte[] Orig = null;      // 撤销用（锁定建立时读取）
  }
  public readonly List<LockEntry> Locks = new List<LockEntry>();
  Timer freezeSlow, freezeFast, refreshTick, attachTimer;
  public long FreezeWrites = 0;
  Button btnLang;            // 标题条里的语言切换
  Label lblAuthor;           // 标题条右侧署名（点开「关于」）
  bool updateScheduled;      // 自动更新检查只排一次（OnShown 可能被多次触发）

  // ---- 控件 ----
  PictureBox picBanner;
  TabControl tabs;
  ListView lvAddrs;
  TextBox txtLog;
  Label lblStatus;
  Label lblAttach;

  // 双缓冲 ListView（LVM_SETEXTENDEDLISTVIEWSTYLE）
  [DllImport("user32.dll", EntryPoint = "SendMessageW")]
  static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  const uint LVM_FIRST = 0x1000;
  const uint LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
  const uint LVS_EX_DOUBLEBUFFER = 0x00010000;

  [STAThread]
  static void Main(string[] args) {
    // ★ 更新助手模式必须在**最前面**分流：助手绝不能构造 MainForm、不附着游戏、
    //   不起定时器、也不碰 ib3_ui.ini —— 它只负责等父进程退出、改名替换、重启。
    if (Updater.IsApplyMode(args)) { Environment.Exit(Updater.RunApply(args)); return; }

    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    // DPI 感知必须早于任何窗口/句柄创建：否则高缩放屏上 Windows 会把整个窗体位图拉伸，
    // 既糊又会撑大（低分辨率/高缩放倍率下装不下的根因之一）。
    // 声明感知后本进程按真实像素排版，界面缩放由 Layout.cs 统一负责。
    try { SetProcessDPIAware(); } catch { }
    // 未处理异常落盘：工具是分发出去的，用户报「打不开/界面空白」时能直接把 ib3_crash.txt 发回来
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) {
      DumpCrash(e.ExceptionObject as Exception);
    };
    Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) {
      DumpCrash(e.Exception);
    };
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new MainForm());
  }

  static void DumpCrash(Exception ex) {
    try {
      string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ib3_crash.txt");
      File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
        (ex == null ? "(exception object 不是 Exception)" : ex.ToString()) +
        Environment.NewLine + new string('-', 70) + Environment.NewLine);
    } catch { }
  }

  public MainForm() {
    ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    // ★ 界面语言必须在**任何**建控件/弹提示之前定下来 —— 注意下面第 3 行的
    //   BusyShow("正在初始化…") 就是在这个构造函数里最早的一次可见文案。
    //   没有 ib3_ui.ini → 按系统语言自动判定（不落盘）；手选过 → 以手选为准。
    I18n.Init(ExeDir);
    // 初始化可能耗时数秒（读 items.csv / ib3_gems.ini、铺 3000 行列表、缩放布局）
    // 先浮一个窗，别让用户以为卡死了。构造函数末尾关掉。
    BusyShow("正在初始化（加载数据库 / 构建界面）");
    AddrIni = Path.Combine(ExeDir, "ib3_addrs.ini");
    Launcher.LoadConfig(ExeDir);
    // 上一轮更新的遗留物（.old / .new / 完成标记）。纯文件 I/O，早于任何 UI，
    // 也必须在读 ib3_update.ini 之前 —— 它可能清掉过期的完成标记。
    string updateLeftover = Updater.CleanupLeftovers(ExeDir);
    BuildChrome();
    BuildAddressDock();
    BuildBottom();
    Log("游戏目录: " + (Launcher.LauncherDir == null ? "未设置（点「游戏目录…」选择启动器所在文件夹）" : Launcher.LauncherDir));
    if (updateLeftover != null) Log(updateLeftover);
    Log("版本 v" + BuildInfo.SemVer + "　项目主页 " + Updater.REPO_URL);
    // 地址簿先于 Tab 构建（Tab 里的「定位」状态要读它）
    int ac = AddrBook.Load(AddrIni);
    tabs.TabPages.Add(BuildTabCombat());   // 战斗·商店（并页）
    tabs.TabPages.Add(BuildTabItems());
    tabs.TabPages.Add(BuildTabGrowth());
    tabs.TabPages.Add(BuildTabScan());
    tabs.TabPages.Add(BuildTabGems());     // 宝石·背包（读背包 → 改 Tier/数值）
    tabs.TabPages.Add(BuildTabSave());     // 存档导出/导入（.ib3save + 自动完整性校验）
    tabs.SelectedIndex = 0;
    SetBanner(0);

    Items.Load(ExeDir);
    int gl = GemDb.Load(Path.Combine(ExeDir, "ib3_gems.ini"));
    Log("物品数据库 " + Items.Rows.Count + " 条 / 宝石公式 " + gl + " 条 / 地址簿 " + ac + " 条");
    if (Items.Rows.Count == 0) Log("⚠ 未找到 items.csv（应位于程序目录）");
    if (gl == 0) Log("⚠ 未找到 ib3_gems.ini");
    // 物品页分类填充（需在数据加载后）
    cboCat.Items.Clear();
    cboCat.Items.AddRange(Items.Categories().ToArray());
    if (cboCat.Items.Count > 0) cboCat.SelectedIndex = 0;
    RebuildSubs();
    ApplyItemFilter();
    SyncDockFromBook();

    ToastMgr.TrainerForm = this;
    // 注入留痕接到本页日志（EngineCall 是静态类，够不着 Log）
    EngineCall.LogHook = delegate(string s) { Log(s); };

    attachTimer = new Timer(); attachTimer.Interval = 2000;
    attachTimer.Tick += delegate { AttachTick(); }; attachTimer.Start();
    AttachTick();

    freezeSlow = new Timer(); freezeSlow.Interval = 500;
    freezeSlow.Tick += delegate { FreezeTick(false); }; freezeSlow.Start();
    freezeFast = new Timer(); freezeFast.Interval = 100;
    freezeFast.Tick += delegate { FreezeTick(true); }; freezeFast.Start();
    refreshTick = new Timer(); refreshTick.Interval = 1000;
    refreshTick.Tick += delegate { RefreshAddrValues(); }; refreshTick.Start();

    // 布局自适应：所有控件此刻都在设计坐标上，先采集一次设计快照，再按屏幕可用区缩放。
    // 必须放在最后 —— 漏采的控件永远不会缩放。
    CaptureDesign();
    FitToScreen();
    CheckUpk(false);
    BusyHideAll();      // 初始化完毕 → 关浮窗，进主界面
  }

  // ================= 窗体骨架（无边框 + 自绘扁平标题条 + 白底） =================
  [DllImport("dwmapi.dll")]
  static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

  // 无边框顶层窗口在部分系统设置/时序下拿不到任务栏按钮
  // （用户反馈：修改器不在前台时，任务栏上找不到它，得先点一下窗体才回来）。
  // ShowInTaskbar 只在建句柄时生效一次，这里再加 WS_EX_APPWINDOW 强制入任务栏。
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= WS_EX_APPWINDOW;
      // ★ 必须同时**清掉** WS_EX_TOOLWINDOW：两者并存时 TOOLWINDOW 胜出，任务栏就没有按钮。
      //   WinForms 会在 ShowInTaskbar=false 时加上它；句柄重建时样式会被重算，
      //   只加不清等于把结果交给时序。
      cp.ExStyle &= ~WS_EX_TOOLWINDOW;
      return cp;
    }
  }

  const int GWL_EXSTYLE = -20;
  const int WS_EX_TOOLWINDOW = 0x00000080;
  const int WS_EX_APPWINDOW = 0x00040000;
  const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
             SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

  [DllImport("user32.dll", SetLastError = true)]
  static extern int GetWindowLong(IntPtr hWnd, int nIndex);
  [DllImport("user32.dll", SetLastError = true)]
  static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
  [DllImport("user32.dll", SetLastError = true)]
  static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

  // 句柄建好之后**再钉一次**任务栏样式。
  // 为什么光靠 CreateParams 不够：① WinForms 在若干属性变更时会重建句柄，重建后的样式
  //   由时机决定；② 实测症状是「不在前台时任务栏上没有按钮，点一下窗体才回来」——
  //   那正是 Explorer 推迟到窗口首次激活才建任务栏按钮的表现。这里主动改一次 ex-style
  //   并发 SWP_FRAMECHANGED，等于催 Explorer 立刻重新评估，不等用户去点。
  void ReinforceTaskbar() {
    if (Handle == IntPtr.Zero) return;
    try {
      int ex = GetWindowLong(Handle, GWL_EXSTYLE);
      int want = (ex | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW;
      if (want != ex) SetWindowLong(Handle, GWL_EXSTYLE, want);
      SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                   SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    } catch { }
  }

  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    ReinforceTaskbar();
  }

  protected override void OnShown(EventArgs e) {
    base.OnShown(e);
    ReinforceTaskbar();
    // 自动更新检查：挂在 OnShown 而不是 ctor —— OnShown 晚于 ctor 末尾的 BusyHideAll()
    // （Ib3Trainer2.cs:152），此刻弹任何东西都不会叠在"正在初始化"浮窗上。
    // 内部再延 3 秒，避开附着/自检那一波日志。
    if (!updateScheduled) { updateScheduled = true; UpdateUI.ScheduleAutoCheck(this); }
  }

  void BuildChrome() {
    Text = I18n.T("无尽之剑Ⅲ修改器");   // 任务栏标题（窗口无边框，可见标题在 TitleBar.OnPaint 里另画一份）
    ShowInTaskbar = true;
    ClientSize = new Size(1200, 1000);
    AutoScaleMode = AutoScaleMode.None;
    FormBorderStyle = FormBorderStyle.None;   // 无边框：标题条自绘
    MaximizeBox = false; MinimizeBox = false;
    BackColor = Theme.BG;
    ForeColor = Theme.Text;
    try { int pref = 2; DwmSetWindowAttribute(Handle, 33, ref pref, 4); } catch { }  // Win11 圆角（DWM 33）

    TitleBar tb = new TitleBar();
    tb.Owner = this;
    tb.SetBounds(0, 0, 1200, 40);
    Controls.Add(tb);

    // 语言切换 + 作者署名：都挂在标题条里。
    // ★ 必须在 CaptureDesign() 之前建 —— 运行期新建的控件不在 _design 里，永远不会缩放
    //   （Layout.cs 的 ApplyRec 会跳过未采集的控件）。标题条右侧原本只有最小化(1124)/关闭(1160)
    //   两个自绘命中区，中间一大段是空的，放这里不动既有布局。
    btnLang = Theme.MkButton(I18n.ToggleLabel, 1020, 6, 92, 28, delegate { ToggleLang(); });
    tb.Controls.Add(btnLang);
    lblAuthor = Theme.MkLabelInk("by Andrew Tong", 560, 11, 448);
    lblAuthor.TextAlign = ContentAlignment.MiddleRight;
    lblAuthor.Cursor = Cursors.Hand;
    lblAuthor.Click += delegate { ShowAbout(); };
    tb.Controls.Add(lblAuthor);

    picBanner = new PictureBox();
    picBanner.SetBounds(12, 40, 1176, 300);   // 横幅（下沿渐隐入应用底色，元素自此线以下排布）
    picBanner.BackColor = Theme.BG;
    picBanner.SizeMode = PictureBoxSizeMode.Normal;
    Controls.Add(picBanner);

    tabs = new FlatTab();
    tabs.SetBounds(12, 348, 830, 500);   // 底端与日志栏对齐（吃掉空白带）
    Theme.StyleTab(tabs);
    tabs.SelectedIndexChanged += delegate { SetBanner(tabs.SelectedIndex); };
    Controls.Add(tabs);
  }

  // ---- 语言切换（标题条右侧按钮）----
  // 只做三件事：切语言 → 重设所有登记过的文案 → 让自绘部分重画。
  // **不重建控件树、不碰定时器、不改任何 bounds** —— 两种语言共用同一套几何（见 I18n.cs 头部注释）。
  void ToggleLang() {
    I18n.SetLang(I18n.IsEn ? I18n.ZH : I18n.EN, ExeDir);
    I18n.Retranslate();
    Invalidate(true);                                  // 页签/列头/下拉/分组标题/标题条都是自绘的
    btnLang.Text = I18n.ToggleLabel;                   // 按钮文字显示"目标语言"，不在字典里，单独设
    Text = I18n.T("无尽之剑Ⅲ修改器");                    // 任务栏标题
    // Retranslate 会跳过"被各自逻辑改写过的"控件（防清空，见 I18n.cs 的护栏），
    // 这里补调两次**只读刷新**让它们按新语言重画（都不写游戏内存）。
    try { OnGemSel(); } catch { }
    try { RefreshStatStatusAll(); } catch { }
    try { RenderGoldChipLabels(); } catch { }
    try { RefreshAddrTable(); } catch { }
    Log(I18n.IsEn ? "UI language: English" : "界面语言：中文");
  }

  void ShowAbout() {
    try {
      using (AboutForm f = new AboutForm(this)) {
        f.ShowDialog(this);
        if (f.CheckUpdateRequested) UpdateUI.CheckNow(this, false);   // 手动检查：不受 6h 节流
      }
    } catch (Exception ex) { Log("关于窗口打开失败: " + ex.Message); }
  }

  readonly string[] BannerKeys = { "Isa", "Raidiar", "Siris", "hideout" };
  Image bannerSrc = null;   // 当前页签的横幅源图（已解码，缩放时按新尺寸重绘用）
  int bannerIdx = -1;

  void SetBanner(int idx) {
    if (idx < 0 || idx >= BannerKeys.Length) return;
    if (idx == bannerIdx && bannerSrc != null) return;
    bannerIdx = idx;
    Image old = bannerSrc; bannerSrc = null;
    if (old != null) { try { old.Dispose(); } catch { } }
    bannerSrc = LoadBannerImage(BannerKeys[idx]);
    RenderBanner();
  }

  // 选图：内嵌资源 → 程序目录 image\ → 游戏目录 Official Image\ → 旧硬编码路径
  // 旧实现写死 @"E:\IB3\Official Image" 且「目录不存在就静默 return」，所以在别人机器上
  // 必然是整块空白。现在图片内嵌进 exe（build.sh 的 -resource:），换个机器也照样显示。
  static Image LoadBannerImage(string key) {
    // ① 内嵌资源（随 exe 走，最可靠）
    try {
      string[] names = typeof(MainForm).Assembly.GetManifestResourceNames();
      for (int i = 0; i < names.Length; i++) {
        if (names[i].IndexOf("banner." + key, StringComparison.OrdinalIgnoreCase) < 0) continue;
        using (Stream st = typeof(MainForm).Assembly.GetManifestResourceStream(names[i])) {
          if (st == null) continue;
          using (Image t = Image.FromStream(st)) return new Bitmap(t);   // 复制一份，避免流生命周期问题
        }
      }
    } catch { }
    // ② 程序目录 image\ 下的同名文件（方便用户自行换图）
    try {
      string[] exts = { ".jpg", ".jpeg", ".png" };
      for (int i = 0; i < exts.Length; i++) {
        string p = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image"),
                                key.ToLowerInvariant() + exts[i]);
        if (File.Exists(p)) { using (Image t = Image.FromFile(p)) return new Bitmap(t); }
      }
    } catch { }
    // ③ 游戏目录 Official Image\（兼容老用户；文件名含关键字即可，不依赖那串超长原名）
    try {
      if (Launcher.GameRoot != null) {
        Image hit = FindBannerIn(Path.Combine(Launcher.GameRoot, "Official Image"), key);
        if (hit != null) return hit;
      }
    } catch { }
    // ④ 旧硬编码绝对路径（仅作兜底，保住开发机原行为）
    try {
      Image hit = FindBannerIn(@"E:\IB3\Official Image", key);
      if (hit != null) return hit;
    } catch { }
    return null;
  }
  static Image FindBannerIn(string dir, string key) {
    if (!Directory.Exists(dir)) return null;
    foreach (string f in Directory.GetFiles(dir)) {
      if (Path.GetFileName(f).IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
      // 逐文件 try：目录里可能有非图片文件，FromFile 会抛
      try { using (Image t = Image.FromFile(f)) return new Bitmap(t); } catch { }
    }
    return null;
  }

  // 按横幅控件当前尺寸重画。缩放后必须重画 —— SizeMode=Normal 只裁剪不拉伸，
  // 沿用旧位图会导致右边/下边被切掉。
  void RenderBanner() {
    if (picBanner == null) return;
    int w = picBanner.Width, h = picBanner.Height;
    if (w <= 0 || h <= 0) return;
    Bitmap bmp = null;
    try {
      bmp = new Bitmap(w, h);
      using (Graphics g = Graphics.FromImage(bmp)) {
        // 拖拽中降到双线性：高质量双三次重绘 1500×515 位图每帧要十几毫秒，会明显拖慢拖拽
        g.InterpolationMode = _sizing ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
        if (bannerSrc != null) {
          // ★ 取景必须与改动前逐像素一致：按源图比例铺满宽度（1000×350 的素材 → 高 = 宽×0.35），
          //   顶部对齐绘制，超出控件下沿的部分由控件自己裁掉。
          //   作者原实现就是 DrawImage 到 (0,0,W,W*0.35) 再让 300 高的区域裁掉下沿 ——
          //   换成「居中裁剪」会把人像放大并切掉头顶，属于把界面改样，不能做。
          int dh = (int)Math.Round(w * (bannerSrc.Height / (float)bannerSrc.Width));
          if (dh < h) dh = h;   // 横幅比素材更宽比时兜底铺满，避免下沿露白
          if (_bannerH >= UiScale.BANNER_FULL - 0.5f) {
            // 满高（绝大多数桌面，可用高度 ≥850px 时都是这一档）：与改动前逐像素一致
            g.DrawImage(bannerSrc, new Rectangle(0, 0, w, dh));
          } else {
            // 仅小屏压缩横幅时改居中裁剪：此时顶部对齐只剩一片天空，居中才留得住人像。
            // bannerH 在启动时就定死了，所以拖动窗口不会看到两种取景来回跳。
            // 注意 sy/sh 都是【源图】像素，不能拿目标像素的 dh 去算。
            float sw = bannerSrc.Width, sh0 = bannerSrc.Height;
            float sh = h * sw / w;                 // 控件高度对应的源图高度
            float sy = (sh0 - sh) / 2f;            // 垂直居中
            if (sy < 0f) { sy = 0f; sh = sh0; }
            if (sy + sh > sh0) sh = sh0 - sy;
            g.DrawImage(bannerSrc, new Rectangle(0, 0, w, h), 0f, sy, sw, sh, GraphicsUnit.Pixel);
          }
        } else {
          // 占位条：找不到任何图时也给出完整观感，绝不静默留白
          using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                   Color.FromArgb(255, 46, 38, 26), Color.FromArgb(255, Theme.BG), LinearGradientMode.Vertical))
            g.FillRectangle(br, 0, 0, w, h);
          using (SolidBrush fg = new SolidBrush(Theme.GoldDim))
            g.DrawString("无 尽 之 剑 Ⅲ", FontBank.Get("Microsoft YaHei", FontStyle.Bold, 18f), fg,
                         UiScale.Px(24), h * 0.30f);
          using (Pen p = new Pen(Theme.Line, 1f)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
        }
        // 下沿渐隐入应用底色（原设计为 170/300 ≈ 0.567 处起渐隐）。
        // 注意：横幅被压缩到 60 高时 h-170 是负数，原写法会让 LinearGradientBrush 抛异常，
        // 而外层 catch{} 会把它静默吞掉 → 表现为「切页签后横幅变空白」。这里必须夹紧。
        int gy = (int)(h * 0.567f);
        if (gy > h - 1) gy = h - 1;
        if (gy < 1) gy = 1;
        Rectangle gr = new Rectangle(0, gy, w, h - gy);
        using (LinearGradientBrush br = new LinearGradientBrush(gr,
                 Color.FromArgb(0, Theme.BG), Color.FromArgb(255, Theme.BG), LinearGradientMode.Vertical))
          g.FillRectangle(br, gr);
      }
      Image old = picBanner.Image;
      picBanner.Image = bmp;
      bmp = null;
      if (old != null) old.Dispose();
    } catch {
      if (bmp != null) { try { bmp.Dispose(); } catch { } }
    }
  }

  // ================= 右侧地址表 Dock =================
  void BuildAddressDock() {
    FlatGroupBox box = new FlatGroupBox();
    box.Title = "地址表（写入即入表：可撤销 / 可锁定 / 可保存）";
    box.SetBounds(850, 348, 338, 500);   // 底端与日志栏对齐（下缘上收）
    Controls.Add(box);

    lvAddrs = new DarkListView();
    Theme.ApplyDarkScroll(lvAddrs);
    lvAddrs.SetBounds(8, 26, 322, 392);
    Theme.StyleList(lvAddrs);
    lvAddrs.CheckBoxes = true;
    lvAddrs.Columns.Add("描述", 90);
    lvAddrs.Columns.Add("地址", 84);
    lvAddrs.Columns.Add("类型", 46);
    lvAddrs.Columns.Add("设定", 52);
    lvAddrs.Columns.Add("当前", 46);
    lvAddrs.ItemChecked += delegate(object s, ItemCheckedEventArgs e) {
      if (!(e.Item.Tag is LockEntry)) return;
      LockEntry le = (LockEntry)e.Item.Tag;
      if (le.Active == e.Item.Checked) return;
      if (e.Item.Checked) {
        // 开锁前先校验地址可读（防止锁到失效旧地址）
        if (H == IntPtr.Zero) { e.Item.Checked = false; ToastMgr.Warn("未附着游戏进程"); return; }
        string cur0; string err0;
        if (!MemIO.ReadValue(H, le.Addr, le.Type, out cur0, out err0)) {
          e.Item.Checked = false;
          le.Active = false;
          ToastMgr.Warn("该地址已失效（" + err0 + "）——请重新定位后再锁定");
          return;
        }
        // 地址簿带入的行：以"当前值"为锁定基准（避免把上次运行的值写回去）
        if (le.Note == "addrbook") le.Val = cur0;
      }
      le.Active = e.Item.Checked;
      Log((le.Active ? "锁定开: " : "锁定关: ") + le.Desc + (le.Active ? "（值=" + le.Val + "）" : ""));
    };
    box.Controls.Add(lvAddrs);
    lvAddrs.HandleCreated += delegate {
      SendMessage(lvAddrs.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)0, (IntPtr)LVS_EX_DOUBLEBUFFER);
    };

    Button btnUndo = Theme.MkButton("撤销选中", 8, 426, 78, 27, delegate { UndoSelected(); });
    Button btnUnlockAll = Theme.MkButton("全部解锁", 90, 426, 78, 27, delegate {
      foreach (LockEntry le in Locks) le.Active = false;
      RefreshAddrTable();
      Log("已全部解锁（不再回写）");
    });
    Button btnDel = Theme.MkButton("删除", 172, 426, 60, 27, delegate { RemoveSelected(); });
    Button btnSave = Theme.MkButton("保存地址簿", 236, 426, 94, 27, delegate {
      AddrBook.Save(AddrIni);
      Log("地址簿已保存: " + AddrBook.Count + " 条 → " + AddrIni);
    });
    box.Controls.Add(btnUndo); box.Controls.Add(btnUnlockAll); box.Controls.Add(btnDel); box.Controls.Add(btnSave);
    box.Controls.Add(Theme.MkHint("写入只进可写私有页（拒绝映像/Guard）；临时值（属性/等级/HP）重启会还原。", 8, 458, 322));
  }

  // 地址簿 → 右侧地址表。可反复调用（原实现有 bookDocked 一次性闸门：干净环境首次启动时
  // 地址簿为空，闸门立刻关闭，此后自动绑定的金币/筹码、定位向导的结果都再也不会上屏，
  // 表现为「日志有自动绑定、右侧却永远 0 条」）。
  // 同步地址/类型，但不同步「设定」值 —— 免得把已锁定行的目标值改掉。
  public void SyncDockFromBook() {
    if (lvAddrs == null) return;
    bool changed = false;
    foreach (AddrEntry e in AddrBook.Snapshot()) {
      LockEntry le = null;
      for (int i = 0; i < Locks.Count; i++) if (Locks[i].Key == e.Key) { le = Locks[i]; break; }
      if (le == null) {
        le = new LockEntry();
        le.Key = e.Key;
        le.Desc = (e.Desc != null && e.Desc.Length > 0) ? e.Desc : e.Key;
        le.Addr = e.Addr; le.Type = e.Type; le.Val = e.Val;
        le.Active = false; le.Note = "addrbook"; le.Fast = false;
        Locks.Add(le);
        changed = true;
      } else {
        if (le.Addr != e.Addr || le.Type != e.Type) {
          le.Addr = e.Addr; le.Type = e.Type;
          changed = true;
        }
        // 未锁定的行「设定」值跟随地址簿刷新（引擎每次附着都会重算并写入新值）；
        // 已锁定的行保留用户设定的目标值，绝不被覆盖。
        // 不然会出现「当前=21 但设定=11」——用户一勾选锁定就锁到了过期值。
        if (!le.Active && le.Val != e.Val) { le.Val = e.Val; changed = true; }
      }
    }
    if (changed) RefreshAddrTable();
  }

  // BookPut 可能在后台注入线程上被调用，而 Locks/ListView 只能在 UI 线程碰 —— 统一回投。
  // 合并排队：一次连续写入只触发一次同步。
  bool dockSyncQueued = false;
  public void QueueDockSync() {
    if (dockSyncQueued) return;
    dockSyncQueued = true;
    try {
      if (InvokeRequired) BeginInvoke((MethodInvoker)delegate { dockSyncQueued = false; SyncDockFromBook(); });
      else { dockSyncQueued = false; SyncDockFromBook(); }
    } catch { dockSyncQueued = false; }
  }

  public void RefreshAddrTable() {
    if (lvAddrs == null) return;
    lvAddrs.BeginUpdate();
    lvAddrs.Items.Clear();
    foreach (LockEntry le in Locks) {
      // ★ 在**渲染时**过 T()，不动 le.Desc 本身：描述是**持久化数据**（BookPut 写进 ib3_addrs.ini），
      //   在那里翻会把"当时的语言"烤进文件。这样英文界面下地址表是英文，而 ini 里始终是中文原文。
      ListViewItem it = new ListViewItem(I18n.T(le.Desc));
      it.SubItems.Add("0x" + le.Addr.ToString("X"));
      it.SubItems.Add(TypeUtil.Name(le.Type));
      it.SubItems.Add(le.Val);
      it.SubItems.Add("…");
      it.Checked = le.Active;
      it.Tag = le;
      lvAddrs.Items.Add(it);
    }
    lvAddrs.EndUpdate();
  }

  void RefreshAddrValues() {
    if (H == IntPtr.Zero || lvAddrs == null || lvAddrs.Items.Count == 0) return;
    lvAddrs.BeginUpdate();
    for (int i = 0; i < lvAddrs.Items.Count; i++) {
      if (!(lvAddrs.Items[i].Tag is LockEntry)) continue;
      LockEntry le = (LockEntry)lvAddrs.Items[i].Tag;
      string v;
      string err;
      if (MemIO.ReadValue(H, le.Addr, le.Type, out v, out err)) lvAddrs.Items[i].SubItems[4].Text = v;
      else lvAddrs.Items[i].SubItems[4].Text = I18n.T("失效");
    }
    lvAddrs.EndUpdate();
  }

  void UndoSelected() {
    if (lvAddrs.SelectedItems.Count == 0) { ToastMgr.Show("先在地址表选中一行"); return; }
    LockEntry le = lvAddrs.SelectedItems[0].Tag as LockEntry;
    if (le == null) return;
    le.Active = false;
    if (le.Orig != null && H != IntPtr.Zero) {
      string err;
      if (MemIO.SafeWrite(H, le.Addr, le.Orig, out err)) Log("已撤销: " + le.Desc + " → 原值");
      else Log("撤销失败: " + le.Desc + " (" + err + ")");
    }
    Log("行已解锁: " + le.Desc);
    RefreshAddrTable();
  }

  void RemoveSelected() {
    if (lvAddrs.SelectedItems.Count == 0) { ToastMgr.Show("先在地址表选中一行"); return; }
    LockEntry le = lvAddrs.SelectedItems[0].Tag as LockEntry;
    if (le == null) return;
    Locks.Remove(le);
    RefreshAddrTable();
  }

  // ================= 底部 =================
  void BuildBottom() {
    txtLog = new TextBox();
    txtLog.SetBounds(12, 854, 1176, 96);
    Theme.ApplyDarkScroll(txtLog);
    txtLog.Multiline = true;
    txtLog.ReadOnly = true;
    txtLog.ScrollBars = ScrollBars.Vertical;
    txtLog.BackColor = Color.FromArgb(24, 21, 19);
    txtLog.ForeColor = Color.FromArgb(196, 188, 172);
    txtLog.BorderStyle = BorderStyle.FixedSingle;
    txtLog.Font = Theme.Mono;
    Controls.Add(txtLog);

    // 用户确认：训练器**不向游戏注入语言设置**，只是唤起启动器 ⇒ 去掉"（中文）"
    Button btnLaunch = Theme.MkButton("启动游戏", 12, 958, 148, 28, delegate {
      if (Launcher.LauncherExe == null || !File.Exists(Launcher.LauncherExe)) {
        ToastMgr.Warn("请先选择游戏目录（启动器所在文件夹）");
        PickGameDir();
        if (Launcher.LauncherExe == null || !File.Exists(Launcher.LauncherExe)) return;
      }
      string err;
      Process lp = Launcher.StartGame(out err);
      if (lp == null) { Log("启动失败: " + err); ToastMgr.Warn("启动失败: " + err); return; }
      Log("已打开移植版启动器（保中文）。该启动器需点 Play —— 正在尝试自动点击…");
      ToastMgr.Show("已打开启动器，正在尝试自动点 Play；游戏起来后训练器会自动附着");
      Launcher.AutoClickPlayAsync(lp);
    });
    Button btnDir = Theme.MkButton("游戏目录…", 166, 958, 86, 28, delegate { PickGameDir(); });
    Button btnAttachNow = Theme.MkButton("立即附着", 258, 958, 86, 28, delegate { AttachTick(); });
    Button btnSaveBook = Theme.MkButton("保存地址簿", 350, 958, 100, 28, delegate {
      AddrBook.Save(AddrIni); Log("地址簿已保存: " + AddrBook.Count + " 条");
    });
    // 一个按钮同时干三件事：已一致 → 只报告；不一致 → 直接部署修复；没游戏目录 → 引导去选
    Button btnUpk = Theme.MkButton("部署/校验 UPK", 458, 958, 116, 28, delegate {
      if (Launcher.GameRoot == null) { PickGameDir(); CheckUpk(true); return; }
      if (Launcher.VerifyUpk(Launcher.GameRoot, ExeDir) == null) { CheckUpk(true); return; }
      if (Launcher.FindGame() != null) {
        ToastMgr.Warn("游戏正在运行 —— 请先关闭游戏再部署 upk");
        Log("UPK 部署被拒绝：游戏进程仍在运行（正在占用该文件）");
        return;
      }
      string r = Launcher.DeployUpk(Launcher.GameRoot, ExeDir);
      Log("UPK 部署: " + r);
      ToastMgr.Show(r);
      CheckUpk(false);
    });
    lblStatus = Theme.MkLabel("未附着", 584, 963, 604);
    lblStatus.ForeColor = Theme.TextDim;
    Controls.Add(btnLaunch); Controls.Add(btnDir); Controls.Add(btnAttachNow);
    Controls.Add(btnSaveBook); Controls.Add(btnUpk); Controls.Add(lblStatus);
  }

  // 随包 upk 校验（醒目报警，不擅自改游戏文件）
  //
  // 为什么关键：giveitemonce / addconsumable / setplayergiveallitems / masterallowneditems /
  // setplayergems / setgivekeyitem / setplayercreatenewlistofstoregems 这些命令在游戏本体
  // IB3.exe 里一个都不存在，全部是随包 SwordGame.upk 里的脚本函数。upk 没部署 = 发放与
  // 掌握必然失败，而 enablecheats/god 这类游戏自带命令却照常工作 —— 这正是外部用户
  // 「只有物品发放失败」的表现。（部署原先只在点「游戏目录…」时才发生，先手动开游戏、
  // 只点「立即附着」的用户就永远没有它。）
  void CheckUpk(bool noisy) {
    if (Launcher.GameRoot == null) {
      if (noisy) {
        Log("UPK 校验：尚未设置游戏目录");
        ToastMgr.Warn("请先点「游戏目录…」选择启动器所在文件夹");
      }
      return;
    }
    string bad = Launcher.VerifyUpk(Launcher.GameRoot, ExeDir);
    if (bad == null) {
      if (noisy) { Log("UPK 校验通过：随包 SwordGame.upk 已正确部署 ✓"); ToastMgr.Show("UPK 校验通过：自定义命令可用"); }
      return;
    }
    Log("⚠ UPK 校验未通过：" + bad);
    Status("⚠ UPK 未部署 —— 物品发放/掌握升阶会失败", Theme.Warn);
    if (noisy) ToastMgr.Warn("UPK 未部署：" + bad + "（点「部署/校验 UPK」修复，需先关游戏）");
  }

  // 手动指定"启动器所在文件夹"（上探一级 = 游戏根目录），并自动部署随包 upk
  void PickGameDir() {
    FolderBrowserDialog fb = new FolderBrowserDialog();
    fb.Description = "请选择【Infinity Blade Launcher.exe】所在的文件夹（通常是游戏的 Binaries 目录，例如 …\\IB3\\Binaries）";
    fb.ShowNewFolderButton = false;
    if (fb.ShowDialog(this) != DialogResult.OK) return;
    string dir = fb.SelectedPath;
    if (!File.Exists(Path.Combine(dir, "Infinity Blade Launcher.exe"))) {
      ToastMgr.Warn("该文件夹下没有 Infinity Blade Launcher.exe —— 请选择启动器所在文件夹");
      Log("游戏目录设置失败：未找到启动器于 " + dir);
      return;
    }
    Launcher.LauncherDir = dir;
    Launcher.SaveConfig(ExeDir, dir);
    string root = Launcher.GameRoot;
    Log("已设置游戏目录：启动器=" + dir + "；游戏根目录=" + root);
    string r = Launcher.DeployUpk(root, ExeDir);
    Log("UPK 部署: " + r);
    ToastMgr.Show(r);
  }

  // ================= 附着 =================
  void AttachTick() {
    if (H != IntPtr.Zero) {
      if (GameProc != null && !GameProc.HasExited) { ProbeRebind(); RefreshGameHwnd(); return; }
      try { Win32.CloseHandle(H); } catch { }
      H = IntPtr.Zero; GameProc = null; GamePid = 0; GameHwnd = IntPtr.Zero;
      ToastMgr.GameHwnd = IntPtr.Zero;
      Status("游戏已退出", Theme.Warn);
    }
    Process p = Launcher.FindGame();
    if (p == null) { Status("未找到游戏进程 — 请点「启动游戏」", Theme.TextDim); return; }
    if (!Launcher.ValidateImage(p)) { Status("发现同名进程但映像路径不符，忽略", Theme.Warn); return; }
    IntPtr h = Win32.OpenProcess(0x438, false, (uint)p.Id);
    if (h == IntPtr.Zero) { Status("OpenProcess 失败 err=" + Marshal.GetLastWin32Error(), Theme.Warn); return; }
    H = h; GameProc = p; GamePid = (uint)p.Id;
    Status("已附着 IB3.exe  PID=" + p.Id + "（无感模式：不抢焦点、不动窗口）", Theme.Ok);
    Log("已附着游戏进程 PID=" + p.Id);
    RefreshGameHwnd();
    // 预热：后台安装/复用信箱挂钩 + 定位候选宿主（不阻塞界面）
    System.Threading.Thread th = new System.Threading.Thread(delegate() {
      try {
        // ★ 2026-10-08：原来这里**忽略返回值**，于是注入器整个没装起来时界面完全看不出来
        //   （只有命令类按钮逐个弹"注入器未就绪"）。现在把失败明确说出来并指明出路。
        if (!EngineCall.Prepare(H, (int)GamePid, Log)) {
          Log("⚠ 注入器未安装成功 —— 命令类功能（物品发放 / 商店刷新 / 掌握升阶）在本会话内不可用。" +
              "原因见上面那行「注入器： …」。多数情况**关闭游戏再重新启动**即可恢复：" +
              "训练器会在新游戏进程里重新装挂钩，且只认自己装的挂钩、不会覆盖外来补丁。");
        }
        AddrEntry ag = AddrBook.Get("misc.gold");
        if (ag != null && ag.Addr != 0) {
          // 先确认这条记录还真读得动再拿去锚定 —— 地址簿可能是上一局留下的过期堆地址
          string gv; string ge;
          if (MemIO.ReadValue(H, ag.Addr, ag.Type, out gv, out ge)) {
            EngineCall.NoteGoldAddr(ag.Addr);
            Log("注入器：以金币地址簿锚定玩家真身 0x" + (ag.Addr - 0x2070).ToString("X"));
          } else {
            Log("地址簿中的金币地址已失效（" + ge + "）——改为按真身结构自动绑定");
          }
        }
        EngineCall.Discover(H, Log);
        EngineBindAndLog();
        ValidateBookAddrs();   // 逐条读回校验，失效的明确报出来
        CheckUpk(false);       // 自定义命令依赖随包 upk，没部署就在这里报警
      } catch (Exception ex) { Log("注入器初始化异常: " + ex.Message); }
    });
    th.IsBackground = true;
    th.Start();
  }

  // 真身绑定 + 金币/筹码结构自动绑定（附着时与"世界切换检测"后共用）
  void EngineBindAndLog() {
    long live = EngineCall.BindLive(H);
    if (live == 0) {
      // ★ 2026-10-08：以前这里是**静默 return** —— 用户只看到"四维未定位"却不知原因。
      //   现在"真身分不足（没有任何数组装载）"会明确拒绝绑定，所以必须说清这是
      //   **正常状态**而不是故障：AttachTick 每 2 秒重试一次，走进藏身地后会自动绑上。
      Log("未绑定真身：当前没有找到装着背包/商店数组的玩家对象。" +
          "若游戏还在读档中、或你正在关卡/商店熔接室界面，这是正常状态 —— " +
          "回到藏身地主界面后会自动绑定（每 2 秒重试一次，无需手动操作）。");
      return;
    }
    long ga = live + 0x2070, ca = live + 0x2094;
    BookPut("misc.gold", "金币", ga, ScanType.I64, "", "engine");
    BookPut("misc.chip", "筹码", ca, ScanType.I32, "", "engine");
    AutoBindGoldChip(ga, ca);
    // 四项基本属性同样按真身结构自动绑定（偏移 +0x1F10 起连续四个 Int32，实机确认）
    // —— 新用户开箱即用，不需要地址簿、也不需要跑定位向导
    AutoBindStats(live);
    Log("金币/筹码已按真身结构自动绑定：金币@0x" + ga.ToString("X") + " 筹码@0x" + ca.ToString("X"));
    Log("四项属性已按真身结构自动绑定：体力/护盾/攻击/魔法 @0x" + (live + 0x1F10).ToString("X") + " 起");
  }

  // 世界切换探测：绑定失效（换存档/读档）→ 后台自动重绑定（期间功能按钮禁用变灰）
  bool rebindRunning = false;
  void ProbeRebind() {
    if (H == IntPtr.Zero || rebindRunning) return;
    if (EngineCall.LiveSane(H)) return;
    rebindRunning = true;
    SetAllActionEnabled(false);
    System.Threading.Thread th = new System.Threading.Thread(delegate() {
      try {
        Log("检测到宿主失效（可能切换了存档）——正在自动重新绑定…（功能按钮已临时禁用）");
        EngineCall.Discover(H, Log);
        EngineBindAndLog();
        ValidateBookAddrs();   // 换存档后原来那批成长属性地址多半也失效了，一并刷新
        Log("自动重新绑定完成——金币/筹码等已更新到当前存档");
      } catch (Exception ex) { Log("重新绑定异常: " + ex.Message); }
      finally { rebindRunning = false; SetAllActionEnabled(true); }
    });
    th.IsBackground = true;
    th.Start();
  }

  // 临时禁用/恢复全部功能按钮（重绑定期间置灰）
  void SetAllActionEnabled(bool en) {
    try {
      BeginInvoke((MethodInvoker)delegate {
        foreach (Control c in Controls) SetButtonsDeep(c, en);
      });
    } catch { }
  }
  static void SetButtonsDeep(Control root, bool en) {
    if (root == null) return;
    if (root is Button) root.Enabled = en;
    foreach (Control c in root.Controls) SetButtonsDeep(c, en);
  }

  void RefreshGameHwnd() {
    if (GamePid == 0) return;
    IntPtr found = IntPtr.Zero;
    Win32.EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!Win32.IsWindowVisible(h)) return true;
      uint pid; Win32.GetWindowThreadProcessId(h, out pid);
      if (pid != GamePid) return true;
      StringBuilder sb = new StringBuilder(256);
      Win32.GetWindowText(h, sb, 256);
      if (sb.Length > 0) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    if (found != IntPtr.Zero) { GameHwnd = found; ToastMgr.GameHwnd = found; }
  }

  // ================= 公共设施（Tab 共用） =================
  public bool RequireH() {
    if (H != IntPtr.Zero) return true;
    ToastMgr.Warn("未附着游戏进程 — 先点「启动游戏」或「立即附着」");
    Status("未附着", Theme.Warn);
    return false;
  }

  // ============ 无感命令注入（EngineCall） ============
  // 单命令：后台执行（逐宿主试投，返回 1 = 游戏真实执行）
  public void InjectCmd(string cmd, string desc) {
    if (!RequireH()) return;
    RunBackground(I18n.T("注入 ") + desc, delegate {
      string err;
      bool ok = EngineCall.Execute(H, cmd, out err);
      if (ok) {
        ToastMgr.Show(I18n.T("已执行：") + desc);
        return "注入成功: " + desc + "  [" + cmd + "]";
      }
      ToastMgr.Warn(I18n.T("执行失败：") + desc + " — " + err);
      return "注入失败: " + desc + "  [" + cmd + "] " + err;
    });
  }

  // 多命令顺序执行（如 enablecheats + god）
  public void InjectCmds(string[] cmds, string desc) {
    if (!RequireH()) return;
    RunBackground(I18n.T("注入 ") + desc, delegate {
      int okc = 0; string lastErr = "";
      foreach (string c in cmds) {
        string err;
        if (EngineCall.Execute(H, c, out err)) okc++;
        else lastErr = err;
        System.Threading.Thread.Sleep(80);
      }
      if (okc == cmds.Length) {
        ToastMgr.Show(I18n.T("已执行：") + desc);
        return "注入成功×" + okc + ": " + desc;
      }
      ToastMgr.Warn(I18n.T("部分失败：") + desc + "（" + okc + "/" + cmds.Length + "）" + lastErr);
      return "注入部分失败: " + desc + "（" + okc + "/" + cmds.Length + "）" + lastErr;
    });
  }

  public void Log(string s) {
    try {
      if (txtLog == null) return;
      if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Log(s); }); return; }
      txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
    } catch { }
  }

  public void Status(string s, Color c) {
    try {
      if (lblStatus == null) return;
      if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Status(s, c); }); return; }
      // 状态栏会随语言切换重设：key = 控件本身 ⇒ 反复登记只保留最后一条，不涨内存、也不会被旧文案顶掉
      I18n.Register(lblStatus, delegate(string v) { lblStatus.Text = v; lblStatus.ForeColor = c; }, s);
    } catch { }
  }

  // 一次写入：SafeWrite + 日志 + 入地址表（lockIt=是否默认锁定）
  public bool WriteOne(string key, string desc, long addr, ScanType t, string val, bool lockIt, bool fast, string note) {
    if (!RequireH()) return false;
    byte[] want;
    try { want = TypeUtil.Encode(t, val); } catch (Exception ex) { ToastMgr.Warn("值无效: " + ex.Message); return false; }
    // 记录原值
    byte[] orig = new byte[want.Length];
    int r;
    bool hasOrig = Win32.ReadProcessMemory(H, (IntPtr)addr, orig, orig.Length, out r) && r == orig.Length;
    string err;
    if (!MemIO.SafeWrite(H, addr, want, out err)) {
      ToastMgr.Warn("写入失败: " + err);
      Log("写入失败 " + desc + " @0x" + addr.ToString("X") + ": " + err);
      return false;
    }
    Log("写入 " + desc + " @0x" + addr.ToString("X") + " = " + val + (lockIt ? "（已锁定）" : ""));
    // 入表/更新
    LockEntry le = null;
    for (int i = 0; i < Locks.Count; i++) if (Locks[i].Key == key && key.Length > 0) { le = Locks[i]; break; }
    if (le == null) {
      le = new LockEntry();
      le.Key = key; le.Desc = desc; le.Addr = addr; le.Type = t;
      le.Orig = hasOrig ? orig : null;
      le.Fast = fast; le.Note = note;
      Locks.Add(le);
    } else {
      le.Addr = addr; le.Type = t; le.Val = val; le.Fast = fast;
      if (le.Orig == null && hasOrig) le.Orig = orig;
    }
    le.Val = val;
    le.Active = lockIt;
    RefreshAddrTable();
    return true;
  }

  // 仅入表（不写）——用于锁定类功能的持续回写
  public void LockAdd(string key, string desc, long addr, ScanType t, string val, bool fast, string note) {
    LockEntry le = null;
    for (int i = 0; i < Locks.Count; i++) if (Locks[i].Key == key && key.Length > 0) { le = Locks[i]; break; }
    byte[] orig = new byte[TypeUtil.Size(t)];
    int r;
    bool hasOrig = H != IntPtr.Zero && Win32.ReadProcessMemory(H, (IntPtr)addr, orig, orig.Length, out r) && r == orig.Length;
    if (le == null) {
      le = new LockEntry();
      le.Key = key; le.Desc = desc; le.Addr = addr; le.Type = t;
      le.Orig = hasOrig ? orig : null;
      Locks.Add(le);
    } else {
      le.Addr = addr; le.Type = t;
      if (le.Orig == null && hasOrig) le.Orig = orig;
    }
    le.Val = val; le.Fast = fast; le.Note = note; le.Active = true;
    RefreshAddrTable();
  }

  // ================= 冻结引擎（读后写） =================
  void FreezeTick(bool fast) {
    if (H == IntPtr.Zero || Locks.Count == 0) return;
    for (int i = 0; i < Locks.Count; i++) {
      LockEntry e = Locks[i];
      if (!e.Active || e.Fast != fast) continue;
      byte[] want;
      try { want = TypeUtil.Encode(e.Type, e.Val); } catch { continue; }
      byte[] cur = new byte[want.Length];
      int r;
      if (!Win32.ReadProcessMemory(H, (IntPtr)e.Addr, cur, want.Length, out r) || r != want.Length) continue;
      bool same = true;
      for (int k = 0; k < want.Length; k++) if (cur[k] != want[k]) { same = false; break; }
      if (same) continue;   // 读后写：值相同不写，零抖动
      string err;
      if (MemIO.SafeWrite(H, e.Addr, want, out err)) FreezeWrites++;
    }
  }

  protected override void OnFormClosing(FormClosingEventArgs e) {
    try { AddrBook.Save(AddrIni); } catch { }
    UpdateUI.StopTimer();      // 别让一次性定时器在窗体拆掉后再触发
    base.OnFormClosing(e);
  }
}

// ============================================================================
// 自绘扁平圆角标题条（无边框窗体：拖动 + 最小化 + 关闭）
// ============================================================================
class TitleBar : Panel {
  public Form Owner;
  bool hoverX = false, hoverM = false;
  [DllImport("user32.dll")] static extern bool ReleaseCapture();
  [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessageMove(IntPtr h, uint m, IntPtr w, IntPtr l);

  public TitleBar() {
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    BackColor = Color.White;
  }
  // 按钮位置是 Width 的纯函数，Width 被缩放后命中测试天然正确；但里面的常量必须过 UiScale
  Rectangle BtnX { get { return new Rectangle(Width - UiScale.Px(40), UiScale.Px(6), UiScale.Px(32), UiScale.Px(28)); } }
  Rectangle BtnM { get { return new Rectangle(Width - UiScale.Px(76), UiScale.Px(6), UiScale.Px(32), UiScale.Px(28)); } }
  protected override void OnMouseMove(MouseEventArgs e) {
    bool hx = BtnX.Contains(e.Location), hm = BtnM.Contains(e.Location);
    if (hx != hoverX || hm != hoverM) { hoverX = hx; hoverM = hm; Invalidate(); }
    base.OnMouseMove(e);
  }
  protected override void OnMouseLeave(EventArgs e) { hoverX = false; hoverM = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnMouseDown(MouseEventArgs e) {
    if (Owner != null && e.Button == MouseButtons.Left && !BtnX.Contains(e.Location) && !BtnM.Contains(e.Location)) {
      ReleaseCapture();
      SendMessageMove(Owner.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // HTCAPTION：拖动窗口
    }
    base.OnMouseDown(e);
  }
  protected override void OnMouseUp(MouseEventArgs e) {
    if (Owner != null) {
      if (BtnX.Contains(e.Location)) { Owner.Close(); return; }
      if (BtnM.Contains(e.Location)) { Owner.WindowState = FormWindowState.Minimized; return; }
    }
    base.OnMouseUp(e);
  }
  protected override void OnPaint(PaintEventArgs e) {
    Graphics g = e.Graphics;
    g.SmoothingMode = SmoothingMode.AntiAlias;
    using (SolidBrush b = new SolidBrush(Theme.BG)) g.FillRectangle(b, ClientRectangle);
    // 原实现在 OnPaint 里每次 new Font —— 每帧泄漏一个 GDI 字体对象，缩放后重绘变频繁会更明显
    using (SolidBrush fg = new SolidBrush(Theme.Gold))
      g.DrawString(I18n.T("无尽之剑Ⅲ修改器"), FontBank.Get("Microsoft YaHei", FontStyle.Bold, 10.5f), fg,
                   UiScale.Px(14), UiScale.Px(9));
    DrawBtn(g, BtnM, hoverM, false);
    DrawBtn(g, BtnX, hoverX, true);
    using (Pen p = new Pen(Theme.Line, 1f)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
  }
  void DrawBtn(Graphics g, Rectangle r, bool hover, bool isClose) {
    Color fill = hover ? (isClose ? Theme.Warn : Theme.CardLiteSolid) : Color.FromArgb(255, 251, 247, 238);
    int rad = Math.Max(2, UiScale.Px(8));
    int lim = Math.Min(r.Width, r.Height) / 2;
    if (rad > lim) rad = Math.Max(2, lim);
    using (GraphicsPath p = Theme.Round(r, rad))
    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
    using (GraphicsPath p = Theme.Round(r, rad))
    using (Pen pen = new Pen(Theme.Line, 1f)) g.DrawPath(pen, p);
    int inset = Math.Max(2, UiScale.Px(11));
    using (Pen pen = new Pen(hover ? Color.White : Theme.Ink, Math.Max(1f, UiScale.Px(1.4f)))) {
      if (isClose) {
        int ix = Math.Min(inset, r.Width / 2), iy = Math.Min(Math.Max(2, UiScale.Px(9)), r.Height / 2);
        g.DrawLine(pen, r.X + ix, r.Y + iy, r.Right - ix, r.Bottom - iy);
        g.DrawLine(pen, r.Right - ix, r.Y + iy, r.X + ix, r.Bottom - iy);
      } else {
        int ix = Math.Min(Math.Max(2, UiScale.Px(9)), r.Width / 2);
        g.DrawLine(pen, r.X + ix, r.Y + r.Height / 2, r.Right - ix, r.Y + r.Height / 2);
      }
    }
  }
}

} // namespace
