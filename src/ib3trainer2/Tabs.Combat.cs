// ============================================================================
// Tabs.Combat.cs — 战斗·商店（并页）：战斗按钮横向排布 + 金币/筹码 + 商店/触发
// ============================================================================
using System;
using System.Drawing;         // chkSyncLevel.BackColor = Color.Transparent
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  bool godOnWired = false;
  NumericUpDown numCollectorWeapon;   // 收藏家「起始武器索引」= setupcollector 的参数（留空/0 = 默认）
  TextBox txtCollectorLv;             // 「对齐等级」的目标值 → setbosslevel
  CheckBox chkSyncLevel;              // 勾选后：点触发按钮时一并把该等级发给当前 Boss

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
    // ★ 2026-10-09：龙战/收藏家这两行原来各自**无条件**再弹一句「已置位…」——与上面宝石按钮
    //   同一类假成功（注入失败时也报成功，双击还叠两条提示）。现在同样不自己弹，
    //   只由 InjectCmd 按真实结果报「已执行」/「执行失败：<原因>」；操作条件写进下方提示。
    Button bDragon = Theme.MkButton("触发龙战", 214, 34, 130, 30, delegate {
      InjectCmd("setplayerdragonfight 0 0", I18n.T("触发龙战"));
    });
    // 收藏家触发：参数 = **起始武器索引**（原生签名 SetupCollector(Int ForceWeaponStart)，
    //   该值写进收藏家的存档槽；0 = 默认）。所以"选哪把武器/哪个奖励"由这个数字决定，
    //   1.x 有这个输入框、2.0 重写时丢了（写死 0）——现在补回来。
    //   ★ 等级**不归这条命令管**：见下面「对齐等级」与提示（等级来自原版 ini 的 CollectorLevel[]）。
    //   勾选「同步等级」时一次做完两件事（省一步）：先 setupcollector 置位，再 setbosslevel 发等级。
    Button bColl = Theme.MkButton("触发收藏家战斗", 354, 34, 150, 30, delegate {
      int ci = 0;                                       // 留空 / 非数字 → 0 = 默认（走游戏自身进度）
      int.TryParse(numCollectorWeapon.Text.Trim(), out ci);
      if (ci < 0) ci = 0;
      if (chkSyncLevel.Checked) {
        int lv;
        if (!int.TryParse(txtCollectorLv.Text.Trim(), out lv) || lv < 0 || lv > 99999999) {
          ToastMgr.Warn(I18n.T("同步等级已勾选：先填目标等级，或取消勾选"));
          return;
        }
        InjectCmds(new string[] { "setupcollector " + ci, "setbosslevel " + lv },
                   I18n.T("触发收藏家战斗") + " · #" + ci + " + " + I18n.T("对齐等级") + " " + lv);
      } else {
        InjectCmd("setupcollector " + ci, I18n.T("触发收藏家战斗") + " · #" + ci);
      }
    });
    b3.Controls.Add(bGem); b3.Controls.Add(bDragon); b3.Controls.Add(bColl);

    // 起始武器索引（0–99，0=默认）
    b3.Controls.Add(Theme.MkLabel("起始武器索引", 512, 40, 80));
    numCollectorWeapon = new NumericUpDown();
    numCollectorWeapon.SetBounds(596, 35, 56, 23);
    numCollectorWeapon.Minimum = 0; numCollectorWeapon.Maximum = 99; numCollectorWeapon.Value = 0;
    numCollectorWeapon.BackColor = Theme.PanelLight; numCollectorWeapon.ForeColor = Theme.Text;
    b3.Controls.Add(numCollectorWeapon);

    // 「同步等级」：勾上后点触发按钮＝一条链做完（置位 + 发等级）。
    chkSyncLevel = new CheckBox();
    // 直接 new 的控件不走 Theme.Mk* 的内部登记，必须自己 Register，否则运行期切语言它不跟着变。
    I18n.Register(chkSyncLevel, delegate(string s) { chkSyncLevel.Text = s; }, "同步等级");
    chkSyncLevel.SetBounds(660, 36, 138, 22);
    chkSyncLevel.ForeColor = Theme.Text;
    chkSyncLevel.BackColor = Color.Transparent;
    b3.Controls.Add(chkSyncLevel);

    // 等级对齐 = 发 setbosslevel <值>（SwordPC.SetBossLevel：GetCurrentBoss().PawnLevel = 值，
    //   紧接着游戏自己调 SetupLevelData 重算该 Boss 的数据）。
    //   两个硬限制写在提示里：① 只作用于"当前 Boss"⇒ 必须收藏家出场后（战斗中）点；
    //   ② 只对当前这场有效 —— 下一场收藏家重新生成时按原版表重算。
    b3.Controls.Add(Theme.MkLabel("对齐等级", 14, 78, 58));
    txtCollectorLv = Theme.MkText(76, 74, 86, "");
    b3.Controls.Add(txtCollectorLv);
    b3.Controls.Add(Theme.MkButton("读玩家等级", 168, 70, 96, 30, delegate {
      if (!RequireH()) return;
      long a = GetStatAddr("lv.level");
      if (a == 0) { ToastMgr.Warn(I18n.T("等级未绑定：先去「成长」页填当前值并「定位」")); return; }
      string cur; string err;
      if (!MemIO.ReadValue(H, a, ScanType.I32, out cur, out err)) {
        ToastMgr.Warn(I18n.T("读玩家等级失败：") + err);
        return;
      }
      txtCollectorLv.Text = cur;
      Log("玩家等级 = " + cur + " @0x" + a.ToString("X"));
    }));
    b3.Controls.Add(Theme.MkButton("发给收藏家", 270, 70, 106, 30, delegate {
      int lv;
      if (!int.TryParse(txtCollectorLv.Text.Trim(), out lv) || lv < 0 || lv > 99999999) {
        ToastMgr.Warn(I18n.T("先填目标等级（0–99999999）"));
        return;
      }
      InjectCmd("setbosslevel " + lv, I18n.T("收藏家等级对齐") + " " + lv);
    }));
    b3.Controls.Add(Theme.MkHint("同步等级＝触发时一并发 setbosslevel；须战斗中", 384, 74, 420));
    b3.Controls.Add(Theme.MkHint("留空/0＝默认（走游戏自身进度）；收藏家等级＝原版 config 表（50…15000，不随世界等级），抬阶梯改 ini 并重启。", 14, 106, 790));
    p.Controls.Add(b3);
    return p;
  }
}

} // namespace
