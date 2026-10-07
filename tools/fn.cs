// fn.cs — UE3 脚本包函数/参数提取器（Infinity Blade RE skill 配套）
// 用法: fn.exe <解压后的.upk> <extract_list.txt> <umodel_list.txt> [funcs|funcbytes 函数名...]
//   funcs      输出全部函数: 所属类\t函数名\t返回类型\t参数
//   funcbytes  输出指定函数的原始字节（用于反汇编/标定）
// 前置: extract_list.txt = extract.exe -list 输出（跳过 3 行日志头）
//       umodel_list.txt  = umodel -list 输出（同日志头；提供 SerialOffset/SerialSize）
// 注意: 属性标志位于属性序列化数据 +20（UE3 v864 布局）；CPF_Parm=0x80, CPF_ReturnParm=0x400
using System; using System.IO; using System.Collections.Generic; using System.Text;

class FN
{
  static byte[] d;
  static int Ri(int p){ return BitConverter.ToInt32(d,p); }

  static void Main(string[] args)
  {
    d = File.ReadAllBytes(args[0]);
    string mode = args.Length > 3 ? args[3] : "funcs";
    var wants = new List<string>();
    for (int i = 4; i < args.Length; i++) wants.Add(args[i]);

    var paths = new List<string>(); var clss = new List<string>();
    foreach (var line in File.ReadAllLines(args[1]))
    {
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
    foreach (var line in File.ReadAllLines(args[2]))
    {
      var t = line.Trim();
      if (t == "" || !char.IsDigit(t[0])) continue;
      var p = t.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
      offs.Add(Convert.ToInt32(p[1], 16)); sizes.Add(Convert.ToInt32(p[2], 16));
    }

    for (int i = 0; i < paths.Count; i++)
    {
      if (paths[i] == null || clss[i] != "Function") continue;
      int o = offs[i], sz = sizes[i];
      if (o <= 0 || sz < 20 || o + sz > d.Length) continue;
      // FunctionFlags 在数据尾部（v864: iNative2+OperPrec1+Flags4+FriendlyName8）
      uint fflags = BitConverter.ToUInt32(d, o + sz - 12);
      if (mode == "flags")
      {
        string p2 = paths[i];
        bool hit = wants.Count == 0;
        foreach (var w in wants) if (p2.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
        if (hit) Console.WriteLine("{0}\t{1:X8}\tsize={2}\toff=0x{3:X}", p2, fflags, sz, o);
        continue;
      }
      if (mode == "funcbytes" && wants.Count > 0)
      {
        string p2 = paths[i];
        bool hit = false;
        foreach (var w in wants) if (p2.EndsWith("." + w, StringComparison.OrdinalIgnoreCase)) hit = true;
        if (!hit) continue;
      }
      if (mode == "funcs" && (fflags & 0x200) == 0) continue;   // 只要 FUNC_Exec

      string path = paths[i];
      int d1 = path.LastIndexOf('.');
      string owner = path.Substring(0, d1);
      string fname = path.Substring(d1 + 1);
      // 参数 = 子属性中 CPF_Parm(0x80) 置位且非 ReturnParm(0x400) 者（局部变量无 0x80）
      var ps = new List<string>(); string ret = "";
      string prefix = path + ".";
      for (int j = 0; j < paths.Count; j++)
      {
        if (paths[j] == null || clss[j] == null) continue;
        if (!clss[j].EndsWith("Property")) continue;
        if (paths[j].StartsWith(prefix) && paths[j].IndexOf('.', prefix.Length) < 0)
        {
          if (offs[j] > 0 && offs[j] + 24 <= d.Length)
          {
            uint pf = BitConverter.ToUInt32(d, offs[j] + 20);
            if ((pf & 0x400) != 0) ret = clss[j];
            else if ((pf & 0x80) != 0) ps.Add(clss[j].Replace("Property","") + " " + paths[j].Substring(prefix.Length));
          }
        }
      }
      if (mode == "funcs")
        Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4:X8}", owner, fname, ret, string.Join(", ", ps), fflags);
      else
      {
        var sb = new StringBuilder();
        int nn = Math.Min(sz, 1200);
        for (int b2 = 0; b2 < nn; b2++) sb.Append(d[o + b2].ToString("x2")).Append(' ');
        Console.WriteLine("== {0}.{1} size={2} off=0x{3:X}\n  {4}", owner, fname, sz, o, sb.ToString());
      }
    }
  }
}
