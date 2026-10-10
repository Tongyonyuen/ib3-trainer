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
//      所以下拉**只列 ib3_potions.csv 里那 90 个**，绝不提供其它模板。
//   ② **数值公式**：`显示值 = 档位表值 × (1 + RandomAddPct) × 10`（那句 ×10 是
//      `SwordPlayer.GemToPotionValueScale`，药水专属）。`getfixedpotion` 只能给档位，
//      发出来 RandomAddPct 恒为 0，所以本页发出的都是档位表原值。
//   ③ **喝掉之后**：InActivePotionList 里那条的 GemName 会被置成 None（墓碑，存档时才压缩），
//      同时复制进 ActivePotions（= 本场生效中）。
//
// C# 5（csc v4.0.30319）：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  ComboBox cboPotion;
  NumericUpDown numPotionTier;
  Label lblPotionInfo;

  // 本页全部文案定义为常量，**I18n.cs 直接用这些常量当字典键**（避免两处手抄中文出错）。
  public const string T_Tab = "魔法剂";
  public const string T_GrantTitle = "发放魔法剂（按名直接入袋，不需要买）";
  public const string T_Template = "模板";
  public const string T_Tier = "档位";
  public const string T_Grant = "发放";
  public const string T_Pick = "先在下拉里选一个模板";
  public const string T_GrantDesc = "发放魔法剂 ";
  public const string T_AboutTitle = "关于魔法剂（实测结论，省得踩坑）";
  public const string HintA = "只列出**能作为药剂发放**的模板（带 PotionType 的 90 个）。未列出的模板发了也不会进袋子 —— 游戏静默忽略。";
  public const string HintB = "档位 0 = 用模板基值；≥1 = 查档位表。发完切一次场景即落盘，背包里在「魔法剂」清单查看。";

  // 说明区整块文本（一个 i18n 条目）。**中文原文就是字典键**，见 I18n.cs 的 Build()。
  public const string NotesText =
    "① 元素攻击药剂 = 把元素宝石强制成药水形态。例：FireGem → 游戏里显示「FIRE ATTACK POTION」，\n" +
    "     数值 = 档位表值 ×(1+随机加成) ×10。档位 1 → 5×1×10 = 50；档位 5 → 200×1×10 = 2000。\n" +
    "② 全游戏只有 3 个模板带「按秒重复触发」药剂专属机制（体力回复 1.0s / 盾牌回复 2.5s /\n" +
    "     无限闪避 0.5s）—— 宝石没有这个机制，宝石只能靠招架/格挡/闪避等**事件**触发。\n" +
    "③ 其余药剂本质就是「宝石效果被搬进药剂形态」，触发器原样带过去。例：ParryGem_1（招架回血 25）\n" +
    "     当药剂灌下去 = 这场战斗内每次招架回血 25。\n" +
    "④ 图标由模板的 PotionType 决定（1~4 四种小瓶），它**不参与效果、不参与数值**。\n" +
    "⑤ 魔法剂清单有容量上限（MaxPotionCount 默认 10，可由技能抬高；本机实测 30）。满了之后\n" +
    "     发放同样**静默失败** —— 先在战斗里用掉几瓶再来发。\n" +
    "⑥ Uber 级元素宝石（UberFireGem / UberAttackGem 等）**没有 PotionType，发不出来**，\n" +
    "     所以它们不在上面的下拉里。";

  TabPage BuildTabPotions() {
    TabPage p = new TabPage(T_Tab);
    p.BackColor = Theme.BG;
    p.ForeColor = Theme.Text;

    // ---------- 发放 ----------
    FlatGroupBox g1 = new FlatGroupBox();
    g1.Title = T_GrantTitle;
    g1.SetBounds(8, 8, 810, 166);
    g1.Fill = Theme.CardGold;

    g1.Controls.Add(Theme.MkLabel(T_Template, 16, 34, 40));
    cboPotion = new ComboBox();
    cboPotion.SetBounds(60, 31, 500, 23);
    Theme.StyleCombo(cboPotion);
    cboPotion.DropDownWidth = 700;                       // 条目较长，展开时给足宽度
    cboPotion.SelectedIndexChanged += delegate { OnPotionSel(); };
    g1.Controls.Add(cboPotion);

    g1.Controls.Add(Theme.MkLabel(T_Tier, 572, 34, 40));
    numPotionTier = new NumericUpDown();
    numPotionTier.SetBounds(612, 31, 60, 23);
    numPotionTier.Minimum = 0; numPotionTier.Maximum = 255; numPotionTier.Value = 0;
    numPotionTier.BackColor = Theme.PanelLight; numPotionTier.ForeColor = Theme.Text;
    numPotionTier.ValueChanged += delegate { OnPotionSel(); };
    g1.Controls.Add(numPotionTier);

    g1.Controls.Add(Theme.MkButton(T_Grant, 682, 29, 110, 28, delegate { GrantPotion(); }));

    lblPotionInfo = Theme.MkLabel("", 16, 64, 780);
    lblPotionInfo.AutoSize = false;
    lblPotionInfo.SetBounds(16, 64, 780, 18);
    g1.Controls.Add(lblPotionInfo);

    g1.Controls.Add(Theme.MkHint(HintA, 16, 90, 780));
    g1.Controls.Add(Theme.MkHint(HintB, 16, 122, 780));
    p.Controls.Add(g1);

    // ---------- 说明与边界 ----------
    FlatGroupBox g2 = new FlatGroupBox();
    g2.Title = T_AboutTitle;
    g2.SetBounds(8, 184, 810, 270);

    // 说明用一整块文本（**一个 i18n 条目**，含换行），不用多行 hint —— 中英同步一处、也省得排 11 个坐标。
    // Theme.MkLabel 内部会 I18n.Register，所以传中文原文即可，切语言时自动重贴。
    Label notes = Theme.MkLabel(NotesText, 16, 34, 780);
    notes.AutoSize = false;
    notes.SetBounds(16, 34, 780, 226);
    notes.ForeColor = Theme.TextDim;
    g2.Controls.Add(notes);
    p.Controls.Add(g2);

    RefreshPotionCombo();
    return p;
  }

  void RefreshPotionCombo() {
    if (cboPotion == null) return;
    cboPotion.Items.Clear();
    for (int i = 0; i < Potions.Rows.Count; i++) cboPotion.Items.Add(Potions.Rows[i].Label());
    if (cboPotion.Items.Count > 0) cboPotion.SelectedIndex = 0;
  }

  PotionRow SelectedPotion() {
    int i = cboPotion == null ? -1 : cboPotion.SelectedIndex;
    if (i < 0 || i >= Potions.Rows.Count) return null;
    return Potions.Rows[i];
  }

  // 选中项变化 / 档位变化 → 刷新信息行（效果摘要 + 该档位的预计数值）
  void OnPotionSel() {
    PotionRow r = SelectedPotion();
    if (r == null || lblPotionInfo == null) return;
    int tier = (int)numPotionTier.Value;
    int maxT = Potions.MaxTier(r);
    string txt = r.Summary;
    if (tier == 0) {
      txt += "　｜　档位 0：用模板基值";
    } else if (maxT == 0) {
      txt += "　｜　该模板没有档位表，档位对它无效（恒为基值）";
    } else if (tier > maxT) {
      txt += "　｜　⚠ 档位超出该模板档位表（共 " + maxT + " 档），会落回基值";
    } else {
      txt += "　｜　档位 " + tier + " → 显示数值约 " + Potions.ValueAt(r, tier, 0.0).ToString("N0");
    }
    lblPotionInfo.Text = txt;
  }

  void GrantPotion() {
    PotionRow r = SelectedPotion();
    if (r == null) { ToastMgr.Show(I18n.T(T_Pick)); return; }
    int tier = (int)numPotionTier.Value;
    InjectCmd("getfixedpotion " + r.Tpl + " " + tier, I18n.T(T_GrantDesc) + r.Tpl);
    // ⚠ 不能在这里报"发放成功"：不带 PotionType 会静默失败，而这里全是带的；
    //   但清单满 / 不在游戏中同样静默失败 —— 交给 InjectCmd 的"已执行/执行失败"如实呈现。
  }
}

} // namespace
