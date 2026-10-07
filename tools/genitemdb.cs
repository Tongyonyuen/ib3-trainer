// genitemdb.cs — 生成 IB3 快捷修改器的物品数据库 items.csv
// 用法: genitemdb.exe <IB3游戏目录> <宝石CSV> <输出csv>
// 列: 模板名,主分类,子分类,中文名,备注
using System; using System.IO; using System.Text; using System.Collections.Generic;

class GDB {
  static Dictionary<string,string> ParseIni(string path) {
    // 返回 "节名|键" -> 值 的平表；同时保留节顺序没必要，用列表另存
    var dict = new Dictionary<string,string>();
    var lines = File.ReadAllLines(path, Encoding.Default);
    string sec = "";
    foreach (var raw in lines) {
      var line = raw.Trim();
      if (line.Length == 0 || line.StartsWith(";")) continue;
      if (line.StartsWith("[")) { int e = line.IndexOf(']'); sec = e > 0 ? line.Substring(1, e-1) : line; int sp = sec.IndexOf(' '); if (sp > 0) sec = sec.Substring(0, sp); continue; }
      int eq = line.IndexOf('='); if (eq <= 0 || sec == "") continue;
      string key = line.Substring(0, eq).Trim();
      string val = line.Substring(eq+1).Trim();
      int br = key.IndexOf('['); if (br > 0) key = key.Substring(0, br); // Socket[0] -> Socket
      dict[sec + "|" + key] = val;
    }
    return dict;
  }
  static string Get(Dictionary<string,string> d, string sec, string key) {
    string v; return d.TryGetValue(sec + "|" + key, out v) ? v : "";
  }
  class Row { public string Tpl, Cat, Sub, Cn, Note; }

  // 解析 Localization/CHN/SwordGame.chn (UTF-16LE): [模板 类] 节 → FriendlyName 官方中文名
  static Dictionary<string,string> ParseChn(string path) {
    var m = new Dictionary<string,string>();
    if (!File.Exists(path)) return m;
    string text = File.ReadAllText(path, Encoding.Unicode);
    string sec = "";
    foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) {
      var line = raw.Trim();
      if (line.StartsWith("[")) {
        int e = line.IndexOf(']');
        sec = e > 0 ? line.Substring(1, e-1) : line;
        int sp = sec.IndexOf(' '); if (sp > 0) sec = sec.Substring(0, sp);
        continue;
      }
      if (sec != "" && line.StartsWith("FriendlyName=") && !m.ContainsKey(sec))
        m[sec] = line.Substring(13).Trim();
    }
    return m;
  }


  static void Main(string[] args) {
    string game = args[0], gemcsv = args[1], outfile = args[2];
    var rows = new List<Row>();
    var cfg = game + "\\SwordGame\\Config\\";
    var chn = ParseChn(game + "\\SwordGame\\Localization\\CHN\\SwordGame.chn");
    Console.WriteLine("chn names: " + chn.Count);
    // 中文名取值优先级: 官方 chn > 宝石CSV推断 > 模板名
    Func<Row, string> cnOf = delegate(Row r) {
      string v; return chn.TryGetValue(r.Tpl, out v) ? v : r.Cn;
    };

    // ---- 装备: DefaultItems.ini ----
    var it = ParseIni(cfg + "DefaultItems.ini");
    var secs = new List<string>();
    foreach (var k in it.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !secs.Contains(s)) secs.Add(s); }
    foreach (var sec in secs) {
      string type = Get(it, sec, "ItemType");
      string sub = Get(it, sec, "ItemSubType");
      string cat = "", sub2 = "";
      if (type == "SIT_Weapon") {
        cat = "装备";
        if (sub == "1") sub2 = "重武器";
        else if (sub == "2" || sub == "5") sub2 = "双武器";
        else if (sub == "3") sub2 = "单手武器";
        else if (sub == "4") sub2 = "长柄武器";
        else sub2 = "武器其他";
      }
      else if (type == "SIT_Shield") { cat = "装备"; sub2 = "盾牌"; } // Shield_332 官方中文名也叫"戒指"，但分类仍归盾牌（用户确认）
      else if (type == "SIT_Helmet") { cat = "装备"; sub2 = "头盔"; }
      else if (type == "SIT_Armor")  { cat = "装备"; sub2 = "盔甲"; }
      else if (type == "SIT_Magic")  { cat = "装备"; sub2 = "戒指"; }
      if (cat == "") continue;
      string rare = Get(it, sec, "ItemRare");
      var r = new Row { Tpl = sec, Cat = cat, Sub = sub2, Cn = sec, Note = "Cost=" + Get(it, sec, "Cost") + (rare != "" ? " 稀有度=" + rare : "") };
      rows.Add(r);
    }

    // ---- Boss 专属: DefaultBossItems.ini ----
    var bi = ParseIni(cfg + "DefaultBossItems.ini");
    var bsecs = new List<string>();
    foreach (var k in bi.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !bsecs.Contains(s)) bsecs.Add(s); }
    foreach (var sec in bsecs) {
      string type = Get(bi, sec, "ItemType");
      string sub2 = type == "SIT_BossWeapon" ? "Boss武器" : type == "SIT_BossDefense" ? "Boss盾甲" : "Boss其他";
      rows.Add(new Row { Tpl = sec, Cat = "装备", Sub = "Boss专属/" + sub2, Cn = sec, Note = "Boss物品" });
    }

    // ---- 收集品（按节去重）----
    var ki = ParseIni(cfg + "DefaultKeyItems.ini");
    var ksecs = new List<string>();
    foreach (var k in ki.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !ksecs.Contains(s)) ksecs.Add(s); }
    foreach (var s in ksecs) {
      bool map = s.StartsWith("TreasureMap");
      rows.Add(new Row { Tpl = s, Cat = "收集品", Sub = map ? "地图" : "钥匙", Cn = s, Note = "setgivekeyitem 发放" });
    }
    var ri = ParseIni(cfg + "DefaultRandomItems.ini");
    var rsecs = new List<string>();
    foreach (var k in ri.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !rsecs.Contains(s)) rsecs.Add(s); }
    foreach (var s in rsecs) rows.Add(new Row { Tpl = s, Cat = "收集品", Sub = "抽奖箱", Cn = s, Note = "item 发放" });
    var wi = ParseIni(cfg + "DefaultWorldItems.ini");
    var wsecs = new List<string>();
    foreach (var k in wi.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !wsecs.Contains(s)) wsecs.Add(s); }
    foreach (var s in wsecs) rows.Add(new Row { Tpl = s, Cat = "收集品", Sub = "材料", Cn = s, Note = "药水材料" });
    var po = ParseIni(cfg + "DefaultPotions.ini");
    var psecs = new List<string>();
    foreach (var k in po.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !psecs.Contains(s)) psecs.Add(s); }
    foreach (var s in psecs) rows.Add(new Row { Tpl = s, Cat = "收集品", Sub = "药水", Cn = s, Note = "也可用 setplayerpotions" });

    // ---- 宝石: DefaultGems.ini + 中文名合并 ----
    var cnmap = new Dictionary<string,string>();
    if (File.Exists(gemcsv)) {
      foreach (var line in File.ReadAllLines(gemcsv, Encoding.UTF8)) {
        var f = line.Split(',');
        if (f.Length >= 2 && f[0].Trim() != "" && f[0] != "模板名(控制台用)") {
          string tpl = f[0].Trim(); string cn = f[1].Trim();
          if (cn != "" && !cnmap.ContainsKey(tpl)) cnmap[tpl] = cn;
        }
      }
    }
    var gi = ParseIni(cfg + "DefaultGems.ini");
    var gsecs = new List<string>();
    foreach (var k in gi.Keys) { int p = k.IndexOf('|'); string s = k.Substring(0, p); if (s != "IconPackages" && !gsecs.Contains(s)) gsecs.Add(s); }
    foreach (var sec in gsecs) {
      string type = Get(gi, sec, "ItemType");
      if (type != "SIT_Gem") continue;
      string sub = Get(gi, sec, "ItemSubType");
      string sub2 = sub != "" ? "孔位" + sub : "孔位未标注";
      string cn; string cnval = cnmap.TryGetValue(sec, out cn) ? cn : sec;
      string restrict = Get(gi, sec, "Restrict");
      string note = restrict == "1" ? "熔炉槽位宝石" : restrict == "2" ? "合成专用宝石" : "";
      rows.Add(new Row { Tpl = sec, Cat = "宝石", Sub = sub2, Cn = cnval, Note = note });
    }

    // ---- 输出 ----
    var sb = new StringBuilder();
    sb.AppendLine("模板名,主分类,子分类,中文名,备注");
    foreach (var r in rows)
      sb.AppendLine(string.Join(",", new object[]{ r.Tpl, r.Cat, r.Sub, cnOf(r), r.Note }));
    File.WriteAllText(outfile, sb.ToString(), new UTF8Encoding(true));
    Console.WriteLine("rows={0} -> {1}", rows.Count, outfile);
  }
}
