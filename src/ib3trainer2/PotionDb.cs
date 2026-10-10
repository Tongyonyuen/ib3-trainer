// ============================================================================
// PotionDb.cs — 魔法剂模板表（ib3_potions.csv: 模板名,分组,中文名,效果摘要,档位表）
//
// 这份清单的**入选条件 = 模板带 `PotionType`**（`SwordInventoryItemGem.PotionType`）。
// 依据（2026-10-10 实测）：`getfixedpotion <模板名>` 内部走
//   GetFixedPotion → GetFixedGem(名, …, ForcedType=14=SIT_Potion) → SavePotionInstanceToPotionList
// 带 PotionType 的能造出来；**不带的静默失败**（游戏不报错、训练器也感知不到 —— 所以这里
// 必须在前端就把不该出现的模板筛掉，否则用户会以为功能坏了）。
//   DefaultGems.ini 164 个模板里 90 个带 PotionType。
//
// 分组（第 2 列）：
//   1 = 药剂专属（按秒）  —— 带 `BattleEffectRetriggerTime`，全游戏**只有这 3 个**，
//        是药剂独有机制（宝石只能靠 BT_OnXxx 事件触发，没有"按秒重复"）
//   2 = 专用药剂         —— 其余 `Potion_*`
//   3 = 元素与基础族     —— FireGem/IceGem/… 与 HealthGem/ShieldGem/MagicGem/AttackGem
//   4 = 其它特效宝石     —— 其余（本质是宝石效果被搬进药剂形态）
//
// 档位表（第 5 列）= 模板的 `UpgradeTier[]`，用于按档位换算**显示数值**：
//   显示值 = 档位表值 × (1 + RandomAddPct) × GemToPotionValueScale(=10)
//   ⚠ 那个 ×10 是 `SwordPlayer.GemToPotionValueScale`，**药水才有**（宝石是 ×1）。
//   档位 0 = 不查档位表，改用模板基值（XB bonus / BattleEffectValue）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

class PotionRow {
  public string Tpl = "", Cn = "", Summary = "", Tiers = "";
  public int Group;
  public long[] TierVals = new long[0];

  // 下拉里显示的一行
  public string Label() {
    string head = Cn.Length > 0 ? (Cn + "（" + Tpl + "）") : Tpl;
    return PotionDb.GroupName(Group) + " ▸ " + head + " ｜ " + Summary;
  }
}

class PotionDb {
  public const int GROUP_RETRIG = 1, GROUP_SPECIAL = 2, GROUP_ELEMENTAL = 3, GROUP_OTHER = 4;

  public readonly List<PotionRow> Rows = new List<PotionRow>();

  public int Load(string dir) {
    Rows.Clear();
    string path = Path.Combine(dir, "ib3_potions.csv");
    if (!File.Exists(path)) return 0;
    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    for (int i = 1; i < lines.Length; i++) {
      string line = lines[i].TrimEnd('\r');
      if (line.Length == 0) continue;
      string[] f = line.Split(',');
      if (f.Length < 5) continue;
      PotionRow r = new PotionRow();
      r.Tpl = f[0].Trim();
      if (r.Tpl.Length == 0) continue;
      int g;
      r.Group = int.TryParse(f[1].Trim(), out g) ? g : GROUP_OTHER;
      r.Cn = f[2].Trim(); r.Summary = f[3].Trim(); r.Tiers = f[4].Trim();
      if (r.Tiers.Length > 0) {
        string[] parts = r.Tiers.Split('/');
        r.TierVals = new long[parts.Length];
        for (int k = 0; k < parts.Length; k++) {
          long v;
          r.TierVals[k] = long.TryParse(parts[k].Trim(), out v) ? v : 0;
        }
      }
      Rows.Add(r);
    }
    return Rows.Count;
  }

  public static string GroupName(int g) {
    if (g == GROUP_RETRIG) return "药剂专属（按秒）";
    if (g == GROUP_SPECIAL) return "专用药剂";
    if (g == GROUP_ELEMENTAL) return "元素与基础族";
    return "其它特效宝石";
  }

  public int MaxTier(PotionRow r) { return r.TierVals == null ? 0 : r.TierVals.Length; }

  // 按档位换算显示数值（pct = RandomAddPct；getfixedpotion 发出来的是 0）
  public long ValueAt(PotionRow r, int tier, double pct) {
    if (r.TierVals == null || r.TierVals.Length == 0) return 0;
    if (tier < 1 || tier > r.TierVals.Length) return 0;
    return (long)Math.Round((double)r.TierVals[tier - 1] * (1.0 + pct) * 10.0);
  }
}

} // namespace
