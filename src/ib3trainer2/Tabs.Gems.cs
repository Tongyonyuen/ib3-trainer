// ============================================================================
// Tabs.Gems.cs — 宝石·背包：定位背包宝石数组 → 列出 → 改 Tier / 显示数值
//
// 记录结构（UE3 FGem，x64，24 字节）：
//   +0x00 i32 FName.Index   宝石名索引
//   +0x04 i32 FName.Number
//   +0x08 u8  GemTier       ← 本页要改的字节
//   +0x09 u8  CookedGemVar  融合过=0x32(50)，未融合=0
//   +0x0A u8[2] 填充
//   +0x0C f32 RandomAddPct  元素宝石用；暗火恒 0.0
//   +0x10 u32 bShowBadge
//   +0x14 u8  Boost + 3 字节填充
//
// 两条必须守的规矩（来自 2026-10-07 实测，详见 E:\ib3_re\宝石研究\README.md）：
//   1) 同一份背包在内存里有 2~3 份拷贝，只有一份权威 → 本页把 tier **写进所有同内容拷贝**。
//      写错拷贝实测无害；只写一份则有很高概率白改。
//   2) tier 必须按 **单字节** 写。写 Int32 会把 +0x09 的 CookedGemVar 清零。
//
// Tier 语义分两类，绝不能混：
//   加法型（模板有 RecipeBoostAmount）：显示值 = 基数 + tier × boost，tier ∈ 0..255
//   下标型（模板有 UpgradeTier[0..4]）：显示值 = 基数 × UpgradeTier[tier-1] × (1+pct)，tier ∈ 1..5
//   下标型写 255 = 读 UpgradeTier[254] = 越界 → 垃圾值甚至崩溃，本页强制夹在 1..5。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

// ---------- 模板的 Tier 语义 ----------
enum GemTierKind { Unknown, Additive, Indexed }

// ---------- 一条宝石记录 ----------
class GemRec {
  public long RecAddr;
  public int ArrId;
  public int NameIdx, Number, Tier, Cook;
  public float Pct;
  public string Tpl = "?";
  public GemTierKind Kind = GemTierKind.Unknown;
  public long Base;                            // 基数（加法型 = 各 Bonus 之和）
  public long Boost;                           // 加法型每 tier 增量（RecipeBoostAmount）
  public List<long> Tiers = new List<long>();  // 下标型档位表 UpgradeTier[]
  public double MaxPct;
  // 档位表里含**真小数**（0.1/0.2/0.3…）→ 显示值乘出来不是整数，本页不假装能算（显示 "?"）。
  // 档位本身照旧有效、tier 仍可改 —— 长度取自 ib3_gems.ini（见 GemDb.TiersFractional）。
  public bool TiersFractional;
  public long TierAddr { get { return RecAddr + 0x08; } }
  public long CookAddr { get { return RecAddr + 0x09; } }
  public long PctAddr { get { return RecAddr + 0x0C; } }

  public bool CanEditValue { get { return Kind != GemTierKind.Unknown; } }
  public int TierMin { get { return Kind == GemTierKind.Indexed ? 1 : 0; } }
  // ⚠ 对「未知类型」这里返回的 255 **不是真上限**，调用方必须先过 TierMaxKnown 再取值
  //   （ApplyGemEdit 在更前面就被 Kind==Unknown 挡掉了；「填上限」按钮已按 TierMaxKnown 分流）。
  public int TierMax { get { return Kind == GemTierKind.Indexed ? Tiers.Count : 255; } }

  // ---- 「填上限」的判据：**上限说得清才允许自动填**，说不清就拒绝并让用户手动取值 ----
  // ★ 2026-10-07 修正：旧版「填上限」直接取 TierMax，而它对未知类型也返回 255，
  //   于是 UberGoldGem / LightGem_3 / (空槽) 这类根本没有档位语义的宝石，会被填上一个
  //   看起来很确定、实际纯属猜的 255（点了「应用」又必被"类型未知"拒掉）。
  //   规则：**上限不是 255 的绝不能填 255；上限不清楚的不能拿任何数顶替**。
  //
  //   tier 上限：
  //     加法型 = 255 —— 这不是猜：GemTier 是 uint8 字段，255 是**字段硬上限**，
  //              README 八 已实测（显示值到 128500 = 1000 + 255×500 就到顶了）。
  //     下标型 = UpgradeTier[] 的表长（实测 FireGem/AttackGem=5、ParryChargeGem=4、ItemDropGem_1=3…），
  //              **不是 255**。
  //     其余   = 说不清（模板既无 RecipeBoostAmount 也无 UpgradeTier，含"空槽"）。
  public bool TierMaxKnown { get { return Kind != GemTierKind.Unknown; } }

  //   显示值上限：
  //     加法型 = 基数 + 255×增量 —— 只用到基数与增量，都从 ib3_gems.ini 取；故**要求两者都 >0**，
  //              基数取不到就说明这个模板没进公式库（GemDb 会丢掉非宝石实体），此时算不出。
  //     下标型 = 基数 × UpgradeTier[末档] × (1 + pct 上限) —— **必须知道 pct 能掷到多少**，
  //              也就是 MaxRandomAddPct 得查到且 >0。ini 里没有这一项时（实测 BossBoostGem 是 -1，
  //              另有一批模板干脆没有），pct 上限无从得知 ⇒ 说不清，不许猜。
  // 显示值**算不算得出来**（与上限共用同一组前提）：
  //   加法型 = 基数 + tier×增量 → 要 Base>0 且 Boost>0
  //   下标型 = 基数 × 档位值 × (1+pct) → 还要档位值是整数（TiersFractional=false）
  // 算不出来时：列表显示 "?"、"显示数值"模式直接拒绝并说明原因（别报"你选的数不行"）
  public bool ValueComputable {
    get {
      if (Kind == GemTierKind.Additive) return Base > 0 && Boost > 0;
      if (Kind == GemTierKind.Indexed) return Base > 0 && Tiers.Count > 0 && !TiersFractional;
      return false;
    }
  }

  // 上限**算不算得出来**：在"算得出显示值"之上，下标型还得知道 pct 能掷到多少（MaxPct>0）；
  // 加法型的显示值与 pct 无关（ValueAt 里压根没用 pct），所以只要值算得出，上限就算得出。
  public bool ValueMaxKnown {
    get {
      if (!ValueComputable) return false;
      if (Kind == GemTierKind.Additive) return true;
      return MaxPct > 0;
    }
  }

  // 「说不清」的具体原因（提示语用 —— 只说"不清楚"用户没法判断该怎么办）
  public string MaxUnknownWhy(bool byValue) {
    if (Tpl == "(空槽)") return "这是空槽，本来就没有宝石";
    if (Kind == GemTierKind.Unknown)
      return "模板 " + Tpl + " 既没有 RecipeBoostAmount 也没有 UpgradeTier，本页不知道它的 tier 语义与上限";
    if (byValue && TiersFractional)
      return "模板 " + Tpl + " 的档位表是小数（0.1/0.2/0.3 这类），显示值不是整数";
    if (byValue && Base <= 0)
      return "模板 " + Tpl + " 的基数没查到（它不在 ib3_gems.ini 的公式库里）";
    if (byValue && Kind == GemTierKind.Indexed && MaxPct <= 0)
      return "模板 " + Tpl + " 没给 MaxRandomAddPct，pct 能掷到多少无从得知";
    return "按 ib3_gems.ini 里的信息算不出这颗宝石的上限";
  }

  public long CurrentValue() { return ValueAt(Tier, Pct); }

  public long ValueAt(int tier, double pct) {
    if (Kind == GemTierKind.Additive) return Base + (long)tier * Boost;
    if (Kind == GemTierKind.Indexed) {
      if (tier < 1 || tier > Tiers.Count) return -1;
      // ★ 算不出就返回 -1（界面显示 "?"），**不要假装算出一个 0**：
      //   ① 基数没查到 —— 模板用的是 BattleEffectValue 那类，本页读不出它的基数
      //   ② 档位表是真小数（0.1/0.2/0.3…），乘出来不是整数
      if (Base <= 0 || TiersFractional) return -1;
      return (long)Math.Round(Base * Tiers[tier - 1] * (1.0 + pct));
    }
    return -1;
  }

  public long MaxValue() {
    if (Kind == GemTierKind.Additive) return ValueAt(255, Pct);
    if (Kind == GemTierKind.Indexed) return ValueAt(Tiers.Count, MaxPct);
    return -1;
  }

  // 反解：目标显示值 → (tier, pct)。不可达返回 false。
  public bool Solve(long target, out int tier, out double pct) {
    tier = Tier; pct = Pct;
    if (Kind == GemTierKind.Additive) {
      if (Boost <= 0) return false;
      long d = target - Base;
      if (d < 0 || d % Boost != 0) return false;
      long t = d / Boost;
      if (t < 0 || t > 255) return false;
      tier = (int)t; pct = Pct;
      return true;
    }
    if (Kind == GemTierKind.Indexed) {
      if (Base <= 0 || TiersFractional) return false;
      for (int t = Tiers.Count; t >= 1; t--) {
        double raw = (double)target / ((double)Base * Tiers[t - 1]) - 1.0;
        if (raw >= -1e-6 && raw <= MaxPct + 1e-6) { tier = t; pct = Math.Max(0.0, raw); return true; }
      }
      return false;
    }
    return false;
  }
}

partial class MainForm {

  // ---------------- 状态 ----------------
  List<GemRec> gemRecs = new List<GemRec>();
  int[] gemArrSize = new int[0];
  ListView lvGems;
  ComboBox cboGemArr, cboGemField;
  TextBox txtGemTarget;
  Label lblGemInfo;
  Button btnGemApply, btnGemMax;
  Dictionary<string, long> gemIndex = new Dictionary<string, long>();      // 模板名(小写) → FName.Index
  Dictionary<long, string> gemNameByKey = new Dictionary<long, string>();  // (Index<<32|Number) → 模板名(原始大小写)
  Dictionary<string, long> gemBoost = new Dictionary<string, long>();      // 模板名(小写) → RecipeBoostAmount

  // 同名族共用 Index、靠 Number 区分（如 UberElementalAttackGem_100=num 0x65 / _200=num 0xC9）
  static long GemKey(int idx, int num) { return ((long)idx << 32) | (uint)num; }

  const int GEM_REC = 24;        // 记录步长
  const int GEM_MIN_RUN = 3;     // 连续几条才算一个数组

  // ================= 页签 =================
  TabPage BuildTabGems() {
    TabPage p = new TabPage("宝石·背包");
    p.BackColor = Theme.BG; p.ForeColor = Theme.Text;

    LoadGemIndex();

    p.Controls.Add(Theme.MkLabelInk("候选数组", 14, 13, 62));
    cboGemArr = new ComboBox(); cboGemArr.SetBounds(78, 10, 264, 23);
    Theme.StyleCombo(cboGemArr);
    cboGemArr.SelectedIndexChanged += delegate { ShowArr(); };
    p.Controls.Add(cboGemArr);

    p.Controls.Add(Theme.MkButton("读取背包", 354, 8, 100, 26, delegate { GemWarnStaleList(); RefreshGems(); }));
    p.Controls.Add(Theme.MkButton("重新扫描", 462, 8, 100, 26, delegate { GemWarnStaleList(); RefreshGems(); }));

    lvGems = new DarkListView();
    Theme.ApplyDarkScroll(lvGems);
    lvGems.SetBounds(14, 42, 788, 248);
    Theme.StyleList(lvGems);
    lvGems.Columns.Add("模板名", 186);
    lvGems.Columns.Add("Tier", 52);
    lvGems.Columns.Add("融合", 52);
    lvGems.Columns.Add("pct", 84);
    lvGems.Columns.Add("当前显示值", 106);
    lvGems.Columns.Add("记录地址", 140);
    lvGems.Columns.Add("可改", 150);
    lvGems.SelectedIndexChanged += delegate { OnGemSel(); };
    p.Controls.Add(lvGems);

    // ---- 修改区 ----
    FlatGroupBox g = new FlatGroupBox();
    g.Title = "修改（改完请在游戏里切一次场景才会落盘）";
    g.SetBounds(8, 298, 800, 158);
    g.Fill = Theme.CardGold;

    g.Controls.Add(Theme.MkLabel("修改项", 16, 34, 52));
    cboGemField = new ComboBox(); cboGemField.SetBounds(70, 31, 112, 23);
    Theme.StyleCombo(cboGemField);
    cboGemField.Items.Add("Tier");
    cboGemField.Items.Add("显示数值");
    cboGemField.SelectedIndex = 0;
    g.Controls.Add(cboGemField);

    g.Controls.Add(Theme.MkLabel("目标值", 198, 34, 52));
    txtGemTarget = Theme.MkText(252, 31, 120, "");
    g.Controls.Add(txtGemTarget);

    btnGemMax = Theme.MkButton("填上限", 382, 29, 76, 26, delegate {
      GemRec r = SelGem();
      if (r == null) { ToastMgr.Show(I18n.T("先选中一颗宝石")); return; }
      bool byValue = (cboGemField.SelectedIndex == 1);
      // ★ 上限**说得清才填**（见 GemRec.TierMaxKnown / ValueMaxKnown）：
      //   说不清的时候**不许拿 255（或任何数）顶替** —— 那会让人以为"这颗宝石最大就是 255"，
      //   而实际上本页根本不知道它的档位语义。改为明说"不清楚"，并建议手动填。
      //   不改动 txtGemTarget 原有内容：用户可能已经自己填了一个值，别替他清掉。
      if (byValue ? !r.ValueMaxKnown : !r.TierMaxKnown) {
        string why = r.MaxUnknownWhy(byValue);
        ToastMgr.Warn(I18n.T("上限不清楚：") + why + I18n.T(" —— 请自己填一个目标") + (byValue ? I18n.T("数值") : " Tier"));
        Log("填上限未填：模板 " + r.Tpl + " 的上限说不清（" + why + "）");
        return;
      }
      // 到这里上限是确定的：加法型 tier 上限 = 255（字段硬上限）；下标型 = 档位表长（不是 255）
      txtGemTarget.Text = byValue ? r.MaxValue().ToString() : r.TierMax.ToString();
    });
    g.Controls.Add(btnGemMax);

    btnGemApply = Theme.MkButton("应用", 466, 29, 90, 26, delegate { GemWarnStaleList(); ApplyGemEdit(); });
    g.Controls.Add(btnGemApply);

    lblGemInfo = Theme.MkHint("", 16, 66, 766);
    lblGemInfo.Height = 82;
    lblGemInfo.ForeColor = Theme.Text;
    g.Controls.Add(lblGemInfo);
    p.Controls.Add(g);

    SetGemControls(false);
    return p;
  }

  // ================= 数据表加载 =================
  // gem_index.ini：每行 “模板名=0x索引”（# 或 ; 开头为注释）。开发期实测生成。
  public void LoadGemIndex() {
    gemIndex.Clear();
    gemNameByKey.Clear();
    // ⚠⚠ 2026-10-07 实测纠正：FName 索引**不是由 DefaultGems.ini 决定的，也不跨进程稳定**！
    //   同一个 ini、同一个槽，两次运行分别读到 暗火 0xAA6A vs 0xAA70、FireGem 0x8557 vs 0x855D。
    //   证据：PID 31588 读 0xAA70；PID 29420（13:36:45 启动，ini 13:12:38 之后）读回 0xAA6A。
    //   ⇒ 静态索引表只能当"提示"，**主路径必须是玩家对象定位**（LocateByIdentity，不依赖索引）。
    //   下面这两行只是索引表缺失/失配时的兜底，且可能只对某次运行有效。
    gemIndex["uberelementalattackgem_100"] = 0xAA6A;
    gemIndex["firegem"] = 0x8557;
    // ★ 必须保留原始大小写：GemDb.Get() 是大小写敏感的字典查找（键 = ini 里的模板名）
    gemNameByKey[GemKey(0xAA6A, 0x65)] = "UberElementalAttackGem_100";
    gemNameByKey[GemKey(0x8557, 0x00)] = "FireGem";
    // 两张都登记，谁命中算谁（应对跨进程漂移）
    gemIndex["uberelementalattackgem_100#b"] = 0xAA70;
    gemIndex["firegem#b"] = 0x855D;
    gemNameByKey[GemKey(0xAA70, 0x65)] = "UberElementalAttackGem_100";
    gemNameByKey[GemKey(0x855D, 0x00)] = "FireGem";

    string f = Path.Combine(ExeDir, "gem_index.ini");
    int n = 0;
    try {
      if (File.Exists(f)) {
        foreach (string raw in File.ReadAllLines(f)) {
          string s = raw.Trim();
          if (s.Length == 0 || s[0] == '#' || s[0] == ';') continue;
          int eq = s.IndexOf('=');
          if (eq <= 0) continue;
          string orig = s.Substring(0, eq).Trim();          // 原始大小写（给 GemDb.Get 用）
          string k = orig.ToLowerInvariant();               // 小写键（查 boost 用）
          // 值后面可能跟注释："0xB2A2  ; num=0xC9  type=additive verified=true"
          string v = s.Substring(eq + 1).Trim();
          int cut = v.IndexOfAny(new char[] { ';', ' ', '\t' });
          if (cut >= 0) v = v.Substring(0, cut);
          if (v.StartsWith("0x") || v.StartsWith("0X")) v = v.Substring(2);
          long idx;
          if (!long.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out idx)) continue;
          // 同族共用 Index、靠 Number 区分 —— 不读 num 会把 _100/_200 张冠李戴
          long num = 0;
          int np = s.IndexOf("num=0x", StringComparison.OrdinalIgnoreCase);
          if (np >= 0) {
            string ns = s.Substring(np + 6).Trim();
            int nc = ns.IndexOfAny(new char[] { ';', ' ', '\t' });
            if (nc >= 0) ns = ns.Substring(0, nc);
            long.TryParse(ns, System.Globalization.NumberStyles.HexNumber, null, out num);
          }
          gemIndex[k] = idx;
          gemNameByKey[GemKey((int)idx, (int)num)] = orig;
          n++;
        }
      }
    } catch (Exception ex) { Log("gem_index.ini 读取失败: " + ex.Message); }
    Log("宝石索引表: 文件 " + n + " 条 + 兜底 → 共 " + gemIndex.Count + " 个名字");
    if (n == 0) Log("⚠ 未找到 gem_index.ini — 目前只能识别暗火与 FireGem");

    // RecipeBoostAmount（加法型判据）直接从 ib3_gems.ini 抓
    gemBoost.Clear();
    try {
      string ini = Path.Combine(ExeDir, "ib3_gems.ini");
      if (File.Exists(ini)) {
        string cur = null;
        foreach (string raw in File.ReadAllLines(ini)) {
          string s = raw.Trim();
          if (s.Length == 0 || s[0] == ';') continue;
          if (s[0] == '[') {
            int sp = s.IndexOf(' ');
            cur = (sp > 0 ? s.Substring(1, sp - 1) : s.Trim('[', ']')).ToLowerInvariant();
          } else if (cur != null && s.StartsWith("RecipeBoostAmount=")) {
            long bv;
            if (long.TryParse(s.Substring(18).Trim(), out bv)) gemBoost[cur] = bv;
          }
        }
      }
    } catch { }
    Log("加法型模板(RecipeBoostAmount): " + gemBoost.Count + " 个");
  }

  // 用户要求（2026-10-07）：宝石页每点一次「读取背包 / 重新扫描 / 应用」都浮窗提醒一次。
  // 理由：列表是**上一次读取那一刻**的内存快照。期间只要游戏内背包变过（熔炉融合、买卖、
  //   换装、升级），列表位置就和游戏对不上了 —— 而本页的宝石名是拿**存档**按位对齐贴上去的，
  //   一旦错位，就是"你看着是这颗、改的是那颗"。所以每次都提醒：不一致时先存档（切一次场景），
  //   再重新扫描，让存档与内存重新同步。
  void GemWarnStaleList() {
    ToastMgr.Warn(I18n.T("提示：若宝石列表与游戏内背包不一致，请先在游戏里存一次档（切一次场景），再点「读取背包」重新扫描"));
  }

  // ================= 扫描 =================
  void RefreshGems() {
    if (!RequireH()) return;
    IntPtr h = H;
    RunBackground(I18n.T("读取背包宝石"), delegate {
      // ★ 2026-10-07 二次改版：主路径换成「身份定位」（真身 +0x1FEC/+0x1FFC），
      //   序列扫描降为「补充同内容拷贝」与兜底。原因见 NameIdentity 上方那段注释 ——
      //   序列扫描拿存档内容当指纹，背包一旦退化成「一串一模一样的 t0/c0/p0 同名记录」
      //   就整条作废，而且还会"成功"地只显示商店。那正是本次实测验收暴露的失败。
      string candNote;
      List<SeqCand> cands = BuildSeqCands(out candNote);

      var groups = new List<List<GemRec>>();   // 每个数组一组，下标即 ArrId
      var starts = new List<long>();           // 各组首址，用于去重
      var trail = new StringBuilder();         // 定位留痕：失败时单凭日志就能判断卡在哪一步

      // ① 主路径：身份定位（玩家真身 + 相对偏移，内容无关）
      string idNote;
      List<GemRec> bag, shop; int bagC, shopC;
      if (LocateByIdentity(h, out bag, out shop, out bagC, out shopC, out idNote)) {
        int slot; byte[] body; string nm;
        NameIdentity(bag, shop, bagC, shopC, out slot, out body, out nm);
        if (bagC > 0) { groups.Add(bag); starts.Add(bag[0].RecAddr); }
        if (shopC > 0) { groups.Add(shop); starts.Add(shop[0].RecAddr); }
        trail.Append("①身份定位：" + nm);
      } else {
        trail.Append("①身份定位失败（" + idNote + "）");
      }

      // ② 补充：序列扫描找「同内容的其他拷贝」一起写（同一份数据在内存里常有多份）。
      //    只在①已定位时才跑 —— 否则白搭一次全内存扫描。
      if (groups.Count > 0 && cands.Count > 0) {
        List<SeqHit> hits = ScanBySequences(h, cands);
        int added = 0;
        foreach (SeqHit ht in hits) {
          if (starts.Contains(ht.Addr)) continue;      // 权威那份已在①里，别重复记
          var g = new List<GemRec>();
          for (int k = 0; k < ht.Cand.Sv.Count; k++) {
            GemRec r = MakeRec(ht.Buf, k * GEM_REC, ht.Addr + k * GEM_REC);
            NameOne(r, ht.Cand.Sv[k].Name);
            g.Add(r);
          }
          groups.Add(g); starts.Add(ht.Addr); added++;
        }
        trail.Append("｜②序列扫描补充 " + added + " 份同内容拷贝");
      }

      // ③ 兜底：序列扫描单独定位（真身拿不到时）
      if (groups.Count == 0 && cands.Count > 0) {
        List<GemRec> recs; int[] sz; string seqNote;
        if (LocateBySaveSequence(h, cands, out recs, out sz, out seqNote)) {
          AddGroupsFrom(recs, sz, groups);
          trail.Append("｜③序列扫描定位：" + seqNote);
        } else {
          trail.Append("｜③序列扫描：" + seqNote);
        }
      }

      // ④ 最后手段：形状扫描（结果含大量非权威拷贝/碎片，必须明说）
      if (groups.Count == 0) {
        List<GemRec> scanned = ScanGemRecords(h);
        if (scanned.Count > 0) {
          int[] ss = SizesOf(scanned);
          int named = AlignAndNameGroups(scanned, ss, cands);
          AddGroupsFrom(scanned, ss, groups);
          trail.Append("｜④形状扫描兜底 " + scanned.Count + " 条（含非权威拷贝，改前请三思），贴名 " + named + " 条");
        }
      }

      if (groups.Count == 0) {
        // ★ 全失败时把「四条路径的留痕 + 各槽存档条数 + 闸门跳过项」全写出来，
        //   单凭日志就能判断是"没扫到"还是"存档与内存不同步"还是"存档侧就没有可用序列"。
        return "未定位到宝石数组｜" + trail.ToString() + "｜存档侧：" + candNote +
               "｜若在菜单/存档选择/战斗界面，请先真正进入藏身地再读。";
      }

      var found = new List<GemRec>();
      var sizes = new List<int>();
      for (int i = 0; i < groups.Count; i++) {
        foreach (GemRec r in groups[i]) { r.ArrId = i; found.Add(r); }
        sizes.Add(groups[i].Count);
      }

      Log("自校准：" + trail.ToString() + "｜存档侧 " + candNote);
      Log(AuthorityNote(h, found));

      BeginInvoke((MethodInvoker)delegate {
        gemRecs = found;
        gemArrSize = sizes.ToArray();
        FillArrCombo();
        SetGemControls(true);
      });
      return "定位到 " + found.Count + " 条宝石记录，分布在 " + sizes.Count + " 个数组";
    });
  }

  // 把「已按 ArrId 分好组的扁平列表」按组搬进 groups（序列扫描/形状扫描的产出用）
  static void AddGroupsFrom(List<GemRec> all, int[] sizes, List<List<GemRec>> groups) {
    for (int a = 0; a < sizes.Length; a++) {
      var g = new List<GemRec>();
      foreach (GemRec r in all) if (r.ArrId == a) g.Add(r);
      if (g.Count > 0) groups.Add(g);
    }
  }

  // ============ 权威定位：玩家对象相对偏移（2026-10-07 经实验验证）============
  //   vtable = 0x140B66BE0（真身首 8 字节）
  //   真身 = 金币地址 − 0x2070
  //   背包 TArray 头 = 真身 + 0x1FEC  {int64 Data, int32 Count, int32 Max}
  //   商店 TArray 头 = 真身 + 0x1FFC（紧挨着）
  // 比形状扫描强的地方：① 唯一权威（不会扫出 2~3 份难辨的拷贝）② 能区分
  // “背包为空”（Count=0，界面不加载背包）和“根本没定位到”。
  // 2026-10-07 二次改版：原 TryLocateBag/TryLocateShop 两个方法合并成 LocateByIdentity
  //   （见下文），因为调用方每次都要同时看背包与商店、却又各调一次 RealBodyOk。
  // 玩家真身类的 vtable（本构建固定 —— 注入器本来就依赖映像基址 0x140000000 无 ASLR）
  const long PLAYER_VT = 0x140B66BE0;

  // 真身校验：地址簿取 misc.gold → 真身 = 金币 − 0x2070 → 首 8 字节必须是 PLAYER_VT。
  // ★ 必须自证清白：地址簿里的 misc.gold 可能是**上个会话/上一场景的过期地址**。
  //   2026-10-07 实测：地址簿 misc.gold=0x7FF4E6A520B0 已经过期（值 76777147 vs 存档 76778412），
  //   该地址上的 vtable=0x14090BF40（另一个类），而**它的 +0x1FEC/+0x1FFC 恰好是干净的全 0**
  //   → 逐条记录形状校验在空数组上"全部通过"，于是旧代码会得到「定位成功但背包为空」的
  //   错误结论（比报错更坏：静默把结果当成真相）。所以这里必须**比对 vtable 常量**，
  //   光判"非 0"不够。
  bool RealBodyOk(IntPtr h, out long realBody, out string note) {
    realBody = 0; note = null;

    // ① 地址簿（GUI 附着后由引擎 hook 刷新成新地址，是最快的一条）
    // ★ 2026-10-07 修复：**光验 vtable 不算数了**。地址簿是在 ini 里持久化的，游戏一重启基本必过期；
    //   而玩家类的"空壳实例"（影子）与真身**同一个类、同一个 vtable**，旧地址被复用成新实例时
    //   vtable 校验照样通过。现场（17:24:28 重启游戏、17:25:31 写入）就是这条：地址簿留着上一局
    //   17:19 写的 misc.gold，此刻那个地址已整块释放（现读它 err=299）。所以这一条也要一并打
    //   真身分，分不够就不采信，交给 ② 扫。
    long ab = RealBody();
    int abScore = 0; string abWhy = null; bool abVtOk = false;
    if (ab != 0 && BodyHasPlayerVt(h, ab)) { abVtOk = true; abScore = BodyScore(h, ab, out abWhy); }

    // ② 按 vtable 常量独立自找（内部同样按真身分挑，不是"地址序第一个"）。
    // ★ 实测必要：地址簿是在 ini 里持久化的，重启/换场景后基本必过期；引擎 hook 也并非总能绑上
    //   （回归自检 gemselftest.exe 不附着 hook，每次都是这条在兜）。没有它，「兜底1：玩家对象相对
    //   偏移」会连带失效，只剩形状扫描那 92 条垃圾 —— 2026-10-07 14:06 那次「写进去游戏无变化」
    //   的现场日志（"玩家对象定位到但背包 Count=0；形状扫描找到 92 条"）就是这么来的。
    int fbScore; string fbWhy;
    long fb = FindBodyByVTable(h, out fbScore, out fbWhy);

    // 谁真身分高用谁；同分优先地址簿（它是引擎 hook 直接给的，比"扫出来的"更权威）
    long best = fb; int bestScore = fbScore;
    if (abVtOk && abScore >= fbScore) { best = ab; bestScore = abScore; }

    // 分 > 0 才算"这是真身"：4 = 至少一组数组真装着东西，2 = 只有金币，1 = 只有筹码。
    // 影子实例两者皆 0（实测同进程 5 个），拿它去读 +0x1FEC/+0x1FFC 只会得到"干净的全 0"。
    if (best != 0 && bestScore > 0) { realBody = best; return true; }

    // 失败：把两条路径**分别**卡在哪写清楚。否则日志只剩一句"没找到"，下次还得重跑一遍全内存扫描。
    var sb2 = new StringBuilder();
    if (ab == 0) sb2.Append("地址簿里没有 misc.gold（引擎自动绑定还没完成）");
    else if (!abVtOk) sb2.Append("地址簿 misc.gold 已过期：0x" + ab.ToString("X") + " 首 8 字节不是玩家 vtable");
    else sb2.Append("地址簿真身 0x" + ab.ToString("X") + " 真身分=" + abScore +
                    "（" + (abWhy.Length > 0 ? abWhy : "无特征") + "）");
    sb2.Append("；vtable 扫描：" + fbWhy);
    note = sb2.ToString();
    return false;
  }

  // 一个 TArray 头 {int64 Data, int32 Count, int32 Max} 是否"装着东西且形状可信"。
  // 只用于挑实例，不做逐条记录校验（那是 RecsShapeOk 的事）。
  static bool ArrayLoaded(IntPtr h, long headerAddr, out long data, out int count) {
    data = 0; count = 0;
    byte[] hdr = new byte[16]; string err;
    if (!MemIO.ReadBytes(h, headerAddr, hdr, out err)) return false;
    data = BitConverter.ToInt64(hdr, 0);
    count = BitConverter.ToInt32(hdr, 8);
    int max = BitConverter.ToInt32(hdr, 12);
    if (count <= 0 || count > 4096) return false;
    if (max < count || max > 65536) return false;
    if (data < 0x10000 || data > 0x7FFFFFFFFFFF) return false;
    return true;
  }

  // 玩家类实例的"真身分"。★ 2026-10-08 起本判据已**上移进 EngineCall.BodyScore**，
  //   成为全局选真身的依据（PickExecutor / ValidAnchor / LiveSane 都用它）。
  //   两处必须保持一致 —— 改这里就要改 EngineCall.cs 的同名函数。
  //   （那边的注释：「真玩家持有真实金币/筹码/商店数组；影子实例全为 0」）。
  // ★ 权重必须是「数组装载(4+4) > 金币(2) > 筹码(1)」：
  //   实测同进程 8 个带 PLAYER_VT 的地址里，有一个是「金币非 0 但背包/商店都 Count=0」
  //   （0x7FF4FD9B0100 —— 正是 17:25 那条日志里被当成"真身"的那个）。只看金币会选中它，
  //   然后读出两组"干净的全 0"，日志报「真身定位到，但两组数组都不可用」。
  //   数组才是本页真正要的东西，所以它压过金币。
  int BodyScore(IntPtr h, long body, out string why) {
    int s = 0;
    var sb = new StringBuilder();
    long d; int c;
    if (ArrayLoaded(h, body + 0x1FEC, out d, out c)) { s += 4; sb.Append("背包 " + c + " 条 "); }
    if (ArrayLoaded(h, body + 0x1FFC, out d, out c)) { s += 4; sb.Append("商店 " + c + " 条 "); }
    byte[] b = new byte[8]; string err;
    if (MemIO.ReadBytes(h, body + 0x2070, b, out err)) {
      long gold = BitConverter.ToInt64(b, 0);
      if (gold > 0 && gold < 1000000000000000L) { s += 2; sb.Append("金币=" + gold + " "); }
    }
    // 筹码是 I32（见 ib3_addrs.ini 的 misc.chip / AddrProbe.OFF_CHIP），别按 I64 读
    if (MemIO.ReadBytes(h, body + 0x2094, b, out err)) {
      long chip = BitConverter.ToInt32(b, 0);
      if (chip > 0 && chip < 1000000000000000L) { s += 1; sb.Append("筹码=" + chip + " "); }
    }
    why = sb.ToString().Trim();
    return s;
  }

  // 真身缓存：vtable 扫描要读遍全部可写私有区（实测数秒），别每次重扫。
  // ★ 2026-10-07 收紧：缓存的有效条件从「vtable 还在」改成「vtable 还在 **且 真身分≥4（装着数组）**」。
  //   只验 vtable 的话，一旦某次挑中/缓存了一个空壳实例，之后**整场都会一直用它**——
  //   因为 vtable 永远不会变，缓存就永远"有效"，于是每次读写都读到两组全 0。
  long vtBodyCache = 0;

  bool BodyHasPlayerVt(IntPtr h, long addr) {
    if (addr == 0) return false;
    byte[] b = new byte[8]; string err;
    if (!MemIO.ReadBytes(h, addr, b, out err)) return false;
    return BitConverter.ToInt64(b, 0) == PLAYER_VT;
  }

  // 按 vtable 常量独立寻找玩家对象：不查地址簿、不靠内容匹配。
  // ★ 2026-10-07 修复（这就是 17:25 那条日志的直接成因）。旧实现有两个洞：
  //   ① **只看背包**（+0x1FEC 的 Count>0）—— 而"玩家在关卡里 / 商店熔接室界面时背包 TArray 为空"
  //      是**正常状态**（LOCATOR.md 铁律 2）。此时真身的**商店**数组（+0x1FFC）往往还是好的
  //      （实测此刻真身 0x7FF4F6590040：背包 0 条、商店 152 条），却因为背包为空被判成"没装载的"。
  //   ② 全都没装载就 `return bare`（**地址序第一个**裸匹配）—— 等于随机挑。实测同进程有
  //      **8 个**带 PLAYER_VT 的地址，其中 5 个是金币/数组全 0 的影子、1 个是金币非 0 但两组数组
  //      都空、1 个是 vtable 假阳性（内容是属性流）；只有 1 个装着数组。挑中前者，读 +0x1FEC/+0x1FFC
  //      就得到两组"干净的全 0"，日志报「真身定位到，但两组数组都不可用」，写入被拒。
  //   现在统一按 BodyScore 打分（背包装载 4 / 商店装载 4 / 金币 2 / 筹码 1）取最高分，并把
  //   "为什么是这个分"通过 why 带回给调用方，日志里能直接分辨"是影子"还是"数组没装载"。
  long FindBodyByVTable(IntPtr h, out int score, out string why) {
    score = 0; why = null;
    if (BodyHasPlayerVt(h, vtBodyCache)) {          // 缓存仍"装着数组"才复用；空壳缓存要重扫
      int cs; string cw;
      cs = BodyScore(h, vtBodyCache, out cw);
      if (cs >= 4) { score = cs; why = cw; return vtBodyCache; }
    }
    vtBodyCache = 0;
    byte[] pat = BitConverter.GetBytes(PLAYER_VT);
    byte[] buf = new byte[0x100000];
    long best = 0; int bestScore = 0; string bestWhy = null;
    int matches = 0;
    long addr = ScanCore.MIN_ADDR;
    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long pos = addr, end = addr + size;
        while (pos < end) {
          int step = (int)Math.Min(0x100000L, end - pos);
          int want = (int)Math.Min((long)buf.Length, end - pos);
          byte[] use = (want == buf.Length) ? buf : new byte[want];
          string err;
          if (MemIO.ReadBytes(h, pos, use, out err)) {
            for (int i = 0; i + 8 <= use.Length; i += 8) {
              if (use[i] != pat[0] || use[i + 1] != pat[1] || use[i + 2] != pat[2] || use[i + 3] != pat[3]) continue;
              if (use[i + 4] != pat[4] || use[i + 5] != pat[5] || use[i + 6] != pat[6] || use[i + 7] != pat[7]) continue;
              long ob = pos + i;
              if (ob % 16 != 0) continue;
              if (!BodyHasPlayerVt(h, ob)) continue;      // 读回确认（缓冲块可能跨越区间边界）
              matches++;
              int s; string w;
              s = BodyScore(h, ob, out w);
              // 同分取地址小的 —— 让结果可复现，不随扫描顺序漂移
              if (s > bestScore) { bestScore = s; best = ob; bestWhy = w; }
              else if (s == bestScore && s > 0 && ob < best) { best = ob; bestWhy = w; }
            }
          }
          pos += step;
        }
      }
      addr += size;
    }
    if (bestScore >= 4) vtBodyCache = best;     // 只有"装着数组"的才值得缓存（见 vtBodyCache 注释）
    why = (matches == 0)
      ? "按 vtable 0x" + PLAYER_VT.ToString("X") + " 扫遍可写私有区没找到玩家对象"
      : (best != 0
          ? "扫到 " + matches + " 个带玩家 vtable 的对象，最高真身分 " + bestScore + " @0x" +
            best.ToString("X") + "（" + (bestWhy.Length > 0 ? bestWhy : "无特征") + "）"
          : "扫到 " + matches + " 个带玩家 vtable 的对象，但没有一个装着背包/商店数组、也没有金币（全是空壳影子）");
    score = bestScore;
    return best;
  }

  // 逐条形状校验：不过就判定位失败，退回下一级兜底。
  // 报错必须带**下标与地址** —— 只写"名索引 0x0"没法定位是哪一条（实测商店 152 条时踩过）。
  static bool RecsShapeOk(List<GemRec> recs, out string note) {
    note = null;
    int valid = 0;
    for (int i = 0; i < recs.Count; i++) {
      GemRec r = recs[i];
      string at = "[第" + i + "条 @0x" + r.RecAddr.ToString("X") + "] ";
      // ★ 名索引 0 = **空槽**（存档侧对应 GemName="None"），是合法占位，不是坏数据。
      //   实测商店第 140 条即是；原先把它当异常、进而废掉整个商店数组，是判错了。
      if (r.NameIdx < 0 || r.NameIdx > 0x100000) { note = at + "名索引 0x" + r.NameIdx.ToString("X"); return false; }
      if (r.Cook != 0 && r.Cook != 50) { note = at + "cook=" + r.Cook; return false; }
      if (r.Pct < -0.001f || r.Pct > 1.5f) { note = at + "pct=" + r.Pct; return false; }
      if (r.NameIdx > 0) valid++;
    }
    // 整组全是空槽 → 多半读到了未装载/已释放的数组，仍判形状异常（否则会重现"定位成功但背包为空"那个坑）
    if (valid == 0) { note = "整组 " + recs.Count + " 条全是空槽 —— 疑似读到了未装载的数组"; return false; }
    return true;
  }

  // 附带自检（不参与定位，只写日志）：命中的数组里**有没有权威那份** ——
  // 真身 +0x1FEC 的 TArray.Data。这是用户实测痛点的直接体检：
  // 「写了 tier，日志报成功，游戏里毫无变化」= 写在了非权威拷贝上。
  // 拿不到真身（地址簿过期 / 没有引擎 hook）就明说"跳过"，绝不把"查不了"说成"不对"。
  string AuthorityNote(IntPtr h, List<GemRec> found) {
    long rb; string note;
    if (!RealBodyOk(h, out rb, out note)) return "权威性自检跳过（" + note + "）";
    byte[] hdr = new byte[16]; string err;
    if (!MemIO.ReadBytes(h, rb + 0x1FEC, hdr, out err)) return "权威性自检跳过：读 +0x1FEC 失败 " + err;
    long data = BitConverter.ToInt64(hdr, 0);
    int cnt = BitConverter.ToInt32(hdr, 8);
    if (cnt <= 0 || data == 0) {
      return "权威性自检：真身背包当前为空（Count=0，界面没装载背包）—— 命中的是别处的同内容拷贝。" +
             "请进入藏宝室界面并切一次场景后再读。";
    }
    foreach (GemRec r in found) {
      if (r.RecAddr == data) {
        return "✓ 权威性自检：真身背包数组 0x" + data.ToString("X") + "（" + cnt + " 条）就在命中列表里";
      }
    }
    return "⚠ 权威性自检：真身背包数组 0x" + data.ToString("X") + "（" + cnt + " 条）**不在**命中列表里 —— " +
           "存档与内存不同步（自上次落盘后背包已变过）。命中的是同内容的旧拷贝，改它多半不生效：" +
           "请回游戏切一次场景让它落盘，再点「读取背包」。";
  }

  // ============================================================================
  // 身份定位（2026-10-07 新增，作为主路径）
  //
  // 为什么必须把它摆回主路径：序列扫描是**拿存档内容当指纹**去找内存，一旦背包内容退化成
  //   「一大串一模一样的 t0/c0/p0 同名记录」，指纹就失效了。实测现场（槽0，23 条）：
  //     20×UberBossBoostGem + 2×UberAttackGem 全是 tier0/cook0/pct0，独特的只剩那颗 tier255，
  //   闸门 SeqUsable 判「非平凡条数 1<2」→ 整条背包序列作废
  //   → 背包既**定位不到**（没有指纹可扫）也**贴不上名**（AlignAndNameGroup 同样只吃 cands）。
  //   而这是**正常游玩状态**：把带 badge 的宝石镶到装备上，背包立刻变成这副样子。
  // 更糟的是主路径当时会「成功返回」——它找到了商店（22 条都独特），于是日志报成功、界面显示
  //   22 条商店宝石当作"背包"，用户看不出哪里不对。
  //
  // 身份定位不碰内容：玩家真身 +0x1FEC/+0x1FFC 就是背包/商店 TArray，找到真身即找到数组。
  // 命名也不需要内容指纹 —— 已经知道内存这份**就是**背包，直接与存档 PlayerUnequippedGems
  // 按位一一对应即可（README 8.2 原本就是这么设计的）。
  // ============================================================================

  // 内存一组记录 vs 存档同名数组：条数 + 逐条 (Tier,Cook,Pct) 全中。无副作用，可反复试算。
  // ★ 这道闸比「非平凡条数≥2」**强得多**：22 条相同记录只要有一条对不上就整组作废。
  //   也正因如此，它不必再要求「名字必须在 GemDb 里查得到」—— 那样会把含冷门模板的
  //   合法存档误判成错位（GemDb 查不到只该导致"这条不可改"，不该导致"整个背包不认"）。
  static bool ZipEquals(List<GemRec> group, List<SaveGem> sv, out string why) {
    why = null;
    int n = (group == null) ? 0 : group.Count;
    int m = (sv == null) ? 0 : sv.Count;
    if (n == 0) { why = "内存组为空"; return false; }
    if (n != m) { why = "条数不符（内存 " + n + " vs 存档 " + m + "）"; return false; }
    for (int i = 0; i < n; i++) {
      GemRec r = group[i]; SaveGem g = sv[i];
      // ★ 空槽特判（必须）：内存全零记录 ↔ 存档 `GemName="None"`。存档那个简化解析器解不出
      //   空槽的 tier/cook，会留下 -1（SaveGem 的默认值），若不特判，**一个空槽就会让整组
      //   按位对齐失败** —— 实测商店第 140 条正是这种情况。
      bool memEmpty = (r.NameIdx <= 0);
      bool svEmpty = (g.Name == null || g.Name.Length == 0 || g.Name == "None");
      if (memEmpty || svEmpty) {
        if (memEmpty != svEmpty) {
          why = "第" + i + "条空槽不对应（内存" + (memEmpty ? "空" : "非空") + " / 存档" + (svEmpty ? "空" : "非空") + "）";
          return false;
        }
        continue;
      }
      if (g.Tier < 0 || g.Cook < 0) { why = "存档第" + i + "条字段缺失"; return false; }
      if (r.Tier != g.Tier || r.Cook != g.Cook) { why = "第" + i + "条 tier/cook 不符"; return false; }
      float d = r.Pct - g.Pct;
      if (d < 0) d = -d;
      if (d >= 0.002f) { why = "第" + i + "条 pct 不符"; return false; }
    }
    return true;
  }

  // 真身 → 背包(+0x1FEC) / 商店(+0x1FFC) 两个 TArray。内容无关，背包退化也照样命中。
  bool LocateByIdentity(IntPtr h, out List<GemRec> bag, out List<GemRec> shop,
                        out int bagC, out int shopC, out string note) {
    bag = new List<GemRec>(); shop = new List<GemRec>(); bagC = 0; shopC = 0; note = null;
    long rb;
    if (!RealBodyOk(h, out rb, out note)) return false;
    string e1, e2;
    if (!ReadGemArray(h, rb + 0x1FEC, out bag, out bagC, out e1)) { note = "读背包数组失败：" + e1; return false; }
    if (!ReadGemArray(h, rb + 0x1FFC, out shop, out shopC, out e2)) { note = "读商店数组失败：" + e2; return false; }
    // ★ 逐条形状校验**只作废出问题的那一组，绝不连坐**。
    //   实测（2026-10-07）：商店 152 条时曾出现单条"名索引 0"的记录 —— 多半是游戏正在重建
    //   该数组的瞬间被我们读到。原来的写法是任一组形状坏就整体 return false，
    //   结果**把好好的背包一起废掉**，界面上整个宝石页变空。背包才是主对象，不能被商店拖死。
    string w;
    int bagRaw = bagC, shopRaw = shopC;    // ★ 先记下"数组头读到的原始条数"：区分"没装载"与"形状坏"
    if (!RecsShapeOk(bag, out w)) {
      note = "背包形状异常（已丢弃该组）：" + w;
      bag = new List<GemRec>(); bagC = 0;
    }
    if (!RecsShapeOk(shop, out w)) {
      note = (note != null ? note + "；" : "") + "商店形状异常（已丢弃该组）：" + w;
      shop = new List<GemRec>(); shopC = 0;
    }
    if (bagC == 0 && shopC == 0) {
      if (bagRaw == 0 && shopRaw == 0) {
        // ★ 2026-10-07：这条分支以前和"形状异常"共用一句话（"两组数组都不可用（空 / 形状异常）"），
        //   结果把一个**正常状态**说成了故障。数组头 Count=0 不是坏数据：玩家在关卡里 / 商店熔接室
        //   界面时背包会被卸载（LOCATOR.md 铁律 2）。此时确实没有任何权威数组可写，必须拒绝，
        //   但要给出**照做得动**的提示 —— 旧提示"请重新点「读取背包」"根本改不了游戏界面，等于空话。
        note = (note != null ? note + "；" : "") +
               "真身 0x" + rb.ToString("X") + " 定位到，但背包(+0x1FEC)/商店(+0x1FFC)两个数组当前都是空的（Count=0）——" +
               "这是**正常状态**：玩家在关卡里 / 商店熔接室界面时背包会被卸载。" +
               "请回到**藏身地主界面**（能看到宝石背包的那个界面）再点一次「读取背包」，然后立刻改。";
      } else {
        note = (note != null ? note + "；" : "") +
               "真身 0x" + rb.ToString("X") + " 定位到，但两组数组都不可用（形状异常）";
      }
      return false;
    }
    return true;
  }

  // 取哪个槽？逐个试 0..2，看内存这两组各能与该槽的存档对上几组，取分高者。
  // 与序列扫描选槽同原理，但判据是逐条字段相等而非内容指纹 —— 不要求背包"够独特"。
  bool NameIdentity(List<GemRec> bag, List<GemRec> shop, int bagC, int shopC,
                    out int slot, out byte[] body, out string note) {
    slot = -1; body = null; note = null;
    var sb = new StringBuilder();
    int bestScore = 0;
    for (int s = 0; s <= 2; s++) {
      string err;
      byte[] b = ReadSaveBody(s, out err);
      if (b == null) { sb.Append("槽").Append(s).Append("无档；"); continue; }
      int sc = 0;
      string w1, w2;
      if (bagC > 0 && ZipEquals(bag, GemsFromBody(b, "PlayerUnequippedGems", 0), out w1)) sc++;
      if (shopC > 0 && ZipEquals(shop, GemsFromBody(b, "CurrentStoreGems", 0), out w2)) sc++;
      sb.Append("槽").Append(s).Append("过").Append(sc).Append("组 ");
      if (sc > bestScore) { bestScore = sc; slot = s; body = b; }
    }
    if (slot < 0 || bestScore == 0) {
      note = "内存记录与 0/1/2 号槽的存档都对不上（" + sb.ToString().Trim() + "）" +
             "→ 只显示裸索引并拒绝修改。若刚在游戏里动过宝石，请切一次场景让它落盘后再读";
      return false;
    }
    int named = 0;
    if (bagC > 0) named += NameByZip(bag, GemsFromBody(body, "PlayerUnequippedGems", 0));
    if (shopC > 0) named += NameByZip(shop, GemsFromBody(body, "CurrentStoreGems", 0));
    note = "已用 " + slot + " 号槽的存档按位对齐命名 " + named + " 条（背包 " + bagC + " + 商店 " + shopC +
           "）｜路径=身份定位＋按位对齐";
    return true;
  }

  // 调用前必须已过 ZipEquals —— 这里直接按下标贴名，不再复核。
  int NameByZip(List<GemRec> group, List<SaveGem> sv) {
    if (group == null || sv == null || group.Count != sv.Count) return 0;
    for (int i = 0; i < group.Count; i++) NameOne(group[i], sv[i].Name);
    return group.Count;
  }

  // 按 ArrId 统计每个数组的条数（数组个数 = 返回值长度）
  static int[] SizesOf(List<GemRec> all) {
    int maxArr = -1;
    foreach (GemRec r in all) if (r.ArrId > maxArr) maxArr = r.ArrId;
    int[] s = new int[maxArr + 1];
    foreach (GemRec r in all) if (r.ArrId >= 0 && r.ArrId < s.Length) s[r.ArrId]++;
    return s;
  }

  // 形状扫描：只碰“已提交 + 可写 + 私有”区，按 4 字节步进找像宝石记录的位置，
  // 连续 >= GEM_MIN_RUN 条归为一个数组。+0x00 必须是索引表里的合法名字索引。
  List<GemRec> ScanGemRecords(IntPtr h) {
    var outp = new List<GemRec>();
    long addr = ScanCore.MIN_ADDR;
    int nextArr = 0;
    var buf = new byte[0x100000 + GEM_REC];

    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long pos = addr, end = addr + size;
        var run = new List<GemRec>();
        long prevRec = -1;
        while (pos < end) {
          long left = end - pos;
          int want = (int)Math.Min(buf.Length, left);
          byte[] use = (want == buf.Length) ? buf : new byte[want];   // 末块另开小缓冲
          string rerr;
          if (MemIO.ReadBytes(h, pos, use, out rerr)) {
            for (int i = 0; i + GEM_REC <= use.Length; i += 4) {
              if (!LooksLikeGem(use, i)) continue;
              long ra = pos + i;
              if (prevRec >= 0 && ra - prevRec != GEM_REC) {
                FlushRun(outp, run, ref nextArr);
                run = new List<GemRec>();
              }
              prevRec = ra;
              run.Add(MakeRec(use, i, ra));
            }
          }
          pos += (int)Math.Min(0x100000, left);
        }
        FlushRun(outp, run, ref nextArr);
      }
      addr += size;
    }
    return outp;
  }

  static void FlushRun(List<GemRec> outp, List<GemRec> run, ref int nextArr) {
    if (run.Count >= GEM_MIN_RUN) {
      foreach (GemRec r in run) { r.ArrId = nextArr; outp.Add(r); }
      nextArr++;
    }
    run.Clear();
  }

  // 记录形状判定。
  // ★ 不再要求「索引在已知表里」—— FName 索引跨进程会漂移，拿它当判据等于把定位
  //   绑死在某个会话上。改成纯形状约束（下面 cook 那条尤其关键：实测只有 0/50）。
  bool LooksLikeGem(byte[] b, int o) {
    int idx = BitConverter.ToInt32(b, o);
    if (idx <= 0 || idx > 0x100000) return false;
    int num = BitConverter.ToInt32(b, o + 4);
    if (num < 0 || num > 0x10000) return false;
    if (b[o + 0x0A] != 0 || b[o + 0x0B] != 0) return false;      // 对齐填充
    int cook = b[o + 0x09];
    if (cook != 0 && cook != 50) return false;                   // 未融合 / 融合过
    float pct = BitConverter.ToSingle(b, o + 0x0C);
    if (pct < -0.001f || pct > 1.5f) return false;
    if (BitConverter.ToUInt32(b, o + 0x10) > 1) return false;    // bShowBadge
    if (b[o + 0x15] != 0 || b[o + 0x16] != 0 || b[o + 0x17] != 0) return false;
    return true;
  }

  GemRec MakeRec(byte[] b, int o, long ra) {
    var r = new GemRec();
    r.RecAddr = ra;
    r.NameIdx = BitConverter.ToInt32(b, o);
    r.Number = BitConverter.ToInt32(b, o + 4);
    r.Tier = b[o + 0x08];
    r.Cook = b[o + 0x09];
    r.Pct = BitConverter.ToSingle(b, o + 0x0C);

    // 名字优先取「原始大小写」那份 —— GemDb.Get() 大小写敏感，用小写会查不到 → Base=0
    string orig;
    if (gemNameByKey.TryGetValue(GemKey(r.NameIdx, r.Number), out orig)) {
      r.Tpl = orig;
    } else {
      r.Tpl = "0x" + r.NameIdx.ToString("X");
      foreach (KeyValuePair<string, long> kv in gemIndex) {
        if (kv.Value == r.NameIdx) { r.Tpl = kv.Key; break; }
      }
    }
    ResolveKind(r);
    return r;
  }

  // 名字定下来之后再判定「加法型 / 下标型」以及基数、每 tier 增量、档位表。
  // 抽成独立方法，是因为**运行时自校准**拿到真名后需要重跑一遍。
  void ResolveKind(GemRec r) {
    r.Kind = GemTierKind.Unknown; r.Boost = 0; r.Base = 0; r.MaxPct = 0;
    r.Tiers = new List<long>();
    r.TiersFractional = false;
    long boost;
    if (gemBoost.TryGetValue(r.Tpl.ToLowerInvariant(), out boost)) { r.Kind = GemTierKind.Additive; r.Boost = boost; }
    GemDef gd = GemDb.Get(r.Tpl);   // ★ 必须传原始大小写
    if (gd != null) {
      r.Base = gd.Base;
      r.MaxPct = gd.MaxPct;
      r.Tiers = new List<long>(gd.Tiers);
      r.TiersFractional = gd.TiersFractional;
      if (r.Kind != GemTierKind.Additive && gd.Rollable) r.Kind = GemTierKind.Indexed;
    }
  }

  // ============ 运行时自校准：用存档当场算出「本次进程有效」的索引表 ============
  // 背景：FName 索引**不跨进程稳定**（同一 ini、同一槽，两次运行实测 0xAA6A vs 0xAA70），
  //       静态表只能当提示，且失配时会**认错宝石** → 可能写到错误对象上。
  // 做法：存档里 PlayerUnequippedGems 的「名字序列」与内存数组的记录**按位 zip**。
  //       取哪个槽？逐个试 0..2，要「条数吻合 + 每个名字都能在 GemDb 里查到」才算数。
  // 真身地址：金币 − 0x2070（没有金币绑定时返回 0）
  long RealBody() {
    AddrEntry g = AddrBook.Get("misc.gold");
    return (g != null && g.Addr != 0) ? (g.Addr - 0x2070) : 0;
  }

  // 读一个 TArray 头 {int64 Data, int32 Count, int32 Max} 指向的宝石记录数组。
  // 返回 true = 读取成功；此时 count==0 表示数组为空（不是失败）。
  bool ReadGemArray(IntPtr h, long headerAddr, out List<GemRec> recs, out int count, out string note) {
    recs = new List<GemRec>(); count = 0; note = null;
    byte[] hdr = new byte[16]; string err;
    if (!MemIO.ReadBytes(h, headerAddr, hdr, out err)) { note = "读数组头失败：" + err; return false; }
    long data = BitConverter.ToInt64(hdr, 0);
    count = BitConverter.ToInt32(hdr, 8);
    if (count <= 0 || data == 0) return true;              // 空数组
    if (count > 4096) { note = "Count 异常(" + count + ")"; return false; }
    byte[] buf = new byte[count * GEM_REC];
    if (!MemIO.ReadBytes(h, data, buf, out err)) { note = "读记录失败：" + err; return false; }
    for (int i = 0; i < count; i++) recs.Add(MakeRec(buf, i * GEM_REC, data + i * GEM_REC));
    return true;
  }

  // ============================================================================
  // 定位（2026-10-07 改版）：按存档记录序列扫描内存
  //
  // 为什么废掉「形状扫描 + pct 对齐」：实测（_tmp\gemwork\calib_sim.py 与 seqscan_after.py）
  //   ① 形状扫描一次产出 ~405 个候选组。去掉「FName 索引必须在已知表里」这条判据后
  //      （索引跨进程会漂移，本来就不能用），只剩弱约束 → 3 条一组的碎片遍地都是，全是假的。
  //   ② pct 对齐是退化的：宝石 RandomAddPct 大量恒为 0，任何一段 pct=0 的记录都能"对上"，
  //      日志里满屏 "对齐到 save[0..2]"，全是假的。
  //   ③ 真正的判据是 **GemTier 序列**（如 255,0,5,0,0），旧代码从没用它。
  // 新做法：把存档里某个宝石数组的 (Tier, Cook, Pct) 三字段序列**逐条、按序、全长度**拿到
  //   内存里找数组首地址 —— 命中即整段相等，几乎不可能凑巧；找到后**名字直接来自同一份对齐**
  //   （不再依赖静态索引表，也不怕 FName 索引漂移）。
  // ============================================================================

  const int SEQ_MIN_LEN = 3;        // 序列短于 3 条不足以当指纹（与 GEM_MIN_RUN 同思路）

  // 一个候选序列 = 某槽存档里某个宝石数组的全部记录
  class SeqCand {
    public int Slot;
    public string Tag;              // PlayerUnequippedGems / CurrentStoreGems
    public string Label;            // 背包 / 商店
    public List<SaveGem> Sv;
    public byte F0Tier, F0Cook;     // 第 0 条的 Tier/Cook（预取，扫描内层循环先用它粗筛）
    public float F0Pct;
  }

  // 一次命中：某候选序列在内存里的数组首地址 + 该处原始字节（扫描时顺手拷出来，免二次读）
  class SeqHit {
    public SeqCand Cand;
    public long Addr;
    public byte[] Buf;
  }

  // 读某槽存档明文；失败返回 null（原因写 err）
  byte[] ReadSaveBody(int slot, out string err) {
    err = null;
    string cloud = CloudDir();
    if (!Directory.Exists(cloud)) { err = "找不到 Cloud 目录：" + cloud; return null; }
    string f = Path.Combine(cloud, "_SwordSaveX_" + slot + "-0.bin");
    if (!File.Exists(f)) { err = "槽" + slot + " 无存档文件"; return null; }
    byte[] raw;
    try { raw = File.ReadAllBytes(f); }
    catch (Exception ex) { err = "槽" + slot + " 读取失败：" + ex.Message; return null; }
    byte[] body; string de;
    if (!Ib3Crypt.TryDecrypt(raw, out body, out de)) { err = "槽" + slot + " 解密失败：" + de; return null; }
    return body;
  }

  // 建候选序列：0..2 号槽 × (背包 PlayerUnequippedGems / 商店 CurrentStoreGems)。
  // note 里带「各槽条数」与「被闸门挡掉的数组及原因」—— 失败时单凭日志就能定位问题。
  List<SeqCand> BuildSeqCands(out string note) {
    var cands = new List<SeqCand>();
    var counts = new StringBuilder();
    var skip = new StringBuilder();
    for (int slot = 0; slot <= 2; slot++) {
      string err;
      byte[] body = ReadSaveBody(slot, out err);
      counts.Append("槽").Append(slot).Append(' ');
      if (body == null) { counts.Append(err).Append("；"); continue; }
      for (int a = 0; a < 2; a++) {
        string tag = (a == 0) ? "PlayerUnequippedGems" : "CurrentStoreGems";
        string label = (a == 0) ? "背包" : "商店";
        List<SaveGem> sv = GemsFromBody(body, tag, 0);
        counts.Append(label).Append('=').Append(sv.Count).Append(a == 0 ? "/" : "；");
        string why;
        if (!SeqUsable(sv, out why)) { skip.Append("槽").Append(slot).Append(label).Append('(').Append(why).Append(") "); continue; }
        var c = new SeqCand();
        c.Slot = slot; c.Tag = tag; c.Label = label; c.Sv = sv;
        c.F0Tier = (byte)sv[0].Tier; c.F0Cook = (byte)sv[0].Cook; c.F0Pct = sv[0].Pct;
        cands.Add(c);
      }
    }
    note = counts.ToString().TrimEnd('；');
    if (skip.Length > 0) note += "｜闸门跳过 " + skip.ToString().Trim();
    return cands;
  }

  // 候选序列可用性闸门。★ 第 4 条尤其关键：实测槽 0 的商店存档是 152 条「全 0 模板默认表」
  //   （tier=0/cook=0/pct=0），这种序列等于"任意一段全 0 记录"，拿它当指纹只会满屏假命中。
  static bool SeqUsable(List<SaveGem> sv, out string why) {
    why = null;
    if (sv.Count < SEQ_MIN_LEN) { why = "条数 " + sv.Count + "<" + SEQ_MIN_LEN; return false; }
    int nontriv = 0;
    foreach (SaveGem g in sv) {
      if (g.Tier < 0 || g.Cook < 0 || !g.HasPct) { why = "字段缺失"; return false; }
      if (g.Cook != 0 && g.Cook != 50) { why = "cook=" + g.Cook + " 不在 {0,50}"; return false; }
      if (g.Pct < -0.001f || g.Pct > 1.5f) { why = "pct=" + g.Pct + " 越界"; return false; }
      if (g.Tier != 0 || g.Cook != 0 || Math.Abs(g.Pct) > 1e-9f) nontriv++;
    }
    if (nontriv < 2) { why = "非平凡条数 " + nontriv + "<2"; return false; }
    return true;
  }

  // 内存一条记录（+0x00 起）与存档一条记录的三个字段是否相等：Tier/Cook 精确，Pct 容差 0.002
  static bool RecFieldsEq(byte[] b, int o, SaveGem g) {
    if (b[o + 8] != (byte)g.Tier) return false;
    if (b[o + 9] != (byte)g.Cook) return false;
    float d = BitConverter.ToSingle(b, o + 0x0C) - g.Pct;
    if (d < 0) d = -d;
    return d < 0.002f;
  }

  // 整段序列匹配（长度 = 存档数组条数）
  static bool SeqMatchAt(byte[] b, int o, List<SaveGem> sv) {
    for (int k = 0; k < sv.Count; k++) {
      if (!RecFieldsEq(b, o + k * GEM_REC, sv[k])) return false;
    }
    return true;
  }

  // 记录名域校验：FName.Index / Number 必须落在合理范围。
  // ★ 实测作用：序列在「真数组前一条记录的位置」也可能凑巧命中（那 8 字节恰好是别的数据）。
  //   加上这道闸门后，5 条序列的假命中从 3 处降到 2 处，留下的 2 处正是真数组的两份拷贝。
  static bool NameFieldsSane(byte[] b, int o, int n) {
    for (int k = 0; k < n; k++) {
      int q = o + k * GEM_REC;
      int idx = BitConverter.ToInt32(b, q);
      int num = BitConverter.ToInt32(b, q + 4);
      if (idx <= 0 || idx > 0x100000) return false;
      if (num < 0 || num > 0x10000) return false;
    }
    return true;
  }

  // 单趟扫描：可写私有区（RegionOk + RegionWritable + Type==MEM_PRIVATE）4 字节步进，
  // 对每个候选先比第 0 条的 (Tier,Cook,Pct)（绝大多数偏移在这一步就被否掉），再验整段序列。
  // 缓冲复用；★ 读窗口只从 pos 起、绝不越过区间起点：
  //   早先写成从 pos-margin 起读，区间首块会读进上一个未提交区 → 整块读失败被丢弃，
  //   实测漏掉 ~500MB（含权威那份数组），必须记住这个坑。
  List<SeqHit> ScanBySequences(IntPtr h, List<SeqCand> cands) {
    var hits = new List<SeqHit>();
    if (cands.Count == 0) return hits;
    int maxNeed = 0;
    foreach (SeqCand c in cands) { int n = c.Sv.Count * GEM_REC; if (n > maxNeed) maxNeed = n; }
    byte[] buf = new byte[0x100000 + maxNeed];

    long addr = ScanCore.MIN_ADDR;
    while (addr < ScanCore.MAX_ADDR) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long pos = addr, end = addr + size;
        while (pos < end) {
          long left = end - pos;
          int step = (int)Math.Min(0x100000L, left);
          int want = (int)Math.Min((long)buf.Length, left);
          byte[] use = (want == buf.Length) ? buf : new byte[want];   // 末块另开小缓冲
          string err;
          if (MemIO.ReadBytes(h, pos, use, out err)) {
            foreach (SeqCand c in cands) {
              int need = c.Sv.Count * GEM_REC;
              for (int i = 0; i + need <= use.Length && i < step; i += 4) {
                if (use[i + 8] != c.F0Tier || use[i + 9] != c.F0Cook) continue;
                float d0 = BitConverter.ToSingle(use, i + 0x0C) - c.F0Pct;
                if (d0 < 0) d0 = -d0;
                if (d0 >= 0.002f) continue;
                if (!SeqMatchAt(use, i, c.Sv)) continue;
                if (!NameFieldsSane(use, i, c.Sv.Count)) continue;      // 名域校验（挡假命中）
                var ht = new SeqHit();
                ht.Cand = c; ht.Addr = pos + i;
                ht.Buf = new byte[need];
                Buffer.BlockCopy(use, i, ht.Buf, 0, need);
                hits.Add(ht);
              }
            }
          }
          pos += step;
        }
      }
      addr += size;
    }
    return hits;
  }

  // 主定位：存档序列 → 内存数组。返回的 found/sizes 就是 UI 要显示的全部内容
  //（★ 不再退回显示形状扫描结果 —— 那正是 405 组 / 92 条垃圾的来源）。
  bool LocateBySaveSequence(IntPtr h, List<SeqCand> cands, out List<GemRec> recs, out int[] sizes, out string note) {
    recs = null; sizes = null; note = null;
    if (cands.Count == 0) { note = "没有可用的存档序列候选（见闸门跳过项）"; return false; }

    List<SeqHit> hits = ScanBySequences(h, cands);
    if (hits.Count == 0) { note = "0/1/2 号槽的存档序列在内存里一处都没命中"; return false; }

    // 按命中记录总数选槽：只有「当前装载的那个槽」的存档会对上，其它槽只会是 0
    var score = new Dictionary<int, int>();
    foreach (SeqHit ht in hits) {
      int s; score.TryGetValue(ht.Cand.Slot, out s);
      score[ht.Cand.Slot] = s + ht.Cand.Sv.Count;
    }
    int best = -1, bestScore = -1;
    for (int s = 0; s <= 2; s++) {
      int v;
      if (score.TryGetValue(s, out v) && v > bestScore) { bestScore = v; best = s; }
    }

    // 只留胜出槽的命中，按 (背包在前, 地址升序) 排序 → ArrId 稳定可复现
    var win = new List<SeqHit>();
    foreach (SeqHit ht in hits) if (ht.Cand.Slot == best) win.Add(ht);
    win.Sort(delegate(SeqHit a, SeqHit b) {
      int la = (a.Cand.Label == "背包") ? 0 : 1;
      int lb = (b.Cand.Label == "背包") ? 0 : 1;
      if (la != lb) return la - lb;
      return a.Addr.CompareTo(b.Addr);
    });

    // ★ 两道闸（2026-10-07 加，实测必要）：序列指纹**会因存档内容退化而失效**。
    //   实测现场：商店 152 条（大量同模板同状态记录，≈`setplayergems 0` 的产物）时，
    //   扫描命中 **11 个数组**，并且把同一个 FName 索引贴成了两个不同名字。
    //   这正是本文件反复强调的"**乱认名字比认不出更坏**"。所以：
    //     ① 命中数超 6 → 指纹不独特（内存里正常只有 1~3 份拷贝）；
    //     ② 名字↔索引撞车 → 对齐错位。
    //   两种都判失败，交回上一级（身份定位为主路径；再不行才轮到形状扫描的裸索引）。
    //   注：这两条原先只写在 GemSelfTest.cs 里当断言 —— 自检报警而产品照跑，是不该有的形态。
    if (win.Count > 6) {
      note = "序列指纹不独特（命中 " + win.Count + " 个数组，正常应为 1~3 份拷贝；存档内容退化所致）";
      return false;
    }
    {
      var seen = new Dictionary<long, string>();
      foreach (SeqHit ht in win) {
        for (int k = 0; k < ht.Cand.Sv.Count; k++) {
          long key = GemKey(BitConverter.ToInt32(ht.Buf, k * GEM_REC),
                            BitConverter.ToInt32(ht.Buf, k * GEM_REC + 4));
          string nm = ht.Cand.Sv[k].Name;
          string old;
          if (seen.TryGetValue(key, out old) && old != nm) {
            note = "序列对齐撞车（FName 索引 0x" + (key >> 32).ToString("X") + " 同时被贴成 " +
                   old + " 与 " + nm + "）";
            return false;
          }
          seen[key] = nm;
        }
      }
    }

    recs = new List<GemRec>();
    var sizeList = new List<int>();
    int nBag = 0, nShop = 0, cBag = 0, cShop = 0;
    for (int i = 0; i < win.Count; i++) {
      SeqHit ht = win[i];
      if (ht.Cand.Label == "背包") { nBag = ht.Cand.Sv.Count; cBag++; } else { nShop = ht.Cand.Sv.Count; cShop++; }
      int need = ht.Cand.Sv.Count;
      for (int k = 0; k < need; k++) {
        GemRec r = MakeRec(ht.Buf, k * GEM_REC, ht.Addr + k * GEM_REC);
        // ★ 本设计的关键：名字不是猜的 —— 就是同一份对齐里那条存档记录的名字，
        //   同时把「名字↔索引」映射填掉（本次运行内所见的 FName 索引都由此确定）。
        NameOne(r, ht.Cand.Sv[k].Name);
        r.ArrId = i;
        recs.Add(r);
      }
      sizeList.Add(need);
    }
    sizes = sizeList.ToArray();
    note = "已用 " + best + " 号槽的存档定位并命名 " + nBag + " 条(背包×" + cBag + "份) + " +
           nShop + " 条(商店×" + cShop + "份)｜路径=序列扫描";
    return true;
  }

  // 贴名字 + 登记「名字↔FName 索引」。索引漂移不影响它：索引是从内存记录现读的。
  void NameOne(GemRec r, string nm) {
    // ★ 空槽（2026-10-07 实测）：商店数组里可以有**全零记录**，存档侧对应 `GemName="None"`。
    //   实测商店第 140 条即是（前面是 MagicGem，后面是 ShieldGemOut）。
    //   那是**合法的占位**：必须原样保留槽位（丢一条后面 141~151 全错位），只是不可命名、不可改。
    if (r.NameIdx <= 0 || nm == null || nm.Length == 0 || nm == "None") {
      r.Tpl = "(空槽)";
      r.Kind = GemTierKind.Unknown;
      r.Boost = 0; r.Base = 0; r.MaxPct = 0;
      r.Tiers = new List<long>();
      r.TiersFractional = false;
      return;
    }
    gemNameByKey[GemKey(r.NameIdx, r.Number)] = nm;
    gemIndex[nm.ToLowerInvariant()] = r.NameIdx;
    r.Tpl = nm;
    ResolveKind(r);
  }

  // 兜底路径的贴名：把「已在内存里定位到的记录」按 (Tier,Cook,Pct) 序列滑动对齐到某个存档数组。
  // 比主路径多一条要求：这段里的每个存档名都要能在 GemDb 里查到 —— 兜底拿到的数组是形状/偏移
  // 来的，没有主路径那么硬，多一道闸门防"对齐到垃圾上乱贴名字"。
  int AlignAndNameGroup(List<GemRec> group, List<SeqCand> cands) {
    if (group == null || group.Count == 0) return 0;
    foreach (SeqCand c in cands) {
      if (group.Count > c.Sv.Count) continue;
      for (int off = 0; off + group.Count <= c.Sv.Count; off++) {
        bool ok = true;
        for (int i = 0; i < group.Count; i++) {
          SaveGem g = c.Sv[off + i];
          GemRec r = group[i];
          if (GemDb.Get(g.Name) == null) { ok = false; break; }
          if (r.Tier != g.Tier || r.Cook != g.Cook) { ok = false; break; }
          float d = r.Pct - g.Pct;
          if (d < 0) d = -d;
          if (d >= 0.002f) { ok = false; break; }
        }
        if (!ok) continue;
        for (int i = 0; i < group.Count; i++) NameOne(group[i], c.Sv[off + i].Name);
        return group.Count;
      }
    }
    return 0;
  }

  // 对「按 ArrId 分组」的整批记录逐组对齐命名（形状扫描兜底用）
  int AlignAndNameGroups(List<GemRec> all, int[] sizes, List<SeqCand> cands) {
    int named = 0;
    for (int a = 0; a < sizes.Length; a++) {
      var g = new List<GemRec>();
      foreach (GemRec r in all) if (r.ArrId == a) g.Add(r);
      if (g.Count > 0) named += AlignAndNameGroup(g, cands);
    }
    return named;
  }

  // ---- 存档明文的极简读取（只够取宝石数组的名字序列，不做完整属性走查）----
  static int FindAscii(byte[] b, string s) {
    for (int i = 0; i + s.Length <= b.Length; i++) {
      bool ok = true;
      for (int k = 0; k < s.Length; k++) if (b[i + k] != (byte)s[k]) { ok = false; break; }
      if (ok) return i;
    }
    return -1;
  }

  static int IndexOfBytes(byte[] b, byte[] pat, int from) {
    for (int i = from; i + pat.Length <= b.Length; i++) {
      bool ok = true;
      for (int k = 0; k < pat.Length; k++) if (b[i + k] != pat[k]) { ok = false; break; }
      if (ok) return i;
    }
    return -1;
  }

  // 读一个 UE3 FString（i32 len 含 NUL），并把 o 推到下一个字段
  static string RdStr(byte[] b, ref int o) {
    if (o + 4 > b.Length) { o = b.Length; return null; }
    int n = BitConverter.ToInt32(b, o);
    if (n <= 0 || o + 4 + n > b.Length) { o += 4; return null; }
    string s = Encoding.ASCII.GetString(b, o + 4, n - 1);
    o += 4 + n;
    return s;
  }

  // 存档里一条宝石记录（只取自校准需要的字段）。
  // ★ Tier/Cook 默认 **-1** 而不是 0：解析不到的字段若默认成 0，序列扫描会拿"tier=0"去比，
  //   凭空造出一个假的指纹（实测这种坑很难看出来）。用 -1 让 SeqUsable 直接判该数组不可用。
  class SaveGem { public string Name; public int Tier = -1; public int Cook = -1; public float Pct; public bool HasPct; }

  // 按顺序取出某宝石数组所有元素的 Name / GemTier / CookedGemVar / RandomAddPct。
  // 元素之间是背靠背的属性列表，且每个都以 GemName 开头 —— 直接逐步找下一个标签即可，
  // 不需要完整属性走查（比走查更不容易被新字段绊倒）。
  // 与参考解析器逐条核对过（_tmp\gemwork\gemparse.py），保持原样别动这段。
  static List<SaveGem> GemsFromBody(byte[] b, string arrayTag, int expect) {
    var res = new List<SaveGem>();
    int i = FindAscii(b, arrayTag);
    if (i < 0) return res;
    int o = i + arrayTag.Length;
    while (o < b.Length && b[o] == 0) o++;       // 标签自身的 NUL
    RdStr(b, ref o);                             // "ArrayProperty"
    if (o + 12 > b.Length) return res;
    o += 4;                                      // size
    o += 4;                                      // arrayIndex
    int cnt = BitConverter.ToInt32(b, o); o += 4;
    if (cnt <= 0 || cnt > 4096) return res;
    if (expect > 0 && cnt != expect) return res; // 条数不符 → 不是这个槽

    byte[] tName = StrTag("GemName");
    byte[] tTier = StrTag("GemTier");
    byte[] tPct  = StrTag("RandomAddPct");
    int p = o;
    for (int k = 0; k < cnt; k++) {
      int j = IndexOfBytes(b, tName, p);
      if (j < 0) break;
      int q = j + tName.Length;
      RdStr(b, ref q);                           // "NameProperty"
      q += 8;                                    // size + arrayIndex
      string nm = RdStr(b, ref q);
      if (nm == null) break;
      SaveGem g = new SaveGem();
      g.Name = nm;

      // GemTier / CookedGemVar：ByteProperty = [FString 枚举名] + 1 字节
      int jt = IndexOfBytes(b, tTier, q);
      if (jt >= 0 && jt - q < 512) {
        int r2 = jt + tTier.Length;
        RdStr(b, ref r2);                        // "ByteProperty"
        r2 += 8;                                 // size + arrayIndex
        if (r2 < b.Length) { RdStr(b, ref r2); g.Tier = b[r2]; }
        int jc = IndexOfBytes(b, StrTag("CookedGemVar"), q);
        if (jc >= 0 && jc - q < 512) {
          int r3 = jc + StrTag("CookedGemVar").Length;
          RdStr(b, ref r3); r3 += 8;
          if (r3 < b.Length) { RdStr(b, ref r3); g.Cook = b[r3]; }
        }
      }
      // RandomAddPct：FloatProperty = 4 字节 float
      int jp = IndexOfBytes(b, tPct, q);
      if (jp >= 0 && jp - q < 512) {
        int r4 = jp + tPct.Length;
        RdStr(b, ref r4);                        // "FloatProperty"
        r4 += 8;                                 // size + arrayIndex
        if (r4 + 4 <= b.Length) { g.Pct = BitConverter.ToSingle(b, r4); g.HasPct = true; }
      }
      res.Add(g);
      p = q;
    }
    return res;
  }

  // UE3 属性标签 = i32 长度(含 NUL) + 名字 + NUL
  static byte[] StrTag(string s) {
    byte[] r = new byte[4 + s.Length + 1];
    Buffer.BlockCopy(BitConverter.GetBytes(s.Length + 1), 0, r, 0, 4);
    for (int i = 0; i < s.Length; i++) r[4 + i] = (byte)s[i];
    return r;
  }

  // ================= 列表 =================
  void FillArrCombo() {
    cboGemArr.Items.Clear();
    for (int i = 0; i < gemArrSize.Length; i++) {
      int n = gemArrSize[i];
      string guess = (n <= 40 && HasCooked(i)) ? I18n.T("疑似背包") : (n > 40 ? I18n.T("疑似商店") : I18n.T("未知"));
      cboGemArr.Items.Add(I18n.T("数组 #") + i + "   " + n + I18n.T(" 条 · ") + guess);
    }
    int best = -1;
    for (int i = 0; i < gemArrSize.Length; i++) {
      if (gemArrSize[i] <= 40 && HasCooked(i) && (best < 0 || gemArrSize[i] > gemArrSize[best])) best = i;
    }
    if (best < 0 && gemArrSize.Length > 0) best = 0;
    if (best < 0) return;
    cboGemArr.SelectedIndex = best;
  }

  bool HasCooked(int arr) {
    foreach (GemRec r in gemRecs) if (r.ArrId == arr && r.Cook != 0) return true;
    return false;
  }

  void ShowArr() {
    int a = cboGemArr.SelectedIndex;
    lvGems.BeginUpdate();
    lvGems.Items.Clear();
    if (a < 0) { lvGems.EndUpdate(); return; }
    foreach (GemRec r in gemRecs) {
      if (r.ArrId != a) continue;
      ListViewItem it = new ListViewItem(r.Tpl);
      it.SubItems.Add(r.Tier.ToString());
      it.SubItems.Add(r.Cook.ToString());
      it.SubItems.Add(r.Pct.ToString("0.####"));
      long v = r.CurrentValue();
      it.SubItems.Add(v < 0 ? "?" : v.ToString());
      it.SubItems.Add("0x" + r.RecAddr.ToString("X"));
      it.SubItems.Add(r.Kind == GemTierKind.Unknown ? "—"
        : (r.Kind == GemTierKind.Additive ? I18n.T("Tier 0-255 / 数值") : "Tier 1-" + r.Tiers.Count + I18n.T(" / 数值")));
      it.Tag = r;
      lvGems.Items.Add(it);
    }
    lvGems.EndUpdate();
    OnGemSel();
  }

  GemRec SelGem() {
    if (lvGems.SelectedItems.Count == 0) return null;
    return lvGems.SelectedItems[0].Tag as GemRec;
  }

  void SetGemControls(bool on) {
    btnGemApply.Enabled = on;
    btnGemMax.Enabled = on;
    txtGemTarget.Enabled = on;
    cboGemField.Enabled = on;
  }

  void OnGemSel() {
    GemRec r = SelGem();
    if (r == null) { lblGemInfo.Text = I18n.T("选中一行后即可修改。"); return; }
    var sb = new StringBuilder();
    sb.Append(I18n.T("模板 ")).Append(r.Tpl)
      .Append(I18n.T("   名索引 0x")).Append(r.NameIdx.ToString("X"))
      .Append("   Number ").Append(r.Number)
      .Append(I18n.T("   记录 0x")).Append(r.RecAddr.ToString("X")).AppendLine();

    if (r.Kind == GemTierKind.Additive) {
      sb.Append(I18n.T("类型：加法型（RecipeBoostAmount=")).Append(r.Boost).Append(I18n.T("）　显示值 = "))
        .Append(r.Base).Append(" + Tier × ").Append(r.Boost).AppendLine();
      sb.Append(I18n.T("合法 Tier 0 ~ 255（uint8 上限）→ 显示值 ")).Append(r.Base)
        .Append(" ~ ").Append(r.ValueAt(255, r.Pct)).AppendLine();
    } else if (r.Kind == GemTierKind.Indexed) {
      sb.Append(I18n.T("类型：下标型（UpgradeTier ")).Append(r.Tiers.Count).Append(I18n.T(" 档）　显示值 = "))
        .Append(r.Base).Append(" × UpgradeTier[Tier-1] × (1+pct)").AppendLine();
      sb.Append(I18n.T("⚠ 合法 Tier 只有 ")).Append(r.TierMin).Append(" ~ ").Append(r.TierMax)
        .Append(I18n.T("；写超范围 = 越界读表 → 垃圾值/崩溃，本页已强制夹住。")).AppendLine();
      // 值算不出来的模板（基数不在公式库 / 档位表是小数值）要说清楚：本页只能改档位，
      // 显示值一栏是 "?" 而不是 0 —— 免得用户以为"这颗宝石就是 0"
      if (!r.ValueComputable)
        sb.Append(I18n.T("⚠ 该模板的显示值算不出来（")).Append(r.MaxUnknownWhy(true))
          .Append(I18n.T("）→ 只能按 Tier 改档位。")).AppendLine();
    } else {
      sb.Append(I18n.T("类型：未知（无 RecipeBoostAmount 也无 UpgradeTier）→ 本页拒绝写入，避免写坏。")).AppendLine();
    }
    sb.Append(I18n.T("改完必须回游戏切一次场景（进/出熔接室）才会写进存档。"));
    lblGemInfo.Text = sb.ToString();
    cboGemField.Enabled = r.CanEditValue;
  }

  // ============ 写入前的地址复核（2026-10-07 新增）============
  // 训练器是「读一次 → 记住地址 → 之后写」。而游戏会把宝石数组**重新分配**：
  //   本次实测：真身 +0x1FEC 的背包 Data 从 0x7FF4EA0814E0 挪到了 0x7FF4F8202500，
  //   旧地址那块被回收复用（开头变成了别处的指针）。此时照旧写旧地址的后果是
  //   **回读校验全过、游戏里毫无变化** —— 2026-10-07 的 14:06 与本次实测各踩了一次。
  // 判别权威数组的办法（也是本次用来定案的）：往候选记录的惰性字段 Boost(+0x14) 写不同标记，
  //   再 setsave 0 看哪个标记进了明文 —— 内存里同内容拷贝很多，只有一份会被序列化。
  // 规则：某组若与当前活数组「同条数 且 逐条四字段一致」，就按同下标搬到活地址；
  //   对不上就整组丢弃；连选中的那颗都丢了就拒绝写入（宁可不动，也不乱写）。
  bool RemapCopiesLive(IntPtr h, List<GemRec> copies, GemRec sel, out string note) {
    note = null;
    List<GemRec> bag, shop; int bagC, shopC; string ls;
    if (!LocateByIdentity(h, out bag, out shop, out bagC, out shopC, out ls)) {
      note = I18n.T("重新定位当前活数组失败（") + ls + I18n.T("）");
      return false;
    }
    // 每个组先判一次「是不是当前活数组的版本」，是就整组搬到活地址
    var groupLive = new Dictionary<int, long>();
    foreach (GemRec c in copies) {
      if (groupLive.ContainsKey(c.ArrId)) continue;
      List<GemRec> g = GroupOf(c.ArrId);
      long found = 0;
      if (SameGroup(g, bag)) found = bag[0].RecAddr;
      else if (SameGroup(g, shop)) found = shop[0].RecAddr;
      groupLive[c.ArrId] = found;
    }
    foreach (KeyValuePair<int, long> kv in groupLive) {
      if (kv.Value == 0) continue;
      List<GemRec> g = GroupOf(kv.Key);
      if (g.Count == 0) continue;
      long old0 = g[0].RecAddr;
      if (old0 == kv.Value) continue;
      for (int i = 0; i < g.Count; i++) g[i].RecAddr = kv.Value + i * GEM_REC;
      Log("地址复核：组#" + kv.Key + " 随游戏重分配搬移 0x" + old0.ToString("X") + " → 0x" +
          kv.Value.ToString("X") + "（" + g.Count + " 条）");
    }
    var keep = new List<GemRec>();
    int dead = 0;
    foreach (GemRec c in copies) {
      if (groupLive[c.ArrId] == 0) { dead++; continue; }
      keep.Add(c);
    }
    if (keep.Count == 0) {
      note = I18n.T("待写拷贝所属的数组已全部失效（") + dead + I18n.T(" 条）—— 请重新点「读取背包」后再试");
      return false;
    }
    if (dead > 0) Log("地址复核：丢弃 " + dead + " 条已失效拷贝");
    copies.Clear(); copies.AddRange(keep);
    if (sel != null && !copies.Contains(sel)) {
      note = I18n.T("选中的那颗宝石所在数组已失效（游戏重分配过）—— 请重新点「读取背包」后再试");
      return false;
    }
    return true;
  }

  // 取某个 ArrId 组的全部记录（顺序 = 数组内顺序）
  List<GemRec> GroupOf(int arrId) {
    var g = new List<GemRec>();
    foreach (GemRec r in gemRecs) if (r.ArrId == arrId) g.Add(r);
    return g;
  }

  // 两组是否「同条数且逐条四字段一致」—— 用来判定某个组是不是当前活数组的旧地址版本
  static bool SameGroup(List<GemRec> a, List<GemRec> b) {
    if (a == null || b == null || a.Count == 0 || a.Count != b.Count) return false;
    for (int i = 0; i < a.Count; i++) {
      if (a[i].NameIdx != b[i].NameIdx || a[i].Number != b[i].Number) return false;
      if (a[i].Tier != b[i].Tier || a[i].Cook != b[i].Cook) return false;
      float d = a[i].Pct - b[i].Pct;
      if (d < 0) d = -d;
      if (d >= 0.002f) return false;
    }
    return true;
  }

  // ================= 应用 =================
  void ApplyGemEdit() {
    GemRec r = SelGem();
    if (r == null) { ToastMgr.Show(I18n.T("先在列表选中一颗宝石")); return; }
    if (!RequireH()) return;
    if (r.Kind == GemTierKind.Unknown) { ToastMgr.Warn(I18n.T("该宝石类型未知，拒绝写入")); return; }

    bool byValue = (cboGemField.SelectedIndex == 1);
    long target;
    if (!long.TryParse(txtGemTarget.Text.Trim(), out target)) { ToastMgr.Warn(I18n.T("目标值不是整数")); return; }

    int newTier; double newPct;
    if (byValue) {
      // ★ 显示值都算不出来的宝石（基数查不到 / 档位表是小数值），"显示数值"这条路根本无从谈起。
      //   旧版会一路走到 Solve 再报「目标 N 无法达成（受基数/增量/档位/pct 上限约束）」——
      //   把"本页算不出这颗的值"说成了"你选的数不行"，用户只会反复试别的数。改为如实说明，
      //   并指一条走得通的路：改用 Tier 模式直接改档位。
      if (!r.ValueComputable) {
        ToastMgr.Warn(I18n.T("这颗宝石的显示值算不出来（") + r.MaxUnknownWhy(true) +
                      I18n.T("）—— 请把「修改项」改成 Tier 来改档位"));
        return;
      }
      if (!r.Solve(target, out newTier, out newPct)) {
        ToastMgr.Warn(I18n.T("目标 ") + target + I18n.T(" 无法达成（受基数/增量/档位/pct 上限约束）"));
        return;
      }
    } else {
      if (target < r.TierMin || target > r.TierMax) {
        ToastMgr.Warn(I18n.T("Tier 必须在 ") + r.TierMin + " ~ " + r.TierMax + I18n.T("（该宝石类型限制）"));
        return;
      }
      newTier = (int)target; newPct = r.Pct;
    }

    // 所有“同内容拷贝”一起写：同名索引 / Number / 原 tier / 原 cook / 原 pct
    // ★ 必须限定在「与被改数组同样规模」的数组里：商店里有一堆同名同状态的宝石
    //   （42 颗暗火里就有 7 颗 T0 cook0 pct0），不加这道闸会把商店一起改掉。
    var copies = new List<GemRec>();
    foreach (GemRec o in gemRecs) {
      if (o.NameIdx != r.NameIdx || o.Number != r.Number || o.Tier != r.Tier) continue;
      if (o.Cook != r.Cook || Math.Abs(o.Pct - r.Pct) >= 1e-6) continue;
      if (o.ArrId >= gemArrSize.Length || r.ArrId >= gemArrSize.Length) continue;
      if (gemArrSize[o.ArrId] != gemArrSize[r.ArrId]) continue;
      copies.Add(o);
    }
    if (copies.Count == 0) copies.Add(r);

    IntPtr h = H;

    // ★ 写前复核地址还活着：读取与写入之间游戏可能把数组重新分配（本次实测发生过：
    //   背包 Data 从 0x7FF4EA0814E0 挪到 0x7FF4F8202500）。照旧写旧地址会
    //   「回读校验通过、游戏里毫无变化」。核不上就拒绝写，绝不留一个假的成功。
    string remapNote;
    if (!RemapCopiesLive(h, copies, r, out remapNote)) {
      // 提示语一律由 remapNote 自带：它已经知道是"游戏界面不对（数组没装载）"还是"地址失效"，
      // 旧版在这里硬接一句"请重新点「读取背包」"，对前者是**改不动**的空话（问题在游戏那侧）。
      ToastMgr.Warn(I18n.T("写入取消：") + remapNote);
      Log("宝石写入取消（地址复核未过）：" + remapNote);
      return;
    }

    int okTier = 0, okPct = 0, okCook = 0; string lastErr = "";
    foreach (GemRec c in copies) {
      string err;
      // ★ 单字节写：写 Int32 会清掉 +0x09 的 CookedGemVar
      if (MemIO.SafeWrite(h, c.TierAddr, new byte[] { (byte)newTier }, out err)) okTier++;
      else lastErr = err;
      // ★★ 2026-10-07 实测定案：**加法型宝石的 GemTier 只在 CookedGemVar=50（进过熔炉）时才被游戏承认**。
      //   同一数组、同一毫秒、同一次切场景的对照实验：cook=50 那颗 tier 保留，cook=0 那颗被归零。
      //   所以改加法型的 tier 必须**同时把 cook 置 50**，否则切场景时会被游戏归一化掉，
      //   表现为「日志报成功、游戏里毫无变化」。下标型（UpgradeTier[]）不受此限，tier 是档位下标。
      if (r.Kind == GemTierKind.Additive && newTier > 0 && c.Cook != 50) {
        if (MemIO.SafeWrite(h, c.CookAddr, new byte[] { 50 }, out err)) okCook++;
        else lastErr = err;
      }
      if (r.Kind == GemTierKind.Indexed && Math.Abs(newPct - c.Pct) > 1e-6) {
        if (MemIO.SafeWrite(h, c.PctAddr, BitConverter.GetBytes((float)newPct), out err)) okPct++;
        else lastErr = err;
      }
    }

    // ★ 写后回读校验：把「实际落地的字节」报出来，别再出现"以为写了 6、实际写了别的"
    var rb = new byte[1];
    int verified = 0; var seen = new StringBuilder();
    foreach (GemRec c in copies) {
      string e2;
      if (MemIO.ReadBytes(h, c.TierAddr, rb, out e2)) {
        if (rb[0] == (byte)newTier) verified++;
        if (seen.Length < 200) seen.Append(" 0x").Append(c.TierAddr.ToString("X")).Append("→").Append(rb[0]);
      }
    }
    Log("回读校验 " + verified + "/" + copies.Count + " 条落地，实际字节:" + seen);

    long preview = r.ValueAt(newTier, newPct);
    if (okTier == copies.Count && verified == copies.Count) {
      BookPut("gem." + r.Tpl + ".tier", r.Tpl + " Tier", r.TierAddr, ScanType.I8, newTier.ToString(), "gemscan");
      ToastMgr.Show(I18n.T("已改 ") + r.Tpl + " → Tier " + newTier +
                    (preview >= 0 ? (I18n.T("（显示值 ") + preview + I18n.T("）")) : "") +
                    I18n.T("，写 ") + okTier + I18n.T(" 份拷贝") + (okCook > 0 ? (I18n.T(" + 融合标记×") + okCook) : "") +
                    (okPct > 0 ? (" + pct×" + okPct) : "") + I18n.T("  ★ 界面会立刻变；存进存档请切一次场景"));
      // ★ 实测教训（2026-10-07）：改内存后**必须立刻在游戏里切一次场景**才会写进 Cloud 真档。
      //   中间若先去游戏里打开宝石界面，该数组会被卸载重载，这次改动就白写了
      //   （训练器那次就是这么丢的：日志报"回读校验 2/2 落地"、游戏里毫无变化）。
      Log("宝石写入: " + r.Tpl + " 0x" + r.TierAddr.ToString("X") + " Tier " + r.Tier + "→" + newTier +
          (okCook > 0 ? (" + CookedGemVar→50×" + okCook + "（加法型必须有它才被游戏承认）") : "") +
          (okPct > 0 ? (" pct→" + newPct.ToString("0.####")) : "") + " 拷贝 " + okTier + "/" + copies.Count +
          "｜★ 内存已改、游戏界面会**立刻**反映（该数组是 UI 直接读的那份）。" +
          "要**存进存档**有两条路：① 切一次场景立即落盘；② 等游戏约 50s 的自动存档。" +
          "两者都必须满足：中途**别打开宝石界面** —— 那会让该数组卸载重载，未落盘的改动被冲掉" +
          "（2026-10-07 实测：训练器那次写入就是这么丢的）");
      RefreshGems();
    } else {
      ToastMgr.Warn(I18n.T("写入不完整（") + okTier + "/" + copies.Count + I18n.T("）：") + lastErr);
    }
  }
}

} // namespace
