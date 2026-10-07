// vscan.cs - find virtual-function call sites by FName index: vscan.exe <upk> <el> <ul> <nameIdx>
using System; using System.IO; using System.Collections.Generic;
class VS {
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
    int want = int.Parse(a[3]);
    byte[] pat = { (byte)(want & 0xFF), (byte)((want>>8)&0xFF), 0, 0, 0, 0, 0, 0 };
    for (int i = 0; i < paths.Count; i++) {
      if (paths[i] == null || clss[i] != "Function") continue;
      int o = offs[i], sz = sizes[i];
      if (o <= 0 || sz < 70) continue;
      for (int k = o + 48; k < o + sz - 24; k++) {
        bool m = true;
        for (int j = 0; j < 8; j++) if (d[k+j] != pat[j]) { m = false; break; }
        if (m && d[k-1] == 0x1b) {
          Console.Write("{0} @script+{1}: 1b VF argbytes:", paths[i], k - o - 48);
          for (int j = -14; j < 18 && k+j < o+sz; j++) Console.Write(" {0:X2}", d[k+j]);
          Console.WriteLine();
          k += 8;
        }
      }
    }
  }
}
