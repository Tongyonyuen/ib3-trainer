// ============================================================================
// ItemDb.cs — 物品数据库（items.csv: 模板名,主分类,子分类,中文名,备注,英文名）
//   第 6 列「英文名」= 游戏本体 Localization\INT\SwordGame.int 里的官方 FriendlyName（2026-10-07 挖出）。
//   ⚠ 第一版只有 609/949 条有：CHN 与 INT 的键集合**完全一致**（差 0），说明游戏本地化里
//     本来就没给另外 340 条起名字（多是玩家拿不到的敌人/杂项武器，如 Sword_1000、6ft_SnS_Deathless）。
//     其中 195 条的中文名就是模板名（没什么可翻的）；另外 145 条的中文名（如「火元素防御宝石」
//     「室内攻击宝石」）是**作者对照游戏内显示整理的** —— 含义一致，但不是源文本原文。
//     这 145 条的英文由这份中文名译出（同样注明"非官方源文本"），其余保持回退到中文名/模板名。
// 两级分类 + 搜索；供物品发放页使用。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

class ItemRow { public string Tpl = "", Cat = "", Sub = "", Cn = "", Note = "", En = ""; }

class ItemDb {
  public readonly List<ItemRow> Rows = new List<ItemRow>();

  public int Load(string dir) {
    Rows.Clear();
    string path = Path.Combine(dir, "items.csv");
    if (!File.Exists(path)) return 0;
    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    for (int i = 1; i < lines.Length; i++) {
      string line = lines[i].TrimEnd('\r');
      if (line.Length == 0) continue;
      string[] f = line.Split(',');
      if (f.Length < 5) continue;
      ItemRow r = new ItemRow();
      r.Tpl = f[0].Trim(); r.Cat = f[1].Trim(); r.Sub = f[2].Trim(); r.Cn = f[3].Trim(); r.Note = f[4].Trim();
      r.En = f.Length >= 6 ? f[5].Trim() : "";   // 老版 items.csv 没有第 6 列也不会出错
      if (r.Tpl.Length == 0) continue;
      Rows.Add(r);
    }
    return Rows.Count;
  }

  public List<string> Categories() {
    List<string> l = new List<string>();
    l.Add("全部");
    foreach (ItemRow r in Rows) if (!l.Contains(r.Cat)) l.Add(r.Cat);
    return l;
  }

  public List<string> Subs(string cat) {
    List<string> l = new List<string>();
    l.Add("全部");
    foreach (ItemRow r in Rows) {
      if (cat != "全部" && r.Cat != cat) continue;
      string s = r.Sub.IndexOf('/') >= 0 ? "Boss专属" : r.Sub;
      if (!l.Contains(s)) l.Add(s);
    }
    return l;
  }

  public List<ItemRow> Filter(string cat, string sub, string q, int cap) {
    List<ItemRow> l = new List<ItemRow>();
    string qq = (q == null ? "" : q.Trim().ToLowerInvariant());
    foreach (ItemRow r in Rows) {
      if (cat != "全部" && r.Cat != cat) continue;
      string rsub = r.Sub.IndexOf('/') >= 0 ? "Boss专属" : r.Sub;
      if (sub != "全部" && rsub != sub) continue;
      if (qq.Length > 0 && r.Tpl.ToLowerInvariant().IndexOf(qq) < 0 && r.Cn.ToLowerInvariant().IndexOf(qq) < 0
          && r.En.ToLowerInvariant().IndexOf(qq) < 0) continue;   // 英文名也能搜
      l.Add(r);
      if (cap > 0 && l.Count >= cap) break;
    }
    return l;
  }
}

} // namespace
