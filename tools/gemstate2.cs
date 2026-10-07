// gemstate2.cs - walk each darkfire gem's full tagged property block
using System; using System.IO; using System.Text; using System.Collections.Generic;
class GS2 {
  static byte[] d;
  static int Find(byte[] d, byte[] pat, int start) {
    for (int i = start; i <= d.Length - pat.Length; i++) {
      bool m = true;
      for (int j = 0; j < pat.Length; j++) if (d[i+j] != pat[j]) { m = false; break; }
      if (m) return i;
    }
    return -1;
  }
  static void Main(string[] a) {
    d = File.ReadAllBytes(a[0]);
    byte[] gn = Encoding.ASCII.GetBytes("GemName\0");
    byte[] uea = Encoding.ASCII.GetBytes("UberElementalAttackGem");
    int pos = 0; int idx = 0;
    var blocks = new List<string[]>();
    while (true) {
      int g = Find(d, gn, pos);
      if (g < 0) break;
      idx++;
      // the GemName payload = [len][chars] — read it
      int r0 = g + 8 + 4 + 13 + 4 + 4;   // after name/type/sz/arr
      int sl = BitConverter.ToInt32(d, r0);
      string nm = "";
      if (sl > 0 && r0+4+sl <= d.Length) nm = Encoding.ASCII.GetString(d, r0+4, sl-1);
      if (nm.StartsWith("UberElementalAttackGem")) {
        // walk the tagged stream from the GemName tag start (g-4 = the nameLen)
        int p = g - 4;
        var props = new List<string>();
        int guard = 0;
        while (guard++ < 300 && p < d.Length - 12) {
          int nl = BitConverter.ToInt32(d, p);
          if (nl <= 0 || nl > 200 || p+4+nl > d.Length) break;
          string pname = Encoding.ASCII.GetString(d, p+4, Math.Max(0,nl-1));
          int q = p + 4 + nl;
          if (q+4 > d.Length) break;
          int tl = BitConverter.ToInt32(d, q);
          if (tl <= 0 || tl > 60 || q+4+tl > d.Length) break;
          string tname = Encoding.ASCII.GetString(d, q+4, Math.Max(0,tl-1));
          int r = q + 4 + tl;
          if (r+8 > d.Length) break;
          int sz = BitConverter.ToInt32(d, r);
          int arr = BitConverter.ToInt32(d, r+4);
          if (pname == "None") { p = r; break; }
          string val = "";
          int adv = sz;
          if (tname == "IntProperty" && sz == 4) val = "" + BitConverter.ToInt32(d, r+8);
          else if (tname == "FloatProperty" && sz == 4) val = "" + BitConverter.ToSingle(d, r+8);
          else if (tname == "ByteProperty" && sz == 1) val = "byte " + d[r+8];
          else if (tname == "BoolProperty" && sz == 0) val = "bool " + (arr != 0);
          else if (tname == "NameProperty" && sz == 8) {
            int ni = BitConverter.ToInt32(d, r+8);
            val = "Name#" + ni;
          }
          else if (tname == "StrProperty") { val = "[str " + sz + "]"; }
          props.Add(pname + "(" + tname + (sz!=0?"/"+sz:"") + ")" + (val!=""?"=" + val:""));
          p = r + 8 + sz;
        }
        blocks.Add(props.ToArray());
      }
      pos = g + 8;
    }
    Console.WriteLine("darkfires: " + blocks.Count);
    for (int i = 0; i < blocks.Count; i++) {
      Console.WriteLine("===== darkfire #" + i + " =====");
      foreach (var s in blocks[i]) Console.WriteLine("  " + s);
    }
  }
}
