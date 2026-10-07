// paramd.cs - dump params of named functions (incl. non-exec)
using System; using System.IO; using System.Collections.Generic;
class PD2 {
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
    var wants = new List<string>();
    for (int i = 3; i < a.Length; i++) wants.Add(a[i]);
    for (int i = 0; i < paths.Count; i++) {
      if (paths[i] == null || clss[i] != "Function") continue;
      bool hit = false;
      foreach (var w in wants) if (paths[i].EndsWith("." + w, StringComparison.OrdinalIgnoreCase)) hit = true;
      if (!hit) continue;
      int o = offs[i], sz = sizes[i];
      uint fflags = BitConverter.ToUInt32(d, o + sz - 12);
      // params = child properties with CPF_Parm
      var ps = new List<string>(); string ret = "";
      string prefix = paths[i] + ".";
      for (int j = 0; j < paths.Count; j++) {
        if (paths[j] == null || clss[j] == null) continue;
        if (!clss[j].EndsWith("Property")) continue;
        if (paths[j].StartsWith(prefix) && paths[j].IndexOf('.', prefix.Length) < 0) {
          if (offs[j] > 0 && offs[j] + 24 <= d.Length) {
            uint pf = BitConverter.ToUInt32(d, offs[j] + 20);
            if ((pf & 0x400) != 0) ret = clss[j];
            else if ((pf & 0x80) != 0) ps.Add(clss[j].Replace("Property","") + " " + paths[j].Substring(prefix.Length));
          }
        }
      }
      Console.WriteLine("{0} flags=0x{1:X} params: {2} ret: {3}", paths[i], fflags, string.Join(", ", ps), ret==""?"-":ret);
    }
  }
}
