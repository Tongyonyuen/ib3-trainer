// checkcon.cs - ensure console open: checkcon.exe <hwnd>  → toggles until green line found (max 3 tries)
using System; using System.Drawing; using System.Drawing.Imaging; using System.IO; using System.Runtime.InteropServices;
class CC {
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  static Bitmap Cap(IntPtr h) {
    var r = new RECT(); GetWindowRect(h, out r);
    var bmp = new Bitmap(r.R-r.L, r.B-r.T);
    using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
    return bmp;
  }
  struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  static bool IsOpen(Bitmap b) {
    for (int y = b.Height*7/8; y < b.Height-20; y++)
      for (int x = 50; x < b.Width-50; x += 7) {
        Color c = b.GetPixel(x,y);
        if (c.G > 180 && c.R < 120 && c.B < 120) return true;
      }
    return false;
  }
  static void Main(string[] a) {
    IntPtr h = (IntPtr)Convert.ToInt64(a[1]);
    SetForegroundWindow(h); System.Threading.Thread.Sleep(200);
    for (int t = 0; t < 4; t++) {
      var b = Cap(h);
      bool open = IsOpen(b);
      Console.WriteLine("try {0}: console {1}", t, open ? "OPEN" : "closed");
      if (open) { b.Save("con_state.png", ImageFormat.Png); return; }
      PostMessage(h, 0x100, (IntPtr)0xBB, (IntPtr)0x1); System.Threading.Thread.Sleep(60);
      PostMessage(h, 0x101, (IntPtr)0xBB, (IntPtr)0xC0000001); System.Threading.Thread.Sleep(600);
    }
    Console.WriteLine("FAILED to open console");
  }
}
