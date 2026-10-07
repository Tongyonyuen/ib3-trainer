// ============================================================================
// Updater.cs — 自动更新（查 GitHub Releases → 下载 → 校验 → 改名替换）
//
// ★ 本文件**不引用 I18n / Theme / MainForm** —— 这样它能被单独编译、单独自测
//   （build.sh 的 updatetest 分支）。所以它返回的是**中文原文**，由调用方过 I18n.T()
//   （与 Toast.cs:27 的"漏斗里翻一次"同一套路）。
//   反过来，若这里用了 WinForms 的 Application.ExecutablePath，就会把
//   System.Windows.Forms 拖进来，自测编译单元就不干净了 —— 所以用 MainModule.FileName。
//
// ★ 版本号从 BuildInfo（I18n.cs）取。发版时必须让 git tag == BuildInfo.SemVer。
//
// 为什么"改名替换"而不是直接覆盖：
//   Windows 不允许运行中的 exe 覆盖自己（File.Copy 到自身 → 共享冲突）。
//   但 loader 是以 FILE_SHARE_DELETE 打开映像的 ⇒ **改运行中 exe 的名字是允许的，删不行**。
//   所以流程是：下载到 xxx.new.exe → 校验 → 起一个分离的助手进程 → 主程序退出 →
//   助手把真名改成 .old、再把 .new 改成真名 → 重启 → 退出。见 RunApply。
//
// C# 5（csc v4.0.30319）：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ib3Trainer2 {

// ⚠ 类名是 Updater 而**不是** Update —— Form 从 Control 继承了一个 `Update()` 方法，
//   在 MainForm / AboutForm / UpdateForm 这些窗体类内部，裸写 `Update.X` 会被解析成
//   那个方法而不是本类型，编译报 CS0119（"是一个方法，这在给定的上下文中无效"）。
//   本文件名叫 Update.cs 无妨，但类型名必须避开 Control 的成员名。
static class Updater {

  public const string REPO_DEFAULT = "Tongyonyuen/ib3-trainer";
  public const string REPO_URL     = "https://github.com/" + REPO_DEFAULT;

  // 载荷名**必须以 .exe 结尾**：扩展名是 .new 的文件无法用 ShellExecute 启动
  // （ShellExecute 按扩展名找关联），而助手进程是要被启动的。
  public const string NEW_NAME   = "IB3训练器2.new.exe";
  public const string OLD_SUFFIX = ".old";
  public const string DONE_FILE  = "ib3_update.done";
  public const string TMP_NAME   = "ib3_update.tmp";
  public const string STATE_FILE = "ib3_update.ini";

  // Fetch 的失败码（调用方据此决定日志与重试节奏）
  public const string E_NOTMODIFIED = "notmodified";   // 304
  public const string E_NOREPO      = "norepo";        // 404：还没发过正式版
  public const string E_RATELIMIT   = "ratelimit";     // 403/429
  public const string E_NOASSET     = "noasset";       // 有 release 但没有 .exe 资产

  // ---------- 版本标识 ----------

  // 只接受严格的 [v]MAJOR.MINOR.PATCH（可带 -pre 后缀）。
  // **不合法一律返回 false ⇒ 调用方静默放弃**：这是 r13 这类旧标签的安全阀 ——
  // 解析不出就不提示，绝不会把一个用 rNN 命名的旧包当成"新版本"推给用户。
  public static bool TryParseTag(string tag, out int major, out int minor, out int patch) {
    major = minor = patch = 0;
    if (tag == null) return false;
    string s = tag.Trim();
    if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
    if (s.Length == 0) return false;

    int dash = s.IndexOf('-');
    if (dash >= 0) s = s.Substring(0, dash);      // 预发布后缀不参与比较

    string[] parts = s.Split('.');
    if (parts.Length != 3) return false;
    int[] v = new int[3];
    for (int i = 0; i < 3; i++) {
      string p = parts[i];
      if (p.Length == 0 || p.Length > 6) return false;      // 拒绝超长组件
      if (p.Length > 1 && p[0] == '0') return false;        // 拒绝前导 0（v01.2.3）
      for (int j = 0; j < p.Length; j++) if (p[j] < '0' || p[j] > '9') return false;
      if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return false;
    }
    major = v[0]; minor = v[1]; patch = v[2];
    return true;
  }

  public static bool IsNewer(int rMaj, int rMin, int rPat) {
    if (rMaj != BuildInfo.Major) return rMaj > BuildInfo.Major;
    if (rMin != BuildInfo.Minor) return rMin > BuildInfo.Minor;
    return rPat > BuildInfo.Patch;
  }

  public static string LocalSemVer { get { return BuildInfo.SemVer; } }

  // 取真实 exe 路径。**不能用 AppDomain.BaseDirectory 拼** —— 它结尾带 '\'，
  // 拼进命令行会转义掉闭合引号（CreateProcess 的经典坑）。
  public static string RealExePath {
    get {
      try {
        Process p = Process.GetCurrentProcess();
        if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
          return p.MainModule.FileName;
      } catch { }
      try {
        string a = Environment.GetCommandLineArgs()[0];
        if (!string.IsNullOrEmpty(a)) return Path.GetFullPath(a);
      } catch { }
      return null;
    }
  }

  // ---------- 状态文件（ib3_update.ini）----------
  // 读取写法照抄 I18n.LoadPref/SavePref（I18n.cs:132-158）：UTF-8 无 BOM、容忍注释、
  // 非破坏式 key=value。**不要**用 Launcher.SaveConfig —— 那是整文件截断成一行（Launcher.cs:37-39）。

  public static UpdateState LoadState(string trainerDir) {
    UpdateState st = new UpdateState();
    try {
      string f = Path.Combine(trainerDir, STATE_FILE);
      if (!File.Exists(f)) return st;
      foreach (string raw in File.ReadAllLines(f)) {
        string t = raw.Trim();
        if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
        int eq = t.IndexOf('=');
        if (eq <= 0) continue;
        string k = t.Substring(0, eq).Trim().ToLowerInvariant();
        string v = t.Substring(eq + 1).Trim();
        switch (k) {
          case "lastcheck": st.LastCheck = v; break;
          case "etag":      st.ETag = v; break;
          case "skip":      st.Skip = v; break;
          case "repo":      if (v.Length > 0) st.Repo = v; break;
          case "feedurl":   st.FeedUrl = v; break;
          case "ok":        st.Ok = (v == "0") ? 0 : 1; break;
          case "devdump":   st.DevDump = (v == "1") ? 1 : 0; break;
          case "devnodl":   st.DevNoDownload = (v == "1") ? 1 : 0; break;
          case "swaptest":  st.SwapTest = (v == "1") ? 1 : 0; break;
        }
      }
    } catch { }
    return st;
  }

  public static void SaveState(string trainerDir, UpdateState st) {
    if (st == null) return;
    try {
      File.WriteAllLines(Path.Combine(trainerDir, STATE_FILE), new string[] {
        "; IB3 训练器2 更新检查状态（自动生成，可手编；删掉本文件 = 恢复默认）",
        "; repo    = OWNER/NAME   换仓库（测试用）",
        "; feedurl = 仅在主机为 127.0.0.1 / localhost 时生效（离线夹具测试用）",
        "; devdump   = 1 只把解析结果打进日志，仍会正常提示更新",
        "; devnodl   = 1 走完下载与校验，但不落盘（验证下载链路用）",
        "; swaptest  = 1 不下载，直接用程序目录里已有的 " + NEW_NAME + " 走一遍替换流程",
        "lastcheck=" + (st.LastCheck == null ? "" : st.LastCheck),
        "ok=" + st.Ok,
        "etag=" + (st.ETag == null ? "" : st.ETag),
        "skip=" + (st.Skip == null ? "" : st.Skip),
        "repo=" + ((st.Repo == null || st.Repo.Length == 0) ? REPO_DEFAULT : st.Repo),
        "feedurl=" + (st.FeedUrl == null ? "" : st.FeedUrl),
        "devdump=" + st.DevDump,
        "devnodl=" + st.DevNoDownload,
        "swaptest=" + st.SwapTest
      }, new UTF8Encoding(false));
    } catch { }
  }

  // 成功时 6 小时一次；上次失败则 1 小时后就重试（临时故障能快速恢复，又不至于反复打 GitHub）
  public static bool ShouldAutoCheck(UpdateState st) {
    if (st == null) return true;
    if (string.IsNullOrEmpty(st.LastCheck)) return true;
    DateTime t;
    if (!DateTime.TryParse(st.LastCheck, CultureInfo.InvariantCulture,
          DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return true;
    double hours = (DateTime.UtcNow - t).TotalHours;
    if (hours < 0) return false;                       // 时钟回拨：先别查
    return hours >= (st.Ok == 0 ? 1.0 : 6.0);
  }

  public static bool IsSkipped(UpdateState st, string tag) {
    return st != null && !string.IsNullOrEmpty(st.Skip) && st.Skip == tag;
  }

  public static string NowIso() {
    return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
  }

  // 默认打官方 API；**只有回环地址**能用 feedurl 覆盖 —— 这样既方便离线夹具测试，
  // 又让"被篡改的 ini 把更新器指向攻击者的 exe"不可能发生。
  public static string FeedUrl(UpdateState st) {
    if (st != null && !string.IsNullOrEmpty(st.FeedUrl) && IsLoopbackHttp(st.FeedUrl)) return st.FeedUrl;
    string repo = (st != null && st.Repo != null && st.Repo.Length > 0) ? st.Repo : REPO_DEFAULT;
    return "https://api.github.com/repos/" + repo + "/releases/latest";
  }

  static bool IsLoopbackHttp(string url) {
    try {
      Uri u = new Uri(url);
      if (u.Scheme != "http") return false;
      string h = u.Host;
      return h == "127.0.0.1" || h == "localhost" || h == "::1";
    } catch { return false; }
  }

  // 只放行 https：打开浏览器/下载都走这里
  public static bool IsSafeUrl(string url) {
    if (string.IsNullOrEmpty(url)) return false;
    return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
  }

  // ---------- 取回 ----------

  static void PrepTls() {
    // csc 产物没有 TargetFrameworkAttribute ⇒ 运行时按 .NET 4.0 对待 ⇒ 默认 Ssl3|Tls，
    // 与只收 TLS1.2+ 的 GitHub 握手必失败。本机注册表里 SchUseStrongCrypto /
    // SystemDefaultTlsVersions 都没设，所以不能指望系统默认值。
    // 3072 = Tls12；用字面量是因为 C#5 下对 4.0 参考程序集未必有 SecurityProtocolType.Tls12。
    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
  }

  static NetClient MakeClient(UpdateState st) {
    PrepTls();
    NetClient c = new NetClient();
    // WebClient.Encoding 默认是**系统 ANSI 代码页**。中文 Windows 上是 GBK ⇒ body 与
    // 资产名会变乱码。作者本机开了 Windows 的 UTF-8 选项，所以这个 bug 在本机看不见 —— 必须显式设。
    c.Encoding = new UTF8Encoding(false);
    c.Headers["Accept"] = "application/vnd.github+json";
    c.Headers["X-GitHub-Api-Version"] = "2022-11-28";
    if (st != null && !string.IsNullOrEmpty(st.ETag)) c.Headers["If-None-Match"] = st.ETag;
    return c;
  }

  public static UpdateInfo Fetch(UpdateState st, out string newEtag, out string err) {
    newEtag = null; err = null;
    NetClient c = MakeClient(st);
    try {
      string body = c.DownloadString(FeedUrl(st));
      try { newEtag = c.ResponseHeaders["ETag"]; } catch { }
      UpdateInfo info = Parse(body, out err);
      if (info != null && string.IsNullOrEmpty(info.AssetUrl)) err = E_NOASSET;
      return info;
    } catch (WebException we) {
      HttpWebResponse hr = we.Response as HttpWebResponse;
      if (hr != null) {
        int code = (int)hr.StatusCode;
        if (hr.StatusCode == HttpStatusCode.NotModified) { err = E_NOTMODIFIED; return null; }
        if (hr.StatusCode == HttpStatusCode.NotFound) { err = E_NOREPO; return null; }
        if (code == 403 || code == 429) { err = E_RATELIMIT; return null; }
        err = "HTTP " + code;
        return null;
      }
      err = we.Message;
      return null;
    } catch (Exception ex) {
      err = ex.Message;
      return null;
    } finally {
      try { c.Dispose(); } catch { }
    }
  }

  public static UpdateInfo Parse(string json, out string err) {
    err = null;
    if (json == null) { err = "空响应"; return null; }
    try {
      int ti = KeyAt(json, "\"tag_name\"", 0);
      if (ti < 0) { err = "响应里没有 tag_name"; return null; }
      string tag = JsonStrAt(json, ti);
      if (tag == null) { err = "tag_name 解析失败"; return null; }

      UpdateInfo info = new UpdateInfo();
      info.Tag = tag;

      // html_url：**第一次出现的是 release 自己的**（author.html_url 在后面）
      int hi = KeyAt(json, "\"html_url\"", 0);
      if (hi >= 0) info.HtmlUrl = JsonStrAt(json, hi);

      int bi = KeyAt(json, "\"body\"", 0);
      if (bi >= 0 && bi < json.Length && json[bi] == '"') {
        string notes = JsonStrAt(json, bi);
        if (notes != null) {
          if (notes.Length > 1500) notes = notes.Substring(0, 1500) + "…";
          info.Notes = notes;
        }
      }

      // ★ assets 必须在**切片内部**找：release 自己的 "name"（发布标题）出现在 assets 之前，
      //   在整份 JSON 里裸找 "name" 会抓到标题而不是资产名。
      int ai = KeyAt(json, "\"assets\"", 0);
      if (ai >= 0 && ai < json.Length && json[ai] == '[') {
        int close = MatchBracket(json, ai, '[', ']');
        if (close > ai) {
          string arr = json.Substring(ai, close - ai + 1);
          List<string> objs = JsonObjects(arr);
          for (int k = 0; k < objs.Count; k++) {
            string o = objs[k];
            int ni = KeyAt(o, "\"name\"", 0);
            string an = (ni >= 0) ? JsonStrAt(o, ni) : null;
            if (an == null) continue;
            if (!an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            int ui = KeyAt(o, "\"browser_download_url\"", 0);
            string au = (ui >= 0) ? JsonStrAt(o, ui) : null;
            if (au == null) continue;
            info.AssetName = an;
            info.AssetUrl = au;
            info.AssetSize = ReadNumber(o, KeyAt(o, "\"size\"", 0));
            int di = KeyAt(o, "\"digest\"", 0);
            if (di >= 0 && di < o.Length && o[di] == '"') {
              string d = JsonStrAt(o, di);
              if (d != null && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                info.AssetSha256 = d.Substring(7).Trim();
            }
            break;   // 只取第一个 .exe 资产
          }
        }
      }
      return info;
    } catch (Exception ex) {
      err = ex.Message;
      return null;
    }
  }

  // "DATA_REV: 3" —— 行首（允许前导空白）出现，且数字大于本地 DataRev 才算
  public static bool HasDataRevBump(string notes) {
    if (notes == null) return false;
    string[] lines = notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    for (int i = 0; i < lines.Length; i++) {
      string t = lines[i].Trim();
      if (!t.StartsWith("DATA_REV:", StringComparison.OrdinalIgnoreCase)) continue;
      string num = t.Substring(9).Trim();
      int sp = num.IndexOf(' ');
      if (sp > 0) num = num.Substring(0, sp);
      int v;
      if (int.TryParse(num, NumberStyles.None, CultureInfo.InvariantCulture, out v))
        return v > BuildInfo.DataRev;
    }
    return false;
  }

  // ---------- 下载 / 校验 ----------

  public static byte[] DownloadAsset(UpdateInfo info, out string err) {
    err = null;
    if (info == null || string.IsNullOrEmpty(info.AssetUrl)) { err = "没有可下载的资产"; return null; }
    NetClient c = MakeClient(null);
    try {
      return c.DownloadData(info.AssetUrl);
    } catch (Exception ex) {
      err = ex.Message;
      return null;
    } finally {
      try { c.Dispose(); } catch { }
    }
  }

  public static string Sha256Hex(byte[] data) {
    using (SHA256 sha = SHA256.Create()) {
      byte[] h = sha.ComputeHash(data);
      StringBuilder sb = new StringBuilder();
      for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
      return sb.ToString();
    }
  }

  // 全部检查通过才算数；**任何一项不过都不落盘**
  public static bool VerifyPayload(byte[] data, UpdateInfo info, out string reason, out bool hashSkipped) {
    reason = null; hashSkipped = false;
    if (data == null || data.Length == 0) { reason = "下载内容为空"; return false; }
    if (data.Length < 100 * 1024) { reason = "文件太小（" + data.Length + " 字节）"; return false; }
    if (data.Length > 32 * 1024 * 1024) { reason = "文件太大（" + data.Length + " 字节）"; return false; }
    if (info != null && info.AssetSize > 0 && info.AssetSize != data.Length) {
      reason = "大小不符（API 说 " + info.AssetSize + "，实收 " + data.Length + "）";
      return false;
    }
    if (data[0] != (byte)'M' || data[1] != (byte)'Z') { reason = "不是可执行文件（缺 MZ 头）"; return false; }
    if (info == null || string.IsNullOrEmpty(info.AssetSha256)) { hashSkipped = true; return true; }
    string got = Sha256Hex(data);
    if (!string.Equals(got, info.AssetSha256, StringComparison.OrdinalIgnoreCase)) {
      reason = "sha256 不符";
      return false;
    }
    return true;
  }

  // ---------- 暂存 / 交接 ----------

  // 真·可写探针。app.manifest 是 asInvoker 且**有清单** ⇒ 没有 UAC 文件虚拟化兜底，
  // 装在 Program Files 下会直接 UnauthorizedAccessException，所以提前问清楚、降级为"打开下载页"。
  public static bool CanWriteDir(string dir) {
    try {
      string p = Path.Combine(dir, TMP_NAME);
      using (FileStream fs = File.Create(p)) { }
      File.Delete(p);
      return true;
    } catch { return false; }
  }

  // 同一目录里还有别的实例在跑？→ 必须拒绝更新：否则助手会把 exe 从第二个实例脚下改名。
  public static string SameDirOtherInstance(string realPath) {
    if (string.IsNullOrEmpty(realPath)) return null;
    try {
      int me = Process.GetCurrentProcess().Id;
      Process[] all = Process.GetProcesses();
      for (int i = 0; i < all.Length; i++) {
        Process p = all[i];
        try {
          if (p.Id == me) continue;
          if (p.MainModule == null) continue;
          if (string.Equals(p.MainModule.FileName, realPath, StringComparison.OrdinalIgnoreCase))
            return p.ProcessName;
        } catch { }
      }
    } catch { }
    return null;
  }

  public static string StagePayload(string dir, byte[] data, out string err) {
    err = null;
    try {
      string p = Path.Combine(dir, NEW_NAME);
      File.WriteAllBytes(p, data);
      return p;
    } catch (Exception ex) { err = ex.Message; return null; }
  }

  public static bool StartHelper(string newPath, string realPath, string tag, out string err) {
    err = null;
    try {
      Process me = Process.GetCurrentProcess();
      ProcessStartInfo psi = new ProcessStartInfo();
      psi.FileName = newPath;
      psi.Arguments = "/apply " + me.Id + " " + me.StartTime.Ticks + " \"" + realPath + "\" " +
                      (tag == null ? "" : tag);
      psi.WorkingDirectory = Path.GetDirectoryName(realPath);
      // UseShellExecute=false：走 CreateProcess —— 不查扩展名关联、不借道 shell，可预测。
      // （Launcher.cs:124-127 用 true 是因为它要启动第三方 GUI 启动器，场景不同。）
      psi.UseShellExecute = false;
      psi.CreateNoWindow = true;
      Process h = Process.Start(psi);
      if (h == null) { err = "CreateProcess 返回 null"; return false; }
      return true;
    } catch (Exception ex) { err = ex.Message; return false; }
  }

  // ---------- 助手模式 ----------

  public static bool IsApplyMode(string[] args) {
    return args != null && args.Length >= 1 &&
           string.Equals(args[0], "/apply", StringComparison.OrdinalIgnoreCase);
  }

  static void HelperFail(string msg) {
    try {
      File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ib3_crash.txt"),
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [update-helper] " + msg + Environment.NewLine);
    } catch { }
    try {
      System.Windows.Forms.MessageBox.Show(msg, "IB3 训练器2 更新",
        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
    } catch { }
  }

  static bool MoveWithRetry(string from, string to, bool deleteTargetFirst) {
    for (int i = 0; i < 20; i++) {
      try {
        if (deleteTargetFirst && File.Exists(to)) File.Delete(to);
        File.Move(from, to);
        return true;
      } catch { Thread.Sleep(250); }
    }
    return false;
  }

  // 返回值即进程退出码。任何失败都**不留下半成品**。
  public static int RunApply(string[] args) {
    try {
      if (args.Length < 4) { HelperFail("更新助手：参数不足"); return 2; }
      int pid; long ticks;
      if (!int.TryParse(args[1], out pid) || pid <= 0) { HelperFail("更新助手：pid 无效"); return 2; }
      if (!long.TryParse(args[2], out ticks)) { HelperFail("更新助手：启动时刻无效"); return 2; }
      string real = args[3];
      string tag = (args.Length >= 5) ? args[4] : null;
      string self = RealExePath;

      if (string.IsNullOrEmpty(real) || !File.Exists(real)) { HelperFail("更新助手：目标不存在\n" + real); return 2; }
      if (self != null && string.Equals(self, real, StringComparison.OrdinalIgnoreCase)) { HelperFail("更新助手：目标就是自己"); return 2; }
      string dir = Path.GetDirectoryName(real);
      if (!CanWriteDir(dir)) { HelperFail("更新助手：目录不可写\n" + dir); return 3; }

      // 等父进程退出。用**启动时刻**而不只 pid —— pid 会被复用，
      // 单看 pid 可能在父进程已死、pid 被别人占用时错误地一直等。
      bool gone = false;
      for (int i = 0; i < 600; i++) {          // 上限 120 秒
        try {
          Process p = Process.GetProcessById(pid);
          try { if (p.StartTime.Ticks != ticks) { gone = true; break; } } catch { }
        } catch (ArgumentException) { gone = true; break; }
        catch { }
        Thread.Sleep(200);
      }
      if (!gone) { HelperFail("更新未完成（修改器仍在运行）—— 请手动关闭后重试"); return 4; }

      string bak = real + OLD_SUFFIX;
      // (a) 真名 → .old。此后 real 短暂不存在 —— 这中间**不做任何 I/O**，把窗口压到最短。
      if (!MoveWithRetry(real, bak, true)) {
        HelperFail("无法重命名原文件（可能被杀软占用）\n" + real);
        return 5;
      }
      // (b) .new → 真名。改的是**助手自己**的映像名 —— loader 用 FILE_SHARE_DELETE 打开映像，
      //     所以允许；这正是整个方案成立的原因。
      bool moved = MoveWithRetry(self, real, false);
      if (moved) {
        WriteDone(dir, tag, "move");
      } else {
        // 退路：此刻 real 不存在，直接复制是合法的（助手自己删不掉自己的映像，.new 留给启动清理）
        try {
          File.Copy(self, real, true);
          WriteDone(dir, tag, "copy");
        } catch (Exception ex) {
          try { File.Move(bak, real); } catch { }        // 回滚
          HelperFail("替换失败，已回滚：" + ex.Message);
          return 6;
        }
      }

      // (c) 立刻校验；不通过就回滚 —— 没有这一步，失败的 (b) 会让用户彻底没有 exe
      bool ok = false;
      try {
        FileInfo fi = new FileInfo(real);
        ok = fi.Exists && fi.Length >= 100 * 1024;
      } catch { }
      if (!ok) {
        try { if (File.Exists(real)) File.Delete(real); } catch { }
        try { File.Move(bak, real); } catch { }
        HelperFail("替换后校验失败，已回滚到旧版本");
        return 7;
      }

      // 重启（这里是启动一个真正的 .exe，UseShellExecute=true 才对）
      try {
        ProcessStartInfo psi = new ProcessStartInfo(real);
        psi.WorkingDirectory = Path.GetDirectoryName(real);
        psi.UseShellExecute = true;
        Process.Start(psi);
      } catch (Exception ex) {
        HelperFail("更新已完成，但自动重启失败，请手动打开 IB3训练器2.exe\n" + ex.Message);
      }
      return 0;
    } catch (Exception ex) {
      HelperFail("更新助手异常：" + ex.Message);
      return 9;
    }
  }

  static void WriteDone(string dir, string tag, string mode) {
    try {
      File.WriteAllLines(Path.Combine(dir, DONE_FILE), new string[] {
        "tag=" + (tag == null ? "" : tag),
        "utc=" + NowIso(),
        "mode=" + mode
      }, new UTF8Encoding(false));
    } catch { }
  }

  // ---------- 启动清理 ----------
  // 返回给 Log() 的中文串，或 null。
  public static string CleanupLeftovers(string dir) {
    string msg = null;
    try {
      string real = RealExePath;
      string bak = (real == null) ? null : real + OLD_SUFFIX;
      string donePath = Path.Combine(dir, DONE_FILE);
      string newer = Path.Combine(dir, NEW_NAME);

      if (File.Exists(donePath)) {
        string tag = null;
        DateTime when = DateTime.MinValue;
        try {
          foreach (string raw in File.ReadAllLines(donePath)) {
            string t = raw.Trim();
            if (t.StartsWith("tag=")) tag = t.Substring(4).Trim();
            else if (t.StartsWith("utc=")) {
              DateTime.TryParse(t.Substring(4).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when);
            }
          }
        } catch { }

        double ageMin = (when == DateTime.MinValue) ? -1 : (DateTime.UtcNow - when).TotalMinutes;
        if (ageMin >= 0 && ageMin < 1.0) {
          msg = "已更新到 " + (string.IsNullOrEmpty(tag) ? "新版本" : tag) +
                "（旧版本保留为 " + Path.GetFileName(bak == null ? NEW_NAME : bak) + "）";
        }
        // 5 分钟后才删 .old：留一个"改个名就能回滚"的窗口。
        // 不能用 .old 的时间戳判断 —— 它带的是**上一个 exe** 的日期，可能好几天前。
        if (ageMin > 5.0) {
          try { if (bak != null && File.Exists(bak)) File.Delete(bak); } catch { }
          try { File.Delete(donePath); } catch { }
        }
      }

      // 陈旧的 .new 才删（>24h）；新鲜的要留着 —— 那是一次成功的下载，别浪费
      try {
        if (File.Exists(newer) &&
            (DateTime.UtcNow - File.GetLastWriteTimeUtc(newer)).TotalHours > 24.0)
          File.Delete(newer);
      } catch { }
    } catch { }
    return msg;
  }

  // ==========================================================================
  // 极简 JSON 取字段
  //
  // 为什么不用 JavaScriptSerializer：① 它的 MaxJsonLength 默认 2MB，release 说明一长就
  // 抛异常 —— 网络路径上崩溃形状的失败；② 会把 1.8MB 的 System.Web.Extensions.dll
  // 拖进一个 520KB 的 exe，与本项目"单文件自足"的取舍相反。
  // 我们只要 5 个标量 + 一个资产列表，且 release 是自己发的、字段顺序稳定。
  // 解析失败一律返回 null ⇒ 静默"无更新"，不抛异常、不写 crash。
  // ==========================================================================

  // 找 "key": 并把下标停在值的第一位。**必须连引号一起找**，否则会命中别人值里的同名词。
  static int KeyAt(string s, string key, int from) {
    int i = from;
    while (i >= 0 && i < s.Length) {
      int p = s.IndexOf(key, i, StringComparison.Ordinal);
      if (p < 0) return -1;
      int j = p + key.Length;
      while (j < s.Length && IsWs(s[j])) j++;
      if (j < s.Length && s[j] == ':') {
        j++;
        while (j < s.Length && IsWs(s[j])) j++;
        return j;
      }
      i = p + key.Length;
    }
    return -1;
  }

  static bool IsWs(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r'; }

  // i 指向开引号；返回已解转义的字符串
  static string JsonStrAt(string s, int i) {
    if (i < 0 || i >= s.Length || s[i] != '"') return null;
    StringBuilder sb = new StringBuilder();
    i++;
    while (i < s.Length) {
      char c = s[i];
      if (c == '\\') {
        i++;
        if (i >= s.Length) break;
        char e = s[i];
        switch (e) {
          case '"':  sb.Append('"');  break;
          case '\\': sb.Append('\\'); break;
          case '/':  sb.Append('/');  break;
          case 'b':  sb.Append('\b'); break;
          case 'f':  sb.Append('\f'); break;
          case 'n':  sb.Append('\n'); break;
          case 'r':  sb.Append('\r'); break;
          case 't':  sb.Append('\t'); break;
          case 'u':
            if (i + 4 < s.Length) {
              int cp;
              if (int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out cp)) {
                sb.Append((char)cp);
                i += 4;
              }
            }
            break;
          default: sb.Append(e); break;
        }
        i++;
      } else if (c == '"') {
        return sb.ToString();
      } else {
        sb.Append(c);
        i++;
      }
    }
    return null;
  }

  // i 指向开引号，返回闭引号之后的下标（按反斜杠跳过转义，不会把 \\" 数错）
  static int SkipString(string s, int i) {
    i++;
    while (i < s.Length) {
      if (s[i] == '\\') { i += 2; continue; }
      if (s[i] == '"') return i + 1;
      i++;
    }
    return -1;
  }

  // s[from] 是 open；返回配对的 close 下标。字符串内部的括号会被跳过。
  static int MatchBracket(string s, int from, char open, char close) {
    int depth = 0;
    int i = from;
    while (i < s.Length) {
      char c = s[i];
      if (c == '"') { i = SkipString(s, i); if (i < 0) return -1; continue; }
      if (c == open) depth++;
      else if (c == close) { depth--; if (depth == 0) return i; }
      i++;
    }
    return -1;
  }

  // 把 "[{...},{...}]" 切成对象串
  static List<string> JsonObjects(string arr) {
    List<string> list = new List<string>();
    int i = 0;
    while (i < arr.Length) {
      int p = arr.IndexOf('{', i);
      if (p < 0) break;
      int e = MatchBracket(arr, p, '{', '}');
      if (e < 0) break;
      list.Add(arr.Substring(p, e - p + 1));
      i = e + 1;
    }
    return list;
  }

  static long ReadNumber(string s, int i) {
    if (i < 0 || i >= s.Length) return 0;
    int j = i;
    if (j < s.Length && s[j] == '-') j++;
    while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++;
    if (j == i) return 0;
    long v;
    if (long.TryParse(s.Substring(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
    return 0;
  }
}

// 一次更新检查的结果
class UpdateInfo {
  public string Tag;
  public string HtmlUrl;
  public string Notes;
  public string AssetName;
  public string AssetUrl;
  public string AssetSha256;
  public long AssetSize;
}

// ib3_update.ini 的内容
class UpdateState {
  public string LastCheck;
  public string ETag;
  public string Skip;
  public string Repo = Updater.REPO_DEFAULT;
  public string FeedUrl;
  public int Ok = 1;
  public int DevDump;
  public int DevNoDownload;
  public int SwapTest;
}

// WebClient 没有 Timeout 属性 —— 只能覆写 GetWebRequest 才设得上。
// 不设的话，一条僵死的 TLS 连接会永久占住一个后台线程（IsBackground 只是不阻止进程退出，
// 并不会把它收回来）。
class NetClient : WebClient {
  protected override WebRequest GetWebRequest(Uri a) {
    WebRequest r = base.GetWebRequest(a);
    try { r.Timeout = 15000; } catch { }
    HttpWebRequest h = r as HttpWebRequest;
    if (h != null) {
      try { h.ReadWriteTimeout = 20000; } catch { }
      // GitHub API 缺 User-Agent 直接 403 —— 用属性设，别塞进 Headers（两者同时设会冲突）
      try { if (string.IsNullOrEmpty(h.UserAgent)) h.UserAgent = "IB3Trainer2/" + BuildInfo.SemVer; } catch { }
    }
    return r;
  }
}

} // namespace
