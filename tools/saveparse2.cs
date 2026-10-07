// saveparse2.cs - full-type save dump parser
using System; using System.IO; using System.Text; using System.Collections.Generic;
class SP2 {
  static void Main(string[] a) {
    var d = File.ReadAllBytes(a[0]);
    var results = new List<string>();
    for (int p = 0; p < d.Length - 60; p++) {
      int nl = BitConverter.ToInt32(d, p);
      if (nl <= 1 || nl > 120) continue;
      bool ok = true;
      for (int k = 0; k < nl-1; k++) { byte c = d[p+4+k]; if (c < 0x20 || c > 0x7E) { ok=false; break; } }
      if (!ok || d[p+4+nl-1] != 0) continue;
      string name = Encoding.ASCII.GetString(d, p+4, nl-1);
      int q = p + 4 + nl;
      int tl = BitConverter.ToInt32(d, q);
      if (tl <= 1 || tl > 30) continue;
      for (int k = 0; k < tl-1; k++) { byte c = d[q+4+k]; if (c < 0x20 || c > 0x7E) { ok=false; break; } }
      if (!ok || d[q+4+tl-1] != 0) continue;
      string type = Encoding.ASCII.GetString(d, q+4, tl-1);
      int r = q + 4 + tl;
      int sz = BitConverter.ToInt32(d, r);
      if (sz < 0 || sz > 4000 || r + 8 + sz > d.Length) continue;
      int arr = BitConverter.ToInt32(d, r+4);
      if (arr < 0 || arr > 1000) continue;
      if (type == "IntProperty" && sz == 4) {
        results.Add(name + " = " + BitConverter.ToInt32(d, r+8));
        p = r + 8 + sz; continue;
      }
      if (type == "BoolProperty" && sz == 0) {
        results.Add(name + " = " + (arr != 0 ? "true" : "false"));
        continue;
      }
      if (type == "FloatProperty" && sz == 4) {
        results.Add(name + " = " + BitConverter.ToSingle(d, r+8));
        p = r + 8 + sz; continue;
      }
      if (type == "NameProperty" && sz == 8) {
        int ni = BitConverter.ToInt32(d, r+8);
        results.Add(name + " = Name#" + ni);
        p = r + 8 + sz; continue;
      }
      if (type == "StrProperty" && sz >= 1 && r+16 <= d.Length) {
        int sl = BitConverter.ToInt32(d, r+8);
        if (sl > 0 && sl < 500 && r+12+sl <= d.Length) {
          results.Add(name + " = \"" + Encoding.ASCII.GetString(d, r+12, sl-1) + "\"");
          p = r + 12 + sl; continue;
        } else if (sl < 0) {
          int cl = -sl; results.Add(name + " = [utf16 " + cl + "]");
          p = r + 12 + cl*2; continue;
        }
        continue;
      }
      if (type == "ByteProperty" && sz == 1) {
        results.Add(name + " = byte " + d[r+8]);
        p = r + 8 + sz; continue;
      }
      p = r + 8 + sz; // skip unhandled property payload by size
    }
    foreach (var s in results) Console.WriteLine(s);
    Console.WriteLine("total: " + results.Count);
  }
}
