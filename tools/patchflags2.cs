// patchflags2.cs - scan-based exec-bit patcher: patchflags2.exe <upk> <el> <ul> <out> name1 name2...
// finds the REAL FunctionFlags dword by searching the tail for the original value
using System; using System.IO; using System.Collections.Generic;
class PF2 {
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
    string outp = a[3];
    for (int k = 4; k < a.Length; k++) {
      for (int i = 0; i < paths.Count; i++) {
        if (paths[i] == null || clss[i] != "Function") continue;
        if (!paths[i].EndsWith("." + a[k], StringComparison.OrdinalIgnoreCase)) continue;
        int o = offs[i], sz = sizes[i];
        // scan the tail region for the plausible original flags dword:
        // must contain Defined(0x2) + Public(0x20000), no Exec(0x200)
        int found = -1, foundCnt = 0;
        for (int fp = o + sz - 48; fp <= o + sz - 12; fp += 4) {
          if (fp < 0 || fp + 4 > d.Length) break;
          uint flv = BitConverter.ToUInt32(d, fp);
          if ((flv & 0x2) != 0 && (flv & 0x20000) != 0 && (flv & 0x200) == 0 && (flv & 0xFFFF0000) == 0) {
            found = fp; foundCnt++;
          }
        }
        if (found < 0) { Console.WriteLine("{0}: flags dword NOT FOUND in tail!", paths[i]); continue; }
        if (foundCnt > 1) Console.WriteLine("{0}: {1} candidates, using first", paths[i], foundCnt);
        uint fl = BitConverter.ToUInt32(d, found);
        Buffer.BlockCopy(BitConverter.GetBytes(fl | 0x200), 0, d, found, 4);
        Console.WriteLine("{0}: flags 0x{1:X} -> 0x{2:X} @0x{3:X} (tail -{4})", paths[i], fl, fl | 0x200, found, o + sz - found);
      }
    }
    File.WriteAllBytes(outp, d);
    Console.WriteLine("written " + outp);
  }
}
