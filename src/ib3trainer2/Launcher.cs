// ============================================================================
// Launcher.cs — 启动与附着：唯一正确启动方式 = 移植版启动器（保中文）。
// 直启 Win64\IB3.exe 会丢语言参数变英文（2026-10-06 事故复盘结论）。
// 注意：启动器开出后还需点其上的 Play 才进游戏 → 提供 best-effort 自动点击
//      （BM_CLICK 消息，不抢焦点；找不到按钮则提示用户手动点）。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ib3Trainer2 {

static class Launcher {
  public const string GAME_SUFFIX = @"Win64\IB3.exe";

  // ---- 游戏目录配置（由玩家手动指定"启动器所在文件夹"；上探一级 = 游戏根目录） ----
  public static string LauncherDir = null;
  public static string LauncherExe { get { return LauncherDir == null ? null : Path.Combine(LauncherDir, "Infinity Blade Launcher.exe"); } }
  public static string GameRoot { get { return LauncherDir == null ? null : Path.GetDirectoryName(LauncherDir); } }

  public static void LoadConfig(string trainerDir) {
    try {
      string f = Path.Combine(trainerDir, "ib3_paths.ini");
      if (!File.Exists(f)) return;
      foreach (string ln in File.ReadAllLines(f)) {
        string t = ln.Trim();
        if (t.StartsWith("LauncherDir=")) {
          string d = t.Substring(12).Trim();
          if (d.Length > 0 && Directory.Exists(d)) LauncherDir = d;
        }
      }
    } catch { }
  }
  public static void SaveConfig(string trainerDir, string dir) {
    try { File.WriteAllLines(Path.Combine(trainerDir, "ib3_paths.ini"), new string[] { "LauncherDir=" + dir }); } catch { }
  }

  // ---- 自动部署随包 upk（游戏根目录\SwordGame\CookedPCConsole\SwordGame.upk）----
  public static string DeployUpk(string gameRoot, string trainerDir) {
    try {
      string src = Path.Combine(trainerDir, "SwordGame.upk");
      if (!File.Exists(src)) return "未找到随包 SwordGame.upk（跳过替换）";
      if (gameRoot == null) return "游戏根目录未确定（跳过替换）";
      string dst = UpkPath(gameRoot);
      string dstDir = Path.GetDirectoryName(dst);
      if (!Directory.Exists(dstDir)) return "目标目录不存在（请核对所选游戏目录）：" + dstDir;
      string m1 = Md5(src);
      string m2 = File.Exists(dst) ? Md5(dst) : null;
      if (m1 == m2) return "upk 已是最新（无需替换）";
      string bak = dst + ".orig";
      if (File.Exists(dst) && !File.Exists(bak)) {
        try { File.Copy(dst, bak); } catch (Exception ex) { return "备份原 upk 失败：" + ex.Message; }
      }
      try { File.Copy(src, dst, true); }
      catch (Exception ex) { return "upk 替换失败（游戏正在运行？请先关闭游戏再重试）：" + ex.Message; }
      return "upk 已替换生效（原文件已备份为 SwordGame.upk.orig）";
    } catch (Exception ex) { return "upk 替换异常：" + ex.Message; }
  }
  // 只读比对：随包 upk 是否已部署到游戏目录。返回 null = 一致；否则返回人话原因。
  //
  // 为什么要单独校验：训练器的自定义控制台命令（giveitemonce / addconsumable /
  // setplayergiveallitems / masterallowneditems / setplayergems / setgivekeyitem /
  // setplayercreatenewlistofstoregems）在游戏本体 IB3.exe 里一个都不存在，全部是
  // SwordGame.upk 里的脚本函数。upk 没部署 → 这些命令一律执行不了（发放/掌握全废），
  // 而 enablecheats/god 这类游戏自带命令照常工作。
  // 本函数只读不写，绝不擅自改用户游戏目录里的文件。
  public static string VerifyUpk(string gameRoot, string trainerDir) {
    try {
      string src = Path.Combine(trainerDir, "SwordGame.upk");
      if (!File.Exists(src)) return "训练器目录缺少随包 SwordGame.upk";
      if (gameRoot == null) return "游戏目录未设置";
      string dst = UpkPath(gameRoot);
      if (!File.Exists(dst)) return "游戏目录内未找到 SwordGame.upk（" + dst + "）";
      if (Md5(src) != Md5(dst))
        return "随包 upk 与游戏内的不一致 —— 物品发放/掌握升阶等自定义命令会全部失效";
      return null;
    } catch (Exception ex) { return "upk 校验异常：" + ex.Message; }
  }

  public static string UpkPath(string gameRoot) {
    return Path.Combine(Path.Combine(Path.Combine(gameRoot, "SwordGame"), "CookedPCConsole"), "SwordGame.upk");
  }

  static string Md5(string f) {
    using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
    using (FileStream fs = File.OpenRead(f)) {
      byte[] h = md5.ComputeHash(fs);
      StringBuilder sb = new StringBuilder();
      for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
      return sb.ToString();
    }
  }

  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, Win32.EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);

  // 精确名 IB3 查找（排除"IB3内存修改器/IB3训练器"等）
  public static Process FindGame() {
    foreach (Process p in Process.GetProcesses()) {
      try { if (string.Equals(p.ProcessName, "IB3", StringComparison.OrdinalIgnoreCase)) return p; } catch { }
    }
    return null;
  }

  // 映像路径校验：必须是移植版的 Win64\IB3.exe
  public static bool ValidateImage(Process p) {
    try {
      if (p == null || p.HasExited || p.MainModule == null) return false;
      string f = p.MainModule.FileName;
      return f.EndsWith(GAME_SUFFIX, StringComparison.OrdinalIgnoreCase);
    } catch { return false; }
  }

  // 启动移植版启动器；返回进程（供 AutoClickPlayAsync 用）
  public static Process StartGame(out string err) {
    err = null;
    try {
      string exe = LauncherExe;
      if (exe == null || !File.Exists(exe)) { err = "尚未设置游戏目录（请点「游戏目录…」选择启动器所在文件夹）"; return null; }
      ProcessStartInfo psi = new ProcessStartInfo(exe);
      psi.WorkingDirectory = LauncherDir;
      psi.UseShellExecute = true;
      return Process.Start(psi);
    } catch (Exception ex) { err = ex.Message; return null; }
  }

  // 启动器需额外点 Play 才进游戏 —— 后台 best-effort 自动点击（不抢焦点）
  public static void AutoClickPlayAsync(Process launcher) {
    if (launcher == null) return;
    Thread th = new Thread(delegate() {
      for (int i = 0; i < 90; i++) {
        Thread.Sleep(1000);
        try {
          if (launcher.HasExited) return;
          IntPtr btn = FindPlayButton((uint)launcher.Id);
          if (btn != IntPtr.Zero) {
            SendMessage(btn, 0x00F5 /* BM_CLICK */, IntPtr.Zero, IntPtr.Zero);
            return;
          }
        } catch { }
      }
    });
    th.IsBackground = true;
    th.Start();
  }

  // 在启动器窗口的子控件里找 Play/开始 按钮
  static IntPtr FindPlayButton(uint pid) {
    IntPtr found = IntPtr.Zero;
    Win32.EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint wpid;
      Win32.GetWindowThreadProcessId(h, out wpid);
      if (wpid != pid) return true;
      EnumChildWindows(h, delegate(IntPtr c, IntPtr l2) {
        if (!Win32.IsWindowVisible(c)) return true;
        StringBuilder sb = new StringBuilder(128);
        Win32.GetWindowText(c, sb, 128);
        string t = sb.ToString().ToLowerInvariant();
        if (t.IndexOf("play") >= 0 || t.IndexOf("start") >= 0 ||
            t.IndexOf("开始") >= 0 || t.IndexOf("启动") >= 0 || t.IndexOf("进入") >= 0) {
          found = c;
          return false;
        }
        return true;
      }, IntPtr.Zero);
      return found == IntPtr.Zero;
    }, IntPtr.Zero);
    return found;
  }
}

} // namespace
