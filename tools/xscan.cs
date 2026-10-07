// xscan.cs - scan all functions for bytecode refs to a given export (1-based): xscan.exe <upk> <el> <ul> <exportIdx0based>
using System; using System.IO; using System.Collections.Generic;
class XS { static string F(string s){ return s; }
  static void Main(string[] a) {
    byte[] d = File.ReadAllBytes(a[0]);
    var paths = new List<string>(); var clss = new List<string>();
    foreach (var line in File.ReadAllLines(a[1])) {
      var t = line.Trim();
      if (t == "" || !char.IsDigit(t[0])) continue;
      int q1 = t.IndexOf('\''); if (q1 < 0) { paths.Add(null); clss.Add(null); continue; }
      string head = t.Substring(0, q1).Trim();
      var hp = head.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
      clss.Add(hp.Length > 1 ? hp[1] : head);
      int q2 = t.LastIndexOf('\'');
      paths.Add(t.Substring(q1 + 1, q2 - q1 - 1));
    }
    var offs = new List<int>(); var sizes = new List<int>();
    foreach (var line in File.ReadAllLines(a[2])) {
      var t = line.Trim();
      if (t == "" || !char.IsDigit(t[0])) continue;
      var p = t.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
      offs.Add(Convert.ToInt32(p[1], 16)); sizes.Add(Convert.ToInt32(p[2], 16));
    }
    int want = int.Parse(a[3]); // 1-based export ref to find
    byte[] pat = BitConverter.GetBytes(want);
    for (int i = 0; i < paths.Count; i++) {
      if (paths[i] == null || clss[i] != "Function") continue;
      int o = offs[i], sz = sizes[i];
      if (o <= 0 || sz < 20) continue;
      for (int k = o + 48; k < o + sz - 16; k++) {
        if (d[k]==pat[0] && d[k+1]==pat[1] && d[k+2]==pat[2] && d[k+3]==pat[3]) {
          Console.WriteLine("{0} @0x{1:X} (script+{2})", paths[i], k, k - o - 48);
          k += 3;
        }
      }
    }
  }
}
