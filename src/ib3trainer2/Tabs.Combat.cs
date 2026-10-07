// ============================================================================
// Tabs.Combat.cs — 战斗·商店（并页）：战斗按钮横向排布 + 金币/筹码 + 商店/触发
// ============================================================================
using System;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  bool godOnWired = false;

  TabPage BuildTabCombat() {
    TabPage p = new TabPage("战斗·商店");
    p.BackColor = Theme.BG;
    p.ForeColor = Theme.Text;

    // ---- 战斗（横向排布）----
    FlatGroupBox b1 = new FlatGroupBox();
    b1.Title = "战斗（无感注入：点按钮直接执行，零窗口变化）";
    b1.SetBounds(8, 8, 810, 122);
    b1.Fill = Theme.CardGold;   // 首页主区块：暗金色（与其他区块不重复）
    b1.Controls.Add(Theme.MkButton("无敌 开/关", 16, 34, 104, 30, delegate {
      godOnWired = !godOnWired;
      bool want = godOnWired;
      RunBackground(I18n.T("无敌切换"), delegate {
        string err;
        if (!EngineCall.Execute(H, "enablecheats", out err)) {
          godOnWired = !want;
          ToastMgr.Warn(I18n.T("无敌失败：") + err);
          return "无敌失败: enablecheats — " + err;
        }
        long mgr = 0;
        long spc = EngineCall.LastHost;
        if (spc != 0) {
          long cand = EngineCall.ReadPtr(H, spc + 0x54C);
          if (cand != 0 && EngineCall.ReadPtr(H, cand) == EngineCall.VT_CHEATMANAGER) mgr = cand;
        }
        if (mgr == 0) mgr = EngineCall.FindByVtable(H, EngineCall.VT_CHEATMANAGER);
        if (mgr == 0) {
          godOnWired = !want;
          ToastMgr.Warn(I18n.T("无敌失败：CheatManager 未生成"));
          return "无敌失败: CheatManager 未生成（enablecheats 已执行）";
        }
        if (!EngineCall.ExecuteOn(H, "god", mgr, out err)) {
          godOnWired = !want;
          ToastMgr.Warn(I18n.T("无敌失败：") + err);
          return "无敌失败: god — " + err;
        }
        ToastMgr.Show(want ? I18n.T("无敌已开启（god）") : I18n.T("无敌已关闭（god）"));
        return "无敌" + (want ? "开启" : "关闭") + " ✓ [enablecheats+god@0x" + mgr.ToString("X") + "]";
      });
    }));
    b1.Controls.Add(Theme.MkButton("充满超能/魔法", 134, 34, 116, 30, delegate {
      InjectCmd("fillsuperandmagicmeters", I18n.T("充满超能/魔法槽"));
    }));
    b1.Controls.Add(Theme.MkButton("击杀当前 Boss", 258, 34, 116, 30, delegate {
      InjectCmd("killboss", I18n.T("击杀当前 Boss"));
    }));
    b1.Controls.Add(Theme.MkHint("无敌=enablecheats+god（管理器自动定位）；击杀Boss仅战斗中有效。", 16, 74, 780));
    p.Controls.Add(b1);

    // ---- 金币 / 筹码 ----
    FlatGroupBox b2 = new FlatGroupBox(); b2.Title = "金币 / 筹码（唯一命中才自动采用）"; b2.SetBounds(8, 138, 810, 168);
    b2.Controls.Add(Theme.MkLabel("金币", 16, 34, 40));
    b2.Controls.Add(Theme.MkLabel("游戏当前值", 60, 34, 70));
    txtGoldCur = Theme.MkText(136, 31, 110, "");
    b2.Controls.Add(txtGoldCur);
    b2.Controls.Add(Theme.MkButton("扫描定位", 254, 29, 84, 26, delegate { LocateValue(true); }));
    b2.Controls.Add(Theme.MkLabel("目标值", 348, 34, 48));
    txtGoldNew = Theme.MkText(400, 31, 120, "");
    b2.Controls.Add(txtGoldNew);
    b2.Controls.Add(Theme.MkButton("写入", 528, 29, 64, 26, delegate { WriteGold(); }));
    lblGoldAddr = Theme.MkLabel("地址: 未定位", 20, 60, 500); lblGoldAddr.ForeColor = Theme.TextDim;
    b2.Controls.Add(lblGoldAddr);
    b2.Controls.Add(Theme.MkLabel("筹码", 16, 92, 40));
    b2.Controls.Add(Theme.MkLabel("游戏当前值", 60, 92, 70));
    txtChipCur = Theme.MkText(136, 89, 110, "");
    b2.Controls.Add(txtChipCur);
    b2.Controls.Add(Theme.MkButton("扫描定位", 254, 87, 84, 26, delegate { LocateValue(false); }));
    b2.Controls.Add(Theme.MkLabel("目标值", 348, 92, 48));
    txtChipNew = Theme.MkText(400, 89, 120, "");
    b2.Controls.Add(txtChipNew);
    b2.Controls.Add(Theme.MkButton("写入", 528, 87, 64, 26, delegate { WriteChip(); }));
    lblChipAddr = Theme.MkLabel("地址: 未定位", 20, 118, 500); lblChipAddr.ForeColor = Theme.TextDim;
    b2.Controls.Add(lblChipAddr);
    b2.Controls.Add(Theme.MkHint("附着游戏后金币/筹码会按真身结构自动绑定（跨存档自愈）；也可手动输入当前值扫描定位。", 16, 140, 780));
    AddrEntry ag = AddrBook.Get("misc.gold");
    if (ag != null) { goldAddr = ag.Addr; goldType = ag.Type; lblGoldAddr.Tag = "book"; lblGoldAddr.Text = I18n.T("地址: 0x") + ag.Addr.ToString("X") + I18n.T("（地址簿）"); }
    AddrEntry ac = AddrBook.Get("misc.chip");
    if (ac != null) { chipAddr = ac.Addr; chipType = ac.Type; lblChipAddr.Tag = "book"; lblChipAddr.Text = I18n.T("地址: 0x") + ac.Addr.ToString("X") + I18n.T("（地址簿）"); }
    p.Controls.Add(b2);

    // ---- 商店 / 战斗触发 ----
    FlatGroupBox b3 = new FlatGroupBox(); b3.Title = "商店 / 战斗触发（无感注入）"; b3.SetBounds(8, 314, 810, 140);
    Button bGem = Theme.MkButton("商店刷新一轮稀有宝石", 14, 34, 190, 30, delegate {
      // ★ 2026-10-07：这里原来**无条件**弹一句「商店已刷新一轮稀有宝石」——注入失败时也弹，
      //   等于替游戏报了个假成功（用户据此以为命令进游戏了，其实可能根本没送到；
      //   用户报"点这个按钮游戏闪退"时，屏幕上留的就是这句假的成功提示）。
      //   现在不自己弹：InjectCmd 按真实结果弹「已执行」/「执行失败：<原因>」，日志另留一行现场。
      //   操作提示（进物品栏·随身商店查看）挂到 desc 上，成功那条提示里照样带着。
      InjectCmd("setplayergems 0", I18n.T("商店刷新一轮稀有宝石（进物品栏·随身商店查看）"));
    });
    Button bDragon = Theme.MkButton("触发龙战", 214, 34, 130, 30, delegate {
      InjectCmd("setplayerdragonfight 0 0", I18n.T("触发龙战"));
      ToastMgr.Show(I18n.T("已置位屠龙战：在下一场符合条件的战斗中生效，受地形与原敌人类型影响"));
    });
    Button bColl = Theme.MkButton("触发收藏家战斗", 354, 34, 150, 30, delegate {
      InjectCmd("setupcollector 0", I18n.T("触发收藏家战斗"));
      ToastMgr.Show(I18n.T("已置位收藏家遭遇：在下一场符合条件的战斗中生效，受地形与原敌人类型影响"));
    });
    b3.Controls.Add(bGem); b3.Controls.Add(bDragon); b3.Controls.Add(bColl);
    b3.Controls.Add(Theme.MkHint("龙战/收藏家为置位式触发：在下一场符合条件的战斗中生效，受地形与原敌人类型影响。", 14, 72, 790));
    b3.Controls.Add(Theme.MkHint("命令经无感注入执行；条件不满足时游戏会静默忽略（日志显示成功/失败）。", 14, 102, 790));
    p.Controls.Add(b3);
    return p;
  }
}

} // namespace
