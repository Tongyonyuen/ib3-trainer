// ============================================================================
// Tabs.Potions.cs — 魔法剂（官方中文）页签
//
// 背景：IB3 的"魔法剂"**不是独立模板**，而是**宝石模板的消耗品形态** ——
//   `SwordInventoryItemGem` 只要带 `PotionType` 就能被造成药剂。
//   发放原语 = `SwordPlayer.GetFixedPotion(ForceDropItemName:Str)`（本项目给它补了 exec 位），
//   控制台形式 **`getfixedpotion <模板名> [档位]`**，**直接入袋**（append 进存档 InActivePotionList），
//   不用买。详见 docs\宝石研究\04_药水（魔法剂）体系与发放.md。
//
// 三条必须记住的边界（都实测过）：
//   ① **不带 `PotionType` 的模板静默失败** —— 游戏不报错、训练器也感知不到
//      （EngineCall.Execute 只反映"命令被执行"，不反映游戏内部是否真入袋）。
//      所以模板表**只列 ib3_potions.csv 里那 90 个**，绝不提供其它模板。
//   ② **数值公式**：`显示值 = 档位表值 × (1 + RandomAddPct) × 10`（那句 ×10 是
//      `SwordPlayer.GemToPotionValueScale`，药水专属）。`getfixedpotion` 只能给档位，
//      发出来 RandomAddPct 恒为 0，所以本页发出的都是档位表原值。
//   ③ **喝掉之后**：InActivePotionList 里那条的 GemName 会被置成 None（墓碑，存档时才压缩），
//      同时复制进 ActivePotions（= 本场生效中）。
//
// ---------------------------------------------------------------------------
// 本页两个模式（共用一个列表区 —— 一页放不下两张表）：
//   「模板表」  ：分组下拉 + 搜索 → 筛选出可发放模板 → 选档位 → 发放（无感注入）
//   「我的药剂」：读内存 InActivePotionList（药水实例）→ 列出模板名/档位/随机加成/显示值
//                → 选中一条，改「档位」或「随机加成」→ 应用
//
// ★★ 核心未知量：药水数组在真身上的偏移 —— **代码自己探出来，不写死** ★★
//   已知：PlayerUnequippedGems = rb+0x1FEC、CurrentStoreGems = rb+0x1FFC（相隔正好 16 字节）。
//   做法：读存档明文 body 取 GemsFromBody(body,"InActivePotionList",0) 当指纹，
//         对候选偏移 rb + 0x1FEC + 16*k（k=2..13，即 +0x200C…+0x20BC）逐个 ReadGemArray，
//         再用 ZipEquals(内存组, 存档指纹) 认领 —— 整段逐条相等，几乎不可能凑巧。
//         命中者即真，并把偏移写进日志；全不中则如实拒绝（提示先切场景落盘）。
//
// C# 5（csc v4.0.30319）：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  // ---- 顶栏 ----
  Button btnPotTplMode, btnPotMineMode;
  ComboBox cboPotGroup;
  TextBox txtPotSearch;
  ListView lvPotion;

  // ---- 底部卡片（两套控件，随模式切显隐）----
  FlatGroupBox potCard;
  Label lblPotTierCap, lblPotFieldCap, lblPotTargetCap;
  NumericUpDown numPotTier;
  Button btnPotGrant;
  Label lblPotTplInfo;
  ComboBox cboPotField;
  TextBox txtPotTarget;
  Button btnPotApply;
  Label lblPotMineInfo;
  Label lblPotHint1, lblPotHint2;

  // ---- 模式与数据 ----
  bool potTemplateMode = true;
  readonly List<PotionRow> potShownTpl = new List<PotionRow>();          // 当前筛选出的模板
  readonly Dictionary<string, PotionRow> potTplLookup = new Dictionary<string, PotionRow>(); // 小写模板名 → 行
  List<GemRec> potionRecs = new List<GemRec>();                        // 内存里的药剂实例（快照）
  List<SaveGem> potionFp = null;                                       // 认领时用的存档指纹
  int potionOffsetK = 0;                                               // 命中的候选步数：头部 = 真身 + 0x1FEC + 16*k

  // 本页全部文案定义为常量，**I18n.cs 直接用这些常量当字典键**（避免两处手抄中文出错）。
  public const string T_Tab = "魔法剂";
  public const string T_TplMode = "模板表";
  public const string T_MineMode = "我的药剂";
  public const string T_AllGroups = "全部分组";
  public const string T_SearchCue = "搜索模板 / 中文名 / 效果";
  public const string T_Summary = "效果摘要";
  // 这一列放的是**换算后的显示数值**（原始档位表要 ×10 才是游戏里看到的值），
  // 标题写「各档显示值」而不是「档位表」，免得被当成原始档位读（实测被误读过一次）。
  public const string T_TierTable = "各档显示值";
  public const string T_ColValue = "当前显示值";
  public const string T_RandPct = "随机加成";
  public const string T_EmptySlot = "（已饮用/空）";
  public const string T_GrantTitle = "发放魔法剂（按名直接入袋，不需要买）";
  public const string T_EditTitle = "修改我的药剂（改完请在游戏里切一次场景才会落盘）";
  public const string T_Tier = "档位";
  public const string T_Template = "模板";
  public const string T_Grant = "发放";
  public const string T_GrantDesc = "发放魔法剂 ";
  public const string T_Pick = "先在列表里选中一个模板";
  public const string T_TplHint = "在列表里选一个模板（可用上方分组与搜索缩小范围）；选好后设档位再点「发放」";
  public const string T_ReadList = "读取药剂清单";
  public const string T_HintTpl1 =
    "只列出**能作为药剂发放**的模板（带 PotionType 的 90 个）—— 未列出的模板发了也不会进袋子，游戏静默忽略。" +
    "档位 0 = 模板基值，≥1 = 查档位表。";
  public const string T_HintTpl2 =
    "清单容量有限（默认 10，可由技能抬高，本机实测 30）—— 满了之后发放同样静默失败，先在战斗里用掉几瓶。" +
    "发放 = 无感注入 getfixedpotion <模板名> <档位>，发完切一次场景即落盘。";
  public const string T_HintMine1 =
    "「我的药剂」= 存档 InActivePotionList 里的药剂实例（结构同宝石记录）。定位靠「真身 + 固定偏移」探出数组、" +
    "再与存档逐条比对认领；探不到就如实拒绝，绝不瞎写。";
  public const string T_HintMine2 =
    "显示值 = 档位表值 ×(1+随机加成) ×10。改完界面立刻变；要写进存档请切一次场景，" +
    "中途别打开背包查看（会让数组重载、改动被冲掉）。";

  // 药水数组的候选偏移：真身 + 0x1FEC（背包）之后每 16 字节一个 TArray 头。
  // k=2 → +0x200C … k=13 → +0x20BC（共 12 个候选；+0x1FFC 已是商店，故从 2 起）。
  const int POT_ARR_STRIDE = 16;
  const int POT_K_FIRST = 2;
  const int POT_K_LAST = 13;
  const int BODY_GEM_BAG_OFF = 0x1FEC;      // PlayerUnequippedGems

  // ================= 页签 =================
  TabPage BuildTabPotions() {
    TabPage p = new TabPage(T_Tab);
    p.BackColor = Theme.BG;
    p.ForeColor = Theme.Text;

    // ---------- 顶栏：模式按钮 + 分组 + 搜索 ----------
    btnPotTplMode = Theme.MkButton(T_TplMode, 14, 8, 84, 26, delegate { SetPotMode(true); });
    p.Controls.Add(btnPotTplMode);
    btnPotMineMode = Theme.MkButton(T_MineMode, 104, 8, 92, 26, delegate { SetPotMode(false); });
    p.Controls.Add(btnPotMineMode);

    cboPotGroup = new ComboBox();
    cboPotGroup.SetBounds(212, 10, 150, 23);
    Theme.StyleCombo(cboPotGroup);
    // 分组名是**封闭集合**（4 个），下拉自绘时过 T() 翻；这里只填中文原文。
    cboPotGroup.Items.Add(T_AllGroups);
    cboPotGroup.Items.Add(PotionDb.GroupName(PotionDb.GROUP_RETRIG));
    cboPotGroup.Items.Add(PotionDb.GroupName(PotionDb.GROUP_SPECIAL));
    cboPotGroup.Items.Add(PotionDb.GroupName(PotionDb.GROUP_ELEMENTAL));
    cboPotGroup.Items.Add(PotionDb.GroupName(PotionDb.GROUP_OTHER));
    cboPotGroup.SelectedIndex = 0;
    cboPotGroup.SelectedIndexChanged += delegate { FillPotionTplList(); };
    p.Controls.Add(cboPotGroup);

    txtPotSearch = Theme.MkText(372, 10, 200, "");
    Theme.SetCue(txtPotSearch, T_SearchCue);
    txtPotSearch.TextChanged += delegate { FillPotionTplList(); };
    p.Controls.Add(txtPotSearch);

    // ---------- 列表（两个模式共用） ----------
    lvPotion = new DarkListView();
    Theme.ApplyDarkScroll(lvPotion);
    lvPotion.SetBounds(14, 42, 788, 240);   // 下沿 282
    Theme.StyleList(lvPotion);
    lvPotion.MultiSelect = false;
    lvPotion.SelectedIndexChanged += delegate { RenderPotSelInfo(); };
    p.Controls.Add(lvPotion);

    // ---------- 底部卡片 ----------
    potCard = new FlatGroupBox();
    potCard.SetBounds(8, 290, 810, 110);    // 下沿 400
    potCard.Fill = Theme.CardGold;

    // 模板表模式：档位 + 发放
    lblPotTierCap = Theme.MkLabel(T_Tier, 16, 30, 40);
    potCard.Controls.Add(lblPotTierCap);
    numPotTier = new NumericUpDown();
    numPotTier.SetBounds(60, 27, 60, 23);
    numPotTier.Minimum = 0; numPotTier.Maximum = 255; numPotTier.Value = 0;
    numPotTier.BackColor = Theme.PanelLight; numPotTier.ForeColor = Theme.Text;
    numPotTier.ValueChanged += delegate { RenderPotSelInfo(); };
    potCard.Controls.Add(numPotTier);
    btnPotGrant = Theme.MkButton(T_Grant, 134, 25, 110, 28, delegate { GrantPotion(); });
    potCard.Controls.Add(btnPotGrant);
    lblPotTplInfo = Theme.MkLabel("", 16, 60, 766);
    lblPotTplInfo.AutoSize = false;
    lblPotTplInfo.SetBounds(16, 60, 766, 44);
    potCard.Controls.Add(lblPotTplInfo);

    // 我的药剂模式：修改项 + 目标值 + 应用
    lblPotFieldCap = Theme.MkLabel("修改项", 16, 30, 52);
    potCard.Controls.Add(lblPotFieldCap);
    cboPotField = new ComboBox();
    cboPotField.SetBounds(70, 27, 112, 23);
    Theme.StyleCombo(cboPotField);
    cboPotField.Items.Add(T_Tier);        // 索引 0 = 档位
    cboPotField.Items.Add(T_RandPct);     // 索引 1 = 随机加成
    cboPotField.SelectedIndex = 0;
    potCard.Controls.Add(cboPotField);
    lblPotTargetCap = Theme.MkLabel("目标值", 198, 30, 52);
    potCard.Controls.Add(lblPotTargetCap);
    txtPotTarget = Theme.MkText(252, 27, 120, "");
    potCard.Controls.Add(txtPotTarget);
    btnPotApply = Theme.MkButton("应用", 382, 25, 90, 28, delegate { ApplyPotionEdit(); });
    potCard.Controls.Add(btnPotApply);
    lblPotMineInfo = Theme.MkLabel("", 16, 60, 766);
    lblPotMineInfo.AutoSize = false;
    lblPotMineInfo.SetBounds(16, 60, 766, 44);
    potCard.Controls.Add(lblPotMineInfo);

    p.Controls.Add(potCard);

    // ---------- 要点提示（2 行） ----------
    lblPotHint1 = Theme.MkHint(T_HintTpl1, 16, 406, 780);
    lblPotHint1.AutoSize = false; lblPotHint1.SetBounds(16, 406, 780, 22);
    p.Controls.Add(lblPotHint1);
    lblPotHint2 = Theme.MkHint(T_HintTpl2, 16, 430, 780);
    lblPotHint2.AutoSize = false; lblPotHint2.SetBounds(16, 430, 780, 22);
    p.Controls.Add(lblPotHint2);

    // 语言切换时把"按模式换过 / 拼接出来"的文案与列表重贴一遍
    // （Retranslate 的 setter 护栏会跳过被业务逻辑改过文本的标签，得自己补一条重画）。
    I18n.OnLang("potions.page", delegate { RenderPotPage(); });

    // ⚠ 页面是在数据加载之前建的（Ib3Trainer2.cs 的 TabPages.Add 顺序），所以这里只摆好控件，
    //   模板表要等 `Potions.Load(ExeDir)` 之后由 `RefreshPotionCombo()` 再填一次。
    SetPotMode(true);
    return p;
  }

  // ================= 模式切换 =================
  // 共用同一个列表区：模板表 ↔ 我的药剂。切到「我的药剂」会自动读一次内存清单。
  void SetPotMode(bool templateMode) {
    potTemplateMode = templateMode;
    if (cboPotGroup != null) cboPotGroup.Visible = templateMode;
    if (txtPotSearch != null) txtPotSearch.Visible = templateMode;
    // 当前模式用金色标出来（RButton 是自绘，改 ForeColor 即可）
    if (btnPotTplMode != null) btnPotTplMode.ForeColor = templateMode ? Theme.Gold : Theme.TextDim;
    if (btnPotMineMode != null) btnPotMineMode.ForeColor = templateMode ? Theme.TextDim : Theme.Gold;

    // ⚠ 两个模式的列宽必须一致：Layout.cs 的 ApplySpecials 按"构造时采集的列宽前缀和"缩放，
    //   运行期改列宽不加进 _design，就会在下次布局时被覆盖回去。所以两套列共用同一组宽度。
    if (lvPotion != null) {
      lvPotion.BeginUpdate();
      // ★★ 列**只建一次**，切模式只改列文字、**绝不 Clear()+Add()**。
      //   DarkListView 的 `HDF_OWNERDRAW` 只在 OnHandleCreated 时按"当时的列数"打一遍标记
      //   （Theme.cs:324-335）。运行期重建出来的新列拿不到标记，系统就用**默认白底**画它的标题行
      //   —— 实测就是这个现象（表格最右列标题行发白）。改文字不影响标记。
      if (lvPotion.Columns.Count == 0) {
        lvPotion.Columns.Add("模板名", 200);
        lvPotion.Columns.Add("中文名", 150);
        lvPotion.Columns.Add(T_Summary, 240);
        lvPotion.Columns.Add(T_TierTable, 198);
      }
      // 列宽两模式共用同一组（见下面 Layout 的说明），这里只换文字
      string[] heads = templateMode
        ? new string[] { "模板名", "中文名", T_Summary, T_TierTable }
        : new string[] { "模板名", T_Tier, T_RandPct, T_ColValue };
      for (int i = 0; i < heads.Length && i < lvPotion.Columns.Count; i++) {
        lvPotion.Columns[i].Text = heads[i];      // 标题绘制时会过 I18n.T，这里存中文原文
      }
      lvPotion.Items.Clear();
      lvPotion.EndUpdate();
    }

    // 卡片内容与标题
    if (potCard != null) {
      potCard.Title = templateMode ? T_GrantTitle : T_EditTitle;
      potCard.Invalidate();
    }
    // 模板表模式的控件
    if (lblPotTierCap != null) lblPotTierCap.Visible = templateMode;
    if (numPotTier != null) numPotTier.Visible = templateMode;
    if (btnPotGrant != null) btnPotGrant.Visible = templateMode;
    if (lblPotTplInfo != null) lblPotTplInfo.Visible = templateMode;
    // 我的药剂模式的控件
    bool mine = !templateMode;
    if (lblPotFieldCap != null) lblPotFieldCap.Visible = mine;
    if (cboPotField != null) cboPotField.Visible = mine;
    if (lblPotTargetCap != null) lblPotTargetCap.Visible = mine;
    if (txtPotTarget != null) txtPotTarget.Visible = mine;
    if (btnPotApply != null) btnPotApply.Visible = mine;
    if (lblPotMineInfo != null) lblPotMineInfo.Visible = mine;

    if (templateMode) {
      FillPotionTplList();
    } else {
      FillPotionMineList();
      StartRefreshPotions();      // 每次切进来都重读一次，保证快照是最新的
    }
    RenderPotHints();
    RenderPotSelInfo();
  }

  // 语言切换时重贴本页"按模式变过 / 拼接出来"的文案与行
  void RenderPotPage() {
    RenderPotHints();
    if (potTemplateMode) FillPotionTplList();
    else FillPotionMineList();
    RenderPotSelInfo();
  }

  void RenderPotHints() {
    if (lblPotHint1 == null || lblPotHint2 == null) return;
    lblPotHint1.Text = I18n.T(potTemplateMode ? T_HintTpl1 : T_HintMine1);
    lblPotHint2.Text = I18n.T(potTemplateMode ? T_HintTpl2 : T_HintMine2);
  }

  // ================= 数据表加载（`Potions.Load` 之后由 Ib3Trainer2.cs 调用）=================
  // ★ 方法名保持 RefreshPotionCombo（调用点在 Ib3Trainer2.cs，不能改）—— 现在做的是
  //   「建模板名索引 + 填模板表」，不再有下拉。
  void RefreshPotionCombo() {
    potTplLookup.Clear();
    for (int i = 0; i < Potions.Rows.Count; i++)
      potTplLookup[Potions.Rows[i].Tpl.ToLowerInvariant()] = Potions.Rows[i];
    FillPotionTplList();
    if (Potions.Rows.Count == 0 && lblPotTplInfo != null) {
      lblPotTplInfo.ForeColor = Theme.Warn;
      lblPotTplInfo.Text = "⚠ 没读到 ib3_potions.csv —— 该文件必须与 IB3训练器2.exe 放在同一目录。";
    }
  }

  // ================= 模板表：筛选 + 搜索 =================
  void FillPotionTplList() {
    if (!potTemplateMode || lvPotion == null || cboPotGroup == null) return;
    int wantGroup = cboPotGroup.SelectedIndex;             // 0 = 全部分组；1..4 = 分组号
    string q = txtPotSearch == null ? "" : txtPotSearch.Text.Trim().ToLowerInvariant();

    potShownTpl.Clear();
    lvPotion.BeginUpdate();
    lvPotion.Items.Clear();
    for (int i = 0; i < Potions.Rows.Count; i++) {
      PotionRow r = Potions.Rows[i];
      if (wantGroup != 0 && r.Group != wantGroup) continue;
      if (q.Length > 0) {
        bool hit = r.Tpl.ToLowerInvariant().IndexOf(q) >= 0
                || r.Cn.ToLowerInvariant().IndexOf(q) >= 0
                || r.Summary.ToLowerInvariant().IndexOf(q) >= 0;
        if (!hit) continue;
      }
      potShownTpl.Add(r);
      ListViewItem it = new ListViewItem(r.Tpl);           // 数据列：不翻（与「物品发放」页一致）
      it.SubItems.Add(r.Cn);
      it.SubItems.Add(r.Summary);
      it.SubItems.Add(Potions.TierValueText(r));    // 换算后的各档显示值（不是原始档位表）
      lvPotion.Items.Add(it);
    }
    lvPotion.EndUpdate();
  }

  // ================= 我的药剂：读内存 =================
  // 探偏移 + ZipEquals 认领。找不到就返回 false 并给出可照做的原因。
  bool LocatePotionArray(IntPtr h, out int kHit, out List<GemRec> recs,
                         out List<SaveGem> fp, out int slotHit, out string note) {
    kHit = 0; recs = null; fp = null; slotHit = -1; note = null;

    long rb; string rbNote;
    if (!RealBodyOk(h, out rb, out rbNote)) { note = "真身没定位到：" + rbNote; return false; }

    // ⓪ ★ 先试**上次命中的那个偏移**。
    //   为什么需要这条快路：探测的判据是"与存档逐条吻合"，而我们**改过 pct/档位之后内存已经
    //   和未落盘的存档不一致**了 —— 只走探测的话，用户每次改完想重新读都得先切场景落盘，
    //   实测就是这么被卡的。TArray 的**头**在真身上的偏移是固定的（游戏重分配只改头里的
    //   Data 指针），所以按记住的 k 重读头即可；再用内存快照 `SameGroup` 确认还是那一份。
    if (potionOffsetK >= POT_K_FIRST && potionOffsetK <= POT_K_LAST
        && potionRecs != null && potionRecs.Count > 0) {
      int off0 = BODY_GEM_BAG_OFF + POT_ARR_STRIDE * potionOffsetK;
      List<GemRec> g0; int c0; string e0;
      if (ReadGemArray(h, rb + off0, out g0, out c0, out e0) && c0 > 0 && SameGroup(g0, potionRecs)) {
        if (potionFp != null) NameByZip(g0, potionFp);      // 命中即按位对齐贴模板名
        kHit = potionOffsetK; recs = g0; fp = potionFp; slotHit = -1;
        Log("药水数组用上次命中的偏移 +0x" + off0.ToString("X") + " 重读（与内存快照一致，" + c0 + " 条）");
        return true;
      }
    }

    // ① 存档侧取指纹：0/1/2 号槽里 InActivePotionList 非空的那些
    var sbSave = new StringBuilder();
    var slots = new List<int>();
    var fps = new List<SaveGem>[3];
    for (int s = 0; s <= 2; s++) {
      string err;
      byte[] body = ReadSaveBody(s, out err);
      if (body == null) { sbSave.Append("槽").Append(s).Append('=').Append(err).Append("；"); continue; }
      List<SaveGem> f = GemsFromBody(body, "InActivePotionList", 0);
      fps[s] = f;
      sbSave.Append("槽").Append(s).Append('=').Append(f.Count).Append(" 条；");
      if (f.Count > 0) slots.Add(s);
    }
    if (slots.Count == 0) {
      note = "0/1/2 号槽的存档里 InActivePotionList 都没有条目（" + sbSave.ToString().Trim() + "）——" +
             "请先在游戏里合成/掉落一瓶魔法剂让它进档，再切一次场景落盘后来读。";
      return false;
    }

    // ② 逐个候选偏移探：rb + 0x1FEC + 16*k
    var sb = new StringBuilder();
    for (int k = POT_K_FIRST; k <= POT_K_LAST; k++) {
      int off = BODY_GEM_BAG_OFF + POT_ARR_STRIDE * k;
      long hdr = rb + off;
      List<GemRec> g; int c; string e;
      if (!ReadGemArray(h, hdr, out g, out c, out e)) {
        sb.Append("[+0x").Append(off.ToString("X")).Append(" 读失败:").Append(e).Append("] ");
        continue;
      }
      if (c == 0) continue;                                  // 空数组，跳过（不当作命中）
      for (int si = 0; si < slots.Count; si++) {
        int s = slots[si];
        string why;
        if (ZipEquals(g, fps[s], out why)) {
          NameByZip(g, fps[s]);                              // 命中即按位对齐贴模板名
          recs = g; fp = fps[s]; kHit = k; slotHit = s;
          Log("药水数组偏移 = +0x" + off.ToString("X") + "（真身+0x" + off.ToString("X") + "，k=" + k + "）" +
              "｜与 " + s + " 号槽的 InActivePotionList 逐条吻合（" + g.Count + " 条）");
          return true;
        }
        sb.Append("[+0x").Append(off.ToString("X")).Append(" vs 槽").Append(s).Append(':').Append(why).Append("] ");
      }
    }
    note = "没找到 InActivePotionList 数组（试了 +0x200C…+0x20BC 共 " + (POT_K_LAST - POT_K_FIRST + 1) +
           " 个候选偏移）—— 请先在游戏里切一次场景落盘再读（内存里那份要能被存档认领）。探测留痕：" + sb.ToString();
    return false;
  }

  void StartRefreshPotions() {
    if (!RequireH()) { SetPotInfo(I18n.T("未附着游戏进程 — 先点「启动游戏」或「立即附着」")); return; }
    IntPtr h = H;
    RunBackground(I18n.T(T_ReadList), delegate {
      int k; List<GemRec> recs; List<SaveGem> fp; int slot; string note;
      bool ok = LocatePotionArray(h, out k, out recs, out fp, out slot, out note);
      if (!ok) {
        BeginInvoke((MethodInvoker)delegate {
          if (potTemplateMode) return;
          SetPotInfo("未读到药剂清单：" + note);
          SetPotEditEnabled(false);
        });
        return "药剂清单未读取：" + note;
      }
      BeginInvoke((MethodInvoker)delegate {
        potionRecs = recs; potionFp = fp; potionOffsetK = k;
        if (potTemplateMode) return;
        FillPotionMineList();
        SetPotEditEnabled(potionRecs.Count > 0);
        RenderPotSelInfo();
      });
      return "已读取药剂清单 " + recs.Count + " 条（数组头 = 真身+0x" +
             (BODY_GEM_BAG_OFF + POT_ARR_STRIDE * k).ToString("X") + "，命名用 " + slot + " 号槽的 InActivePotionList）";
    });
  }

  void FillPotionMineList() {
    if (potTemplateMode || lvPotion == null) return;
    lvPotion.BeginUpdate();
    lvPotion.Items.Clear();
    for (int i = 0; i < potionRecs.Count; i++) {
      GemRec r = potionRecs[i];
      ListViewItem it;
      if (r.NameIdx <= 0) {                                  // 已饮用的墓碑（GemName=None）
        it = new ListViewItem(I18n.T(T_EmptySlot));
        it.SubItems.Add("—"); it.SubItems.Add("—"); it.SubItems.Add("—");
      } else {
        long v = PotionValue(r.Tpl, r.Tier, r.Pct);
        it = new ListViewItem(r.Tpl);                        // 数据列：不翻
        it.SubItems.Add(r.Tier.ToString());
        it.SubItems.Add(PctText(r.Pct));
        it.SubItems.Add(v < 0 ? "?" : v.ToString("N0"));
      }
      lvPotion.Items.Add(it);
    }
    lvPotion.EndUpdate();
  }

  // 显示值 = 档位表值 ×(1+pct) ×10（PotionDb.ValueAt 已含 ×10）。
  // 档位 0 / 超出档位表 / 模板不在 ib3_potions.csv 里 → -1（界面显示 "?"）。
  long PotionValue(string tpl, int tier, double pct) {
    if (tier < 1) return -1;
    PotionRow row;
    if (!potTplLookup.TryGetValue(tpl.ToLowerInvariant(), out row)) return -1;
    if (tier > Potions.MaxTier(row)) return -1;
    return Potions.ValueAt(row, tier, pct);
  }

  static string PctText(double p) { return (p * 100.0).ToString("0.#") + "%"; }

  void SetPotEditEnabled(bool en) {
    if (cboPotField != null) cboPotField.Enabled = en;
    if (txtPotTarget != null) txtPotTarget.Enabled = en;
    if (btnPotApply != null) btnPotApply.Enabled = en;
  }

  // ================= 选择 / 信息行 =================
  PotionRow SelPotTpl() {
    if (lvPotion == null || lvPotion.SelectedIndices.Count == 0) return null;
    int i = lvPotion.SelectedIndices[0];
    if (i < 0 || i >= potShownTpl.Count) return null;
    return potShownTpl[i];
  }

  int SelPotMineIdx() {
    if (lvPotion == null || lvPotion.SelectedIndices.Count == 0) return -1;
    int i = lvPotion.SelectedIndices[0];
    if (i < 0 || i >= potionRecs.Count) return -1;
    return i;
  }

  void SetPotInfo(string s) {
    if (lblPotMineInfo == null) return;
    lblPotMineInfo.Text = s;
  }

  void RenderPotSelInfo() {
    if (potTemplateMode) {
      if (lblPotTplInfo == null || numPotTier == null) return;
      PotionRow r = SelPotTpl();
      if (r == null) { lblPotTplInfo.Text = I18n.T(T_TplHint); return; }
      int tier = (int)numPotTier.Value;
      int maxT = Potions.MaxTier(r);
      string txt = r.Summary;
      if (tier == 0) txt += "　｜　档位 0：用模板基值";
      else if (maxT == 0) txt += "　｜　该模板没有档位表，档位对它无效（恒为基值）";
      else if (tier > maxT) txt += "　｜　⚠ 档位超出该模板档位表（共 " + maxT + " 档），会落回基值";
      else txt += "　｜　档位 " + tier + " → 显示数值约 " + Potions.ValueAt(r, tier, 0.0).ToString("N0");
      lblPotTplInfo.Text = txt;
    } else {
      if (lblPotMineInfo == null) return;
      int i = SelPotMineIdx();
      if (i < 0) {
        // 注意：不改动 txtPotTarget 原有内容 —— 用户可能已经自己填了一个值。
        lblPotMineInfo.Text = "已读取 " + potionRecs.Count + " 条 —— 选中一条后：" +
                              "修改项选「档位」或「随机加成」，填目标值再点「应用」。";
        return;
      }
      GemRec r = potionRecs[i];
      if (r.NameIdx <= 0) { lblPotMineInfo.Text = I18n.T("这一条是已饮用的空记录（GemName=None），没有可改的药剂"); return; }
      long v = PotionValue(r.Tpl, r.Tier, r.Pct);
      lblPotMineInfo.Text = "第 " + i + " 条：" + r.Tpl + "　｜　档位 " + r.Tier + "　｜　随机加成 " + PctText(r.Pct) +
                            "　｜　当前显示值 " + (v < 0 ? "?（档位 0 / 档位表未收录，按基值）" : v.ToString("N0"));
    }
  }

  // ================= 发放（模板表模式） =================
  void GrantPotion() {
    PotionRow r = SelPotTpl();
    if (r == null) { ToastMgr.Show(I18n.T(T_Pick)); return; }
    int tier = (int)numPotTier.Value;
    InjectCmd("getfixedpotion " + r.Tpl + " " + tier, I18n.T(T_GrantDesc) + r.Tpl);
    // ⚠ 不能在这里报"发放成功"：不带 PotionType 会静默失败（而这里全是带的）；但清单满 /
    //   不在游戏中同样静默失败 —— 交给 InjectCmd 的"已执行/执行失败"如实呈现。
  }

  // ================= 修改（我的药剂模式） =================
  // 写前复核：真身还在 + 数组仍是我们列出的那一份（先比内存快照，再退一步用存档指纹核）。
  bool VerifyPotionLive(IntPtr h, out List<GemRec> live, out string note) {
    live = null; note = null;
    long rb; string rbNote;
    if (!RealBodyOk(h, out rb, out rbNote)) { note = "真身已失效：" + rbNote; return false; }
    // TArray 的**头**在真身上的偏移固定（游戏重分配时动的只是头里的 Data 指针），所以按记住的 k 重读头即可。
    long hdr = rb + BODY_GEM_BAG_OFF + (long)POT_ARR_STRIDE * potionOffsetK;
    List<GemRec> g; int c; string e;
    if (!ReadGemArray(h, hdr, out g, out c, out e)) { note = "重读药剂数组失败：" + e; return false; }
    if (c == 0) { note = "药剂数组当前为空（Count=0）——玩家在关卡/战斗界面时该数组会被卸载，请回到藏身处界面再试"; return false; }
    if (SameGroup(g, potionRecs)) { live = g; return true; }     // 同一会话内改过 pct/tier 后仍成立
    string why = null;
    if (potionFp != null && ZipEquals(g, potionFp, out why)) { live = g; return true; }   // 退路：与存档指纹再核
    note = "药剂数组内容已变（与刚读取时对不上）——请切回「模板表」再切到「我的药剂」重新读取后再改" +
           (why == null ? "" : ("（" + why + "）"));
    return false;
  }

  void ApplyPotionEdit() {
    if (potTemplateMode) return;
    if (ScanBusy) { ToastMgr.Warn(I18n.T("正在读取药剂清单，请等它结束再点「应用」")); return; }
    int idx = SelPotMineIdx();
    if (idx < 0) { ToastMgr.Show(I18n.T("先在列表里选中一条药剂")); return; }
    GemRec sel0 = potionRecs[idx];
    if (sel0.NameIdx <= 0) { ToastMgr.Warn(I18n.T("这一条是已饮用的空记录（GemName=None），没有可改的药剂")); return; }

    int field = cboPotField == null ? 0 : cboPotField.SelectedIndex;   // 0=档位 1=随机加成
    string raw = txtPotTarget == null ? "" : txtPotTarget.Text.Trim();
    if (raw.Length == 0) { ToastMgr.Warn(I18n.T("请先填目标值")); return; }

    int newTier = sel0.Tier;
    double newPct = sel0.Pct;
    if (field == 0) {
      int t;
      if (!int.TryParse(raw, out t)) { ToastMgr.Warn(I18n.T("档位必须是整数")); return; }
      if (t < 0 || t > 255) { ToastMgr.Warn(I18n.T("档位必须在 0 ~ 255（GemTier 是单字节字段）")); return; }
      newTier = t;
    } else {
      // 接受 "0.25" 或 "25%"（百分比）。上限按 0~1 收 —— 不是"再大没有"，而是本页查不到
      // 模板的 MaxRandomAddPct，无法判断"多大算大"，与其瞎放不如收在常见区间。
      double p;
      bool pctSign = raw.EndsWith("%");
      string num = pctSign ? raw.Substring(0, raw.Length - 1).Trim() : raw;
      if (!double.TryParse(num, out p)) { ToastMgr.Warn(I18n.T("随机加成请输入数字（0~1 的小数，或 25%）")); return; }
      if (pctSign) p = p / 100.0;
      if (p < 0.0 || p > 1.0) { ToastMgr.Warn(I18n.T("随机加成请给 0 ~ 1 之间（0.25 = +25%）；过大会被游戏钳制 / 显示异常")); return; }
      newPct = p;
    }

    if (!RequireH()) return;
    IntPtr h = H;

    List<GemRec> live; string vnote;
    if (!VerifyPotionLive(h, out live, out vnote)) {
      SetPotInfo(I18n.T("写入取消：") + vnote);
      ToastMgr.Warn(I18n.T("写入取消：") + vnote);
      Log("药剂写入取消：" + vnote);
      return;
    }
    if (idx >= live.Count) { ToastMgr.Warn(I18n.T("选中的那条在活数组里找不到了——请重新读取药剂清单")); return; }
    GemRec tgt = live[idx];
    if (tgt.NameIdx <= 0) { ToastMgr.Warn(I18n.T("这一条是已饮用的空记录（GemName=None），没有可改的药剂")); return; }

    potionRecs = live;                       // 游戏可能重分配过 → 用活数组（含新地址）替换快照

    // ★ 只写这一个字段：**不要**照搬宝石页"加法型自动补 CookedGemVar=50"——药水不是加法型宝石。
    string err;
    bool okw;
    if (field == 0) okw = MemIO.SafeWrite(h, tgt.TierAddr, new byte[] { (byte)newTier }, out err);
    else okw = MemIO.SafeWrite(h, tgt.PctAddr, BitConverter.GetBytes((float)newPct), out err);
    if (!okw) {
      ToastMgr.Warn(I18n.T("写入失败：") + err);
      Log("药剂写入失败：" + tgt.Tpl + " — " + err);
      return;
    }

    // 回读校验（SafeWrite 已自带，这里再读一遍把实际落地字节写进日志）
    byte[] back = new byte[field == 0 ? 1 : 4];
    string e2;
    string seen = "?";
    if (MemIO.ReadBytes(h, field == 0 ? tgt.TierAddr : tgt.PctAddr, back, out e2)) {
      seen = field == 0 ? back[0].ToString() : BitConverter.ToSingle(back, 0).ToString("0.####");
    }

    // 同步内存快照 —— 下一次「应用」的 SameGroup 复核要靠它
    if (field == 0) potionRecs[idx].Tier = newTier;
    else potionRecs[idx].Pct = (float)newPct;

    FillPotionMineList();
    if (idx < lvPotion.Items.Count) {
      lvPotion.Items[idx].Selected = true;
      lvPotion.Items[idx].Focused = true;
      lvPotion.EnsureVisible(idx);
    }

    long shown = PotionValue(potionRecs[idx].Tpl, potionRecs[idx].Tier, potionRecs[idx].Pct);
    string what = field == 0 ? ("档位 → " + newTier) : ("随机加成 → " + PctText(newPct));
    ToastMgr.Show(I18n.T("已改 ") + tgt.Tpl + "：" + what +
                  (shown < 0 ? "" : ("（显示值 " + shown.ToString("N0") + "）")) +
                  I18n.T("　★ 界面会立刻变；存进存档请切一次场景"));
    Log("药剂写入：" + tgt.Tpl + " " + what + " @0x" +
        (field == 0 ? tgt.TierAddr : tgt.PctAddr).ToString("X") + "（回读 " + seen + "）" +
        "｜★ 内存已改、游戏界面会立刻反映；要存进存档请**切一次场景**（或等约 50s 自动存档），" +
        "中途别打开背包的魔法剂界面 —— 会让该数组重载、未落盘的改动被冲掉");
  }
}

} // namespace
