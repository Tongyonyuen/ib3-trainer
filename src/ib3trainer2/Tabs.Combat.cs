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

  // ===== 「清零收藏家进度」用的字段偏移（2026-10-09 与作者共同定位）=====
  // 背景：`SwordPC.CollectorCanSpawn()` 的门 ④ = `GetCollectorItemsTakenThisPlaythrough() <
  //   MaxCollectorItemsPerPlaythrough(ini=1)`，而该计数器就是 `SwordPlayer.IsaCollectorItems` /
  //   `SirisCollectorItems`（按 `eCurrentPlayerType` 取）⇒ **他一旦在本血脉拿过你一件东西，
  //   本血脉就再也不会被替换上场**（`setupcollector` 只置标志位 58 + 倒计时归零，改不了这条）。
  // 定位法（可复现）：把存档里 `SirisCollectorItems` 改成唯一哨兵（123456789）——必须**同时**
  //   更新 `LocalFileHeaderCache` 里的 SHA1，否则游戏会静默回退到 `_BackupX_*.bin`（实测踩过）——
  //   载入后在内存里扫哨兵 ⇒ 命中真身对象内 +0x3E6C。清 0 后实测收藏家立刻能再刷出（初始状态）。
  const long OFF_COLLECTOR_ITEMS_SIRIS = 0x3E6C;   // Siris 的计数器（已实测）
  const long OFF_COLLECTOR_ITEMS_ISA   = 0x3E68;   // Isa 的计数器：按导出顺序（Isa 声明在 Siris 之前）推断；
                                                   // 只读不写 ⇒ 即使推断错也只是显示错，不会写坏内存
  // 门的实测判据：GetCollectorItemsTakenThisPlaythrough() <= MaxCollectorItemsPerPlaythrough(=1)
  // ⇒ 原始值 > 1 就是"本周目这个角色已经刷满"
  const int  COLLECTOR_CAP_RAW = 1;

  // 读两个角色的"本周目收藏家计数"（任意一步失败都返回 false，调用方据此显示"读不到"）
  bool CollectorCounts(out int siris, out int isa) {
    siris = isa = -1;
    if (!RequireH()) return false;
    long host = EngineCall.LivePlayerHint(H);
    if (host == 0) return false;
    string cur; string err;
    if (!MemIO.ReadValue(H, host + OFF_COLLECTOR_ITEMS_SIRIS, ScanType.I32, out cur, out err)) return false;
    if (!int.TryParse(cur, out siris)) return false;
    if (!MemIO.ReadValue(H, host + OFF_COLLECTOR_ITEMS_ISA, ScanType.I32, out cur, out err)) return false;
    if (!int.TryParse(cur, out isa)) return false;
    if (siris < 0 || siris > 999 || isa < 0 || isa > 999) return false;   // 读飞了就当读不到
    return true;
  }
  // Isa 的对应字段按导出顺序推断在 +0x3E68，但**未验证** ⇒ 本按钮不写它（不写没验证的内存）。
  // 若当前角色是 Isa 而按钮无效，用同样的哨兵法把 Isa 的偏移验出来即可。

  // 当前「怪物等级」= [[映像基址 + 0xCE3588] + 0x7B0]（Int32）—— 一条**固定指针链**，
  // 不需要扫描、也不需要用户填参数。
  //
  // 2026-10-09 差分定位实证（作者配合拆/装 UberBossBoostGem）：
  //   · 显示等级 = **原始掷点等级 + 宝石加成**：194378 − 64750 = 129628，而实测下一只 129602
  //     —— 差的 26 就是作者说的"掷点波动"
  //   · 加成**不是**玩家对象里实时变化的字段：拆掉宝石后**已加载的怪等级不变**
  //     ⇒ 等级在**加载时固化**（所以"某只怪的显示等级"不能当定位目标，那是 pawn 侧一次性结果）
  //   · 可读的是**当前场景的等级数据对象**：它随场景换堆块，但**指向它的两个全局在游戏映像内**
  //     （0xCE3588 / 0xD20388），而本游戏无 ASLR ⇒ 链本身是固定的
  //   · 该对象里前两个槽位（+0x7B0 / +0xDC0）实测同值；取第一个
  //
  // 读不到就返回 0，由调用方明确提示 —— **绝不拿玩家等级或猜的值顶替**（那会把难度改错方向）。
  const long RVA_ENEMY_LV_OBJ = 0xCE3588;   // 映像内全局 → 当前场景的等级数据对象
  const long OFF_ENEMY_LV     = 0x7B0;      // 该对象内第一个槽位（Int32）

  int EnemyLevel() {
    if (!RequireH()) return 0;
    long obj = EngineCall.ReadPtr(H, EngineCall.ImgBase + RVA_ENEMY_LV_OBJ);
    if (obj == 0) return 0;
    long a = obj + OFF_ENEMY_LV;
    string cur; string err;
    if (!MemIO.ReadValue(H, a, ScanType.I32, out cur, out err)) {
      Log("怪物等级读取失败: " + err + " @0x" + a.ToString("X"));
      return 0;
    }
    int v;
    if (!int.TryParse(cur, out v)) return 0;
    if (v <= 0 || v > 10000000) return 0;   // 范围守卫：读飞了就当没读到（宁可不发等级）
    return v;
  }

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
    //   ★ 等级**不归这条命令管**：等级来自原版 ini 的 CollectorLevel[]。
    //   等级由**独立按钮**在"收藏家出场后"发（见右边「设置当前敌人等级」）—— 不在触发时发：
    //   `setbosslevel` 只作用于**当前 Boss**，而触发那一刻收藏家还没出场（作者 2026-10-09 实测：
    //   触发后连打几场都没见到他，反而在兽形场景把面前那只怪换成了**黑模型** —— 说明替换确实
    //   响应了，只是该场景的刷怪位装不下人形怪；所以"何时发"必须等到他真的出场）。
    Button bColl = Theme.MkButton("触发收藏家战斗", 354, 34, 150, 30, delegate {
      int ci = 0;                                       // 留空 / 非数字 → 0 = 默认（走游戏自身进度）
      int.TryParse(numCollectorWeapon.Text.Trim(), out ci);
      if (ci < 0) ci = 0;
      // ★ 触发前预检（2026-10-09 作者要求）：先看两个角色本周目的收藏家计数，满了就别傻点
      int cS, cI; string tail = "";
      if (CollectorCounts(out cS, out cI)) {
        tail = "  [" + I18n.T("本周目计数：Siris ") + cS + " / " + I18n.T("Isa ") + cI + "]";
        bool sFull = cS > COLLECTOR_CAP_RAW, iFull = cI > COLLECTOR_CAP_RAW;
        if (sFull && iFull) {
          ToastMgr.Warn(I18n.T("两个角色本周目的收藏家计数都已满（Siris ") + cS + " / Isa " + cI +
                        I18n.T("）——先点「清零收藏家进度」，再触发"));
          return;                                    // 两个都满 ⇒ 发了也刷不出，直接不发
        }
        if (sFull || iFull) {
          ToastMgr.Warn((sFull ? "Siris " + cS : "Isa " + cI) + I18n.T(" 本周目计数已满 —— 用那个角色刷不出；先点「清零收藏家进度」") +
                        I18n.T("（另一个角色仍可刷）"));
        }
      } else {
        tail = "  [" + I18n.T("本周目计数读取失败") + "]";
      }
      InjectCmd("setupcollector " + ci, I18n.T("触发收藏家战斗") + " · #" + ci + tail);
    });

    // 「设置当前敌人等级」= 发 setbosslevel <当前场景怪物等级>（值由固定指针链实时读取）。
    //   · 用它把**当前在场的敌人/Boss**（也就是收藏家，等他出场后）设成本场景怪物等级
    //   · 为什么不是玩家等级（作者定）：玩家等级不代表战斗难度 —— 高周目真正抬敌人等级的是
    //     身上带的 UberBossBoostGem（每颗可加数万级），玩家每周目等级上限只加三位数
    //   · 读不到怪物等级（停在标题/加载界面）就**明确提示且不发**，绝不拿玩家等级或猜的值顶替
    Button bSetLv = Theme.MkButton("设置当前敌人等级", 660, 34, 140, 30, delegate {
      int lv = EnemyLevel();
      if (lv <= 0) {
        ToastMgr.Warn(I18n.T("没读到怪物等级（停在标题/加载界面？），未发等级"));
        return;
      }
      InjectCmd("setbosslevel " + lv, I18n.T("设置当前敌人等级") + " = " + lv);
    });
    b3.Controls.Add(bSetLv);
    b3.Controls.Add(bGem); b3.Controls.Add(bDragon); b3.Controls.Add(bColl);

    // 起始武器索引（0–99）：它写进收藏家的存档槽，是**奖励阶梯的起点** ⇒ 每次触发都从它开始。
    // ★ 2026-10-09 作者实测修正：以前这里写的"0＝默认（走游戏自身进度）"是**错的** —— 发 0 就是
    //   每次都强制从列表第一件开始（他实测"第二个收藏家还是初始装备"）。想要哪一件就填哪个序号。
    b3.Controls.Add(Theme.MkLabel("起始武器索引", 512, 40, 80));
    numCollectorWeapon = new NumericUpDown();
    numCollectorWeapon.SetBounds(596, 35, 56, 23);
    numCollectorWeapon.Minimum = 0; numCollectorWeapon.Maximum = 99; numCollectorWeapon.Value = 0;
    numCollectorWeapon.BackColor = Theme.PanelLight; numCollectorWeapon.ForeColor = Theme.Text;
    b3.Controls.Add(numCollectorWeapon);

    // 「清零收藏家进度」：把真身里的 SirisCollectorItems 写 0 —— 解除 `CollectorCanSpawn()` 的门 ④。
    //   先读出来显示（透明），再写 0；用 MemIO.SafeWriteValue（项目铁律：只写可写私有页 + 回读校验）。
    //   当前角色必须是 **Siris**（本偏移是 Siris 的；Isa 的偏移未验证，见字段注释）。
    b3.Controls.Add(Theme.MkButton("清零收藏家进度", 14, 70, 150, 30, delegate {
      if (!RequireH()) return;
      long host = EngineCall.LivePlayerHint(H);
      if (host == 0) {
        ToastMgr.Warn(I18n.T("真身对象未绑定：等自动绑定完成（或点「立即附着」）后再试"));
        return;
      }
      long a = host + OFF_COLLECTOR_ITEMS_SIRIS;
      string cur; string err;
      if (!MemIO.ReadValue(H, a, ScanType.I32, out cur, out err)) {
        ToastMgr.Warn(I18n.T("读收藏家进度失败：") + err);
        return;
      }
      byte[] wrote;
      if (!MemIO.SafeWriteValue(H, a, ScanType.I32, "0", out wrote, out err)) {
        ToastMgr.Warn(I18n.T("写收藏家进度失败：") + err);
        return;
      }
      ToastMgr.Show(I18n.T("收藏家进度已清零：") + cur + " → 0" + I18n.T("（当前角色须为 Siris；他下次就能再出场）"));
      Log("收藏家进度清零: " + cur + " → 0 @0x" + a.ToString("X") + "（真身 0x" + host.ToString("X") + " + 0x3E6C）");
    }));

    b3.Controls.Add(Theme.MkHint("「起始武器索引」＝每次触发都从这里开始（0＝列表第一件，1/2/3…依次往后）；「设置当前敌人等级」「清零收藏家进度」见说明。", 172, 74, 632));
    b3.Controls.Add(Theme.MkHint("触发后是**下次进图**时生效：全图刷怪点里随机挑一个强制成他（其余点照常刷，所以出现位置随机）；等级＝原版表（50…15000）。", 14, 106, 790));
    p.Controls.Add(b3);
    return p;
  }
}

} // namespace
