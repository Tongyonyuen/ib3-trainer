# IB3/IB2 逆向项目总参考

> 本文件 = 项目唯一总索引：环境地图、已完成工作、机制结论、工具清单、修正日志、后续计划。
> 详细数据文件（宝石列表 CSV、控制台命令表 CSV/MD）为独立文件，见文末索引。
> **最后更新：2026-10-06**。新对话请先读本文件再引用项目记忆。

---

## 一、项目与环境

- **IB3 游戏目录**：`E:\IB3`（UE3 v868 licensee0，DX9，64 位）
  - 主脚本包：`E:\IB3\SwordGame\CookedPCConsole\SwordGame.upk`（已改造为未压缩版含 exec 位补丁，原版压缩备份 `E:\ib3_re\orig\SwordGame_compressed_orig.upk`）
  - 引擎脚本包：同目录 `Engine.upk`
  - 启动本地化：`Startup_LOC_<语言>.upk`（CHN 版已做字体修复改造，原版备份 `E:\ib3_re\orig\Startup_LOC_CHN.upk`）
  - 配置：`E:\IB3\SwordGame\Config\Default*.ini`（DefaultGems.ini 已改造，备份 `E:\ib3_re\DefaultGems.ini.orig`）
  - 用户存档/配置：`文档\My Games\Infinity Blade III\`
- **IB2 游戏目录**：`E:\IB2`（UE3 v864 licensee1；SwordGame.upk 已打经验命令 exec 补丁，备份 `E:\ib3_re\ib2\IB2_SwordGame_orig_backup.upk`）
- **工作区**：`E:\ib3_re`（全部工具、脚本、存档解析、文档）
- **控制台**：IB3 键位 = `=`（用户已改，配置 `ConsoleKey=Equals`）；IB2 = `=`

---

## 二、已完成工作与状态

| 工作 | 状态 | 交付物 |
|---|---|---|
| IB3 中文控制台不可见修复 | ✅ 已部署（Startup_LOC_CHN.upk 字体植入） | `E:\ib3_re\FIX_中文控制台不可见.md` |
| IB3 控制台命令表（621 条含中文说明/参数/示例） | ✅ | `E:\ib3_re\控制台手册\IB3_控制台命令表.csv` |
| IB3 控制台命令手册 | ✅ | `E:\ib3_re\控制台手册\IB3_控制台命令手册.md` |
| IB3 宝石列表（167 条含中文名/配方族） | ✅ | `E:\ib3_re\控制台手册\IB3_宝石列表.csv` |
| 宝石合成公式（含暗火升级配方） | ✅（本文件第四节 = 最新版） | `E:\ib3_re\控制台手册\IB3_宝石合成公式.md` |
| 掌握/升级机制研究 | ✅ | `E:\ib3_re\装备掌握与铁匠升级_研究报告.md` |
| exec 位补丁（13 个函数控制台化） | ✅ 已部署 IB3+SwordGame.upk / IB2+SwordGame.upk | 工具 `patchflags2.exe` |
| 存档差分验证仪 | ✅ | `dumpunencryptedsavefile` 命令 + `saveparse2.exe`/`gemdump.exe` |
| 一键 GUI 快捷修改器 | ✅（2026-10-05 已开发，待游戏内实测清单见其使用说明） | `E:\ib3_re\ib3trainer\IB3快捷修改器.exe` + items.csv + 使用说明.md |
| 内存直写修改器（独立版） | ✅ v2（2026-10-06：目标向导 + 未知初始值(快照)扫描 + Float 容差 + 撤销写入；**金币 Int64 / 属性四维 Int32 游戏内实测通过**） | `E:\ib3_re\ib3trainer_mem\IB3内存修改器.exe` + 源码 + `memtest.exe`(实测工具) + 使用说明.md |
| **IB3 训练器 2.0（无感整合版，主线）** | 🚧 阶段0 完成 + 阶段1 大部（2026-10-06）：零控制台注入；引擎自检 **9/9**（含实况无 ASLR 断言 image base==0x140000000）；实机自动附着；金币/属性/等级/HP/发现模式/物品浏览器可用；发放族与战斗项待阶段2 注入器、阶段3 现场RE | `E:\ib3_re\ib3trainer2\`（IB3训练器2.exe + enginetest2.exe + 源码 + build.sh）+《2026-10-06_训练器2.0骨架.md》 |

---

## 三、控制台命令速查（详细见命令表 CSV）

**物品/宝石发放**（`item` 的 SelectItem 监听器只在**物品列表网格页** SwordItemListScene，装备详情页/宝石袋页静默无效；`giveandmasteritem` 空壳不可用）：
```
giveitemonce <模板名>                      — ✅ 首选：直入背包、任意界面（exec6 包新增，需 SwordGame.upk md5=240246fe 版本）
item <模板名>                              — 仅在物品列表网格页打开时生效
setplayergiveallitems                      — 一次性发放全套物品（无界面要求，已实测）
setplayergems 0                            — 重新生成商店列表（0=参数），可从商店购买稀有宝石
setplayercreatenewlistofstoregems 1 <模板名> 0 <数量> 1
                                           — ✅ 实测(2026-10-06)：按模板名精确刷新随身商店宝石列表（AllSameType=宝石模板名）；
                                             真序=bUseCheatGems, AllSameType, bCreatePotions, ForceCount, bCheatHighEndGems；
                                             实测 14×FireGem 入 CurrentStoreGems 并存档持久。详见《2026-10-06_宝石商店路线打通.md》
```

**掌握/等级/经验**（需在游戏内，Pawn 已生成）：
```
masterallowneditems <A> <B>                — 一键：全物品 ForgeLevel=B-1、经验条填充、玩家升级、属性点+N
                                             （参数 B = 目标等级[界面显示值]；参数 A 实测无独立效果）
setplayerlevel <等级>                      — 设置玩家等级（按等级差自动发属性点）
giveweaponxp / giveshieldxp / givearmorxp / givehelmetxp / givemagicxp <XP>
                                           — 给已装备部位灌掌握经验（IB2 同款命令已补丁）
setplayergold <数额>                       — 设置金币
```

**存档/调试**：
```
dumpunencryptedsavefile 0 0 <名字>         — 导出解密存档到 Binaries\Win64\（saveparse2.exe 可解析）
checkforgemrecipes <CookVar> <配方名> <Gems> — 宝石配方检查（Gems 数组参数控制台传不了，内部用）
```

**已 exec 化但控制台受限的函数**（对象参数无法从控制台传入）：
QuickMasterItem / SelectedItemMastered / SelectedItemBeingForged / SelectItemForgeLevel /
StartForgingItem / CompleteForgingItem / GiveItemXp

---

## 四、宝石合成公式（最终版）

### 4.1 机制
- 熔炉**三合一**：三颗同配方族（RecipeMatch 相同）宝石 → 产物 = 族内 `MPParent` 指向的模板
- `CheckForUberGemCreation(RecipeGemName, Gems)` 字节码遍历 Gems[0/1/2] 三槽（已确认）
- 融合判定三重匹配（`SameNameRecipeGemCheck`）：ObjectTemplateName + GetFullGemGeneratedName + RandomAddPct 必须一致
- `Restrict=1/2` = 熔炉槽位/合成专用标记（此类宝石不可装备）
- 暗火系升级走 `CheckForImproveableUberGem(UberGemIndex, RecipeGemName, Gems)` 专用分支

### 4.2 十二族标准配方（三合一）
**X·In + X·Out + X → 稀有X宝石**，覆盖 12 族（Attack/Health/Shield/Magic + Fire/Ice/Elec/Poison/Light/Dark/Water/Wind）。
In/Out 为商人有售的合成试剂（Restrict=1/2）。详见合成公式文档第一节。

### 4.3 特殊配方
- PerfectParryGem_1/2 同族三颗 → GreatParryAllGem
- BossBoostGem ×3 → UberBossBoostGem（Boss 等级 +25 → +1000）
- 元素隐藏链：元素 Uber 组合 → UberElementalGem1 →（MPParent 链）→ UberElementalGemMax（全抗+100）
  （CheckForGemRecipes 硬编码 "UberElementalGem1"）

### 4.4 暗火/光谱升级配方（用户实测确认）
**任意暗火 + 任意两颗元素攻击系 Uber 宝石（元素可混搭）→ 下一档暗火**
- **⭐ 数值公式（2026-10-07 存档三样本定论）**：`显示值 = 模板基础值 + GemTier × RecipeBoostAmount`——**加法，每次融合 +500**（暗火；光谱 `_100` 是 250）。同名 `UberElementalAttackGem_100` 实测 GemTier=0→1000 / =1→1500 / =2→2000。**`GemTier` = 融合次数**（Byte 0~255 → 上限 1000+255×500=**128500**，**无合成上限**）。`CookedGemVar` 恒 50（不参与数值，勿误判）。
- **跨端成立**：数值存在实例 `GemTier` 里、模板是官方 `_100` → 手机端同公式自算，**不需改 ini**；对照：自定义 `_200` 模板直购那类值全靠查 ini → 不可跨端。
- **素材硬要求 = 400（tier5+pct1.0）元素宝石**：`UberFireGem`(500)、tier5 非满 roll（358/344/398.6）均被拒。
- **400 素材来源 = `setplayergems 0`**（标准刷店：八元素族常规宝石全满 roll；另直售暗火 500/1000/2000）。⚠️ 与 `setplayercreatenewlistofstoregems`（按名刷店，pct 随机）**行为不同，勿混**。
- **宝石研究的完整记录已迁至独立文件夹 `E:\ib3_re\宝石研究\`**（`README.md` 为入口，自成体系；新会话只读该文件夹即可，无需读其他文档与历史记忆）。原《2026-10-06_宝石商店路线打通.md》现为 `宝石研究\02_宝石发放链路（商店路线）.md`
- 光谱（Rainbow）大概率同配方（+250/次），未实测
- **被拒组合**（实测无效，勿再试）：暗火×3、火+冰+暗火、暗火+低阶暗火混放
- 已定义 +2000 模板：⚠️ 旧记录"`item uberelementalattackgem_200` 直接发放（道具界面内）"系**误记**（2026-10-06 用户确认+复测：`item` 对宝石无效）。+2000 模板实际获取途径：`setplayergems 0`/`setplayergems 1` 刷新商店列表后**直接购买**（实测暗火+2000/光谱+2000 均出现在商店，金币管够即得）

### 4.5 ini 自定义升级链（已实施）
DefaultGems.ini 已加：暗火/彩虹的 base→_100→_200 MPParent 链 + _200 新模板。
扩链方法：复制 _200 段改名 _400、改 ImmuneToAllBonus、_200 段加 MPParent 指向即可。

---

## 五、关键机制结论

- **item 命令**：SelectItem UI 事件转发，仅道具界面内生效；不需作弊模式
- **giveandmasteritem**：创建物品但加入背包的调用是空壳（UnusedStr）→ 物品不进背包，勿用
- **masterallowneditems**：走 QuickMasterItem 作弊直写路径——等级/经验条/玩家升级/属性点全真实发生，但不扣金币
- **宝石实例状态**：存档中每颗宝石独立块（GemName/GemTier/CookedGemVar/Boost/RandomAddPct），来源不同的宝石隐藏状态不同
- **暗火升级**：配方 = 暗火 + 2× 任意元素攻击 Uber；增值 = RecipeBoostAmount(500)/次
- **物品类命令的前提**：Pawn 已生成（真正进入游戏），标题界面下全部无效
- **内存数值布局（2026-10-06 游戏内实测）**：金币 = Int64 唯一地址（`setplayergold` 改值可跟踪验证）；属性四维 = **连续 4 个 Int32**，内存顺序（体力,护盾,攻击,魔法），`setplayerstats` 控制台参数顺序 = **攻击、体力、护盾、魔法**（整数参数；小数会被截断）；玩家 HP = Int32（`setplayerhealth` 可改，写 2222222 游戏内显示确认 ✓）；快照模式实测本游戏可写私有区 1638 MB，全量快照约 5.6 秒；两遍扫描对属性收敛到 1~2 地址（另有一组"加成后"副本，写基础值那组最稳）
- **物品结构与掌握经验（2026-10-06 全链路实证）**：物品结构间距 **0x2C** 连续排列，[+8]=当前经验(Int32)、[+0xC]=**数量**（写 2 → 游戏显示"数量2"）、**[+0x10]=等级(Int32，0 基！显示等级=值+1，免锻造提级且装备属性真实生效——用户确认)**、**[+0x14]=已掌握标志(0/2)**；装备内存序=武器→盾→盔甲→头盔→戒指→仓库物品（本例 0x7FF4F736562C 起）。**经验字段定位流**：界面读上限 R（如 0/240）→ 扫 Int32 R → 灌该部位经验（givehelmetxp 等）→ 重扫差分 → 新地址即经验字段（写满=已掌握、写小=显示分数，实测 137/240 显示确认 ✓）。`giveweaponxp` 类只能填满当前锻造上限不能突破（提上限=游戏内锻造或 masterallowneditems——用户实证）
- **玩家等级与技能点（2026-10-06 实证）**：玩家等级=Int32 直接存储（`0x7FF4E8F6xxxx` 族，本例 0x7FF4E8F607F0；内存写 42 → 界面显示 42 ✓）；技能点=Int32 同族 +0x178C（本例 0x7FF4E8F61F7C；写 8888 → 界面显示 ✓；升级/掌握事件会重算覆盖，且存在多处镜像副本）。定位法=用 `setplayerlevel` 造大而独特的数值（如 334级/3794点）扫描后差分
- **持久化（2026-10-06 重启实测）**：物品等级/经验内存写入**随存档持久化** ✓；玩家等级/技能点/属性/HP 的内存注入**不持久**——重启后按真实进度重算（等级由"装备掌握产生的经验"结算，本例重启后=45级/技能点131/四维各1）。**要永久等级→喂真实来源（装备掌握经验可写且持久），让游戏自己结算**
- **宝石发放链路（2026-10-06 实测）**：`giveitemonce`/`item`/`setplayergiveallitems` 均**不发宝石**（item 旧"可发宝石"记录系误记）；`setplayergems 0/1` = **刷新商店宝石列表**（含暗火+2000/光谱+2000 等链模板，购买=真实入袋持久 ✓）；**exec7 已部署**（md5=cbe531bb…，仅 1 字节=给 `GetRandomGem` 加 exec 位）→ `getrandomgem <6参>` 可调用但直呼静默无产出（参数语义未破译，疑内置上下文校验）；**`setplayergiverandomgem <RewardLevel> <GoldScale> <FavorSocketType>` = 可用钓鱼命令**：GoldScale=**目标成本**（GetCostCloseToValue 选成本最接近的模板，如 750000→ParryGem_2 稳定复现）、RewardLevel=世代档位、Favor=软偏好（favor=2 偏招架系）；偶发"窗口内无候选"静默失败。**按成本钓鱼=当前"近似精确发放"实用方案**（167 条宝石成本表在手）
- **包装函数解码（字节级权威）**：`setplayergiverandomgem` 内部以 (RewardLevel, GoldScale, Unused→GoldValue, FavorSocketType→PrefGemType, 4A, 4A) 调 GetRandomGem——**paramd 输出的参数顺序与真实声明序相反**（setplayerstats 校准过：paramd=倒序）。签名全集：`GetRandomGem(bDontSaveToList, ForcedType:Byte, PrefGemType, GoldValue, GoldScale, RewardLevel)`、`CreateFixedStoreGemFromName(bPotions, bHighEndOnly, out FixedGem:Struct, GemName:Str)`、`LoadGemInstance(ForceType, InPC, GemSavedData) → 宝石对象`、`SaveGemInstanceToUnequippedList(bUpdateBadges, bAllowToFail, GemItem)`（**手术 GiveGemOnce 的全部拼图已齐**）

---

## 六、ini 文件管辖范围（SwordGame\Config\）

| 文件 | 节数 | 管辖 |
|---|---|---|
| DefaultGems.ini | 166（含自定义 2）| 宝石模板（RecipeMatch/MPParent/Restrict/属性）|
| DefaultItems.ini | 435 | 装备模板（Armor_100 等武器盾甲盔戒指）|
| DefaultBossItems.ini | 312 | Boss 专属装备（8ft_2W_SpikeArms 等）|
| DefaultAbilities.ini | 83 | 玩家能力（All_PerfectParryHitWindowBoost 等）|
| DefaultQuests.ini | 39 | 任务 |
| DefaultKeyItems.ini | 22 | 钥匙物品（TRA_Key_Small 等）|
| DefaultUI.ini | 14 | 界面配置 |
| DefaultWorldItems.ini | 12 | 场景物品（TRA_World_1_Cactus 等）|
| DefaultRandomItems.ini | 8 | 随机物品池/转轮（TRA_GrabBag_Small 等）|
| DefaultClashMob.ini | 5 | ClashMob 服务配置 |
| DefaultPotions.ini | 2 | 药水转轮（TRA_Potion_HealthL）|
| DefaultChallenges.ini | 1 | 社交挑战 Perks |
| DefaultGameFlags.ini | 1 | 游戏标志位 SeqAct |
| DefaultTutorial.ini | 1 | 教程弹窗 |

---

## 七、工具清单（E:\ib3_re\）

| 工具 | 用途 |
|---|---|
| patchflags2.exe | 扫描式 exec 位补丁（flags2 = 尾部搜索版，修正过 GiveXp 系偏移问题）|
| patchbody.exe | 函数体字节码替换（v1 实验用——注意：脚本字节码有完整性校验，改现有函数会启动崩溃）|
| pkgmerge4.exe | 包手术：向 loc 包植入 EngineFonts 对象（控制台修复用）|
| fn.exe / paramd.exe | 函数参数/签名提取 |
| xscan.exe / vscan.exe | 字节码引用扫描（按导出/按 FName）|
| saveparse2.exe / gemdump.exe / gemstate2.exe | 解密存档解析（属性流/宝石实例）|
| checkcon.exe | 控制台开关状态检测（绿色线像素判定，自动开关到打开态）|
| win.exe / win2.exe / click.exe / dragv.exe / postkeyx.exe / postctrlv.exe | 窗口自动化（截图/按键/点击/拖拽/粘贴）|
| realclick.exe / click2.exe | 真实鼠标点击（SetCursorPos+mouse_event，WinForms 界面用）/ PostMessage 点击 |
| cjkstr.exe | 提取二进制内含 CJK 的 UTF-16 字符串（判定包内有无中文文本）|
| genitemdb.exe | 生成 GUI 修改器物品库（解析 7 个 ini + 合并宝石中文名 → items.csv）|
| ib3trainer\ | **GUI 快捷修改器**（exe + items.csv + 源码 + 使用说明）|
| ib3trainer_mem\ | **内存直写修改器**（exe + 源码 + 使用说明 + _selftest 自检）|
| pkgdump.exe / sumdump.exe / hexdump.exe / intnames.exe / namecmp.exe / dbg.exe | 包结构分析辅助 |
| tools\（gildor 三件套）| umodel/decompress/extract |

**输入方法经验**：控制台文字用 `postchar`（WM_CHAR 直发，支持下划线）；postkey 逐键会丢字符；
`checkcon.exe` 先确保控制台打开；粘贴（Ctrl+V）不被控制台支持。

---

## 八、修正日志（错误结论 → 修正，防旧错误复发）

| 旧结论（错误） | 修正 |
|---|---|
| 暗火升级 = 三颗暗火三合一 | ✗ 实测拒绝。正确 = 暗火 + 2× 任意元素攻击 Uber（用户实测确认）|
| 火500+冰500+暗火 = 暗火升级配方 | ✗ 实测拒绝 |
| 进度变量(ModVar83)门控导致新档无法融合 | ✗ 原存档（通关进度）同样被拒——被拒原因是配方组合本身 |
| 商人购买 vs 任务/藏宝图奖励的宝石来源区分导致无法融合 | ✗ 非来源问题。融合判定 = ObjectTemplateName + 生成名 + RandomAddPct 三重一致 |
| 给GiveAndMasterItem 可正常发放物品 | ⚠️ 物品被创建但加入背包的调用是空壳桩(UnusedStr)→ 物品不进背包 |
| `exec <文件名>` 可用于外部触发命令（尝试做零控制台注入） | ✗ 该移植版文件系统沙盒：绝对/相对/引擎 Paths/文档目录/Exec 目录全部 `?CHN?Core.Errors.FileNotFound?`，loose 文件不可读 |
| WM_CHAR 是控制台注入的可靠方式 | ⚠️ 只对"已激活"的控制台成立。**刚打开的控制台丢弃 WM_CHAR，仅认 VK 按键事件**；且 VK 映射无视 shift（:→;）。最终方案 = 首字符 VK 激活 + 其余 WM_CHAR。真键盘 keybd_event 被中文输入法拦截，不可用 |
| 游戏数据里没有任何中文显示名（本会话曾误判） | ✗ 错因：只扫了 CookedPCConsole。**官方中文在 `SwordGame/Localization/CHN/*.chn`**（loose UTF-16LE 文本，`[模板名 类]` 节 + `FriendlyName=`，1073 条，含太和一字剑等全部装备/收集品名）。物品显示名走该文件按模板名查表 |
| item 命令任意界面可用 | ⚠️ 仅道具界面（SelectItem 事件监听器所在），其他界面静默无效 |
| 光谱宝石与暗火同机制可升级 | ❓ 未实测。结构上光谱无 RecipeMatch/RecipeBoost，大概率同样走暗火式专用分支，待验证 |
| masterallowneditems "只写状态无真实效果" | ✗ 新档实测：经验/升级/属性点全部真实发生（旧存档看不到是因为物品已满级）|
| v864 操作码表 0x0F=Skip | ⚠️ 实为 Let（标准 UE3 定义），IB3 实测确认。使用 skill 操作码表时注意此条 |
| GiveXp 系函数 IB3 独有 | ✗ IB2 有完全同名同签名的一套，同款 exec 补丁有效 |
| `setplayerstats` 参数顺序 = 护盾、魔法、体力、攻击（旧 CSV/训练器标注+早期测试） | ✗ 2026-10-06 内存实证 = **攻击、体力、护盾、魔法**（屏幕显示值与内存四字段逐项核对 + 连续改值跟踪）|
| 属性四维 = Float（"UE3 PawnStat 是实数"推断） | ✗ 实为 **Int32**；独特浮点值写入后全类型 0 命中，此前 Float"命中"是游戏数据巧合（写整数参数即可）|
| 物品结构 +0xC 直觉 = 等级字段 | ✗ 实测为**数量**字段（写 2 → 游戏内显示"数量2"）；等级字段实为 +0x10（0 基，2026-10-06 已定位） |
| `item` 命令可发宝石（旧记录"item uberelementalattackgem_200 直接发放"） | ✗ **误记**。2026-10-06 用户确认+复测：`item` 对宝石无效（giveitemonce 亦无效）——宝石发放需其他路径（商店购买已验证可用；按名精确发放待方案） |
| `setplayergems` = 补宝石到背包（命令表 CSV 描述） | ✗ 实测=**刷新商店宝石列表**（进袋无效）；列表为档位抽样、含自定义链模板（暗火+2000/光谱+2000 实测在内） |
| 使用说明.md「exec8 包 = 首次"新增导出函数"手术成功案例」 | ✗ **错误记录**。实际：自构脚本字节码被加载器原生校验拦截（v3/v6/v7/v8/v9 六变体全崩，连最小 Return Nothing 都崩；仅"指向真实数据"的克隆可过），当日已回退；现役 = exec10（exec7 + 2 个 exec 位） |
| `dumpunencryptedsavefile 0 0 <名字>`（旧文档序） | ✗ 真序 = **`<名字> <Index> <bDeleteSave>`（名字在前）**；且它导出的是磁盘旧档（各状态字节实测完全相同），不能做游戏内验证——验证改用解密 `Cloud\_SwordSaveX_0-0.bin`（工具 decsave.exe） |

---

## 九、下一步计划

1. **GUI 快捷修改器** ✅ 已完成（2026-10-05）：`E:\ib3_re\ib3trainer\`，功能/命令映射/待实测项见该目录使用说明.md。
   新 RE 结论：setplayerstats=直写 PawnStat 四字段；GiveItemXp 单次=掌握1级；官方中文物品名在 `SwordGame/Localization/CHN/SwordGame.chn`（已合入 items.csv，609 模板）。
2. **GUI 已用户实测（2026-10-05）**：注入链路全程有效、命令真实生效；背包效果需关闭重开刷新（勿误判无效）。
   游戏失焦自动关控制台（历史里 mobile PauseSong 刷屏=焦点切换痕迹）；外部程序 ~2.5s 抢前台，SendOne 已双重补聚焦。
   细节待逐项确认：chips / god / 部位xp五连 / setplayeritemlevel / fillsuperandmagicmeters / 高级商店三参版的精确效果。
3. **内存直写修改器 v2** ✅ 已完成（2026-10-06）：`E:\ib3_re\ib3trainer_mem\`（GUI + `memtest.exe` 命令行实测工具 + `enginetest.exe` 引擎自检，**共用同一份 ScanCore 引擎**）。引擎自检 9/9 全过；游戏内实测：金币 Int64 唯一命中全链路 ✓、属性四维 Int32 两遍扫描收敛 1~2 地址 ✓、快照模式 1638MB/5.6s ✓。下一步：掌握经验/HP 走快照流程实战（giveweaponxp / 战斗挨打）、玩家等级、宝石数值。
4. （可选）CheckForGemRecipes CookVar==3 分支全解码：融合系统的精确门控条件
5. （可选）IB2 深度研究：IB2 的 exec 补丁已就位，命令行为可按 IB3 同方法验证
6. **宝石按名发放（2026-10-06）**：✅ 商店路线已打通（第三节新命令 +《2026-10-06_宝石商店路线打通.md》）；"直入背包版 GiveGemOnce"仍被原生校验墙挡（新增函数自构字节码=启动崩），破墙实验设计已记录，待用户决策是否攻坚

---

## 十、独立数据文件索引

| 文件 | 内容 |
|---|---|
| 控制台手册\IB3_控制台命令表.csv | 621 条命令完整表（中文说明/参数/示例）|
| 控制台手册\IB3_控制台命令手册.md | 按类分组的命令手册 |
| 控制台手册\IB3_宝石列表.csv | 167 条宝石模板（中文名/配方族/属性）|
| 控制台手册\IB3_宝石合成公式.md | 合成机制+配方+暗火升级（含本文件第四节的完整版）|
| FIX_中文控制台不可见.md | 控制台修复技术文档 |
| 装备掌握与铁匠升级_研究报告.md | 掌握/升级机制研究报告 |
| orig\ + ib2\ | 游戏原版包备份 |
| 2026-10-06_宝石商店路线打通.md | 按模板名发宝石（商店路线）实证 + 手术墙定论 + 存档验证通道 + GEM 发放链破译（GetRandomGem/FCGN/GetRewardTreasure） |
