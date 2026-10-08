// ============================================================================
// I18n.cs — 中文/英文界面切换（2026-10-07 新增）
//
// 为什么不用 .resx：build.sh 是写死的 csc 编译（无 MSBuild / 无 resgen），
//   没有资源嵌入路径，所以语言表就是一个普通字典。
//
// 三条设计取舍（都由本项目的结构决定）：
//   ① 键 = **中文原文**，`T()` 查不到就原样返回 ⇒ 自然降级：漏翻的地方显示中文，
//      永远不会出现空白或 "gem.read" 这种键名。所以字典可以增量补。
//   ② 翻译分两层：
//      · **渲染点**：Theme.cs 把下拉项/页签标题/列头/分组标题都自己画，在画的地方
//        过一道 T() 就覆盖了一大片（而且不碰存储值 —— 像 "全部" 这种哨兵值必须
//        保持原样，`ItemDb.cs` 在拿它做比较）。
//      · **Register**：真正的控件（Button/Label/Hint）在建立时登记 setter，
//        切换语言时由 Retranslate() 重设 .Text。
//   ③ **不重建控件树**。本项目一切控件都是绝对定位 + 固定宽高，且 Layout.cs 按
//      "构造时采集的设计坐标"缩放（重建会双重缩放 S²，运行期新建的控件又永不缩放）；
//      运行时还有 4 个定时器（其中 FreezeTick 会写游戏内存）与后台线程 BeginInvoke。
//      原地重译体验一样（点一下立刻变），风险为零。
//      ⚠ 由此得到一条硬约束：**两种语言共用同一套几何** —— 切换语言不许改任何
//        bounds（下一次布局就会被 _design 覆盖回去）。所以英文要写短；
//        实在放不下的地方改的是**设计尺寸本身**（中文界面也跟着变）。
//        经验换算：中文字宽约 13px、拉丁字母约 6.5px ⇒ 英文可用字符数≈中文的 2 倍。
//
// C# 5（csc v4.0.30319，无 /langversion）：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;   // Register 的 key 是 Control（Retranslate 要读它当前的 .Text 来判断有没有被改过）

namespace Ib3Trainer2 {

static class BuildInfo {
  // ① 显示串：只给人看（「关于」弹窗 AboutForm.cs:42）。**不参与版本比较**，可以是任意形式。
  public const string Version = "v1.1.4 · 2026-10-08";

  // ② 机器可比的版本三元组：**发版时必须与 git tag 严格一致**（三处一起改）。
  //    自动更新靠它比对远端 tag —— 见 Updater.TryParseTag / Updater.IsNewer。
  //
  //  ⚠ 漏改的后果不是"显示错一个数字"。更新器拿**本地** SemVer 去比**远端 tag**：
  //    本地若停留在旧值，远端新 tag 永远"更大" ⇒ **每次都提示有新版本**；
  //    而点更新下载下来的还是同一份（它仍自称旧版本）⇒ **无限更新循环**，只有手动
  //    选「跳过此版本」才能停。
  //    v1.1.1 就漏改过一次（那份 exe 自称 1.1.0，而 tag 是 v1.1.1）。发版前请核对三处：
  //      git tag  ==  Version 串里的版本号  ==  Major.Minor.Patch
  public const int Major = 1, Minor = 1, Patch = 4;

  // ③ 随包数据文件的修订号（items.csv / ib3_gems.ini / SwordGame.upk / image\）。
  //    自动更新只换 exe ⇒ 数据文件变了就把这里 +1，并在 Release 说明里写一行 `DATA_REV: <n>`；
  //    更新器看到远端 DATA_REV 更大就**不自动替换**，改为打开下载页。
  public const int DataRev = 1;

  public static string SemVer { get { return Major + "." + Minor + "." + Patch; } }
}

static class I18n {
  public const string ZH = "zh", EN = "en";

  static string lang = ZH;
  public static string Lang { get { return lang; } }
  public static bool IsEn { get { return lang == EN; } }

  // 登记表：key 用"控件本身"（引用相等）。
  // ★ 为什么按 key 覆盖而不是追加：**动态文案的控件会被反复登记**（状态栏每 2 秒一次、
  //   扫描状态、宝石信息栏…）。若用 List 追加，① 永不释放、② Retranslate 越来越慢、
  //   ③ 旧条目会把新文案顶掉。用字典覆盖 = 永远只有"最后一次登记的那条"，天然正确。
  class Entry {
    public string Zh;      // 中文原文（切换语言时照它重查）
    public string Last;    // 我们上次贴上去的那串
    public Action Apply;
  }
  static readonly Dictionary<Control, Entry> setters = new Dictionary<Control, Entry>();

  // 建控件/改文案时调用：立刻按当前语言贴一次，同时登记下来（同控件覆盖旧的）
  public static void Register(Control c, Action<string> setter, string zh) {
    if (c == null || setter == null) return;
    Entry e = new Entry();
    e.Zh = zh;
    e.Apply = delegate { setter(T(zh)); };
    e.Last = T(zh);
    setters[c] = e;
    setter(e.Last);
  }

  // 语言切换后要重跑的"渲染函数"，**键固定** ⇒ 反复登记只留一条（扫描/刷新流程里会反复跑到）。
  // 用途：**拼接出来**的文案没法进字典（整串含变量，T() 命不中），只能登记一个重画函数，
  // 由它自己用 T() 拼当前语言。例：地址栏「地址: 0x…（地址簿）」、备份路径那一行。
  // ★ 这类"只在设置那一刻翻一次"的地方，切换语言后**不会自己变** —— 这就是用户报的那类漏网。
  static readonly Dictionary<string, Action> rerenders = new Dictionary<string, Action>();
  public static void OnLang(string key, Action render) {
    if (key == null || render == null) return;
    rerenders[key] = render;
  }

  // 重设"真控件"的 .Text；自绘部分（页签/列头/下拉/分组标题）由调用方 Invalidate 触发重画。
  // ★ 护栏：**只在我们上次贴的那串还挂在那儿时才重贴**。
  //   有些控件是"用占位串建出来、之后由各自的逻辑改写"的（成长页 8 个状态标签、
  //   宝石信息栏 lblGemInfo、地址栏 "地址: 0x…"）。它们若被强行重贴，就退回建控件时的
  //   占位串 —— 实测会变成**空白**。被改过的一律跳过，交给各自的刷新路径
  //   （ToggleLang 里会补调 OnGemSel / RefreshAddrTable）。
  public static void Retranslate() {
    var list = new List<KeyValuePair<Control, Entry>>(setters);
    for (int i = 0; i < list.Count; i++) {
      Control c = list[i].Key; Entry e = list[i].Value;
      if (c == null || c.IsDisposed) continue;
      if (c.Text != e.Last) continue;          // 别人改过 → 不碰
      e.Last = T(e.Zh);
      try { e.Apply(); } catch { }
    }
    // 再跑一遍登记过的"重画函数"（拼接文案由它们自己按新语言重拼）
    var rs = new List<Action>(rerenders.Values);
    for (int i = 0; i < rs.Count; i++) {
      try { rs[i](); } catch { }
    }
  }

  // 手动切换（会写盘，此后手选优先于自动判定）
  public static void SetLang(string l, string trainerDir) {
    lang = (l == EN) ? EN : ZH;
    SavePref(trainerDir);
  }

  // 切换按钮上的文字 = **目标**语言
  public static string ToggleLabel { get { return lang == EN ? "中文" : "English"; } }

  // ---------- 语言判定 / 持久化（ib3_ui.ini） ----------
  // ⚠ 不要往 ib3_paths.ini 加键：Launcher.SaveConfig 是 File.WriteAllLines 整文件
  //   截断成一行（Launcher.cs:38），加进去会被抹掉。
  static string PrefPath(string trainerDir) { return Path.Combine(trainerDir, "ib3_ui.ini"); }

  // 没有配置文件 → 按系统语言自动判定（**不落盘**）；有 → 用手选值
  public static void Init(string trainerDir) {
    string saved = LoadPref(trainerDir);
    if (saved != null) { lang = saved; return; }
    AutoDetect();
  }

  // 系统 UI 语言以 zh 开头 → 中文；其余（含 en 与第三语言）一律英文 —— 英文是安全的国际默认
  public static void AutoDetect() {
    try {
      string n = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
      lang = (n == "zh") ? ZH : EN;
    } catch { lang = ZH; }
  }

  static string LoadPref(string trainerDir) {
    try {
      string f = PrefPath(trainerDir);
      if (!File.Exists(f)) return null;
      foreach (string raw in File.ReadAllLines(f)) {
        string t = raw.Trim();
        if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
        int eq = t.IndexOf('=');
        if (eq <= 0) continue;
        if (t.Substring(0, eq).Trim().ToLowerInvariant() != "lang") continue;
        string v = t.Substring(eq + 1).Trim().ToLowerInvariant();
        if (v == ZH || v == EN) return v;
      }
    } catch { }
    return null;
  }

  static void SavePref(string trainerDir) {
    try {
      // UTF-8 无 BOM（与 ib3_addrs.ini 一致，见 Recipes.cs）
      File.WriteAllLines(PrefPath(trainerDir), new string[] {
        "; IB3 训练器2 界面偏好（自动生成，可手编）",
        "; lang = zh | en　（留空或删掉本文件 = 按系统语言自动判定）",
        "lang=" + lang
      }, new UTF8Encoding(false));
    } catch { }
  }

  // ==========================================================================
  // 字典：中文原文 → 英文
  // 约定：**英文按字符数控制在中文字符数的 2 倍以内**（中文 ≈13px/字、拉丁 ≈6.5px/字），
  //   否则在固定宽度的控件里会被截断。窄控件（338px 地址坞 / 344px 忙碌浮层）尤其要短。
  // ==========================================================================
  static readonly Dictionary<string, string> map = Build();

  public static string T(string zh) {
    if (zh == null || lang == ZH) return zh;
    string en;
    if (map.TryGetValue(zh, out en)) return en;
    return zh;   // 未命中 → 原样返回（漏翻显示中文，不出空白/键名）
  }

  static Dictionary<string, string> Build() {
    var d = new Dictionary<string, string>();
    // ---- 窗口标题 / 横幅占位 ----
    d["无尽之剑Ⅲ修改器"] = "Infinity Blade III Trainer";
    d["无 尽 之 剑 Ⅲ"] = "INFINITY BLADE III";

    // ---- 标题条：语言切换 / 署名 / 关于弹窗 ----
    d["by Andrew Tong"] = "by Andrew Tong";          // 署名不翻（人名）
    d["作者"] = "Author";
    d["版本"] = "Version";
    d["界面语言"] = "UI language";
    d["关闭"] = "Close";
    d["单机游戏修改器，仅供个人离线使用。改动会写进存档，请先自行备份。"] =
      "Offline trainer for a single-player game. Changes are written to your save — back it up first.";

    // ---- 页签（StyleTab 把 ItemSize 固定 120×36，标题必须短）----
    d["战斗·商店"] = "Combat";
    d["物品发放"] = "Items";
    d["成长"] = "Growth";
    d["宝石·背包"] = "Gems";
    d["存档导出/导入"] = "Save";
    d["发现模式"] = "Scan";

    // ---- 列头（DarkListView 用 EndEllipsis，长了只截断不破版）----
    d["描述"] = "Description";
    d["地址"] = "Address";
    d["类型"] = "Type";
    d["设定"] = "Set";
    d["值"] = "Value";
    d["当前"] = "Current";
    d["检查项"] = "Check";
    d["结果"] = "Result";
    d["说明"] = "Notes";
    d["模板名"] = "Template";
    d["中文名"] = "Name";   // 该列英文界面显示的是官方英文名（没有的才回退中文），所以不叫 Chinese Name
    d["子分类"] = "Subcategory";
    d["备注"] = "Note";
    d["融合"] = "Cooked";
    d["当前显示值"] = "Display Value";
    d["记录地址"] = "Record Addr";
    d["可改"] = "Editable";

    // ---- 分组标题（OnPaint 无裁剪矩形；右侧地址坞只有 338px，那几个要特别短）----
    d["地址表（写入即入表：可撤销 / 可锁定 / 可保存）"] = "Address Table (undo / lock / save)";
    d["金币 / 筹码（唯一命中才自动采用）"] = "Gold / Chips (auto-bind on unique match)";
    d["属性四维（临时值：重启/读档会被游戏重算）"] = "Stats (temp: recomputed on reload)";
    d["等级 / 技能点 / 生命"] = "Level / Skill Points / HP";
    d["战斗（无感注入：点按钮直接执行，零窗口变化）"] = "Combat (silent injection, no window change)";
    d["商店 / 战斗触发（无感注入）"] = "Shop / Battle Triggers (silent injection)";
    d["发放（无感注入：点击即生效，零窗口变化）"] = "Grant (silent injection, instant effect)";
    d["掌握升阶（无感注入 · 实测可用）"] = "Mastery Upgrade (verified)";
    d["修改（改完请在游戏里切一次场景才会落盘）"] = "Edit (switch scene in-game to save)";
    d["① 导出：把某个槽位打包成一个 .ib3save（先校验本地缓存是否自洽）"] =
      "1) Export: pack one slot into a .ib3save (verifies local cache first)";
    d["② 导入：选 .ib3save → 自动完整性校验 → 应用（会先把本机 Cloud\\ 整目录备份）"] =
      "2) Import: pick .ib3save -> verify -> apply (backs up Cloud\\ first)";

    // ---- 底排 / 右上 ----
    d["启动游戏"] = "Launch Game";                    // 用户要求去掉"（中文）"：训练器只唤起启动器，不注入语言
    d["游戏目录…"] = "Game Folder…";
    d["立即附着"] = "Attach Now";
    d["保存地址簿"] = "Save Addrs";
    d["部署/校验 UPK"] = "Deploy/Verify UPK";

    // ---- 状态栏 / 忙碌浮层（多与数值拼接，在各调用点包 T()；这里放纯文案与片段）----
    d["未附着"] = "Not attached";
    d["游戏已退出"] = "Game exited";
    d["未找到游戏进程 — 请点「启动游戏」"] = "Game process not found — click \"Launch Game\"";
    d["未附着游戏进程 — 先点「启动游戏」或「立即附着」"] =
      "Not attached — click \"Launch Game\" or \"Attach Now\" first";
    d["发现同名进程但映像路径不符，忽略"] = "Same-name process found but image path differs; ignored";
    d["OpenProcess 失败 err="] = "OpenProcess failed err=";
    d["已附着 IB3.exe  PID="] = "Attached to IB3.exe  PID=";
    d["⚠ UPK 未部署 —— 物品发放/掌握升阶会失败"] = "⚠ UPK not deployed — item grant / mastery will fail";
    d["正在初始化（加载数据库 / 构建界面）"] = "Initializing (loading data / building UI)";
    d["读取背包宝石"] = "Reading gem bag";
    d["内存快照（可写私有区）"] = "Memory snapshot (writable private)";
    d["注入 "] = "Injecting ";
    d["校验存档包"] = "Verifying save package";
    d["校验并打包 槽"] = "Verifying & packing slot ";
    d["写出存档包"] = "Writing save package";
    d["导入存档（备份 + 写入）"] = "Importing save (backup + write)";
    d["校验本地缓存"] = "Verifying local cache";
    d["首次扫描"] = "First scan";
    d["筛选 "] = "Filter ";
    d["1.5s 稳定化"] = "Stabilize 1.5s";      // RunBackground 用的带空格版
    d["1.5s稳定化"] = "Stabilize 1.5s";       // 按钮上的字面量没有空格 —— 两个键都要有，否则静默落回中文
    d["锁定"] = "Lock";
    d["无敌切换"] = "God mode toggle";
    d["取消"] = "Cancel";

    // ---- 通用按钮/标签（跨页复用）----
    d["应用"] = "Apply";
    d["写入"] = "Write";
    d["删除"] = "Delete";
    d["定位"] = "Locate";
    d["搜索"] = "Search";
    d["数量"] = "Count";
    d["目标"] = "Target";
    d["目标值"] = "Target Value";
    d["目标等级"] = "Target Level";
    d["主分类"] = "Category";
    d["槽位"] = "Slot";
    d["扫描定位"] = "Scan & Locate";
    d["重新扫描"] = "Rescan";
    d["重新校验"] = "Re-verify";
    d["重载数据库"] = "Reload DB";
    d["读当前"] = "Read Current";
    d["读取背包"] = "Read Bag";
    d["填上限"] = "Fill Max";
    d["修改项"] = "Field";
    d["候选数组"] = "Arrays";
    d["值 / 参照值"] = "Value / Ref";
    d["快照筛:变化"] = "Snap: changed";
    d["快照筛:等于"] = "Snap: equals";
    d["等于参照值"] = "Equals ref";
    d["筛选:增大"] = "Filter: larger";
    d["试写+1"] = "Test write +1";
    d["新快照"] = "New snapshot";
    d["减小"] = "Lower";
    d["变化"] = "Changed";
    d["未变化"] = "Unchanged";
    d["加入地址表"] = "Add to table";
    d["撤销选中"] = "Undo selected";
    d["条目描述"] = "Entry description";
    d["失效"] = "stale";
    d["备份位置…"] = "Backup folder…";
    d["导出为 .ib3save"] = "Export as .ib3save";
    d["选择存档文件…"] = "Choose save file…";
    d["应用导入"] = "Apply import";
    d["强制导入"] = "Force import";
    d["扫描本地存档"] = "Scan local saves";
    d["掌握全部物品 → 目标等级"] = "Master all items → target level";
    d["游戏当前值"] = "Current in-game value";
    d["金币"] = "Gold";
    d["筹码"] = "Chips";
    d["无敌 开/关"] = "God mode on/off";
    d["充满超能/魔法"] = "Fill Super/Magic";
    d["击杀当前 Boss"] = "Kill Boss";
    d["全部解锁"] = "Unlock All";
    d["发放所有物品"] = "Give All Items";
    d["发放选中物品"] = "Give Selected";
    d["商店刷新一轮稀有宝石"] = "Refresh Rare Gems";
    d["触发龙战"] = "Trigger Dragon";
    d["触发收藏家战斗"] = "Trigger Collector";
    d["复制中文名"] = "Copy CN name";
    d["复制模板名"] = "Copy template";
    d["地址: 未定位"] = "Addr: not located";

    // ---- 说明文字（MkHint）。经验：英文 ≈ 中文 2 倍字符以内才不会挤 ----
    d["写入只进可写私有页（拒绝映像/Guard）；临时值（属性/等级/HP）重启会还原。"] =
      "Writable private pages only; temp values (stats/level/HP) reset on restart.";
    d["唯一命中可直接入表锁定；多命中就改值后再筛选。"] =
      "One match → lock directly; multiple → change value and re-filter.";
    d["定位：填当前值→「定位」；游戏内改动后再填新值→「定位」；唯一命中自动绑定。"] =
      "Enter current value → Locate; change it in-game, enter new → Locate.";
    d["流程: 快照 → 游戏内做动作/打探针 → 变化 → 1.5s稳定化。"] =
      "Flow: snapshot → act in-game → Changed → stabilize 1.5s.";
    d["无敌=enablecheats+god（管理器自动定位）；击杀Boss仅战斗中有效。"] =
      "God = enablecheats+god (auto-located); Kill Boss works in battle only.";
    d["命令经无感注入执行；条件不满足时游戏会静默忽略（日志显示成功/失败）。"] =
      "Runs via silent injection; silently ignored if conditions fail (see log).";
    d["龙战/收藏家为置位式触发：在下一场符合条件的战斗中生效，受地形与原敌人类型影响。"] =
      "Triggers arm the next eligible battle; terrain and enemy type still apply.";
    d["浏览器立即可用；发放按钮 = 无感注入（点即入包）。路由：宝石→刷商店；材料/药水→消耗品；藏宝图→钥匙；装备→未拥有才发。"] =
      "Browser is instant; grant buttons inject silently. Routing: gems→shop; materials/potions→consumables; maps→keys; gear→only if not owned.";
    d["附着游戏后金币/筹码会按真身结构自动绑定（跨存档自愈）；也可手动输入当前值扫描定位。"] =
      "Gold/chips auto-bind to the real player object once attached; you can also scan manually.";
    d["实测：全部物品推到目标等级并掌握（角色等级+技能点随之上涨）。掌握只升不降——建议先在测试档验证。"] =
      "Maxes all owned items to the target level (level + skill points rise too). Mastery never decreases — test on a spare save.";
    d["尚未选择存档包。"] = "No save package selected yet.";

    // ==== Tabs.Save.cs（存档导出/导入）========================================
    // 由子代理逐条包裹后回报。拼接串是**按片段**翻的（"前缀" + 变量 + "后缀"），
    // 所以这里会有大量纯标点/半句的键 —— 那是正常的，别当成重复或垃圾清理掉。
    d[" / SHA1 一致"] = " / SHA1 OK";
    d[" / SHA1 不符（缓存 "] = " / SHA1 mismatch (cache ";
    d[" > 明文 "] = " > plain ";
    d[" contentLen 超出明文长度"] = " contentLen exceeds plain length";
    d[" —— 无法原地改写（本工具不新增文档条目）。请先在游戏里存过一次该槽。"] =
      " — cannot rewrite in place (this tool never adds doc entries). Save that slot in-game first.";
    d[" —— 现在可以启动游戏了"] = " — you can start the game now";
    d[" → 槽 "] = " → slot ";
    d[" ⚠ 与 CloudDocIndex 不符"] = " ⚠ differs from CloudDocIndex";
    d[" 不同——应用时会按包的槽号改写）"] = "; import rewrites to the package slot)";
    d[" 与本机下标一致 ✅（不会成为孤儿）"] = " matches the local index ✅ (will not become an orphan)";
    d[" 个条目，"] = " entries, ";
    d[" 失败（偏移越界）"] = " failed (offset out of range)";
    d[" 失败："] = " failed: ";
    d[" 字节"] = " bytes";
    d[" 字节解析到底，终点 == contentLen，尾部全零"] = " bytes parsed; end == contentLen, tail all zero";
    d[" 字节，6 个条目）"] = " bytes, 6 entries)";
    d[" 字节，尾部"] = " bytes, tail ";
    d[" 条不一致 —— 见校验表；备份在 "] = " mismatches — see the table; backup at ";
    d[" 条不一致（见下表）——先修好来源再导出，否则导出的包本身就有问题。"] =
      " mismatches (see table) — fix the source first, or the package is bad too.";
    d[" 条不一致（见校验表）。备份："] = " mismatches (see table). Backup: ";
    d[" 条全部通过，可以放心导出。"] = " entries all pass — safe to export.";
    d[" 条，该槽 4 条 + CurrentSlot 都能对上"] = " entries; the slot's 4 + CurrentSlot all match";
    d[" 解密失败："] = " decrypt failed: ";
    d[" 项必需检查未通过——请勿直接导入。"] = " required checks failed; do not import.";
    d[" 颗"] = "";                       // 中文量词无对应词；英文行读作 "Main: bag gems 3 / shop 1"
    d[" 颗 / 随身商店 "] = " / shop ";
    d["(SHA1 不符) "] = " (SHA1 mismatch) ";
    d["(新建) "] = " (new) ";
    d["(缓存无记录) "] = " (no cache record) ";
    d["(缺失) "] = " (missing) ";
    d["(解密失败) "] = " (decrypt failed) ";
    d["/4 文件）"] = "/4 files)";
    d["1. 容器结构（magic / 版本 / 条目数 / 无截断）"] = "1. Container structure (magic/version/count)";
    d["10. 本机目标状态（会被覆盖的文件 / 缓存够不够原地改写）"] =
      "10. Local target state (files overwritten / in-place OK)";
    d["2. 该槽 4 个文件齐全"] = "2. All 4 slot files present";
    d["3. 每个条目都能解密"] = "3. Every entry decrypts";
    d["4. 包内 LocalFileHeaderCache 可解析且含该槽 4 条"] = "4. Bundled cache parses; has the slot's 4 docs";
    d["5. 哈希匹配 SHA1(明文[0:contentLen]) == 包内缓存记录"] = "5. SHA1(plain[0:contentLen]) == bundled cache record";
    d["6. 主档以 FString \"ValidSave\" 开头，且 tagged property 端到端可解析"] =
      "6. Main save starts with FString \"ValidSave\"; parses end-to-end";
    d["7. 槽元数据可解析，且含 CloudDocIndex + CharacterName"] = "7. Slot meta parses; has CloudDocIndex + CharacterName";
    d["8. _CurrentSlot 在 0..2 且是纯 i32"] = "8. _CurrentSlot in 0..2 and plain i32";
    d["9. 交叉核对：本机列表下标 vs 进来的 CloudDocIndex"] = "9. Cross-check: local index vs incoming CloudDocIndex";
    d["B / 明文 "] = "B / plain ";
    d["B(将被覆盖) "] = "B (will be overwritten) ";
    d["B）"] = "B)";
    d["IB3 存档包"] = "IB3 save package";
    d["\"，CloudDocIndex="] = "\", CloudDocIndex=";
    d["\r\n主档：宝石数未取到（"] = "\r\nMain: gem count unavailable (";
    d["\r\n主档：背包宝石 "] = "\r\nMain: bag gems ";
    d["\r\n现在可以启动游戏（点底部「启动游戏」）。"] = "\r\nYou can start the game now (click Launch Game below).";
    d["——两侧文档列表顺序/条数不同。导入后该槽可能被当作孤儿（点了也读不到）。"] =
      " — the two doc lists differ; after import the slot may be treated as an orphan (unclickable).";
    d["… 实际 "] = "… actual ";
    d["…）"] = "…)";
    d["⚠ 孤儿陷阱：进来的 CloudDocIndex="] = "⚠ Orphan trap: incoming CloudDocIndex=";
    d["⚠ 导入已写入，但复验有 "] = "⚠ Import written, but re-verify found ";
    d["✅ 导入完成：槽 "] = "✅ Import done: slot ";
    d["✅ 必需检查全部通过，可以应用导入。"] = "✅ Required checks passed.";
    d["❌ 有 "] = "❌ ";
    d["　CloudDocIndex "] = " CloudDocIndex ";
    d["　_CurrentSlot "] = " _CurrentSlot ";
    d["　地图 "] = "  map ";
    d["　等级 "] = "  level ";
    d["　角色 "] = "  char ";
    d["　金币 "] = "  gold ";
    d["。备份："] = ". Backup: ";
    d["不一致/无法验证："] = "Mismatch / unverifiable: ";
    d["主档 / 备份主档 / 槽元数据 / 备份槽元数据 4/4 都在"] = "Main / backup main / slot meta / backup slot meta all 4/4";
    d["主档缺失或包内缓存不可用"] = "Main save missing or bundled cache unusable";
    d["依赖第 4 项：包内缓存不可用"] = "Depends on item 4: bundled cache unusable";
    d["入口存档：槽 "] = "Source save: slot ";
    d["全零"] = "all zero";
    d["内部错误：缓存体长被改变（"] = "Internal error: cache body length changed (";
    d["写 "] = "Write ";
    d["写 LocalFileHeaderCache 失败："] = "Cannot write LocalFileHeaderCache: ";
    d["写盘失败："] = "Write failed: ";
    d["包内缓存解密失败："] = "Bundled cache decrypt failed: ";
    d["包内缓存解析失败："] = "Bundled cache parse failed: ";
    d["包内缓存里没有该主档的条目或 contentLen 越界（contentLen="] =
      "No main-save entry in bundled cache, or contentLen out of range (contentLen=";
    d["包里没有 LocalFileHeaderCache（导入方拿不到权威 contentLen）"] =
      "No LocalFileHeaderCache in package (importer cannot get the authoritative contentLen)";
    d["包里没有 LocalFileHeaderCache，无法同步 contentLen"] = "Package has no LocalFileHeaderCache; cannot sync contentLen";
    d["包里缺 "] = "Package lacks ";
    d["包里缺 _CurrentSlot"] = "Package lacks _CurrentSlot";
    d["备份失败，已中止导入："] = "Backup failed; import aborted: ";
    d["失败"] = "Fail";
    d["存档包读不出来，后面的检查无法进行。"] = "Cannot read the package; further checks skipped.";
    d["导入前整目录备份到："] = "Backup to: ";
    d["导入后把默认启动槽切到该槽"] = "Set default start slot to it";
    d["导入完成（槽 "] = "Import done (slot ";
    d["导入已写入，但复验有 "] = "Import written, but re-verify found ";
    d["导入异常："] = "Import error: ";
    d["导出异常："] = "Export error: ";
    d["导出成功："] = "Exported: ";
    d["导出会带上该槽的 4 个文件 + 本机的 LocalFileHeaderCache + _CurrentSlot，导入方靠缓存里的 contentLen 做校验。"] =
      "Export packs the slot's 4 files + this machine's LocalFileHeaderCache + _CurrentSlot (importer verifies via cached contentLen).";
    d["属性解析失败："] = "Property parse failed: ";
    d["已导出槽 "] = "Exported slot ";
    d["已重新扫描本地存档槽位"] = "Local saves rescanned";
    d["强制导入会跳过「必需检查未通过」的阻拦，直接覆盖本机的存档文件。\n\n虽然仍会先整目录备份，但强烈建议先看清校验表里的失败项。\n\n确定要继续吗？"] =
      "Force import bypasses the failed-required-check block and overwrites this machine's save files.\n\nIt still backs up the whole folder first, but read the failing items in the table.\n\nContinue?";
    d["强制导入确认"] = "Confirm Force Import";
    d["必需检查未通过，已阻止导入（要硬来请点「强制导入」）"] = "Required checks failed; import blocked (use Force import to override)";
    d["所有文件"] = "All files";
    d["扫描失败: "] = "Scan failed: ";
    d["找不到本机 Cloud 目录："] = "Local Cloud folder not found: ";
    d["找不到真档目录："] = "Cloud folder not found: ";
    d["改写本机缓存条目 "] = "Rewrite local cache entry ";
    d["文件 "] = "file ";
    d["无"] = "none";
    d["明文只有 "] = "Plain text only ";
    d["最近导入："] = "Last import: ";
    d["本地缓存不自洽，已拒绝导出（见下表）"] = "Local cache inconsistent; export refused (see table)";
    d["本地缓存有 "] = "Local cache has ";
    d["本地缓存自洽："] = "Local cache consistent: ";
    d["本机 LocalFileHeaderCache 不可用"] = "Local LocalFileHeaderCache unusable";
    d["本机 LocalFileHeaderCache 读不出/解析失败（"] = "Local LocalFileHeaderCache unreadable or unparsable (";
    d["本机找不到 Cloud 目录（"] = "No local Cloud folder (";
    d["本机文档列表里没有 "] = "Local doc list has no ";
    d["本机没有 Cloud 目录："] = "No local Cloud folder: ";
    d["本机缓存缺条目，无法原地改写："] = "Local cache missing entries; cannot rewrite in place: ";
    d["本机缓存解密失败："] = "Local cache decrypt failed: ";
    d["本机缓存解析失败"] = "Local cache parse failed";
    d["本机缓存解析失败："] = "Local cache parse failed: ";
    d["本机缓存读不出"] = "Local cache unreadable";
    d["本机缓存里没有 "] = "Local cache has no ";
    d["来源机文档列表 "] = "Source doc list ";
    d["槽 "] = "Slot ";
    d["槽号 "] = "Slot ";
    d["没读出 CloudDocIndex/CharacterName（CloudDocIndex="] = "No CloudDocIndex/CharacterName read (CloudDocIndex=";
    d["游戏正在运行 —— 请先完全关闭 IB3.exe 再导入"] = "Game is running — fully close IB3.exe before importing";
    d["目录 "] = "Dir ";
    d["磁盘 "] = "Disk ";
    d["第 7 项没读出 CloudDocIndex，无法核对（本机下标 "] = "Item 7 read no CloudDocIndex; cannot check (local index ";
    d["缓存解密失败: "] = "Cache decrypt failed: ";
    d["缓存解密失败："] = "Cache decrypt failed: ";
    d["缓存解析失败: "] = "Cache parse failed: ";
    d["缓存解析失败："] = "Cache parse failed: ";
    d["缓存里缺："] = "Cache missing: ";
    d["缺 "] = "Missing ";
    d["缺少文件："] = "Missing file: ";
    d["缺少："] = "Missing: ";
    d["角色名 \""] = "Character \"";
    d["解密失败："] = "Decrypt failed: ";
    d["警告"] = "Warn";
    d["请先选择并校验一个 .ib3save"] = "Pick and verify a .ib3save first";
    d["读不到 "] = "Cannot read ";
    d["读不到 LocalFileHeaderCache: "] = "Cannot read LocalFileHeaderCache: ";
    d["读取存档包失败："] = "Cannot read save package: ";
    d["读本机 LocalFileHeaderCache 失败："] = "Cannot read local LocalFileHeaderCache: ";
    d["还没有选择存档包"] = "No save package selected yet";
    d["进来的 CloudDocIndex="] = "Incoming CloudDocIndex=";
    d["选择【导入前整目录备份】放到哪个文件夹（每次导入会在其下新建 cloud_backup_时间戳 子目录）"] =
      "Pick the folder for pre-import full backups (each import creates a cloud_backup_<timestamp> subfolder inside it)";
    d["通过"] = "Pass";
    d["非零 ⚠"] = "non-zero ⚠";
    d["首属性 ValidSave；整档 "] = "First property ValidSave; whole ";
    d["（"] = " (";
    d["（与包的槽号 "] = " (differs from package slot ";
    d["（先确认游戏装过/进过一次存档）"] = " (install the game and save once)";
    d["（在来源机列表里的下标也正好是 "] = " (its index in the source list is also ";
    d["（来源机列表里没有这条）"] = " (not in the source list)";
    d["（磁盘 "] = " (disk ";
    d["（该槽在本机是孤儿槽，例如 2 号槽）——进来的 CloudDocIndex="] =
      " (orphan slot locally, e.g. slot 2) — incoming CloudDocIndex=";
    d["）"] = ")";
    d["）。备份在 "] = "). Backup at ";
    d["），已中止写盘"] = "); write aborted";
    d["），无法交叉核对"] = "); cannot cross-check";
    d["，"] = ", ";
    d["，但本机列表里的下标是 "] = ", but the local index is ";
    d["，已中止"] = "; aborted";
    d["，明文 "] = ", plain ";
    d["，槽 "] = ", slot ";
    d["，角色名="] = ", name=";
    d["："] = ": ";
    d["｜本机缓存 5 条都在，可原地改写 SHA1/contentLen（长度不变）"] =
      "| all 5 local cache entries present; SHA1/contentLen rewritable in place";
    d["6 个条目全部带 \"yeK \" 魔数且体长为 16 的倍数"] = "All 6 entries carry \"yeK \" magic; body length is a multiple of 16";
    d["5 个文件（4 槽文件 + _CurrentSlot）的 SHA1 与包内缓存逐条一致"] =
      "All 5 files (4 slot + _CurrentSlot) match the bundled cache SHA1";
    d["👉 必需检查全部通过，可以点「应用导入」。"] = "👉 Required checks passed — click Apply import.";
    d["👉 有必需检查未通过，请勿直接导入（要硬来只能点「强制导入」，仍会先备份）。"] =
      "👉 Required checks failed — do not import (Force import still backs up first).";

    // ==== Tabs.Gems / Growth / Items / Combat / Misc / Scan ====================
    // 同样按**片段**翻（"前缀" + 变量 + "后缀"），因此有大量以空格开头/结尾的键 —— 空格是有意义的，
    // 不要"顺手清理"。★ 标记的两条是**故意翻成空串**，见注释。
    // ---- Tabs.Gems.cs ----
    d["先选中一颗宝石"] = "Select a gem first";
    d["上限不清楚："] = "Limit unknown: ";
    d[" —— 请自己填一个目标"] = " — enter a target yourself";
    d["数值"] = "value";
    d["提示：若宝石列表与游戏内背包不一致，请先在游戏里存一次档（切一次场景），再点「读取背包」重新扫描"] =
      "If the gem list differs from your bag: save in-game (switch scene), then click \"Read Bag\"";
    d["疑似背包"] = "likely bag";
    d["疑似商店"] = "likely shop";
    d["未知"] = "unknown";
    d["数组 #"] = "Array #";
    d[" 条 · "] = " items · ";
    d["Tier 0-255 / 数值"] = "Tier 0-255 / value";
    d[" / 数值"] = " / value";
    d["选中一行后即可修改。"] = "Select a row to edit.";
    d["模板 "] = "Template ";
    d["   名索引 0x"] = "   name idx 0x";
    d["   记录 0x"] = "   record 0x";
    d["类型：加法型（RecipeBoostAmount="] = "Type: additive (RecipeBoostAmount=";
    d["）　显示值 = "] = ")  value = ";
    d["合法 Tier 0 ~ 255（uint8 上限）→ 显示值 "] = "Valid Tier 0~255 (uint8 cap) -> value ";
    d["类型：下标型（UpgradeTier "] = "Type: indexed (UpgradeTier ";
    d[" 档）　显示值 = "] = " tiers)  value = ";
    d["⚠ 合法 Tier 只有 "] = "⚠ Valid Tier ";
    d["；写超范围 = 越界读表 → 垃圾值/崩溃，本页已强制夹住。"] = "; out of range = OOB table read -> garbage/crash; clamped here.";
    d["⚠ 该模板的显示值算不出来（"] = "⚠ Value not computable for this template (";
    d["）→ 只能按 Tier 改档位。"] = "); edit Tier only.";
    d["类型：未知（无 RecipeBoostAmount 也无 UpgradeTier）→ 本页拒绝写入，避免写坏。"] =
      "Type: unknown (no RecipeBoostAmount / UpgradeTier) -> write refused.";
    d["改完必须回游戏切一次场景（进/出熔接室）才会写进存档。"] = "Switch scene once in-game (enter/leave forge) to save.";
    d["重新定位当前活数组失败（"] = "Re-locate current live array failed (";
    d["待写拷贝所属的数组已全部失效（"] = "All arrays holding the copies to write are gone (";
    d[" 条）—— 请重新点「读取背包」后再试"] = " items) — click \"Read Bag\" and retry";
    d["选中的那颗宝石所在数组已失效（游戏重分配过）—— 请重新点「读取背包」后再试"] =
      "Selected gem's array is gone (game reallocated) — click \"Read Bag\" and retry";
    d["先在列表选中一颗宝石"] = "Select a gem in the list first";
    d["该宝石类型未知，拒绝写入"] = "Unknown gem type; write refused";
    d["目标值不是整数"] = "Target value is not an integer";
    d["这颗宝石的显示值算不出来（"] = "Display value not computable for this gem (";
    d["）—— 请把「修改项」改成 Tier 来改档位"] = "); set Field to Tier to edit the tier";
    d["目标 "] = "Target ";
    d[" 无法达成（受基数/增量/档位/pct 上限约束）"] = " unreachable (base/step/tier/pct caps)";
    d["Tier 必须在 "] = "Tier must be in ";
    d["（该宝石类型限制）"] = " (this gem type)";
    d["写入取消："] = "Write cancelled: ";
    d["已改 "] = "Changed ";
    d["（显示值 "] = " (value ";
    d["，写 "] = ", wrote ";
    d[" 份拷贝"] = " copies";
    d[" + 融合标记×"] = " + cooked flag×";
    d["  ★ 界面会立刻变；存进存档请切一次场景"] = "  ★ UI updates instantly; switch scene to save";
    d["写入不完整（"] = "Incomplete write (";
    d["）："] = "): ";
    // ---- Tabs.Growth.cs ----
    d["目标等级请输入正整数"] = "Target level must be a positive integer";
    d["掌握全部物品至 "] = "Master all items to level ";
    d[" 级"] = "";   // ★ 故意空串：EN 读作 "Master all items to level 10"
    d["已发送：全部物品 → "] = "Sent: all items → ";
    d[" 级掌握。等级/技能点会上涨，去加点界面分配。注意：掌握只升不降！"] =
      " mastery. Level/skill points rise — assign them in-game. Note: mastery never drops!";
    d["已定位（上次运行地址）——附着后点「读当前」验证"] = "Located (last run) — attach, then Read Current";
    d["未定位——填当前数值后点「定位」"] = "Not located — enter the current value, then Locate";
    d[" 未定位：先在「目标」框填当前数值 → 点「定位」"] = " not located: enter the current value in Target → Locate";
    d[" 读取失败: "] = " read failed: ";
    d["——点「定位」重新查找"] = " — click Locate to search again";
    d[" 未定位：先填当前数值→「定位」"] = " not located: enter the current value → Locate";
    d["已写入（临时值）"] = "Written (temp value)";
    d["等级已写入（临时值）。请去加点界面分配新技能点。"] = "Level written (temp). Assign the new skill points in-game.";
    d["已写入 "] = "Wrote ";
    d["（临时值：重启/读档后会被游戏重算还原）"] = " (temp: recomputed on restart/reload)";
    d["已锁定·持续回写"] = "Locked · rewriting";
    d[" 已锁定持续回写（临时值：重启会还原；生命受伤时会有一次回补跳动）"] =
      " locked, rewriting (temp: resets on restart; HP jumps back once)";
    d["第①步：在「目标」框填入游戏内当前的"] = "Step 1: enter the in-game ";
    d["数值，再点「定位」"] = " value in Target, then Locate";
    d[" 定位·首扫"] = " locate · first scan";
    d["首扫 "] = "First scan ";
    d[" 条候选"] = " candidates";
    d[" 首扫 "] = " first scan: ";
    d[" 条候选 — 去游戏里改变该属性(加点/换装)，把新值填框后再点「定位」"] =
      " candidates — change it in-game (spend/equip), enter the new value, Locate again";
    d["候选已重置，请重新首扫"] = "Candidates reset; run the first scan again";
    d["值未变化：筛选不会收敛——请先在游戏里改变该属性，再把新值填框点「定位」"] =
      "Value unchanged: filtering won't converge — change it in-game, then enter the new value and Locate";
    d[" 定位·筛选"] = " locate · filter";
    d["已定位·双副本"] = "Located · 2 copies";
    d["已定位·可用"] = "Located · usable";
    d["已绑定 "] = "Bound ";
    d["（双副本：两处一起读写）"] = " (2 copies: read/write both)";
    d["，并已按结构偏移推断出同组另外三项（标注为「推断」，核对后再写）"] =
      ", other 3 stats of this group inferred by offset (marked \"inferred\" — verify before writing)";
    d[" — 填想要的值后「写入」或直接「锁定」"] = " — enter the value, then Write or Lock";
    d[" 筛选 0 条：值未变化或填错——已重置，可再来一轮"] = " filter: 0 hits — value unchanged or typo; reset, try again";
    d["剩 "] = "Left ";
    d[" 剩 "] = " left: ";
    d[" 条候选 — 再改变一次该属性并把新值填框后点「定位」"] = " candidates — change it again, enter the new value and Locate";
    d["推断地址"] = "Inferred addr";
    d["已定位（上次运行地址）"] = "Located (last run)";
    d["推断 @0x"] = "Inferred @0x";
    d["已定位 @0x"] = "Located @0x";
    d["  现值 "] = "  now ";
    d["（核对后再写）"] = " (verify)";
    d["失效 @0x"] = "Stale @0x";
    d["——请重新定位"] = " — relocate";
    d["  读数为 0 —— 若游戏里不是 0，点「定位」重绑"] = "  reads 0 — if the game shows otherwise, click Locate";
    // 这 8 条同时是成长页的行标签（MkLabel(name) 绑定那一刻过 T()）
    d["攻击"] = "Attack";
    d["体力"] = "Stamina";
    d["护盾"] = "Shield";
    d["魔法"] = "Magic";
    d["等级"] = "Level";
    d["技能点"] = "Skills";        // 行标签只有 44px 宽，用 Skills 比 Skill Pts 稳
    d["生命"] = "Health";
    d["最大生命"] = "MaxHP";
    d["目标"] = "Tgt";             // 覆写前面的 Target：该标签只有 34px，Target 会溢出到值输入框
    // ---- Tabs.Items.cs ----
    d["先选中一行"] = "Select a row first";
    d["已复制模板名: "] = "Template copied: ";
    d["已复制中文名"] = "Chinese name copied";
    d["先在列表选中一行"] = "Select a row in the list first";
    d["发放材料 "] = "Grant material ";
    d["已发放材料 ×"] = "Granted material ×";
    d["（去背包消耗品区查看）"] = " (see bag · consumables)";
    d["发放体力回满药水 ×"] = "Grant full-heal potion ×";
    d["已发放体力回满药水 ×"] = "Granted full-heal potion ×";
    d["发放消耗品(类型 "] = "Grant consumable (type ";
    d["已发放消耗品(类型 "] = "Granted consumable (type ";
    d["把商店刷成 "] = "Stock shop with ";
    d["已把随身商店刷成该宝石 ×"] = "Shop restocked with this gem ×";
    d["——进物品栏·随身商店购买即入袋（金币可改）"] = " — buy it in inventory · store to keep it (gold editable)";
    d["发放藏宝图 "] = "Grant treasure map ";
    d["已发放藏宝图（钥匙通道）"] = "Treasure map granted (key slot)";
    d["发放 "] = "Grant ";
    d["发放所有物品（全套）"] = "Give all items (full set)";
    // ---- Tabs.Combat.cs ----
    d["无敌失败："] = "God failed: ";
    d["无敌失败：CheatManager 未生成"] = "God failed: CheatManager not created";
    d["无敌已开启（god）"] = "God mode ON (god)";
    d["无敌已关闭（god）"] = "God mode OFF (god)";
    d["充满超能/魔法槽"] = "Fill super/magic";
    d["地址: 0x"] = "Addr: 0x";
    d["（地址簿）"] = " (addr book)";
    d["商店刷新一轮稀有宝石（进物品栏·随身商店查看）"] = "Refresh rare gems (see inventory)";
    d["已置位屠龙战：在下一场符合条件的战斗中生效，受地形与原敌人类型影响"] =
      "Dragon fight armed: next eligible battle (terrain/enemy apply)";
    d["已置位收藏家遭遇：在下一场符合条件的战斗中生效，受地形与原敌人类型影响"] =
      "Collector encounter armed: next eligible battle (terrain/enemy apply)";
    // ---- Tabs.Misc.cs ----
    d["（真身结构·自动绑定）"] = " (real player object · auto-bound)";
    d["请输入游戏内当前数值"] = "Enter the current in-game value";
    d["金币定位"] = "Locate gold";
    d["筹码定位"] = "Locate chips";
    d["（已定位 "] = " (located ";
    d["已定位，可填写目标值后写入"] = " located — enter a target and write";
    d["命中 "] = "Hits: ";
    d[" 条，无法自动采用：请去发现模式做两遍扫描"] = " — can't auto-bind: run two scans in the Scan tab";
    d["请先扫描定位金币地址"] = "Locate the gold address first";
    d["金币已设置为 "] = "Gold set to ";
    d["（随存档持久）"] = " (persists with save)";
    d["请先扫描定位筹码地址"] = "Locate the chips address first";
    d["筹码已设置为 "] = "Chips set to ";
    d["（持久性未验证，建议重启确认）"] = " (persistence unverified; restart to confirm)";
    // ---- Tabs.Scan.cs ----
    d["已请求取消…"] = "Cancel requested…";
    d["已有扫描任务在执行（可点「取消」）"] = "A scan is already running (click Cancel)";
    d["请输入当前已知数值（或先用「新快照」走未知值模式）"] = "Enter the known value (or use New snapshot first)";
    d["首扫命中 "] = "First scan: ";
    d[" 条"] = " hits";
    d["（唯一，可试写+1 验证）"] = " (unique — verify with Test write +1)";
    d["请先做「首次扫描」或「快照筛」得到候选"] = "Run First scan or Snapshot filter first";
    d["「等于参照值」需要填写参照值"] = "\"Equals ref\" needs a reference value";
    d["筛选:"] = "Filter:";
    d["筛选后剩 "] = "After filter: ";
    d["请先「新快照」"] = "Take a new snapshot first";
    d["「快照筛:等于」需要填写参照值"] = "\"Snap: equals\" needs a reference value";
    d["快照:等于"] = "Snap: equals";
    d[" 命中 "] = " hits: ";
    d["（截断）"] = " (truncated)";
    d["快照:变化"] = "Snap: changed";
    d["（截断，建议换类型或再筛）"] = " (truncated — change type or filter more)";
    d["没有可稳定化的候选"] = "No candidates to stabilize";
    d["稳定化"] = "Stabilized";
    d["稳定化后剩 "] = "After stabilize: ";
    d[" 条（被游戏自己重算的已剔除）"] = " left (game-recomputed ones dropped)";
    d["先在结果里选中一行"] = "Select a row in the results first";
    d["读取失败: "] = "Read failed: ";
    d["试写失败: "] = "Test write failed: ";
    d["已试写 +1 @0x"] = "Test wrote +1 @0x";
    d["（回游戏看现象确认）"] = " (check the game to confirm)";
    d["已加入右侧地址表（未锁定）"] = "Added to the address table (unlocked)";
    // ---- 地址簿里的条目描述（**持久化数据**：BookPut 写进 ib3_addrs.ini）----
    // 这些**不在写入处翻**，而是在右侧地址表**渲染时**过 T()（Ib3Trainer2.cs 的 RefreshAddrTable）——
    // 否则会把"当时的语言"烤进 ini，换语言后文件里就是混的。描述列只有 90px，长了会被省略号截断，
    // 所以英文一律取短。
    d["攻击力"] = "Attack";
    d["等级（写入后请去加点）"] = "Level (spend pts)";
    d["生命（有滞后副本，写入时两处一起写）"] = "Health (mirror copy)";
    d["最大生命（写入后可回生命界面确认）"] = "Max HP";
    d["生命·副本"] = "Health · mirror";
    d["·镜像"] = " · mirror";
    d["（锁定）"] = " (locked)";
    d["·镜像（锁定）"] = " · mirror (locked)";
    d["候选@0x"] = "Cand @0x";

    // ==== Tabs.Save.cs 解析器诊断原文（Ib3Crypt / Ib3Cache / Ib3Props / Ib3Bundle）====
    // 这些是 `err = ...` 出参，最终显示在校验表的「说明」列或提示里。
    // ★ 协议常量与"被比较"的 token 一律没包：`"yeK "`、`"IB3SAVE1"`、`"None"`、
    //   StructProperty/ArrayProperty/ByteProperty/BoolProperty 等类型分派串，以及
    //   `" @"`、`" cnt="` 这类纯分隔符（英文里也一样）—— 它们出现在上面这些英文句子里时保持原样。
    d["空数据"] = "Empty data";
    d["缺少 \"yeK \" 魔数（未加密 / 文件损坏 / 不是 Cloud\\ 里的文件）"] = "No \"yeK \" magic (unencrypted / corrupt / not a Cloud\\ file)";
    d["只有魔数没有内容"] = "Magic, no body";
    d["加密体 "] = "cipher ";
    d[" 字节不是 16 的倍数"] = " bytes not a multiple of 16";
    d["解密失败: "] = "Decrypt failed: ";
    d["缓存体太短"] = "Cache body too short";
    d["版本号不是 2（="] = "Version not 2 (=";
    d["条目数不合理（="] = "Bad entry count (=";
    d["条目 "] = "Entry ";
    d[" 被截断"] = " truncated";
    d[" 哈希长度非法（"] = " bad hash len (";
    d[" 路径越界"] = " path out of range";
    d[" 文档名越界"] = " doc name out of range";
    d[" contentLen 越界"] = " contentLen out of range";
    d[" contentLen 不合理（"] = " bad contentLen (";
    d[" 哈希不是 40 位"] = " hash length != 40";
    d[" 末位非 0"] = " last int != 0";
    d["字符串头越界 @"] = "String header out of range @";
    d["字符串越界（长 "] = "String out of range (len ";
    d["宽字符串越界 @"] = "Wide string out of range @";
    d["嵌套过深"] = "Nesting too deep";
    d["属性越界 @"] = "Property out of range @";
    d["标签头越界 @ "] = "Tag header out of range @ ";
    d[" 的 size<0"] = " size<0";
    d["StructProperty 缺结构体名 @ "] = "StructProperty missing struct name @ ";
    d["数组元素数越界 @ "] = "Array count out of range @ ";
    d["数组元素数不合理（"] = "Bad array count (";
    d["）@ "] = ") @ ";
    d["数组 size<4 @ "] = "Array size<4 @ ";
    d["数组 "] = "Array ";
    d[" 元素无法解析（rem="] = " elements unparsable (rem=";
    d["ByteProperty 越界 @ "] = "ByteProperty out of range @ ";
    d["BoolProperty 越界 @ "] = "BoolProperty out of range @ ";
    d[" 的 "] = " ";
    d[" 越界"] = " out of range";
    d["未知属性类型 "] = "Unknown property type ";
    d["明文太短"] = "Plain too short";
    d["开头不是 -1 哨兵（不是 UE3 tagged property 档）"] = "No -1 sentinel at start (not a UE3 tagged property save)";
    d["首属性是 \""] = "First property is \"";
    d["\"，期望 \""] = "\", expected \"";
    d["结构解析结束于 "] = "Struct parse ended at ";
    d["，与缓存里的 contentLen "] = ", cache contentLen ";
    d[" 不符"] = " mismatch";
    d["尾部零填充区 @"] = "Tail zero-pad @";
    d[" 非零"] = " non-zero";
    d["数组元素数不合理 @ "] = "Bad array count @ ";
    d[" 元素无法解析"] = " elements unparsable";
    d["文件太小（"] = "File too small (";
    d[" 字节）"] = " bytes)";
    d["魔数不是 IB3SAVE1（不是本工具导出的存档包）"] = "Magic is not IB3SAVE1 (not a package from this tool)";
    d["容器版本 "] = "Container version ";
    d[" 不支持（本工具只认 "] = " unsupported (only ";
    d[" 不合理"] = " invalid";
    d["条目数 "] = "Entry count ";
    d[" 名长被截断"] = " name length truncated";
    d[" 名长非法（"] = " bad name length (";
    d[" 数据长被截断"] = " data length truncated";
    d["）数据长非法或被截断（"] = ") data length bad or truncated (";
    d["条目名重复："] = "Duplicate entry name: ";
    d["包尾部多出 "] = "Extra ";
    d[" 字节（文件被追加/损坏）"] = " bytes at tail (appended/corrupt)";

    // ---- 物品分类（items.csv 的 主分类 / 子分类；封闭集合，全部收进来）----
    // 分类同时出现在下拉框（走 StyleCombo 渲染点）与列表单元格（系统绘制 ⇒ 在填行时过 T()）
    d["装备"] = "Gear";
    d["宝石"] = "Gems";
    d["收集品"] = "Collectibles";
    d["消耗品"] = "Consumables";
    d["盔甲"] = "Armor";
    d["头盔"] = "Helmet";
    d["盾牌"] = "Shield";
    d["戒指"] = "Ring";
    d["单手武器"] = "One-handed";
    d["双武器"] = "Dual weapons";
    d["轻武器专属宝石"] = "Light weapon gems";
    d["重武器"] = "Heavy weapons";
    d["重武器专属宝石"] = "Heavy weapon gems";
    d["长柄武器"] = "Polearms";
    d["武器其他"] = "Weapons · other";
    d["地图"] = "Maps";
    d["钥匙"] = "Keys";
    d["材料"] = "Materials";
    d["药水"] = "Potions";
    d["抽奖箱"] = "Loot chests";
    d["基础属性宝石"] = "Base stat gems";
    d["元素伤害宝石"] = "Elemental damage gems";
    d["双武器专属宝石"] = "Dual weapon gems";
    d["通用战斗宝石"] = "Combat gems";
    d["防御/加成宝石"] = "Defense / boost gems";
    d["高级战斗宝石"] = "High-tier combat gems";
    d["Boss专属/Boss武器"] = "Boss · weapons";
    d["Boss专属/Boss盾甲"] = "Boss · shield/armor";
    d["Boss专属/Boss其他"] = "Boss · other";
    d["稀有度"] = "Rarity";       // 备注列 "Cost=557000 稀有度=2" 里的中文标签

    // ---- 注入结果的三个前缀（InjectCmd/InjectCmds；desc 片段已在各页包过 T()）----
    d["已执行："] = "Done: ";
    d["执行失败："] = "Failed: ";
    d["部分失败："] = "Partly failed: ";

    // ---- 自动更新 + 项目主页（Updater.cs / UpdateForm.cs / AboutForm.cs）----
    // 凡是会走到 ToastMgr.Show/Warn（Toast.cs:27 翻译整串）或 BusyShow（BusyOverlay.cs:84）
    // 的文案都必须在表里。只进 Log() 的按惯例留中文（排障用）。
    d["发现新版本"] = "Update available";
    d["当前版本"] = "Current";
    d["最新版本"] = "Latest";
    d["更新说明"] = "What's new";
    d["（此版本没有写更新说明）"] = "(no release notes)";
    d["现在更新"] = "Update now";            // 按钮 100×28，≈65px ✓
    d["跳过此版本"] = "Skip this";           // 版本号就在上面两行，不必再写 version
    d["以后再说"] = "Later";
    d["打开下载页"] = "Open page";
    d["检查更新"] = "Check update";          // 「关于」里的按钮 100×28，≈78px ✓
    d["项目主页"] = "Project";
    d["更新会重启修改器，当前锁定与地址表会丢失（游戏本身不受影响）"] =
      "Update restarts the trainer; locks are lost (the game is unaffected).";
    d["此版本无法自动更新，将为你打开下载页"] = "Cannot auto-update here - opening the download page";
    d["正在下载新版本"] = "Downloading update";       // BusyOverlay 固定 380×98 / 标签 344px
    d["已是最新版本"] = "Up to date";                // Toast 自适应宽高
    d["检查更新失败"] = "Update check failed";
    d["新版本下载失败"] = "Download failed";
    d["下载的文件校验失败，已放弃更新"] = "Downloaded file failed verification - update aborted";
    d["更新助手启动失败"] = "Updater helper failed to start";
    d["打开浏览器失败"] = "Could not open browser";

    // ---- 宝石批量（2026-10-08）----
    // ⚠ 批量提示是**拼出来**的，所以按片段登记（T() 整串查表，拼出来的串不会命中）。
    //   少数几个纯标点连接词（如「）与 」）故意不收 —— 在英文界面里它们会保持中文，
    //   这是本字典一贯的取舍（漏翻显示中文，不出空白或键名）。
    d["批量取消："] = "Batch rejected: ";
    d["批量完成：成功 "] = "Batch done: ";
    d[" 颗"] = " gem(s)";
    d["。失败："] = ". Failed: ";
    d["；"] = "; ";
    d["已选 "] = "Selected ";
    d[" 颗 —— 可批量：下面这个目标值会应用到全部选中项。"] =
      " gem(s) — batchable: the target below applies to all selected.";
    d[" 是未知类型（不可修改）—— 请把它排除后再批量"] = " is an unknown type (not editable) — exclude it to batch";
    d["Tier 上限不同——"] = "Tier ranges differ — ";
    d["）不能一起批改，请分开选"] = ") cannot be batched together; select them separately";
    d[" 的显示值算不出来——「显示数值」模式下不能批量。"] =
      " has no computable displayed value — cannot batch in Value mode. ";
    d["可改用 Tier 模式，或把它排除"] = "Use Tier mode, or exclude it";
    d["（显示值="] = " (value=";
    d["（Tier="] = " (Tier=";
    d["  ★ 界面会立刻变；存进存档请切一次场景"] = "  UI updates at once; change scene to persist";
    d["正在刷新宝石列表，请等它结束（约几秒）再点「应用」"] =
      "Refreshing the gem list — wait a few seconds, then click Apply";

    return d;
  }
}

} // namespace
