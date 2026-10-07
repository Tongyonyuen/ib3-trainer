// sendcmd.cs — 复刻训练器 SendOne 的注入逻辑，命令行版：sendcmd.exe <命令...>
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
class SC {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  static IntPtr gameHwnd = IntPtr.Zero;

  static bool FindGame() {
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      var sb = new System.Text.StringBuilder(256);
      GetWindowText(h, sb, 256);
      string t = sb.ToString();
      if (t.StartsWith("Infinity Blade III")) { gameHwnd = h; return false; }
      return true;
    }, IntPtr.Zero);
    return gameHwnd != IntPtr.Zero;
  }
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int n);

  static void FocusGame() {
    if (IsIconic(gameHwnd)) ShowWindow(gameHwnd, 9);
    SetForegroundWindow(gameHwnd);
    uint pid = 0; GetWindowThreadProcessId(gameHwnd, out pid);
    IntPtr fg = GetForegroundWindow();
    uint fgPid = 0, fgTid = GetWindowThreadProcessId(fg, out fgPid);
    uint myTid = GetCurrentThreadId();
    AttachThreadInput(myTid, fgTid, true);
    SetForegroundWindow(gameHwnd);
    BringWindowToTop(gameHwnd);
    AttachThreadInput(myTid, fgTid, false);
    Thread.Sleep(180);
  }
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

  static bool ConsoleOpen() {
    RECT r; GetWindowRect(gameHwnd, out r);
    int w = r.R - r.L, h = r.B - r.T;
    if (w <= 0 || h <= 0) return false;
    Bitmap bmp = new Bitmap(w, h);
    using (Graphics g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(gameHwnd, dc, 2);
      g.ReleaseHdc(dc);
    }
    bool found = false;
    for (int y = h * 7 / 8; y < h - 20; y += 2) {
      for (int x = 50; x < w - 50; x += 7) {
        Color c = bmp.GetPixel(x, y);
        if (c.G > 180 && c.R < 120 && c.B < 120) { found = true; break; }
      }
      if (found) break;
    }
    bmp.Dispose();
    return found;
  }

  static void EnsureConsole() {
    for (int t = 0; t < 4; t++) {
      if (ConsoleOpen()) return;
      PostMessage(gameHwnd, 0x100, (IntPtr)0xBB, (IntPtr)0x1);
      Thread.Sleep(60);
      PostMessage(gameHwnd, 0x101, (IntPtr)0xBB, (IntPtr)0xC0000001);
      Thread.Sleep(700);
    }
    if (!ConsoleOpen()) throw new Exception("无法打开控制台");
  }

  static void CharToVk(char c, out byte vk, out bool shift) {
    shift = false; vk = 0;
    if (c >= 'a' && c <= 'z') vk = (byte)('A' + (c - 'a'));
    else if (c >= 'A' && c <= 'Z') { vk = (byte)c; shift = true; }
    else if (c >= '0' && c <= '9') vk = (byte)c;
    else if (c == ' ') vk = 0x20;
    else if (c == '_') { vk = 0xBD; shift = true; }
    else if (c == '-') vk = 0xBD;
    else if (c == '.') vk = 0xBE;
    else if (c == '/') vk = 0xBF;
    else { short s = VkKeyScan(c); vk = (byte)(s & 0xFF); shift = ((s >> 8) & 1) != 0; }
  }

  static void SendOne(string cmd) {
    FocusGame();
    bool wasOpen = ConsoleOpen();
    if (!wasOpen) EnsureConsole();
    if (wasOpen) {
      for (int i = 0; i < 40; i++) {
        PostMessage(gameHwnd, 0x100, (IntPtr)0x08, (IntPtr)0xE);
        PostMessage(gameHwnd, 0x101, (IntPtr)0x08, (IntPtr)0xC000000E);
      }
      Thread.Sleep(60);
    }
    FocusGame();
    bool first = true;
    foreach (char c in cmd) {
      if (first) {
        byte vk; bool sh; CharToVk(c, out vk, out sh);
        if (vk == 0) continue;
        if (sh) { PostMessage(gameHwnd, 0x100, (IntPtr)0x10, (IntPtr)0x2A0001); Thread.Sleep(25); }
        PostMessage(gameHwnd, 0x100, (IntPtr)vk, (IntPtr)1);
        Thread.Sleep(35);
        PostMessage(gameHwnd, 0x101, (IntPtr)vk, (IntPtr)0xC0000001);
        Thread.Sleep(15);
        if (sh) { PostMessage(gameHwnd, 0x101, (IntPtr)0x10, (IntPtr)0xC02A0001); Thread.Sleep(10); }
        first = false;
      } else {
        PostMessage(gameHwnd, 0x102, (IntPtr)c, IntPtr.Zero);
        Thread.Sleep(22);
      }
    }
    Thread.Sleep(80);
    FocusGame();
    PostMessage(gameHwnd, 0x100, (IntPtr)0x0D, (IntPtr)0x1C0001);
    Thread.Sleep(50);
    PostMessage(gameHwnd, 0x101, (IntPtr)0x0D, (IntPtr)0xC01C0001);
    Thread.Sleep(500);
    PostMessage(gameHwnd, 0x100, (IntPtr)0xBB, (IntPtr)0x1); // 收起控制台
    Thread.Sleep(60);
    PostMessage(gameHwnd, 0x101, (IntPtr)0xBB, (IntPtr)0xC0000001);
    Thread.Sleep(200);
  }

  static void Main(string[] a) {
    if (a.Length < 1) { Console.WriteLine("用法: sendcmd.exe <命令> [命令2 ...]"); return; }
    if (!FindGame()) { Console.WriteLine("未找到游戏窗口"); Environment.Exit(1); }
    foreach (string c in a) { SendOne(c); Console.WriteLine("已发送: " + c); }
  }
}
