// ============================================================================
// Tabs.Items.cs — 物品发放：两级分类 + 搜索浏览器（立即可用）
//                 发放按钮阶段2（注入器）打通后点亮；宝石三步向导阶段3。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  ComboBox cboCat, cboSub;
  TextBox txtItemSearch;
  ListView lvItems;
  NumericUpDown numItemCnt;

  TabPage BuildTabItems() {
    TabPage p = new TabPage("物品发放");
    p.BackColor = Theme.BG; p.ForeColor = Theme.Text;

    p.Controls.Add(Theme.MkLabelInk("主分类", 14, 13, 50));
    cboCat = new ComboBox(); cboCat.SetBounds(66, 10, 100, 23);
    Theme.StyleCombo(cboCat);
    cboCat.SelectedIndexChanged += delegate { RebuildSubs(); ApplyItemFilter(); };
    p.Controls.Add(cboCat);

    p.Controls.Add(Theme.MkLabelInk("子分类", 178, 13, 50));
    cboSub = new ComboBox(); cboSub.SetBounds(230, 10, 120, 23);
    Theme.StyleCombo(cboSub);
    cboSub.SelectedIndexChanged += delegate { ApplyItemFilter(); };
    p.Controls.Add(cboSub);

    p.Controls.Add(Theme.MkLabelInk("搜索", 362, 13, 36));
    txtItemSearch = Theme.MkText(400, 10, 150, "");
    txtItemSearch.TextChanged += delegate { ApplyItemFilter(); };
    p.Controls.Add(txtItemSearch);

    p.Controls.Add(Theme.MkButton("重载数据库", 560, 8, 88, 25, delegate {
      int n = Items.Load(ExeDir);
      RebuildSubs(); ApplyItemFilter();
      Log("物品数据库重载: " + n + " 条");
    }));

    lvItems = new DarkListView();
    Theme.ApplyDarkScroll(lvItems);
    lvItems.SetBounds(14, 42, 788, 316);
    Theme.StyleList(lvItems);
    lvItems.Columns.Add("模板名", 180);
    lvItems.Columns.Add("中文名", 190);
    lvItems.Columns.Add("子分类", 110);
    lvItems.Columns.Add("备注", 308);
    p.Controls.Add(lvItems);
    lvItems.HandleCreated += delegate {
      SendMessage(lvItems.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)0, (IntPtr)LVS_EX_DOUBLEBUFFER);
    };

    p.Controls.Add(Theme.MkButton("复制模板名", 14, 366, 90, 26, delegate {
      string t = SelectedTpl();
      if (t == null) { ToastMgr.Show(I18n.T("先选中一行")); return; }
      try { Clipboard.SetText(t); ToastMgr.Show(I18n.T("已复制模板名: ") + t); } catch { }
    }));
    p.Controls.Add(Theme.MkButton("复制中文名", 112, 366, 90, 26, delegate {
      if (lvItems.SelectedItems.Count == 0) { ToastMgr.Show(I18n.T("先选中一行")); return; }
      try { Clipboard.SetText(lvItems.SelectedItems[0].SubItems[1].Text); ToastMgr.Show(I18n.T("已复制中文名")); } catch { }
    }));
    Label hBrowse = Theme.MkHint("浏览器立即可用；发放按钮 = 无感注入（点即入包）。路由：宝石→刷商店；材料/药水→消耗品；藏宝图→钥匙；装备→未拥有才发。", 214, 366, 560);
    hBrowse.Height = 20;
    hBrowse.ForeColor = Theme.Ink;
    p.Controls.Add(hBrowse);

    // ---- 发放区（无感注入已上线；紧凑单行） ----
    FlatGroupBox g = new FlatGroupBox();
    g.Title = "发放（无感注入：点击即生效，零窗口变化）";
    g.SetBounds(8, 390, 800, 64);
    numItemCnt = new NumericUpDown();
    numItemCnt.SetBounds(60, 26, 50, 23);
    numItemCnt.Minimum = 1; numItemCnt.Maximum = 30; numItemCnt.Value = 1;
    numItemCnt.BackColor = Theme.PanelLight; numItemCnt.ForeColor = Theme.Text;
    g.Controls.Add(Theme.MkLabel("数量", 16, 29, 40));
    g.Controls.Add(numItemCnt);
    Button bSel = Theme.MkButton("发放选中物品", 122, 24, 110, 28, delegate {
      string tpl = SelectedTpl();
      if (tpl == null) { ToastMgr.Show(I18n.T("先在列表选中一行")); return; }
      int n = (int)numItemCnt.Value;
      string cat = cboCat.SelectedItem != null ? cboCat.SelectedItem.ToString() : "";
      // 材料 TRA_World_N → 消耗品通道 addconsumable 类型=30+N（与游戏内映射实测一致）
      if (tpl.StartsWith("TRA_World_")) {
        string num = tpl.Substring(10).Split('_')[0];
        int w;
        if (int.TryParse(num, out w)) {
          InjectCmd("addconsumable " + (30 + w) + " " + n + " 1", I18n.T("发放材料 ") + tpl + " ×" + n);
          ToastMgr.Show(I18n.T("已发放材料 ×") + n + I18n.T("（去背包消耗品区查看）"));
          return;
        }
      }
      if (tpl == "TRA_Potion_HealthL") {
        InjectCmd("addconsumable 29 " + n + " 1", I18n.T("发放体力回满药水 ×") + n);
        ToastMgr.Show(I18n.T("已发放体力回满药水 ×") + n);
        return;
      }
      // 消耗品类型行 TYPE_N（22=水攻药水；**30=未知**，见下）
      // ★ 2026-10-07 作者回忆 + 实测：type 30 当年发过 80 个，但**背包里翻遍都没有数量为 80 的物品**，
      //   而那个数量在内存里一直存在 ⇒ 这个类型不对应可见的背包物品，**按未知处理**。
      //   ⚠ 因此不要把体力回满药水的 29 改成 30 —— 29 是对的（30 是材料 TRA_World_N 的起始号 30+N 的 30，
      //     两个 30 不是一回事，别合并）。
      if (tpl.StartsWith("TYPE_")) {
        string ty = tpl.Substring(5);
        InjectCmd("addconsumable " + ty + " " + n + " 1", I18n.T("发放消耗品(类型 ") + ty + ") ×" + n);
        ToastMgr.Show(I18n.T("已发放消耗品(类型 ") + ty + ") ×" + n + I18n.T("（去背包消耗品区查看）"));
        return;
      }
      // 宝石 → 刷进随身商店（购买即真实入袋；giveitemonce 对宝石无效——已实测）
      if (cat.IndexOf("宝石") >= 0) {
        InjectCmd("setplayercreatenewlistofstoregems 1 " + tpl + " 0 " + n + " 1", I18n.T("把商店刷成 ") + tpl + " ×" + n);
        ToastMgr.Show(I18n.T("已把随身商店刷成该宝石 ×") + n + I18n.T("——进物品栏·随身商店购买即入袋（金币可改）"));
        return;
      }
      // 藏宝图 → 钥匙通道（setgivekeyitem）
      if (tpl.StartsWith("TreasureMap")) {
        InjectCmd("setgivekeyitem " + tpl, I18n.T("发放藏宝图 ") + tpl);
        ToastMgr.Show(I18n.T("已发放藏宝图（钥匙通道）"));
        return;
      }
      // 装备/藏宝图 → 原通道（未拥有才发）
      string[] cmds = new string[n];
      for (int i = 0; i < n; i++) cmds[i] = "giveitemonce " + tpl;
      InjectCmds(cmds, I18n.T("发放 ") + tpl + (n > 1 ? (" ×" + n) : ""));
    });
    Button bAll = Theme.MkButton("发放所有物品", 240, 24, 110, 28, delegate {
      InjectCmd("setplayergiveallitems", I18n.T("发放所有物品（全套）"));
    });
    g.Controls.Add(bSel); g.Controls.Add(bAll);
    p.Controls.Add(g);
    return p;
  }

  public void RebuildSubs() {
    string cat = cboCat.SelectedItem != null ? cboCat.SelectedItem.ToString() : "全部";
    List<string> subs = Items.Subs(cat);
    cboSub.Items.Clear();
    cboSub.Items.AddRange(subs.ToArray());
    if (cboSub.Items.Count > 0) cboSub.SelectedIndex = 0;
  }

  public void ApplyItemFilter() {
    string cat = cboCat.SelectedItem != null ? cboCat.SelectedItem.ToString() : "全部";
    string sub = cboSub.SelectedItem != null ? cboSub.SelectedItem.ToString() : "全部";
    List<ItemRow> rows = Items.Filter(cat, sub, txtItemSearch.Text, 3000);
    lvItems.BeginUpdate();
    lvItems.Items.Clear();
    for (int i = 0; i < rows.Count; i++) {
      ListViewItem it = new ListViewItem(rows[i].Tpl);
      // 「名称」列：英文界面优先显示游戏官方英文名（items.csv 第 6 列，609/949 条有）；
      // 没有官方英文名的（游戏本地化里压根没起名）回退显示中文名 —— 中文界面始终显示中文名。
      it.SubItems.Add(I18n.IsEn && rows[i].En.Length > 0 ? rows[i].En : rows[i].Cn);
      // 分类是**封闭集合**（主分类 4 + 子分类 25）⇒ 过字典翻；
      // 模板名/中文名/备注是**数据**（949 条，items.csv 里没有英文名）⇒ 不翻。
      // 备注里唯一的中文标签是 "稀有度"，单独替换。
      it.SubItems.Add(I18n.T(rows[i].Sub));
      it.SubItems.Add(I18n.IsEn ? rows[i].Note.Replace("稀有度", "Rarity") : rows[i].Note);
      lvItems.Items.Add(it);
    }
    lvItems.EndUpdate();
    // 列表单元格是**系统绘制**的（不像页签/列头/下拉能自绘翻），所以切换语言必须重建行。
    // key 固定 ⇒ 每次填充只留一条登记。
    I18n.OnLang("items.rows", ApplyItemFilter);
  }

  string SelectedTpl() {
    if (lvItems.SelectedItems.Count == 0) return null;
    return lvItems.SelectedItems[0].Text;
  }
}

} // namespace
