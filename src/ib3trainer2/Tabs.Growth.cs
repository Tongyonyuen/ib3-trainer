// ============================================================================
// Tabs.Growth.cs — 成长类：属性四维 / 等级 / 技能点 / 生命
// 界面不出现内存地址（玩家无意义）：地址由「定位」三步向导自动绑定并写入
// 地址簿（后台记录；调试视图=右侧地址表 + ib3_addrs.ini）。
//   ①「目标」框填游戏内当前数值 → 点「定位」(首扫)
//   ②游戏里改变该属性(加点/换装) → 填新值再点「定位」(筛选，可多轮)
//   ③唯一命中自动绑定 → 之后填想要的值 → 写入 / 锁定
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  // 后台地址记录（键→地址），从地址簿载入、定位/写入时更新
  readonly Dictionary<string, long> statAddrs = new Dictionary<string, long>();

  public long GetStatAddr(string key) {
    long a;
    if (statAddrs.TryGetValue(key, out a) && a != 0) return a;
    AddrEntry ae = AddrBook.Get(key);
    if (ae != null) { statAddrs[key] = ae.Addr; return ae.Addr; }
    return 0;
  }

  TabPage BuildTabGrowth() {
    TabPage p = new TabPage("成长");
    p.BackColor = Theme.BG;
    p.ForeColor = Theme.Text;

    // ---- 属性四维 ----
    FlatGroupBox b1 = new FlatGroupBox(); b1.Title = "属性四维（临时值：重启/读档会被游戏重算）"; b1.SetBounds(8, 8, 810, 170);
    Label hLocG = Theme.MkHint("定位：填当前值→「定位」；游戏内改动后再填新值→「定位」；唯一命中自动绑定。", 14, 26, 790);
    hLocG.Height = 18;
    b1.Controls.Add(hLocG);
    int y = 50;
    RowStat(b1, ref y, "攻击", "stat.atk", "攻击力");
    RowStat(b1, ref y, "体力", "stat.sta", "体力");
    RowStat(b1, ref y, "护盾", "stat.shd", "护盾");
    RowStat(b1, ref y, "魔法", "stat.mag", "魔法");
    p.Controls.Add(b1);

    // ---- 等级 / 技能点 / 生命 / 最大生命 ----
    // 高度 114 → 142：多一行「最大生命」（原设计只到 950 高，这里往下延 28px 仍在页内）
    FlatGroupBox b2 = new FlatGroupBox(); b2.Title = "等级 / 技能点 / 生命"; b2.SetBounds(8, 184, 810, 142);
    y = 28;
    RowStat(b2, ref y, "等级", "lv.level", "等级（写入后请去加点）");
    RowStat(b2, ref y, "技能点", "lv.points", "技能点");
    RowStat(b2, ref y, "生命", "hp.cur", "生命（有滞后副本，写入时两处一起写）");
    RowStat(b2, ref y, "最大生命", "hp.max", "最大生命（写入后可回生命界面确认）");
    p.Controls.Add(b2);

    // ---- 掌握升阶（紧凑；实测：masterallowneditems <忽略> <目标等级>） ----
    FlatGroupBox b3 = new FlatGroupBox(); b3.Title = "掌握升阶（无感注入 · 实测可用）"; b3.SetBounds(8, 332, 810, 104);
    TextBox tLv = Theme.MkText(230, 34, 60, "10");
    Button bAll = Theme.MkButton("掌握全部物品 → 目标等级", 14, 30, 200, 30, delegate {
      string lv = tLv.Text.Trim();
      int n2;
      if (!int.TryParse(lv, out n2) || n2 < 1) { ToastMgr.Warn(I18n.T("目标等级请输入正整数")); return; }
      InjectCmd("masterallowneditems 1 " + lv, I18n.T("掌握全部物品至 ") + lv + I18n.T(" 级"));
      ToastMgr.Show(I18n.T("已发送：全部物品 → ") + lv + I18n.T(" 级掌握。等级/技能点会上涨，去加点界面分配。注意：掌握只升不降！"));
    });
    b3.Controls.Add(bAll);
    b3.Controls.Add(tLv);
    b3.Controls.Add(Theme.MkLabel("目标等级", 296, 37, 60));
    Label hMst = Theme.MkHint("实测：全部物品推到目标等级并掌握（角色等级+技能点随之上涨）。掌握只升不降——建议先在测试档验证。", 14, 68, 780);
    hMst.Height = 20;
    b3.Controls.Add(hMst);
    p.Controls.Add(b3);
    return p;
  }

  void RowStat(FlatGroupBox box, ref int y, string name, string key, string desc) {
    int yy = y;
    TextBox v = Theme.MkText(164, yy, 96, "0");
    Label status = Theme.MkLabel("", 456, yy + 2, 344);
    status.ForeColor = Theme.TextDim;
    // Label.AutoSize 默认为 true，会把宽度撑成文字长度 —— 本行文字含地址，容易越出卡片右沿。
    // 显式关掉并重设边界，让设计宽度真正生效。
    status.AutoSize = false;
    status.SetBounds(456, yy + 2, 344, 18);
    statStatus[key] = status;      // 登记，便于附着后统一刷新（校验/推断都要用）
    statName[key] = name;
    if (H != IntPtr.Zero) {
      UpdateStatStatus(key);
    } else if (GetStatAddr(key) != 0) {
      // 地址簿里有记录，但此刻没附着游戏 —— 只能提示「上次运行地址」，别谎称可用
      status.Text = I18n.T("已定位（上次运行地址）——附着后点「读当前」验证");
      status.ForeColor = Theme.TextDim;
    } else {
      status.Text = I18n.T("未定位——填当前数值后点「定位」");
      status.ForeColor = Theme.TextDim;
    }

    Button read = Theme.MkButton("读当前", 60, yy - 2, 62, 25, delegate {
      if (!RequireH()) return;
      long addr = GetStatAddr(key);
      if (addr == 0) { ToastMgr.Warn(I18n.T(name) + I18n.T(" 未定位：先在「目标」框填当前数值 → 点「定位」")); return; }
      string cur; string err;
      if (MemIO.ReadValue(H, addr, ScanType.I32, out cur, out err)) {
        v.Text = cur;
        Log(name + " 当前 = " + cur + " @0x" + addr.ToString("X"));
      } else {
        ToastMgr.Warn(I18n.T(name) + I18n.T(" 读取失败: ") + err + I18n.T("——点「定位」重新查找"));
        Log(name + " 读取失败: " + err + " @0x" + addr.ToString("X"));
      }
      UpdateStatStatus(key);   // 统一由它出状态文案（含「推断」标注与失效提示）
    });
    Button write = Theme.MkButton("写入", 268, yy - 2, 56, 25, delegate {
      long addr = GetStatAddr(key);
      if (addr == 0) { ToastMgr.Warn(I18n.T(name) + I18n.T(" 未定位：先填当前数值→「定位」")); return; }
      BookPut(key, name, addr, ScanType.I32, v.Text, "wizard");
      if (!WriteOne(key, desc, addr, ScanType.I32, v.Text, false, false, null)) return;
      if (key == "hp.cur") {   // 双副本：镜像一起写
        long m = GetStatAddr("hp.cur.m");
        if (m != 0) WriteOne("hp.cur.m", desc + "·镜像", m, ScanType.I32, v.Text, false, false, null);
      }
      status.Text = I18n.T("已写入（临时值）");
      status.ForeColor = Theme.Gold;
      if (key == "lv.level") ToastMgr.Warn(I18n.T("等级已写入（临时值）。请去加点界面分配新技能点。"));
      else ToastMgr.Warn(I18n.T("已写入 ") + I18n.T(name) + I18n.T("（临时值：重启/读档后会被游戏重算还原）"));
    });
    Button lockb = Theme.MkButton("锁定", 330, yy - 2, 56, 25, delegate {
      long addr = GetStatAddr(key);
      if (addr == 0) { ToastMgr.Warn(I18n.T(name) + I18n.T(" 未定位：先填当前数值→「定位」")); return; }
      BookPut(key, name, addr, ScanType.I32, v.Text, "wizard");
      if (!WriteOne(key, desc + "（锁定）", addr, ScanType.I32, v.Text, true, false, null)) return;
      if (key == "hp.cur") {   // 双副本：镜像一起锁定回写
        long m = GetStatAddr("hp.cur.m");
        if (m != 0) WriteOne("hp.cur.m", desc + "·镜像（锁定）", m, ScanType.I32, v.Text, true, false, null);
      }
      status.Text = I18n.T("已锁定·持续回写");
      status.ForeColor = Theme.Gold;
      ToastMgr.Warn(I18n.T(name) + I18n.T(" 已锁定持续回写（临时值：重启会还原；生命受伤时会有一次回补跳动）"));
    });
    Button locate = Theme.MkButton("定位", 392, yy - 2, 56, 25, delegate { LocateStat(v, status, key, name); });

    box.Controls.Add(Theme.MkLabel(name, 16, yy + 2, 44));
    box.Controls.Add(read);
    box.Controls.Add(Theme.MkLabel("目标", 130, yy + 2, 34));
    box.Controls.Add(v);
    box.Controls.Add(write);
    box.Controls.Add(lockb);
    box.Controls.Add(locate);
    box.Controls.Add(status);
    y += 28;
  }

  // ============ 定位三步向导 ============
  class LocateWiz { public List<ScanHit> Hits; public string LastSeed; }
  readonly Dictionary<string, LocateWiz> wizards = new Dictionary<string, LocateWiz>();

  void LocateStat(TextBox v, Label status, string key, string name) {
    if (!RequireH()) return;
    string seed = v.Text.Trim();
    LocateWiz w;
    wizards.TryGetValue(key, out w);
    if (w == null) {
      if (seed.Length == 0) { ToastMgr.Warn(I18n.T("第①步：在「目标」框填入游戏内当前的") + I18n.T(name) + I18n.T("数值，再点「定位」")); return; }
      RunBackground(I18n.T(name) + I18n.T(" 定位·首扫"), delegate {
        List<ScanHit> hits;
        try { hits = ScanCore.FirstScanKnown(H, ScanType.I32, TypeUtil.Encode(ScanType.I32, seed), ScanCore.MAX_HITS, null, null); }
        catch (Exception ex) { return "定位失败: " + ex.Message; }
        LocateWiz nw = new LocateWiz(); nw.Hits = hits;
        wizards[key] = nw;
        try {
          BeginInvoke((MethodInvoker)delegate {
            status.Text = I18n.T("首扫 ") + hits.Count + I18n.T(" 条候选");
            status.ForeColor = Theme.TextDim;
            ToastMgr.Show(I18n.T(name) + I18n.T(" 首扫 ") + hits.Count + I18n.T(" 条候选 — 去游戏里改变该属性(加点/换装)，把新值填框后再点「定位」"));
          });
        } catch { }
        return "定位[首扫] " + name + " = " + seed + " → 候选 " + hits.Count + " 条";
      });
      return;
    }
    if (seed.Length == 0 || w.Hits == null || w.Hits.Count == 0) { wizards.Remove(key); ToastMgr.Warn(I18n.T("候选已重置，请重新首扫")); return; }
    if (seed == w.LastSeed) { ToastMgr.Warn(I18n.T("值未变化：筛选不会收敛——请先在游戏里改变该属性，再把新值填框点「定位」")); return; }
    w.LastSeed = seed;
    RunBackground(I18n.T(name) + I18n.T(" 定位·筛选"), delegate {
      List<ScanHit> keep;
      try { keep = ScanCore.FilterHitsExact(H, ScanType.I32, w.Hits, FilterKind.Equal, seed, null, null); }
      catch (Exception ex) { return "筛选失败: " + ex.Message; }
      // 生命值 = 已知双副本（主册+镜像）：允许 2 条命中并双绑定；其余行仍要求唯一
      int accept = (key == "hp.cur") ? 2 : 1;
      if (keep.Count == accept) {
        long ad = keep[0].Addr;
        wizards.Remove(key);
        statAddrs[key] = ad;
        string cur; string err;
        MemIO.ReadValue(H, ad, ScanType.I32, out cur, out err);
        BookPut(key, name, ad, ScanType.I32, cur, "wizard");
        bool dual = false;
        if (accept == 2) {
          long m = keep[1].Addr;
          statAddrs[key + ".m"] = m;
          string mv; string merr;
          MemIO.ReadValue(H, m, ScanType.I32, out mv, out merr);
          BookPut(key + ".m", name + "·镜像", m, ScanType.I32, mv, "wizard");
          dual = true;
        }
        statInferred.Remove(key);     // 直接定位到 = 已核实，撤掉「推断」标记
        int gi = Array.IndexOf(StatGroup, key);
        try {
          // 涉及控件与共享字典，全部回到 UI 线程做
          BeginInvoke((MethodInvoker)delegate {
            status.Text = dual ? I18n.T("已定位·双副本") : I18n.T("已定位·可用");
            status.ForeColor = Theme.Ok;
            if (gi >= 0) DeriveSiblings(gi, ad);   // 四项属性连续排列 → 一次定位拿齐四项
            UpdateStatStatus(key);
            ToastMgr.Show(dual
              ? (I18n.T("已绑定 ") + I18n.T(name) + I18n.T("（双副本：两处一起读写）"))
              : (gi >= 0
                  ? (I18n.T("已绑定 ") + I18n.T(name) + I18n.T("，并已按结构偏移推断出同组另外三项（标注为「推断」，核对后再写）"))
                  : (I18n.T("已绑定 ") + I18n.T(name) + I18n.T(" — 填想要的值后「写入」或直接「锁定」"))));
          });
        } catch { }
        return "定位[筛选] " + name + " = " + seed + " → " + (dual ? "双副本命中（主册+镜像已绑定）" : "唯一命中（已绑定）") +
               (gi >= 0 ? "（同组其余三项已按偏移推断）" : "");
      }
      if (keep.Count == 0) {
        wizards.Remove(key);
        try { BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn(I18n.T(name) + I18n.T(" 筛选 0 条：值未变化或填错——已重置，可再来一轮")); }); } catch { }
        return "定位[筛选] " + name + " = " + seed + " → 0 条（已重置）";
      }
      w.Hits = keep;
      try {
        BeginInvoke((MethodInvoker)delegate {
          status.Text = I18n.T("剩 ") + keep.Count + I18n.T(" 条候选");
          ToastMgr.Show(I18n.T(name) + I18n.T(" 剩 ") + keep.Count + I18n.T(" 条候选 — 再改变一次该属性并把新值填框后点「定位」"));
        });
      } catch { }
      return "定位[筛选] " + name + " = " + seed + " → 剩 " + keep.Count + " 条";
    });
  }

  public void BookPut(string key, string desc, long addr, ScanType t, string val, string anchor) {
    AddrEntry ae = new AddrEntry();
    ae.Key = key; ae.Desc = desc; ae.Addr = addr; ae.Type = t; ae.Val = val;
    ae.Verified = true; ae.Anchor = anchor == null ? "" : anchor;
    AddrBook.Put(ae);
    QueueDockSync();   // 让自动绑定 / 定位向导 / 扫描入表的结果立刻出现在右侧地址表
  }

  // ---- 四项基本属性的结构偏移（实测：体力@+0 / 护盾@+4 / 攻击@+8 / 魔法@+0xC）----
  // 定位到任一项即可按偏移推出其余三项，省掉三次三步定位；推断出来的行会在状态栏
  // 明确标注「推断」，不伪装成已定位，避免用户没核对就往里写。
  static readonly string[] StatGroup = { "stat.sta", "stat.shd", "stat.atk", "stat.mag" };
  static readonly int[] StatOff = { 0, 4, 8, 0xC };

  readonly Dictionary<string, Label> statStatus = new Dictionary<string, Label>();
  readonly Dictionary<string, string> statName = new Dictionary<string, string>();
  readonly HashSet<string> statInferred = new HashSet<string>();

  // 语言切换后重刷全部状态标签。放这里是因为 UpdateStatStatus 只在定位/写入流程里被调，
  // 平时不会自己跑；不补这一下，切语言后这 8 行会停在切换前的文字（**不会被清空** ——
  // I18n.Retranslate 会跳过"被改写过"的控件，见 I18n.cs 的护栏）。
  // 只读：内部只查地址簿 + 一次 ReadProcessMemory，不写游戏内存、不写盘。
  public void RefreshStatStatusAll() {
    var keys = new List<string>(statStatus.Keys);   // 复制一份，防遍历中改动
    for (int i = 0; i < keys.Count; i++) {
      try { UpdateStatStatus(keys[i]); } catch { }
    }
  }

  // 刷新某一行属性的状态栏（读一次现值，让用户能直接和游戏内数字对照）
  void UpdateStatStatus(string key) {
    Label st;
    if (!statStatus.TryGetValue(key, out st) || st == null) return;
    long a = GetStatAddr(key);
    bool inf = statInferred.Contains(key);
    if (a == 0) {
      st.Text = I18n.T("未定位——填当前数值后点「定位」");
      st.ForeColor = Theme.TextDim;
      return;
    }
    if (H == IntPtr.Zero) {
      st.Text = (inf ? I18n.T("推断地址") : I18n.T("已定位（上次运行地址）")) + " @0x" + a.ToString("X");
      st.ForeColor = inf ? Theme.Warn : Theme.TextDim;
      return;
    }
    string cur; string err;
    if (MemIO.ReadValue(H, a, ScanType.I32, out cur, out err)) {
      // ★ 读数为 0 时别报"已定位"（2026-10-07 其他用户实测：打开就显示"已定位、现值 0"，
      //   实际是自动绑定认错了真身、把平凡值当成了数值）。四维/等级/最大生命不可能是 0。
      long zz;
      if (long.TryParse(cur, out zz) && zz == 0 && ZeroSuspicious(key)) {
        // 措辞不冤枉人：0 可能是合法的（技能点已排除；护盾等理论上也可能为 0），
        // 所以只陈述"读到 0"+ 给出自证方法，不直接断言失效。
        st.Text = I18n.T("@0x") + a.ToString("X") + I18n.T("  读数为 0 —— 若游戏里不是 0，点「定位」重绑");
        st.ForeColor = Theme.Warn;
        return;
      }
      // 文案控制在 344px 设计宽内（地址 14 字符 + 中文按 13px 估算；英文按 2 倍字符数估）
      st.Text = (inf ? I18n.T("推断 @0x") : I18n.T("已定位 @0x")) + a.ToString("X") + I18n.T("  现值 ") + cur +
                (inf ? I18n.T("（核对后再写）") : "");
      st.ForeColor = inf ? Theme.Warn : Theme.Ok;
    } else {
      st.Text = I18n.T("失效 @0x") + a.ToString("X") + I18n.T("——请重新定位");
      st.ForeColor = Theme.Warn;
    }
  }

  // ---- 四项基本属性在真身对象里的固定偏移（2026-10-07 实机确认）----
  //
  // 证据链：
  //   ① 会话A（10-06）真身 0x7FF4F65F0040，体力在 0x7FF4F65F1F50 → +0x1F10
  //   ② 会话B（10-07）真身 0x7FF4E75B0040，体力在 0x7FF4E75B1F50 → +0x1F10（真身搬家、偏移不变）
  //   ③ 把游戏内四维改成 11/31/21/41 后，全内存搜该四元组【只有 1 处命中】，正是 真身+0x1F10
  // 因此四项属性与金币(+0x2070)/筹码(+0x2094)一样，可以每次附着自动绑定，不再依赖地址簿
  // —— 这才是真正能随修改器迁移到别人机器上的形态。
  const int OFF_STAT_STA = 0x1F10;

  // 由后台附着线程调用：按真身结构自动绑定成长类字段。
  //
  // 每一条都经过「改值前后 + 真身搬家」双重对比（命中交集法），不是猜的：
  //   四维     +0x1F10/+4/+8/+0xC  体力/护盾/攻击/魔法（跨两次真身搬家园偏移不变）
  //   当前生命 +0x378              506 → 476 时只有它跟着变
  //   最大生命 +0x37C              15550 → 16050
  //   生命副本 +0x1E1C             滞后副本：受伤后仍留旧值，随当前生命一起写以刷新显示
  //   技能点   +0x1F3C             153 → 143
  // 等级尚未做「改值」验证（两次读都是 86），故不在此自动绑定，留给「定位」向导或后续确认。
  //   等级     +0x7B0              86 → 87 时只有它跟着变；同区块的 +0x1E14 仍留 86（滞后副本）
  static readonly string[] LiveKeys = {
    "stat.sta", "stat.shd", "stat.atk", "stat.mag",
    "hp.cur", "hp.cur.m", "hp.max", "lv.points", "lv.level"
  };
  static readonly string[] LiveNames = {
    "体力", "护盾", "攻击", "魔法",
    "生命", "生命·副本", "最大生命", "技能点", "等级"
  };
  static readonly int[] LiveOffs = {
    OFF_STAT_STA, OFF_STAT_STA + 4, OFF_STAT_STA + 8, OFF_STAT_STA + 12,
    0x378, 0x1E1C, 0x37C, 0x1F3C, 0x7B0
  };

  // 四维(idx 0..3)与等级(idx 8)是否"像真身"。
  // ★ 2026-10-07 用户纠正：**四维全相同是合法加点**（有人就爱那么加），不能当绑错判据 —— 已撤掉。
  //   真正要挡的只有"整组平凡"：四维**全 0**（实测的绑错身体特征，会让人以为数值真的是 0），
  //   以及等级为 0（等级不可能是 0）。单项为 0 不拦（免得把合法加点/空护盾误判成失效）。
  static bool StatGroupPlausible(string[] vals) {
    if (vals == null || vals.Length < 9) return false;
    int nonzero = 0;
    for (int i = 0; i < 4; i++) {
      long v;
      if (vals[i] == null || !long.TryParse(vals[i], out v)) return false;
      if (v < 0 || v > 100000000L) return false;
      if (v > 0) nonzero++;
    }
    if (nonzero == 0) return false;                    // 四维全 0 ⇒ 判绑错
    long lv;
    if (vals[8] == null || !long.TryParse(vals[8], out lv)) return false;
    if (lv <= 0 || lv > 100000000L) return false;      // 等级非零
    return true;
  }

  // 某个键"读数为 0"算不算可疑（=该地址已失效/绑错）。等级/四维/最大生命不可能是 0；
  // 技能点可以是 0（点满用完），当前生命可以是 0（刚倒下）—— 这两个不作可疑处理。
  static bool ZeroSuspicious(string key) {
    if (key == null) return false;
    if (key == "lv.points" || key == "hp.cur" || key == "hp.cur.m") return false;
    return true;
  }

  public void AutoBindStats(long live) {
    if (live == 0 || H == IntPtr.Zero) return;
    string[] vals = new string[LiveKeys.Length];
    for (int i = 0; i < LiveKeys.Length; i++) {
      string v; string err;
      if (!MemIO.ReadValue(H, live + LiveOffs[i], ScanType.I32, out v, out err)) continue;
      long n;
      // 值域校验：读出指针/浮点被当成整数这类垃圾时宁可不绑，
      // 也不要把错误地址当成「已定位」暴露给用户去写。
      if (!long.TryParse(v, out n) || n < 0 || n > 100000000L) continue;
      vals[i] = v;
    }
    int ok = 0;
    for (int i = 0; i < vals.Length; i++) if (vals[i] != null) ok++;
    if (ok == 0) {
      Log("成长类字段未能按真身结构自动绑定（偏移与当前游戏版本可能不符）——请用「定位」向导手工绑定");
      return;
    }
    // ★★ 2026-10-07 加强（其他用户实测反馈）：上面的 0..1e8 值域**挡不住"绑错身体"**——
    //   真身选错时那些偏移上读出来的往往是个平凡值，两种实测特征：
    //     ① 全 0（用户反馈："打开显示四维/等级/技能点已定位、现值 0"）
    //     ② 全 1（本机地址簿里确实留下过 stat.*=1/1/1/1）
    //   真身的四维不可能是 0、也不可能四项全相同；等级同样不可能是 0。
    //   ⇒ 这组数不自洽就**整组放弃写入**（连地址簿都不写，免得把错地址持久化下去），
    //     并明说改用「定位」向导。宁可显示"未定位"，也不要把错地址当"已定位"给用户去写。
    if (!StatGroupPlausible(vals)) {
      Log("成长类字段自动绑定**已放弃**：真身 + 固定偏移读出的四维/等级不合理（全 0 或四项全同）——" +
          "说明这次认到的真身不对（多半撞上了影子/空壳实例）。请用成长页各行的「定位」向导手工绑定。");
      return;
    }
    try {
      BeginInvoke((MethodInvoker)delegate {
        for (int i = 0; i < LiveKeys.Length; i++) {
          if (vals[i] == null) continue;
          string k = LiveKeys[i];
          long a = live + LiveOffs[i];
          statAddrs[k] = a;
          statInferred.Remove(k);   // 引擎绑定 = 权威来源，撤掉「推断」标记
          BookPut(k, statName.ContainsKey(k) ? statName[k] : LiveNames[i], a, ScanType.I32, vals[i], "engine");
          UpdateStatStatus(k);
        }
      });
    } catch { }
    Log("成长字段已按真身结构自动绑定 " + ok + "/" + LiveKeys.Length + " 项（生命/最大生命/技能点/四维）");
  }

  // 由同组已知项推出其余三项
  void DeriveSiblings(int gi, long addr) {
    long baseAddr = addr - StatOff[gi];
    for (int i = 0; i < StatGroup.Length; i++) {
      if (i == gi) continue;
      string k = StatGroup[i];
      long a = baseAddr + StatOff[i];
      statAddrs[k] = a;
      statInferred.Add(k);
      string cur; string err;
      if (!MemIO.ReadValue(H, a, ScanType.I32, out cur, out err)) cur = "0";
      BookPut(k, statName.ContainsKey(k) ? statName[k] : k, a, ScanType.I32, cur, "infer");
    }
    // 不在这里刷状态栏 —— 本函数只应在 UI 线程上被调用（status 控件跨线程写会出问题），
    // 由调用方在 BeginInvoke 里统一 UpdateStatStatus。
  }

  // 地址簿「值域校验」（附着后调用）。
  // 堆地址每局会漂移，而过期地址往往仍然「已提交 + 可写」—— MemIO.SafeWrite 的页校验
  // 挡不住它，直接写进去就是静默改坏别的数据。所以这里先逐条读一遍：
  // 读得到才算可复用，读不到就明确标失效，逼用户重新定位。
  public void ValidateBookAddrs() {
    if (H == IntPtr.Zero) return;
    List<AddrEntry> snap = AddrBook.Snapshot();
    if (snap.Count == 0) {
      Log("地址簿为空：金币/筹码会在附着时自动绑定（不受影响）；成长属性需各自用「定位」向导绑定一次");
      return;
    }
    int ok = 0, bad = 0;
    foreach (AddrEntry e in snap) {
      if (e.Addr == 0) { bad++; continue; }
      string cur; string err;
      if (MemIO.ReadValue(H, e.Addr, e.Type, out cur, out err)) ok++; else bad++;
    }
    Log("地址簿校验：" + ok + " 条可读 / " + bad + " 条已失效" +
        (bad > 0 ? "（失效项要在对应页重新定位 —— 堆地址每局会漂移）" : ""));
    try {
      BeginInvoke((MethodInvoker)delegate {
        for (int i = 0; i < StatGroup.Length; i++) UpdateStatStatus(StatGroup[i]);
        UpdateStatStatus("hp.cur"); UpdateStatStatus("hp.cur.m"); UpdateStatStatus("hp.max");
        UpdateStatStatus("lv.level"); UpdateStatStatus("lv.points");
      });
    } catch { }
  }
}

} // namespace
