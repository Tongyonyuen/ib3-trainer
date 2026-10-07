// ============================================================================
// IB3 快捷修改器 — Infinity Blade III PC 移植版 控制台快捷指令工具
// 独立 WinForms 窗口，通过 WM_CHAR 向游戏控制台注入命令（checkcon/postchar 同款机制）
// 编译: csc.exe /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll Ib3Trainer.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ib3Trainer {

class ItemRow {
  public string Tpl = "", Cat = "", Sub = "", Cn = "", Note = "";
}

static class Win32 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  public struct RECT { public int L, T, R, B; }
  public const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102;
}

class Mainform : Form {
  // ---------- 状态 ----------
  IntPtr gameHwnd = IntPtr.Zero;
  string gameTitle = "";
  bool godOn = false;
  bool sending = false;
  List<ItemRow> items = new List<ItemRow>();
  Random rnd = new Random();

  // ---------- 控件 ----------
  Label lblConn; Button btnReconn;
  TextBox txtLog;
  TabControl tabs;
  // 常用
  TextBox txtGold, txtChips, txtLevel;
  // 物品
  ComboBox cboCat, cboSub; TextBox txtSearch, txtGoldScale; ListView lv; NumericUpDown numItemCnt, numRewardLevel;
  // 掌握
  TextBox txtMasterLevel; NumericUpDown numPartTimes;
  // 属性
  TextBox txtStatHP, txtStatDmg, txtStatShield, txtStatMagic, txtDragonMap;
  NumericUpDown numDragonStart, numCollectorWeapon;

  public Mainform() {
    Text = "IB3 快捷修改器";
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(820, 660);

    lblConn = new Label();
    lblConn.SetBounds(12, 12, 620, 20);
    lblConn.Text = "游戏窗口: 未检测";
    lblConn.ForeColor = Color.Firebrick;
    Controls.Add(lblConn);

    btnReconn = new Button();
    btnReconn.SetBounds(650, 9, 150, 26);
    btnReconn.Text = "重新检测游戏窗口";
    btnReconn.Click += new EventHandler(OnReconn);
    Controls.Add(btnReconn);

    tabs = new TabControl();
    tabs.SetBounds(8, 42, 796, 490);
    tabs.TabPages.Add(MakeTabQuick());
    tabs.TabPages.Add(MakeTabItems());
    tabs.TabPages.Add(MakeTabGrant());
    tabs.TabPages.Add(MakeTabMaster());
    tabs.TabPages.Add(MakeTabStat());
    Controls.Add(tabs);

    txtLog = new TextBox();
    txtLog.SetBounds(8, 540, 796, 110);
    txtLog.Multiline = true;
    txtLog.ReadOnly = true;
    txtLog.ScrollBars = ScrollBars.Vertical;
    txtLog.Font = new Font("Consolas", 9);
    Controls.Add(txtLog);

    Load += new EventHandler(OnLoad);
  }

  // ================= UI 构建 =================
  Label MkLabel(string t, int x, int y, int w) {
    Label l = new Label(); l.SetBounds(x, y, w, 20); l.Text = t; return l;
  }
  // 灰色说明标签：自动换行且随内容增高（修复换行被截断）
  Label MkHint(string t, int x, int y, int w) {
    Label l = new Label(); l.SetBounds(x, y, w, 20); l.Text = t;
    l.ForeColor = Color.Gray;
    l.AutoSize = true;
    l.MaximumSize = new Size(w, 0);
    return l;
  }
  TextBox MkText(int x, int y, int w, string init) {
    TextBox t = new TextBox(); t.SetBounds(x, y, w, 23); t.Text = init; return t;
  }
  Button MkBtn(string t, int x, int y, int w, EventHandler h) {
    Button b = new Button(); b.SetBounds(x, y, w, 30); b.Text = t; b.Click += h; return b;
  }

  TabPage MakeTabQuick() {
    TabPage p = new TabPage("常用命令");
    p.Controls.Add(MkLabel("货币（进游戏后任意界面可用）", 12, 12, 400));
    p.Controls.Add(MkLabel("金币", 12, 45, 40));
    txtGold = MkText(55, 42, 130, "999999999");
    p.Controls.Add(txtGold);
    p.Controls.Add(MkBtn("设置金币", 195, 40, 90, delegate { SendUi("setplayergold " + txtGold.Text.Trim(), "设置金币"); }));
    p.Controls.Add(MkBtn("清零", 293, 40, 60, delegate { SendUi("setplayergold 0", "金币清零"); }));
    p.Controls.Add(MkLabel("筹码", 12, 82, 40));
    txtChips = MkText(55, 79, 130, "99999");
    p.Controls.Add(txtChips);
    p.Controls.Add(MkBtn("设置筹码", 195, 77, 90, delegate { SendUi("setplayerchips " + txtChips.Text.Trim(), "设置筹码"); }));

    p.Controls.Add(MkLabel("等级", 12, 119, 40));
    txtLevel = MkText(55, 116, 130, "50");
    p.Controls.Add(txtLevel);
    p.Controls.Add(MkBtn("设置玩家等级(按差值自动发属性点)", 195, 114, 240, delegate { SendUi("setplayerlevel " + txtLevel.Text.Trim(), "设置玩家等级"); }));

    GroupBox g1 = new GroupBox(); g1.SetBounds(12, 155, 760, 110); g1.Text = "物品 / 商店";
    g1.Controls.Add(MkBtn("发放全部物品", 15, 28, 130, delegate { SendUi("setplayergiveallitems", "发放全部物品(道具界面)"); }));
    g1.Controls.Add(MkBtn("补满药水", 155, 28, 100, delegate { SendUi("setplayerpotions", "补满药水"); }));
    g1.Controls.Add(MkBtn("解锁全部Perk", 265, 28, 120, delegate { SendUi("setplayergiveallperks", "解锁全部Perk"); }));
    g1.Controls.Add(MkBtn("商店刷新·稀有宝石", 15, 68, 150, delegate { SendUi("setplayergems 0", "商店刷新(稀有宝石)"); }));
    g1.Controls.Add(MkBtn("商店刷新·按名(见定向发放页)", 175, 68, 190, delegate { ShowToast("请到「定向发放」页：输入宝石模板名后点「按名刷满 42 格」"); }));
    Label l1 = MkLabel("若商品异常请用左侧按钮", 380, 72, 370);
    g1.Controls.Add(MkHint(l1.Text, l1.Left, l1.Top, l1.Width));
    p.Controls.Add(g1);

    GroupBox g2 = new GroupBox(); g2.SetBounds(12, 275, 760, 90); g2.Text = "槽位 / 状态";
    g2.Controls.Add(MkBtn("填满必杀槽+魔法槽", 15, 30, 150, delegate { SendUi("fillsuperandmagicmeters", "填满必杀+魔法槽"); }));
    g2.Controls.Add(MkBtn("回满魔法", 175, 30, 100, delegate { SendUi("givefullmagic", "回满魔法"); }));
    g2.Controls.Add(MkBtn("无敌 开/关", 285, 30, 100, delegate {
      SendBatch(new string[] { "enablecheats", "god" }, "无敌切换");
      godOn = !godOn;
    }));
    Label lg = MkLabel("(= enablecheats + god，再次点击关闭)", 395, 34, 250);
    g2.Controls.Add(MkHint(lg.Text, lg.Left, lg.Top, lg.Width));
    p.Controls.Add(g2);

    Label lh = MkLabel("提示: 龙战/收藏家/无敌 在「属性·战斗」页；物品类命令需 Pawn 已生成（真正进入游戏后）", 12, 380, 760);
    p.Controls.Add(MkHint(lh.Text, lh.Left, lh.Top, lh.Width));
    return p;
  }

  TabPage MakeTabItems() {
    TabPage p = new TabPage("物品发放");
    p.Controls.Add(MkHint("giveitemonce 直入背包·任意界面（需部署 exec6 包并重启游戏）。效果需关闭背包重新打开才刷新；新发宝石 ForgeLevel=0，发完到「掌握升级」拉等级。", 12, 12, 760));

    p.Controls.Add(MkLabel("主分类", 12, 45, 50));
    cboCat = new ComboBox(); cboCat.SetBounds(65, 42, 110, 23); cboCat.DropDownStyle = ComboBoxStyle.DropDownList;
    cboCat.Items.AddRange(new object[] { "全部", "装备", "收集品", "宝石" });
    cboCat.SelectedIndex = 0;
    cboCat.SelectedIndexChanged += delegate { RebuildSubs(); ApplyFilter(); };
    p.Controls.Add(cboCat);

    p.Controls.Add(MkLabel("子分类", 190, 45, 50));
    cboSub = new ComboBox(); cboSub.SetBounds(243, 42, 130, 23); cboSub.DropDownStyle = ComboBoxStyle.DropDownList;
    cboSub.SelectedIndexChanged += delegate { ApplyFilter(); };
    p.Controls.Add(cboSub);

    p.Controls.Add(MkLabel("搜索(模板名/中文名)", 390, 45, 130));
    txtSearch = MkText(522, 42, 160, "");
    txtSearch.TextChanged += delegate { ApplyFilter(); };
    p.Controls.Add(txtSearch);

    lv = new ListView();
    lv.SetBounds(12, 75, 670, 300);
    lv.View = View.Details;
    lv.FullRowSelect = true;
    lv.MultiSelect = false;
    lv.Columns.Add("模板名", 190);
    lv.Columns.Add("中文名", 210);
    lv.Columns.Add("子分类", 120);
    lv.Columns.Add("备注", 130);
    lv.DoubleClick += delegate { SendSelected(); };
    p.Controls.Add(lv);

    Button btnReload = MkBtn("重载数据库", 694, 75, 88, delegate { LoadItems(); ApplyFilter(); });
    p.Controls.Add(btnReload);
    Label lc = MkLabel("items.csv 可自行补充装备中文名", 694, 110, 100);
    lc.ForeColor = Color.Gray; lc.Height = 60; p.Controls.Add(MkHint(lc.Text, lc.Left, lc.Top, lc.Width));

    p.Controls.Add(MkLabel("数量", 12, 390, 40));
    numItemCnt = new NumericUpDown(); numItemCnt.SetBounds(55, 386, 60, 23);
    numItemCnt.Minimum = 1; numItemCnt.Maximum = 99; numItemCnt.Value = 1;
    p.Controls.Add(numItemCnt);
    p.Controls.Add(MkBtn("发放选中物品", 130, 384, 120, delegate { SendSelected(); }));
    p.Controls.Add(MkLabel("宝石奖励等级", 262, 390, 90));
    numRewardLevel = new NumericUpDown(); numRewardLevel.SetBounds(352, 386, 55, 23);
    numRewardLevel.Minimum = 0; numRewardLevel.Maximum = 999; numRewardLevel.Value = 2;
    p.Controls.Add(numRewardLevel);
    p.Controls.Add(MkLabel("目标价值", 415, 390, 65));
    txtGoldScale = MkText(478, 386, 90, "100000");
    p.Controls.Add(txtGoldScale);
    Label lg2 = MkLabel("装备=giveitemonce；宝石=setplayergiverandomgem <等级> <价值> <孔位偏好>——产出为档位池内随机，偏好为弱倾向", 262, 415, 480);
    p.Controls.Add(MkHint(lg2.Text, lg2.Left, lg2.Top, lg2.Width));

    return p;
  }

  // ================= 定向发放（2026-10-06 实测命令） =================
  TabPage MakeTabGrant() {
    TabPage p = new TabPage("定向发放");
    GroupBox g = new GroupBox(); g.SetBounds(12, 12, 760, 168); g.Text = "单件/单项定向发放（当前使用角色，任意界面可用）";
    g.Controls.Add(MkLabel("装备模板名", 15, 33, 85));
    TextBox txtG = MkText(105, 30, 170, "");
    g.Controls.Add(txtG);
    g.Controls.Add(MkBtn("giveitemonce 发放", 290, 28, 150, delegate { SendUi("giveitemonce " + txtG.Text.Trim(), "发放装备 " + txtG.Text.Trim()); }));
    g.Controls.Add(MkHint("例：Armor_100（完整模板名去「物品发放」页查）", 460, 33, 285));
    g.Controls.Add(MkLabel("藏宝图模板名", 15, 76, 85));
    TextBox txtK = MkText(105, 73, 170, "");
    g.Controls.Add(txtK);
    g.Controls.Add(MkBtn("setgivekeyitem 发放", 290, 71, 150, delegate { SendUi("setgivekeyitem " + txtK.Text.Trim(), "发放藏宝图 " + txtK.Text.Trim()); }));
    g.Controls.Add(MkHint("例：TreasureMap20 ~ TreasureMap38", 460, 76, 285));
    g.Controls.Add(MkLabel("消耗品/材料类型", 15, 119, 105));
    TextBox txtT = MkText(125, 116, 45, "29");
    g.Controls.Add(txtT);
    g.Controls.Add(MkLabel("数量", 182, 119, 35));
    TextBox txtN = MkText(220, 116, 55, "10");
    g.Controls.Add(txtN);
    g.Controls.Add(MkBtn("addconsumable 发放", 290, 114, 150, delegate { SendUi("addconsumable " + txtT.Text.Trim() + " " + txtN.Text.Trim() + " 1", "发放消耗品(类型" + txtT.Text.Trim() + ")"); }));
    g.Controls.Add(MkHint("29=体力回满药水；31=绿仙人掌等材料槽(30~41)；参数序=(类型,数量,忽略上限)", 460, 119, 285));
    p.Controls.Add(g);

    GroupBox g2 = new GroupBox(); g2.SetBounds(12, 194, 760, 150); g2.Text = "随身商店（按模板名刷店）/ 片头";
    g2.Controls.Add(MkLabel("宝石模板名", 15, 33, 85));
    TextBox txtS = MkText(105, 30, 170, "FireGem");
    g2.Controls.Add(txtS);
    g2.Controls.Add(MkBtn("按名刷满 42 格", 290, 28, 150, delegate { SendUi("setplayercreatenewlistofstoregems 1 " + txtS.Text.Trim() + " 0 12 1", "按名刷商店 " + txtS.Text.Trim()); }));
    g2.Controls.Add(MkHint("把随身商店 42 格刷成该模板（全档位+随机 roll），进物品栏即见；买进包后可用内存修改器改任意数值", 15, 62, 730));
    g2.Controls.Add(MkBtn("片头加播段 开/关", 15, 106, 170, delegate { ToggleSkipIntro(); }));
    g2.Controls.Add(MkHint("只关配置里的 StartupLoop 加播段（重启生效）；Startup 本体由启动器无条件播放、改名会卡启动（已实测），彻底跳过需改 exe", 200, 109, 545));
    p.Controls.Add(g2);
    return p;
  }

  TabPage MakeTabMaster() {
    TabPage p = new TabPage("掌握升级");
    GroupBox g1 = new GroupBox(); g1.SetBounds(12, 12, 760, 70); g1.Text = "一键掌握（游戏内，无需道具界面，绕过轮回等级上限）";
    g1.Controls.Add(MkLabel("目标等级", 15, 30, 60));
    txtMasterLevel = MkText(80, 27, 80, "50");
    g1.Controls.Add(txtMasterLevel);
    g1.Controls.Add(MkBtn("掌握全部拥有装备到该等级", 170, 25, 200, delegate {
      SendUi("masterallowneditems 0 " + txtMasterLevel.Text.Trim(), "一键掌握全部装备(等级" + txtMasterLevel.Text.Trim() + ")");
    }));
    Label la = MkLabel("= masterallowneditems 0 <等级>，只升不降，不扣金币", 380, 32, 370);
    g1.Controls.Add(MkHint(la.Text, la.Left, la.Top, la.Width));
    p.Controls.Add(g1);

    GroupBox g2 = new GroupBox(); g2.SetBounds(12, 92, 760, 110); g2.Text = "当前装备部位·经验灌注（原版机制：灌满本级经验条；轮回上限内可继续掌握）";
    g2.Controls.Add(MkLabel("执行次数", 15, 32, 60));
    numPartTimes = new NumericUpDown(); numPartTimes.SetBounds(80, 29, 55, 23);
    numPartTimes.Minimum = 1; numPartTimes.Maximum = 99; numPartTimes.Value = 1;
    g2.Controls.Add(numPartTimes);
    g2.Controls.Add(MkBtn("武器", 150, 27, 70, delegate { SendPartXp("giveweaponxp"); }));
    g2.Controls.Add(MkBtn("盾牌", 228, 27, 70, delegate { SendPartXp("giveshieldxp"); }));
    g2.Controls.Add(MkBtn("盔甲", 306, 27, 70, delegate { SendPartXp("givearmorxp"); }));
    g2.Controls.Add(MkBtn("头盔", 384, 27, 70, delegate { SendPartXp("givehelmetxp"); }));
    g2.Controls.Add(MkBtn("魔法", 462, 27, 70, delegate { SendPartXp("givemagicxp"); }));
    Label lb = MkHint("每次给 999999999 经验，只灌满本级经验条（原版机制）。跨轮回上限的一键直写命令（masterequipped*）已完成手术但被加载器校验拦截，包已回滚——该功能转入存档编辑路线。", 15, 70, 730);
    g2.Controls.Add(lb);
    p.Controls.Add(g2);

    return p;
  }

  TabPage MakeTabStat() {
    TabPage p = new TabPage("属性·战斗");
    GroupBox g1 = new GroupBox(); g1.SetBounds(12, 12, 760, 120); g1.Text = "四维属性（技能加点属性，直接覆写；默认改当前使用角色）";
    g1.Controls.Add(MkLabel("护盾", 15, 33, 40));
    txtStatShield = MkText(55, 30, 80, "500");
    g1.Controls.Add(txtStatShield);
    g1.Controls.Add(MkLabel("魔法", 155, 33, 40));
    txtStatMagic = MkText(195, 30, 80, "500");
    g1.Controls.Add(txtStatMagic);
    g1.Controls.Add(MkLabel("体力", 295, 33, 40));
    txtStatHP = MkText(335, 30, 80, "500");
    g1.Controls.Add(txtStatHP);
    g1.Controls.Add(MkLabel("攻击", 435, 33, 40));
    txtStatDmg = MkText(475, 30, 80, "500");
    g1.Controls.Add(txtStatDmg);
    g1.Controls.Add(MkBtn("设置四维属性", 580, 28, 110, delegate {
      SendUi("setplayerstats " + txtStatShield.Text.Trim() + " " + txtStatMagic.Text.Trim() + " " + txtStatHP.Text.Trim() + " " + txtStatDmg.Text.Trim(), "设置四维属性");
    }));
    Label l1 = MkHint("= setplayerstats <护盾> <魔法> <体力> <攻击>（用户实测顺序），直接覆写不消耗技能点。默认修改当前使用角色（Siris）；要改 Isa 的属性，先把当前角色切到 Isa（角色栏切换或在道具栏切到 Isa 页）。", 15, 68, 730);
    g1.Controls.Add(l1);
    p.Controls.Add(g1);

    GroupBox g3 = new GroupBox(); g3.SetBounds(12, 140, 760, 230); g3.Text = "战斗 / 特殊";
    g3.Controls.Add(MkBtn("无敌 开/关", 15, 28, 110, delegate {
      SendBatch(new string[] { "enablecheats", "god" }, "无敌切换"); godOn = !godOn;
    }));
    Label lg3 = MkHint("= enablecheats + god。无敌标志挂在玩家控制器上，但原生战斗流程会在部分节点重置——战斗后若失效，进下一场战斗前再点一次。", 135, 24, 610);
    g3.Controls.Add(lg3);
    g3.Controls.Add(MkBtn("进入屠龙战", 15, 68, 110, delegate {
      SendUi("setplayerdragonfight " + (txtDragonMap.Text.Trim() == "" ? "0" : txtDragonMap.Text.Trim()) + " " + (int)numDragonStart.Value, "进入屠龙战");
    }));
    g3.Controls.Add(MkLabel("强制地图", 135, 72, 60));
    txtDragonMap = MkText(198, 68, 150, "0");
    g3.Controls.Add(txtDragonMap);
    g3.Controls.Add(MkLabel("起始方式", 358, 72, 60));
    numDragonStart = new NumericUpDown(); numDragonStart.SetBounds(420, 68, 55, 23);
    numDragonStart.Minimum = 0; numDragonStart.Maximum = 99; numDragonStart.Value = 0;
    g3.Controls.Add(numDragonStart);
    Label ld1 = MkHint("= setplayerdragonfight <ForceMap> <FixedStart>（参数顺序以游戏内自动补全为准）。ForceMap=强制地图名（0=默认龙巢；可试 K02_Dragonlair_Def）；FixedStart=起始方式(0=常规)。原生实现，触发需满足剧情进度/场景条件，不满足时静默无效。", 15, 100, 730);
    g3.Controls.Add(ld1);
    g3.Controls.Add(MkBtn("进入收藏家战斗", 15, 140, 130, delegate {
      SendUi("setupcollector " + (int)numCollectorWeapon.Value, "进入收藏家战斗");
    }));
    g3.Controls.Add(MkLabel("起始武器索引", 155, 144, 90));
    numCollectorWeapon = new NumericUpDown(); numCollectorWeapon.SetBounds(248, 140, 55, 23);
    numCollectorWeapon.Minimum = 0; numCollectorWeapon.Maximum = 99; numCollectorWeapon.Value = 0;
    g3.Controls.Add(numCollectorWeapon);
    g3.Controls.Add(MkBtn("填满必杀+魔法槽", 320, 140, 140, delegate { SendUi("fillsuperandmagicmeters", "填满必杀+魔法槽"); }));
    Label l3 = MkHint("= setupcollector <ForceWeaponStart>（0=默认起始武器）。收藏家遭遇由游戏标志位驱动（SetupCollector 会置位），能否刷出受剧情进度/当前场景限制；屠龙战/收藏家都会切换场景，先存档。", 15, 172, 730);
    g3.Controls.Add(l3);
    p.Controls.Add(g3);
    return p;
  }

  // ================= 数据库 =================
  void OnLoad(object s, EventArgs e) {
    LoadItems();
    RebuildSubs();
    ApplyFilter();
    DetectGame(false);
  }

  void LoadItems() {
    items.Clear();
    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items.csv");
    if (!File.Exists(path)) { Log("未找到物品数据库 items.csv"); return; }
    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    for (int i = 1; i < lines.Length; i++) {
      string line = lines[i].TrimEnd('\r');
      if (line.Length == 0) continue;
      string[] f = line.Split(',');
      if (f.Length < 5) continue;
      ItemRow r = new ItemRow();
      r.Tpl = f[0]; r.Cat = f[1]; r.Sub = f[2]; r.Cn = f[3]; r.Note = f[4];
      items.Add(r);
    }
    Log("物品数据库已加载: " + items.Count + " 条");
  }

  void RebuildSubs() {
    cboSub.Items.Clear();
    string cat = cboCat.SelectedItem != null ? cboCat.SelectedItem.ToString() : "全部";
    List<string> subs = new List<string>();
    subs.Add("全部");
    foreach (ItemRow r in items) {
      if (cat != "全部" && r.Cat != cat) continue;
      string s = r.Sub;
      if (s.IndexOf('/') >= 0) s = "Boss专属";
      if (!subs.Contains(s)) subs.Add(s);
    }
    cboSub.Items.AddRange(subs.ToArray());
    cboSub.SelectedIndex = 0;
  }

  void ApplyFilter() {
    string cat = cboCat.SelectedItem != null ? cboCat.SelectedItem.ToString() : "全部";
    string sub = cboSub.SelectedItem != null ? cboSub.SelectedItem.ToString() : "全部";
    string q = txtSearch.Text.Trim().ToLower();
    lv.BeginUpdate();
    lv.Items.Clear();
    int n = 0;
    foreach (ItemRow r in items) {
      if (cat != "全部" && r.Cat != cat) continue;
      string rsub = r.Sub.IndexOf('/') >= 0 ? "Boss专属" : r.Sub;
      if (sub != "全部" && rsub != sub) continue;
      if (q != "" && r.Tpl.ToLower().IndexOf(q) < 0 && r.Cn.ToLower().IndexOf(q) < 0) continue;
      ListViewItem it = new ListViewItem(r.Tpl);
      it.SubItems.Add(r.Cn);
      it.SubItems.Add(r.Sub);
      it.SubItems.Add(r.Note);
      lv.Items.Add(it);
      n++;
      if (n >= 800) break;
    }
    lv.EndUpdate();
  }

  // ================= 游戏窗口 / 注入 =================
  void OnReconn(object s, EventArgs e) { DetectGame(true); }

  void DetectGame(bool verbose) {
    gameHwnd = IntPtr.Zero; gameTitle = "";
    Win32.EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!Win32.IsWindowVisible(h)) return true;
      StringBuilder sb = new StringBuilder(256);
      Win32.GetWindowText(h, sb, 256);
      string t = sb.ToString();
      if (t.StartsWith("Infinity Blade III")) { gameHwnd = h; gameTitle = t; return false; }
      return true;
    }, IntPtr.Zero);
    if (gameHwnd != IntPtr.Zero) {
      lblConn.Text = "游戏窗口: " + gameTitle + "  [已找到]";
      lblConn.ForeColor = Color.SeaGreen;
      if (verbose) Log("已找到游戏窗口: " + gameTitle);
    } else {
      lblConn.Text = "游戏窗口: 未找到（请先启动游戏）";
      lblConn.ForeColor = Color.Firebrick;
      if (verbose) Log("未找到游戏窗口（窗口标题以 Infinity Blade III 开头）");
    }
  }

  bool ConsoleOpen() {
    if (gameHwnd == IntPtr.Zero || !Win32.IsWindow(gameHwnd)) return false;
    Win32.RECT r; Win32.GetWindowRect(gameHwnd, out r);
    int w = r.R - r.L, h = r.B - r.T;
    if (w <= 0 || h <= 0) return false;
    Bitmap bmp = new Bitmap(w, h);
    using (Graphics g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      Win32.PrintWindow(gameHwnd, dc, 2);
      g.ReleaseHdc(dc);
    }
    bool found = false;
    for (int y = h * 7 / 8; y < h - 20; y += 2) {
      for (int x = 50; x < w - 50; x += 7) {
        Color c = bmp.GetPixel(x, y);
        if (c.G > 180 && c.R < 120 && c.B < 120) { found = true; break; }
      }
      if (found) break;
    }
    bmp.Dispose();
    return found;
  }

  void EnsureConsole() {
    for (int t = 0; t < 4; t++) {
      if (ConsoleOpen()) return;
      Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)0xBB, (IntPtr)0x1);
      Thread.Sleep(60);
      Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)0xBB, (IntPtr)0xC0000001);
      Thread.Sleep(700);
    }
    if (!ConsoleOpen()) throw new Exception("无法打开控制台（请确认游戏在运行、控制台键为 =）");
  }

  void FocusGame() {
    if (Win32.IsIconic(gameHwnd)) Win32.ShowWindow(gameHwnd, 9);
    Win32.SetForegroundWindow(gameHwnd);
    uint pid = 0; Win32.GetWindowThreadProcessId(gameHwnd, out pid);
    IntPtr fg = Win32.GetForegroundWindow();
    uint fgPid = 0, fgTid = Win32.GetWindowThreadProcessId(fg, out fgPid);
    uint myTid = Win32.GetCurrentThreadId();
    Win32.AttachThreadInput(myTid, fgTid, true);
    Win32.SetForegroundWindow(gameHwnd);
    Win32.BringWindowToTop(gameHwnd);
    Win32.AttachThreadInput(myTid, fgTid, false);
    Thread.Sleep(180);
  }

  // 字符 → 虚拟键码（游戏控制台只认 VK 按键事件；WM_CHAR 在刚打开的控制台上会被丢弃）
  static void CharToVk(char c, out byte vk, out bool shift) {
    shift = false; vk = 0;
    if (c >= 'a' && c <= 'z') vk = (byte)('A' + (c - 'a'));
    else if (c >= 'A' && c <= 'Z') { vk = (byte)c; shift = true; }
    else if (c >= '0' && c <= '9') vk = (byte)c;
    else if (c == ' ') vk = 0x20;
    else if (c == '_') { vk = 0xBD; shift = true; }  // Shift + OEM_MINUS
    else if (c == '-') vk = 0xBD;
    else if (c == '.') vk = 0xBE;
    else if (c == '/') vk = 0xBF;
    else {
      short s = Win32.VkKeyScan(c);
      vk = (byte)(s & 0xFF);
      shift = ((s >> 8) & 1) != 0;
    }
  }

  void SendOne(string cmd) {
    FocusGame();
    bool wasOpen = ConsoleOpen();
    if (!wasOpen) EnsureConsole();
    if (wasOpen) { // 控制台本来就开着：可能残留输入，先退格清空
      for (int i = 0; i < 40; i++) {
        Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)0x08, (IntPtr)0xE);
        Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)0x08, (IntPtr)0xC000000E);
      }
      Thread.Sleep(60);
    }
    FocusGame(); // 打字前再聚焦一次：外部程序可能在开控制台的间隙抢走前台（游戏失焦会自动关控制台）
    // 混合注入：首字符必须走 VK 按键（激活控制台字符输入态），其余用 WM_CHAR 直发
    //（刚打开的控制台会丢弃 WM_CHAR；纯 VK 又会丢 shift 映射——冒号变分号、下划线变减号）
    bool first = true;
    foreach (char c in cmd) {
      if (first) {
        byte vk; bool sh; CharToVk(c, out vk, out sh);
        if (vk == 0) continue;
        if (sh) {
          Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)0x10, (IntPtr)0x2A0001);
          Thread.Sleep(25);
        }
        Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)vk, (IntPtr)1);
        Thread.Sleep(35);
        Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)vk, (IntPtr)0xC0000001);
        Thread.Sleep(15);
        if (sh) {
          Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)0x10, (IntPtr)0xC02A0001);
          Thread.Sleep(10);
        }
        first = false;
      } else {
        Win32.PostMessage(gameHwnd, Win32.WM_CHAR, (IntPtr)c, IntPtr.Zero);
        Thread.Sleep(22);
      }
    }
    Thread.Sleep(80);
    FocusGame(); // 回车是关键动作，发送前确保仍在前台
    Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)0x0D, (IntPtr)0x1C0001);
    Thread.Sleep(50);
    Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)0x0D, (IntPtr)0xC01C0001);
    Thread.Sleep(500);
    // 无感化：控制台回车后不会自动关，主动收起
    if (ConsoleOpen()) {
      Win32.PostMessage(gameHwnd, Win32.WM_KEYDOWN, (IntPtr)0xBB, (IntPtr)0x1);
      Thread.Sleep(50);
      Win32.PostMessage(gameHwnd, Win32.WM_KEYUP, (IntPtr)0xBB, (IntPtr)0xC0000001);
      Thread.Sleep(250);
    }
  }

  // ---------- 后台发送 ----------
  delegate string[] BatchFn();

  void RunBg(string desc, string[] cmds, int intervalMs) {
    if (sending) { Log("正在发送中，请稍候…"); return; }
    DetectGame(false);
    if (gameHwnd == IntPtr.Zero) { Log("未找到游戏窗口，请先启动游戏再操作。"); return; }
    sending = true;
    Enabled = false;
    string[] list = cmds;
    string d = desc;
    new Thread(delegate() {
      try {
        foreach (string c in list) {
          SendOne(c);
          Log("已发送: " + c);
          if (list.Length > 1) Thread.Sleep(intervalMs);
        }
        BeginInvoke((MethodInvoker)delegate {
          Log("完成: " + d + " (" + list.Length + " 条命令)");
          ShowToast("✔ " + d + " 已生效 — 关闭人物栏重进查看效果");
        });
      } catch (Exception ex) {
        BeginInvoke((MethodInvoker)delegate { Log("失败: " + ex.Message); });
      } finally {
        BeginInvoke((MethodInvoker)delegate { Enabled = true; sending = false; });
      }
    }).Start();
  }

  void SendUi(string cmd, string desc) { RunBg(desc, new string[] { cmd }, 0); }
  void SendBatch(string[] cmds, string desc) { RunBg(desc, cmds, 500); }

  void SendPartXp(string cmd) {
    int n = (int)numPartTimes.Value;
    string[] list = new string[n];
    for (int i = 0; i < n; i++) list[i] = cmd + " 999999999";
    RunBg("部位经验灌注×" + n, list, 400);
  }

  void SendSelected() {
    if (lv.SelectedItems.Count == 0) { Log("请先在列表中选择一件物品"); return; }
    string tpl = lv.SelectedItems[0].Text;
    string sub = lv.SelectedItems[0].SubItems[2].Text;
    int n = (int)numItemCnt.Value;
    string[] list;
    string desc;
    if (sub.StartsWith("孔位")) {
      // 宝石：GiveItemOnce 只认装备模板表，宝石走 setplayergiverandomgem
      // 真实参数顺序（游戏自动补全确认）：RewardLevel, GoldScale, FavorSocketType
      // GoldScale=目标金币价值（引擎找 Cost 接近该值的模板，窗口外静默失败）
      string sock = sub.Length > 2 && char.IsDigit(sub[2]) ? sub.Substring(2, 1) : "0";
      int rl = (int)numRewardLevel.Value;
      string gs = txtGoldScale.Text.Trim();
      list = new string[n];
      for (int i = 0; i < n; i++) list[i] = "setplayergiverandomgem " + rl + " " + gs + " " + sock;
      desc = "发放宝石(" + tpl + "同孔位随机) ×" + n;
    } else {
      list = new string[n];
      for (int i = 0; i < n; i++) list[i] = "giveitemonce " + tpl;
      desc = "发放 " + tpl + " ×" + n;
    }
    RunBg(desc, list, 350);
  }

  // ================= 跳过片头动画（改 DefaultEngine.ini） =================
  const string ENGINE_INI = @"E:\IB3\SwordGame\Config\DefaultEngine.ini";
  bool IntroSkipOn() {
    string[] lines = File.ReadAllLines(ENGINE_INI);
    for (int i = 0; i < lines.Length; i++)
      if (lines[i].TrimStart().StartsWith(";+StartupMovies=Startup")) return true;
    return false;
  }
  void ToggleSkipIntro() {
    try {
      string[] lines = File.ReadAllLines(ENGINE_INI);
      bool on = IntroSkipOn();
      string bak = ENGINE_INI + ".orig";
      if (!File.Exists(bak)) File.Copy(ENGINE_INI, bak, false);
      for (int i = 0; i < lines.Length; i++) {
        string t = lines[i].TrimStart();
        if (on) {
          if (t.StartsWith(";+StartupMovies=Startup")) {
            int idx = lines[i].IndexOf(';');
            if (idx >= 0) lines[i] = lines[i].Remove(idx, 1);
          }
        } else {
          if (t.StartsWith("+StartupMovies=Startup")) {
            int idx = lines[i].IndexOf("+StartupMovies");
            if (idx >= 0) lines[i] = lines[i].Insert(idx, ";");
          }
        }
      }
      File.WriteAllLines(ENGINE_INI, lines);
      // 注意：Startup.bik 由启动器无条件播放，改文件名会导致游戏无法启动（已实测）——只关配置里的加播段
      ShowToast(on ? "已恢复片头加播（重启游戏生效）" : "已关闭片头加播段（重启游戏生效；Startup 本体仍在，彻底跳过需改 exe）");
      Log(on ? "片头加播: 已恢复" : "片头加播: 已关闭");
    } catch (Exception ex) { Log("片头设置失败: " + ex.Message); }
  }

  // ================= 浮窗提示 =================
  void ShowToast(string msg) {
    try {
      int x, y;
      if (gameHwnd != IntPtr.Zero && Win32.IsWindow(gameHwnd)) {
        Win32.RECT r; Win32.GetWindowRect(gameHwnd, out r);
        x = r.L + (r.R - r.L) / 2 - 260;
        y = r.B - 150;
      } else {
        x = Left + Width / 2 - 260;
        y = Top + Height - 120;
      }
      new ToastForm(msg, x, y).Show();
    } catch { }
  }

  // ================= 日志 =================
  void Log(string s) {
    string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine;
    if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { txtLog.AppendText(line); }); return; }
    txtLog.AppendText(line);
  }

  [STAThread]
  static void Main() {
    Application.EnableVisualStyles();
    Application.Run(new Mainform());
  }
}

// 浮窗提示：无边框置顶、不抢焦点，3 秒自动消失
class ToastForm : Form {
  public ToastForm(string msg, int x, int y) {
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    Location = new Point(x, y);
    Size = new Size(520, 64);
    BackColor = Color.FromArgb(28, 28, 30);
    ShowInTaskbar = false;
    TopMost = true;
    Label l = new Label();
    l.Dock = DockStyle.Fill;
    l.Text = msg;
    l.ForeColor = Color.White;
    l.TextAlign = ContentAlignment.MiddleCenter;
    l.Font = new Font("Microsoft YaHei", 10.5f);
    Controls.Add(l);
    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
    t.Interval = 3200;
    t.Tick += delegate { Close(); };
    t.Start();
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override CreateParams CreateParams {
    get {
      CreateParams cp = base.CreateParams;
      cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE：弹出时不夺游戏焦点
      cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW：不进 Alt-Tab
      return cp;
    }
  }
}

} // namespace
