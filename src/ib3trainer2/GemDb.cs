// ============================================================================
// GemDb.cs — 宝石公式库（ib3_gems.ini，源自 DefaultGems.ini 转储）
// 显示值 = Base(各 *Bonus 之和) × UpgradeTier[tier-1] × (1 + RandomAddPct)
//   实测校验: FireGem tier=5(->200) × (1+0.77) = 354；tier=5 × (1+1.0) = 400
// 可掷档位模板（有 UpgradeTier）= Rollable；固定值模板只有 Base。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

class GemDef {
  public string Tpl = "";
  public int SubType;
  public readonly List<string> BonusKeys = new List<string>();
  public readonly List<long> BonusVals = new List<long>();
  public readonly List<long> Tiers = new List<long>();
  public double MaxPct;
  // 下面三个只在 Load 里用：加法型宝石里有一类**没有** `*Bonus` 键，基数写在 BattleEffectValue 上
  //（实测 UberBossBoostGem：BattleEffect=BE_VarAdd_BossLevelPlus / BattleEffectValue=1000）。
  // 见 Load 末尾那段「补基数」的注释。
  public bool HasRecipeBoost;
  public bool HasEffectVal;
  public long EffectVal;
  // ★ 档位表里出现了**真小数**（如 GlobalScaleGoldGem_1 的 0.1/0.2/0.3）：
  //   档位**是存在的**（长度照数，tier 因此可改），但"显示值 = 基数 × 档位值 × (1+pct)"
  //   乘出来不再是整数 —— 本页不假装能算，界面上显示 "?"。
  //   注意「整数值的小数写法」（3.0/4.0/5.0，ItemDropGem_1 就是）**不算**小数，会还原成整数。
  public bool TiersFractional;

  public long Base {
    get {
      long s = 0;
      for (int i = 0; i < BonusVals.Count; i++) s += BonusVals[i];
      return s;
    }
  }
  public bool Rollable { get { return Tiers.Count > 0; } }

  // tier1 = 存档/内存中的 1 基档位（+0x14 字段）
  public long ValueAt(int tier1, double pct) {
    if (Tiers.Count == 0) return Base;
    int idx = tier1 - 1;
    if (idx < 0) idx = 0;
    if (idx >= Tiers.Count) idx = Tiers.Count - 1;
    double v = (double)Base * (double)Tiers[idx] * (1.0 + pct);
    if (v < -9e18 || v > 9e18) return Base;
    return (long)Math.Round(v);
  }

  public string BonusDesc() {
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < BonusKeys.Count; i++) {
      if (sb.Length > 0) sb.Append(' ');
      sb.Append(BonusKeys[i]).Append('=').Append(BonusVals[i]);
    }
    return sb.ToString();
  }
}

static class GemDb {
  static readonly Dictionary<string, GemDef> defs = new Dictionary<string, GemDef>();

  public static int Count { get { return defs.Count; } }
  public static List<GemDef> All { get { return new List<GemDef>(defs.Values); } }
  public static GemDef Get(string tpl) {
    GemDef d;
    if (tpl != null && defs.TryGetValue(tpl, out d)) return d;
    return null;
  }

  public static int Load(string path) {
    defs.Clear();
    if (!File.Exists(path)) return 0;
    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    GemDef cur = null;
    for (int i = 0; i < lines.Length; i++) {
      string line = lines[i].Trim();
      if (line.Length == 0) continue;
      if (line[0] == '[') {
        int sp = line.IndexOf(' ');
        int rb = line.IndexOf(']');
        string name = sp > 1 ? line.Substring(1, sp - 1) : (rb > 1 ? line.Substring(1, rb - 1) : "");
        cur = new GemDef();
        cur.Tpl = name;
        defs[name] = cur;
        continue;
      }
      if (cur == null) continue;
      int eq = line.IndexOf('=');
      if (eq <= 0) continue;
      string key = line.Substring(0, eq).Trim();
      string val = line.Substring(eq + 1).Trim();
      try {
        if (key == "ItemSubType") { cur.SubType = int.Parse(val); continue; }
        if (key == "MaxRandomAddPct") { cur.MaxPct = double.Parse(val, CultureInfo.InvariantCulture); continue; }
        // 只记"有没有 RecipeBoostAmount"（= 加法型的判据），基数由它 + BattleEffectValue 一起定
        if (key == "RecipeBoostAmount") { cur.HasRecipeBoost = true; continue; }
        if (key == "BattleEffectValue") {
          // 只收**整数量**：这一类基数都是整数（1000 / 25 / 10 …），而百分比效应
          //（GlobalScaleGoldGem_1=0.05、UberTakeHitGem_2=0.02、LightGem_5=0.90）不是"基数"，
          // 收进来会被 long 截成 0，反而把"未知"显示成"0"。
          double dv;
          if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)
              && dv >= 1 && dv < 1e15 && Math.Abs(dv - Math.Round(dv)) < 1e-9) {
            cur.HasEffectVal = true; cur.EffectVal = (long)Math.Round(dv);
          }
          continue;
        }
        if (key.StartsWith("UpgradeTier")) {
          // ★ 2026-10-07 修复：原来是 `long.Parse(val)`，遇到小数直接抛异常、被外层 catch 吞掉 ——
          //   结果**整个档位表一条都读不进来**（连 3.0/4.0/5.0 这种"整数值的小数写法"也抛），
          //   于是既无 *Bonus 又无档位的模板被下面那段"非宝石实体"清理丢掉 →
          //   界面上显示成未知类型、**拒绝修改**。全库正好 14 个模板中招（实测枚举，见 GemSelfTest
          //   的 limits.known）：ItemDropGem_1..4 / BonusComboGem_5,6 / FinalHitGem_5 /
          //   BreakBossGetMagic_1 / BreakBossGetSuper_1 / GlobalScale*Gem_1 / UberTakeHitGem_2,3。
          //   现在：整数值（含 3.0 写法）按整数收；真小数**占位但值记 0**并置 TiersFractional，
          //   档位长度因此是准的（tier 可改），只是显示值不再假装算得出。
          double dv;
          if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) continue;
          long tv = 0;
          if (dv > -9e15 && dv < 9e15 && Math.Abs(dv - Math.Round(dv)) < 1e-9) tv = (long)Math.Round(dv);
          else cur.TiersFractional = true;
          int lb = key.IndexOf('[');
          if (lb > 0) {
            int idx = int.Parse(key.Substring(lb + 1, key.Length - lb - 2));
            while (cur.Tiers.Count <= idx) cur.Tiers.Add(0);
            cur.Tiers[idx] = tv;
          } else cur.Tiers.Add(tv);
          continue;
        }
        if (key.Length > 5 && key.EndsWith("Bonus", StringComparison.Ordinal)) {
          long bv;
          if (long.TryParse(val, out bv)) { cur.BonusKeys.Add(key); cur.BonusVals.Add(bv); }
          continue;
        }
      } catch { }
    }
    // ★ 2026-10-07 修复「UberBossBoostGem 显示值恒少 1000」：
    //   15 个加法型模板（有 RecipeBoostAmount）里，其余 14 个的基数都写在 `*Bonus` 键上
    //   （UberAttackGem DamageBonus=250、UberElementalAttackGem_100 ImmuneToAllBonus=1000 …），
    //   唯独 UberBossBoostGem 写的是 `BattleEffect=BE_VarAdd_BossLevelPlus` +
    //   `BattleEffectValue=1000`。它因此既没有 `*Bonus` 也没有 UpgradeTier，
    //   被下面那段"非宝石实体（药水等）"清理**连宝石一起丢掉** →
    //   Tabs.Gems.cs 的 GemDb.Get() 返回 null → Base=0 → 界面「当前显示值」按 0+tier×250 算
    //   （tier 255 显示 63750，实际应为 1000+63750=64750），「显示数值」反解同样错 1000。
    //   加法型公式（README 二 / §8.4-④）= 显示值 = 基数 + tier × RecipeBoostAmount，
    //   基数取 BattleEffectValue 与另外 14 个取 `*Bonus` 是同一回事，故只对**加法型**补。
    //   ✅ 2026-10-07 游戏内实测确认这条推导（不是"看着像"，是读数对上）：
    //      UberBossBoostGem  Tier 0 → 显示 1000　Tier 255 → 显示 64750（= 1000 + 255×250）。
    //   ⚠ 下标型的 BattleEffectValue（BossBoostGem=25、ParryGem_1=25、StabAttackGem=10、
    //   GlobalScaleGoldGem_1=0.05…）语义未实测，**不在这里补** —— 那是另一件事，
    //   顺手补等于把没验证过的数当"权威值"显示出来。
    foreach (KeyValuePair<string, GemDef> kv in defs) {
      GemDef d = kv.Value;
      if (d.BonusVals.Count == 0 && d.HasRecipeBoost && d.HasEffectVal) {
        d.BonusKeys.Add("BattleEffectValue");
        d.BonusVals.Add(d.EffectVal);
      }
    }
    // 清理：无 Bonus 且无档位 = 非宝石实体（药水等），移除
    List<string> junk = new List<string>();
    foreach (KeyValuePair<string, GemDef> kv in defs)
      if (kv.Value.BonusVals.Count == 0 && kv.Value.Tiers.Count == 0) junk.Add(kv.Key);
    foreach (string k in junk) defs.Remove(k);
    return defs.Count;
  }
}

} // namespace
