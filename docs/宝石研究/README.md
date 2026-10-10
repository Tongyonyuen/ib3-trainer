# IB3 宝石研究（独立文件夹）

> **给新会话的说明**：这个文件夹**自成体系**。研究宝石相关问题时，**只读本文件夹即可**，
> 不需要读 `E:\ib3_re` 的其他文档，也不需要读 Claude 的历史记忆——那些多数是控制台注入、
> 训练器、中文化的记录，与宝石无关，读了只会浪费上下文。
>
> 唯一例外：如果需要"控制台注入命令"的操作细节，见 `02_宝石发放链路（商店路线）.md` 第三节。

---

## 一、环境速查

| 项 | 值 |
|---|---|
| 游戏根目录 | `E:\IB3` |
| 启动方式 | **必须用** `E:\IB3\Binaries\Infinity Blade Launcher.exe`（直接跑 `Win64\IB3.exe` 会变英文界面） |
| 存档目录 | `文档\My Games\Infinity Blade III\SwordGame\Cloud\`（**唯一存档位置**，已全盘搜索确认） |
| 主存档 | `_SwordSaveX_0-0.bin`（槽 0）；另有 `_BackupX_0-0.bin` 备份档、`_SwordSaveSlotX_0.bin` 槽元数据 |
| 解密工具 | `E:\ib3_re\decsave.exe <in.bin> <out.bin>`（明文也以 `"yeK "` 开头） |
| 加解密工具 | `E:\ib3_re\aespack.py`（`decrypt_file` / `encrypt_file`，需 pycryptodome） |
| AES 密钥 | `366E486D6A643A6862574E663D397C554F323A3F3B4B30792B675A4C2D6A5035`（AES-256-**ECB**） |
| 内存工具 | `E:\ib3_re\ib3trainer_mem\memtest.exe`（`info` / `scan <Type> <val>` / `check <Type> <addr,...>` / `read` / `write <addr> <Type> <val>`，Type = `Int8/Int16/Int32/Int64/Float/Double`）<br>**2026-10-07 新增**：`dump <hexAddr> <len>` / `scanpat <hex模式>`（`??` 通配）/ `records <hexPctAddr>` —— 见 8.1 |
| 存档明文探针 | `sendcmd.exe "setsave 0"` → 覆盖写 `E:\IB3\Binaries\Win64\UnencryptedSave0.bin`（**明文，不用 decsave**）；`sendcmd.exe "DumpUnencryptedSaveFile 0 0 x"` → 写 `Binaries\Win64\00.bin`。二者**都不动 `Cloud\` 的真档** |
| 控制台注入 | `E:\ib3_re\sendcmd.exe "<命令>"` |

**落盘时机**：游戏焦点运行时约 50s 自动存档一次；**场景切换立即写盘**。
⇒ 验证前必须 `setsave 0` 或切场景，否则解密的还是旧档。
⇒ ⚠️ **`hideout` 会重刷商店**；`setsave 0` 不会。买好的素材存完档别用 hideout。

---

## 二、核心结论速查

| 问题 | 答案 |
|---|---|
| 暗火数值公式 | **`显示值 = 模板基础值 + GemTier × RecipeBoostAmount`**（加法） |
| 暗火每次融合增值 | **+500**（`_100` 模板的 `RecipeBoostAmount=500`） |
| 光谱数值公式 | **`1000 + GemTier × 250`**（2026-10-08 实测：Tier 255 → **64750**）。升级路径原版未配置，只能直接改 Tier |
| 数值上限 | `GemTier` 是 Byte(0~255) → `1000 + 255×500 =` **128500**，**无合成上限** |
| 数值存在哪 | **宝石实例的 `GemTier` 字段**（存档里），不是模板 |
| 官方模板档位 | 只有 **+500**（`UberElementalAttackGem`）和 **+1000**（`_100`）；`_200`(+2000) 是本仓库自定义 ini |
| 跨端（手机）能用吗 | **能**——融合产物名是官方 `_100`，数值在实例字段里 → 手机端同公式自算，**不改 ini** |
| 什么不能跨端 | 靠自定义 `_200` 模板直购的那类（值全靠查 ini 表） |

**实测样本**（同一存档，六颗同名 `UberElementalAttackGem_100`）：
`GemTier=0→1000 / 1→1500 / 2→2000 / 3→2500 / 4→3000 / 5→3500` ✅ 六发六中

**反证记录**：曾误判为乘法 `基础值×(1+CookedGemVar/100)`——若成立应为 `1000→1500→2250`，
实测第三颗是 **2000**，乘法排除。**`CookedGemVar` 不参与数值**（融合过恒=50、未融合=0）。

---

## 三、融合机制（熔炉）

**配方**：`1 颗暗火 + 2 颗元素宝石` → 暗火 `GemTier +1`

| 素材要求 | 结果 |
|---|---|
| **`FireGem`/`IceGem` tier5 且 pct=1.0（值 = 400）** | ✅ 唯一可用 |
| `UberFireGem`/`UberIceGem`（值 500） | ✗ 被拒 |
| tier5 但 pct<1.0（值 358 / 344 / 398.6 / 385.4） | ✗ 被拒 |
| 暗火×3、暗火+低阶暗火混放 | ✗ 被拒 |

**400 素材来源 = `setplayergems 0`**：标准刷店产出 **154 颗**，八个元素族（Fire/Ice/Elec/Poison/
Light/Dark/Water/Wind）的"常规"宝石全部 tier5 + pct1.0 = **400**。

**起跳档位**：基础档 `UberElementalAttackGem`(+500) **没有** `RecipeBoostAmount` → **不可升级**，
升级链**必须从 `_100` 起跳**（商店有售）。
**光谱**：ini 里 `Rainbow…_100` **有** `RecipeBoostAmount`，但**实测无法融合**；
字节码 `SpecialUberElementalCheck` 只认 `UberElementalAttackGem`（暗火专用分支）→
**光谱 1000 是正常玩法的终点**。
但光谱实例**自带 `GemTier` 字段**，所以**可以强改**：实测改成 Tier 255 → 显示 **64750**，
反推公式 = **`1000 + GemTier × 250`**（与原先「预期 +250」吻合）。详见 `01_数值公式与融合机制.md` 第二节。

**操作顺序**：
```
setplayergems 0                         → 商店元素宝石全 400
买 1 颗 FireGem + 1 颗 IceGem
熔炉：暗火 + 这两颗 → GemTier+1，数值+500
setsave 0                               → ★ 别用 hideout
decsave.exe 解密验证 GemTier 增量
```

---

## 四、两条刷店命令（**行为不同，勿混**）

| 命令 | pct 掷法 | 能否出 400 | 备注 |
|---|---|---|---|
| `setplayercreatenewlistofstoregems 1 <模板名> 0 <数量> 1` | **随机**（0.02~0.8） | ✗ | 真序 = `(bUseCheatGems, AllSameType=模板名, bCreatePotions, ForceCount, bCheatHighEndGems)`；`bCheatHighEndGems` 只影响 **tier**，不碰 pct |
| `setplayergems 0` | **元素族拉满 1.0** | ✓ | `0` = 非低档 = 高档 |

⚠️ **商店列表会被"确定性重生成"**：刷新/购买/重进商店后可能还原成上一次的掷点。
实测：`setplayercreatenewlistofstoregems 1 UberElementalAttackGem_100 …` 设好后进店渲染，
列表又被还原成先前那批 FireGem。**按名刷店有竞态，不要依赖它做持久改动。**

---

## 五、存档格式（解密后）

**宝石记录 = 6 个属性**（顺序固定）：
```
GemName        NameProperty    名字（决定模板基础值）
GemTier        Byte            融合次数（暗火）/ UpgradeTier 下标（元素宝石，1 基）
CookedGemVar   Byte            烹饪标记：融合过=50、未融合=0（不参与数值）
RandomAddPct   Float           随机加成（元素宝石用；暗火恒 0.0）
bShowBadge     Bool            角标
Boost          Byte            未观测到变化
```

**⚠️ 读字段的坑（曾因此误判一整轮）**：
此移植版把 ByteProperty 的值序列化成 **`'None'` + 数字** 的形式——
**真正的值在 `'None\0'` 之后那一个字节**，不要把 `'None'` 的**长度前缀 `05`** 当成字段值。

**数组定位**：`PlayerUnequippedGems`（背包）/ `CurrentStoreGems`（随身商店）都是 ArrayProperty，
**必须按 ArrayProperty 头的 count 精确切分**（两节相邻，易串读）：
```
标签字符串位置 i  →  p = i + len(标签) + 1     ← +1 是字符串的 NUL
typeLen(4) + 'ArrayProperty\0'  +  size(4) + arrayIdx(4) + count(4)  →  之后是数据
```

---

## 六、内存记录布局（2026-10-07 二次实测，**已修正本文旧版的 4 字节错位**）

运行时 `FGem` 结构 = **24 字节**，与存档的 6 字段一一对应（`GemName` 是 **FName**）：

```
+0x00  int32  FName.Index   ← 宝石名索引（UberElementalAttackGem_100 = 0xAA6A）
+0x04  int32  FName.Number  ← 恒 0
+0x08  uint8  GemTier       ← 融合次数/档位
+0x09  uint8  CookedGemVar  ← 融合过 = 0x32(50)，未融合 = 0
+0x0A          对齐填充
+0x0C  float  RandomAddPct  ← 元素宝石用；暗火恒 0.0
+0x10  uint32 bShowBadge
+0x14  uint8  Boost + 3 字节填充
```

**定位法**：`memtest scan Float <该宝石的独特 pct>` → 命中地址即 **pct 字段**；
**tier = 命中地址 − 4**，**记录起点(FName.Index) = 命中地址 − 0x0C**。

> ⚠️ **本文旧版把记录起点写成 `pct−8`，整体错位 4 字节。** 后果是：
> 旧版看到的"+0x14 = 0x8557 常量"其实是**下一条记录的 FName.Index**——
> 那批宝石名字相同所以看起来像常量。`tier = pct−4` 那条结论本身是对的。
> 判别技巧：**看 `+0x09` 的 cook 字节**。同一批暗火里融合过的 tier 字是 `05 32`，
> 未融合的是 `05 00`；只看 tier 会扫出一堆假命中。

**已完成的内存写入先例**（普通宝石）：把背包火宝石 `pct 0.8216→1.0` → 存档确认 400 ✓
（即"内存改写 → 游戏自己序列化存档"这条路是通的）

### 6.1 运行态有多份同名拷贝，只有一份权威（2026-10-07 最终结论）

背包数据在内存里存在**多份 24 字节记录数组**，内容几乎一致，
**只有一份写进去会被序列化**。判别只能靠**实验**（见 8.1-④），不要靠"哪个看着像"。

两点纠错：

- `GemName` 在**内存**里是 **FName**（Index+Number 各 4 字节），存档里是**字符串**——
  那是 UE3 存盘时 `UNameProperty` 把 FName 转 string 的正常行为，**不是两套结构**。
  （曾据此误判"存档对象是 FString 版 32 字节结构"，用 `scanpat` 扫 `ptr+1A0000001A000000` 得 **0 命中**，已证伪。）
- **数组名随场景变**：在**熔接室**时 `PlayerUnequippedGems` 不出现、只剩 `CurrentStoreGems`；
  回到**藏身地主界面**才出现。判断"数组在不在"要先看当前界面。

---

## 七、已排除的路线（勿重撞）

| 路线 | 结论 |
|---|---|
| **直接改存档文件** | ⚠️ **2026-10-07 翻案**：改档**是有效的**——实测改槽1 角色名 → 同步缓存哈希 → 重启后游戏内存里就是改后的值。此前失败是方法缺陷，不是"另有校验层"。详见 `03_存档迁移.md` 第六节 |
| `LocalFileHeaderCache` 校验 | ✅ **完整破译**：条目 = `[i32 41][40位hex SHA1 + NUL][i32 路径长][相对路径][i32 名长][文档名][i32 contentLen][i32 0]`，**`sha1 = SHA1(明文[:contentLen])`**（不是"最后一个 None\0+5"，那只是存档的巧合）。**列表恒 11 条，只覆盖槽 0/1，没有槽 2** |
| 按名刷店（`setplayercreatenewlistofstoregems`） | ⚠️ 有"确定性重生成"竞态，不适合做持久改动 |
| 商店内存注入 tier | ❌ 数组**每次重新分配**，暗火 `pct=0.0` 无独特值可扫，稳定定位不成立 |
| `item` / `giveitemonce` / `setplayergiveallitems` 发宝石 | ❌ 均不发宝石（旧记录"item 可发宝石"是误记） |

---

## 八、内存直写 tier → 128500 【✅ 2026-10-07 已达成】

**目标**：不改 ini、不进 255 次熔炉，直接把背包宝石的 `tier` 改成 255 → 128500。**已完成。**

**成品流程**（一步不差）：

```
1) 读权威数组的 FName 索引（暗火 = 0xAA6A / Number = 0x65）：
   memtest scanpat 6AAA00006500000005320000??????????????????????????????
   → 会命中 2~3 份拷贝，见 8.1-④ 的判别法挑出「权威」那份
2) memtest write <权威首条+0x08> Int8 255     ★ 必须 Int8！
   （用 Int32 会把 +0x09 的 cook 一起清成 0）
3) sendcmd.exe "setsave 0"    → 验 E:\IB3\Binaries\Win64\UnencryptedSave0.bin 明文
4) 在游戏里切一次场景（进熔接室再退出）→ 写 Cloud\_SwordSaveX_0-0.bin
5) decsave 确认 GemTier=255
```

**实测结果**：`Cloud\_SwordSaveX_0-0.bin` @12:36:24 `PlayerUnequippedGems[0]`
= `GemTier 255 / CookedGemVar 50`，游戏内该宝石由 **3500 → 128500** 显示正常。

**上限确认**：`tier` 是 uint8，255 就是天花板 → **128500 是硬上限**，无法再高。

---

### 8.1 2026-10-07 本次进展（新）

**① 结构破译完成** —— 见第六节。24 字节 `FGem`，cook 字节是关键判别位。

**② memtest.exe 已扩容**（`ib3trainer_mem/MemTestCli.cs`，用文件头注释里的 csc 命令重编）：

| 新命令 | 用途 |
|---|---|
| `memtest dump <hexAddr> <len>` | hex+ASCII 转储（免去 memdump.exe 要 PID） |
| `memtest scanpat <pattern> [maxHits] [maxPrint]` | **任意字节模式扫描，空白可省、`??` 为通配** |
| `memtest records <hexPctAddr> [count] [strideHex]` | 按 24 字节记录回读游走 |

> `scanpat` 是目前最好用的定位工具：把 `???????? 00000000 05320000 00000000 00000000 00000000`
> （`??` = 未知 FName 索引，`05 32` = tier5+cook50）一贴，直接命中暗火记录。
> 原来的 `scan` 只能扫单个数值类型，扫不到"结构"。

**③ 权威数组已锁定** —— `0x7FF4F72B7D80`（本次会话），**7 条 × 24 字节**：

```
+0x00 暗火 tier5 cook50   ← tier 字段 = +0x08 = 0x7FF4F72B7D88（改这里）
+0x18 暗火 tier4 cook50
+0x30 暗火 tier3 cook50
+0x48 暗火 tier2 cook50
+0x60 暗火 tier1 cook50
+0x78 暗火 tier0 cook0
+0x90 FireGem tier5 cook0 pct=0.4155   ← 与存档第 7 颗完全一致
```

**定位手法**：`scanpat` 扫「暗火 FName 索引 + 名字编号 + tier/cook」
`6AAA0000 65000000 05320000 …`（`0xAA6A`=`UberElementalAttackGem_100`，`0x65`=Number）。

**④ ⚠️ 内存里有三份拷贝，只有一份是权威的 —— 必须用实验区分，别靠"看着像"猜**

| 地址 | 身份 | 特征 |
|---|---|---|
| **`0x7FF4F72B7D80`** | ✅ **权威** | 写进去会被序列化 |
| `0x7FF4FBB97880` | ❌ 镜像 | 后面紧跟着商店 42 颗，**看着才像正主**，实则不是 |
| `0x7FF4E630E700` | ❌ 残缺拷贝 | 第 1 条的 FName 索引被清零，往它写**完全无效** |

**判别法**：给两份**写不同的值**（200 / 255）→ `setsave 0` → 看明文 dump 出哪个。
本次 dump 出 **200** ⇒ 权威是 `0x7FF4F72B7D80`。
（本次先往 `0x7FF4E630E700` 写 255，白走一轮——那是个陷阱。）

**⑤ 已验证：`tier=255` 进入序列化** ——
向 `0x7FF4F72B7D88` 写 `Int8 255`（`0x3205 → 0x32FF`，cook 保持 50，**别用 Int32 写否则 cook 被清零**），
`setsave 0` 后 `UnencryptedSave0.bin` 里 `PlayerUnequippedGems[0].tier = 255`
⇒ 显示值 = `1000 + 255×500` = **128500** ✓

**⑥ 验证探针（不用等落盘、不用 decsave）**：
`sendcmd.exe "setsave 0"` → 覆盖写 `E:\IB3\Binaries\Win64\UnencryptedSave0.bin`（明文）
→ `python gemparse.py <该文件> PlayerUnequippedGems` 直接看 tier。
> ⚠️ 在**熔接室界面**时这个 dump **不含** `PlayerUnequippedGems`（只有 `CurrentStoreGems`）；
> 回到**藏身地主界面**才含。别被这个骗了。

**⑦ 最后一步：写进 `Cloud\` 真档** ——
`setsave 0` **不更新** `Cloud\_SwordSaveX_0-0.bin`（实测 mtime 不动）；
窗口前台化等待 95s **也不自动存档**；控制台 `GemScene 0` / `MapScene 0` 也不写。
**实测有效的是"在游戏里退出熔接室"这种真实场景切换**（12:27:51 四文件同时更新）。
⇒ 流程：**改内存 → `setsave 0` 验明文 → 游戏里切一次场景 → 解 `Cloud\` 确认**。

**下一步方案：快照差分**
```
1) setplayergems 0 → 买 2 颗 IceGem（各 400）
2) 快照内存
3) 熔炉把 tier=5 那颗融一次 → 变 tier=6
4) 再快照 → 差分找出「5 变成 6」的那个 int32
   → 该结构即背包宝石数组，tier 字段随之锁定
5) memtest write <地址> Int32 255 → 切场景存档 → 验证 128500
```
快照差分是**精确定位**，不需要独特值。待解决：`memtest.exe` 的两段式手动快照用法
（`snapcmd` 只能包**控制台命令**，而熔炉是 UI 操作）。

**其他可查方向**：`_CTN`（时间戳 `2026.10.07-11.17.17`）、`_CTRB`、`_CurrentSlot`、
`CloudStorage.ini` —— 疑似"本地 vs 云端"的时间戳仲裁，可能是存档直改失败的未知层。

---

### 8.2 ⚠️⚠️ FName 索引**不跨进程稳定** —— 静态索引表不可靠（2026-10-07 实测，推翻本节旧版）

**先纠错**：本节旧版写"改 `DefaultGems.ini` 会按块重排索引"。**那是错的。**
当时看到的 `0xAA6A→0xAA70` 位移，后来被证明跟 ini **无关**。

**实测事实**（这是本条唯一可靠的结论）：

| 进程 | 启动时间 | ini | 暗火 `UberElementalAttackGem_100` | `FireGem` |
|---|---|---|---|---|
| PID 31588 | ~13:1x | 现版（无 `_200`） | **0xAA70** | **0x855D** |
| PID 29420 | **13:36:45** | 同一份（sha1 `82b750d9…`） | **0xAA6A** | **0x8557** |

29420 比 ini 修改（13:12:38）**晚 24 分钟**启动，却用回旧索引 ⇒
**索引由运行时的名字注册顺序决定，同一 ini、同一槽、两次运行可以不同。**

⇒ **后果**：
- 任何**静态** `名字→索引` 表（如 `gem_index.ini`）**只能在生成它的那个进程里用**；
  拿到别的进程里用会**认错宝石**——比"认不出"更危险（会写错对象）。
- 训练器宝石页的**主路径必须是玩家对象定位**（`realBody+0x1FEC`，不依赖索引）；
  索引表只能当"提示"，且失配时要宁可显示裸索引 `0xNNNN` 也不要乱猜名字。

**留档**：`ib3trainer2\gem_index.ini.wrongrun_134248`（PID 31588 那次生成的 152 条表；
在 29420 里整表失配，已挪走）。`gem_index.ini.stale_131757` 是更早一版。

**✅ 已实现（2026-10-07 13:45）**：`Tabs.Gems.cs` 的 `CalibrateFromSave()` —— 每次「读取背包」时，
解密 `Cloud\_SwordSaveX_{0,1,2}-0.bin`，把 `PlayerUnequippedGems` 的**名字按数组顺序**与内存记录**按位 zip**，
当场生成"本次进程有效"的索引映射。取槽判据 = **条数吻合 + 每个名字都能在 `GemDb` 里查到**（防错位）。
未校准到的记录只显示裸索引 `0xNNNN` 且**拒绝修改**（宁可不动，也不乱认）。

字节走查已用真实存档验证：取出 7 条（6×`UberElementalAttackGem_100` + `FireGem`）与参考解析器逐条一致。
静态表（`gem_index.ini`）因此**不再是主路径**，三份留档文件仅作历史参考。

**顺带一条**：部署的 ini 里 `_100` 两段比纯净基线**多一行 `RecipeMatch=<自己>`**——
那是**让 `_100` 能当融合目标**的键（暗火升级链靠它），**不是** `_200` 改造的一部分，别误删。

### 8.3 训练器接入（`E:\ib3_re\ib3trainer2`）

新页签 **宝石·背包**（`Tabs.Gems.cs`）：形状扫描定位宝石数组 → 列出模板名/Tier/融合标记/pct/
当前显示值/地址 → 输入目标值应用。要点：
- **加法型**（有 `RecipeBoostAmount`）Tier 合法 **0~255**；**下标型**（有 `UpgradeTier[]`）
  强制夹在 **1~5**，写超范围会越界读表；两者都没有的模板**拒绝修改**。
- tier **必须单字节写**（写 Int32 会清掉 `+0x09` 的 `CookedGemVar`）。
- 会**把所有同规模数组里的同内容拷贝一起写**（内存里有多份）——但**必须限定同规模**，
  否则商店里 7 颗同状态 T0 暗火会被一起改掉（已实测踩过）。
- 写入走 `MemIO.SafeWrite`（含逐字节回读校验），日志会打印"回读校验 N/M 条落地"。

### 8.4 实测验收结论（2026-10-07 下午）—— 宝石页写入链路打通

**验收结果：通过。** 训练器把暗火 `128000→128500` 后切一次场景，
`Cloud\_SwordSaveX_0-0.bin` 里 `PlayerUnequippedGems[0].tier = 255`，商店 22 条无误伤。

#### ① 权威数组 = 玩家真身 `+0x1FEC`（实验定案）

判别手法：给候选记录的惰性字段 `Boost(+0x14)` 写**互不相同的标记** → `setsave 0` →
看哪个标记进明文。31 个候选里只有 `0x7FF4F8202500` 进了；而全内存**只有一处指针**
指向它：`0x7FF4E8BA202C` = 玩家对象 `+0x1FEC`。**故 `+0x1FEC` 就是权威 `PlayerUnequippedGems`。**

> ⚠️ 由此**推翻** 8.1-③④ 的做法：用 `scanpat` 找暗火记录、再按"看着像"挑权威那份是
> **不可靠的** —— 内存里同内容拷贝极多（实测一次扫出 **31 份**）。可靠的挑法只有
> "谁被指针引用"。

#### ② ⚠️ 数组地址**高频重分配** —— 14:06「写进去游戏无变化」的真正成因

一次会话内实测到的地址变迁：

```
0x7FF4EA0814E0 → 0x7FF4F8202500 → 0x7FF4EA080BE0 → 0x7FF4E748D420 → 0x7FF4E748C7C0
```

训练器原实现是「读一次 → 记住地址 → 之后写」。写入时地址早已失效，
于是出现最坏的情况：**回读校验全过、游戏里毫无变化**（旧地址那块被回收复用，
读写都"成功"，只是改的不是宝石）。14:06 那次就是这个，不是权威性判断错。

⇒ **已修**：`ApplyGemEdit` 写前调 `RemapCopiesLive()` 当场重新定位，
按"同条数 + 逐条四字段一致"把拷贝搬到活地址；核不上就丢弃该组；
连选中的那颗都失效就**拒绝写入**并提示重新读取。

#### ③ 落盘时机：界面立刻变，但要**存进存档**得切场景（中途别打开宝石界面）

| 操作顺序 | 结果 |
|---|---|
| 改内存 → **立刻**切场景 | ✅ 进 Cloud 真档 |
| 改内存 → 先去游戏里打开宝石界面 → 再切场景 | ❌ 改动被丢弃 |

原因：打开宝石界面会让该数组**卸载重载**，未落盘的内存改动被冲掉。
（训练器那次失败正是这个：16:42:57 写入并回读成功，用户先去看游戏，之后才切场景。）

**⚠️ 但要分清"看得到"和"存得住"（2026-10-07 实测补充）**：

- `+0x1FEC` 那个数组**就是游戏 UI 直接读的那一份** ⇒ 改完内存，**界面立刻变，不需要切场景**。
- 切场景（或等游戏约 50 s 的自动存档）解决的是**写进 `Cloud\` 存档**，不是"让界面刷新"。

所以正确的提示是"**界面会立刻变；存进存档请切一次场景**"，而不是"必须切场景才生效"。
训练器写入后的 Toast/日志已按这个措辞改过。

**`setsave 0` 的正确用法**：它读的就是 `+0x1FEC` 那个工作数组，
所以对"内存改没改"是可靠探针，但**不代表已落盘**（实测 `setsave` 显示 `tier=5`、
而随后切场景的 Cloud 仍是 `tier=0`）。判断"是否真生效"必须解 `Cloud\`。

#### ④ ✅ 已破译：加法型宝石的 `GemTier` 只在 `CookedGemVar=50` 时才被游戏承认

**判据实验**（同一数组、同一毫秒写入、同一次切场景，唯一变量是 cook）：

| 记录 | 写入 | Cloud 真档 |
|---|---|---|
| `UberElementalAttackGem_100` | tier 255→254, cook 50 | **254 保留** ✅ |
| `UberAttackGem` | tier 0→6, **cook→50** | **6 保留** ✅ |
| `UberAttackGem` | tier 0→7, **cook 保持 0** | **0 被归零** ❌ |

⇒ **规则**：**加法型**（有 `RecipeBoostAmount`）宝石的 `GemTier` 只在
`CookedGemVar=50`（进过熔炉）时被游戏采用；`cook=0` 的宝石，游戏在切场景重建背包时
把 tier 归一化回 0。
**下标型**（有 `UpgradeTier[]`）不受此限 —— tier 是档位下标，`cook=0` 也照常持久
（商店里 `AttackGem tier=5 cook=0` 即是）。

**这解释了之前两次「改了不起作用」**：那两颗 `UberAttackGem` 从没进过熔炉，
对游戏来说它们本就不该有 tier。**不是地址错、不是模板错，是缺融合标记。**

⇒ **已修**：`ApplyGemEdit` 写加法型 tier 时**同时把 `CookedGemVar` 置 50**。

**受影响的就是这 15 个模板**（官方基线里全部带 `RecipeBoostAmount` 的模板；
`ib3_gems.ini` 已全数收录，自检的 `additive.all` 逐条断言）：

| 模板 | boost | 模板 | boost |
|---|---|---|---|
| `UberAttackGem` | 100 | `UberLightGem` | 200 |
| `UberHealthGem` | 20 | `UberDarkGem` | 200 |
| `UberShieldGem` | 50 | `UberWaterGem` | 200 |
| `UberMagicGem` | 50 | `UberWindGem` | 200 |
| `UberFireGem` | 200 | `UberElementalAttackGem_100` | 500 |
| `UberIceGem` | 200 | `RainbowElementalAttackGem_100` | 250 |
| `UberElecGem` | 200 | `UberBossBoostGem` | 250 |
| `UberPoisonGem` | 200 | | |

> ⚠️ `UberElementalAttackGem`（**没有** `_100` 后缀的那个）**两者皆无** —— 既无
> `RecipeBoostAmount` 也无 `UpgradeTier[]`，是**不可改**的（与 §三「基础档不可升级」一致）。
> 界面上会对它显示为未知类型并拒绝修改。

<details><summary>排查过程留档（三个被否掉的假设）</summary>

先后提过并**被自己的数据否掉**的假设：
1. "`RecipeBoostAmount` 是自定义 ini 键、游戏不认" —— 否：官方基线里就有。
2. "没有升级链所以被夹回 0" —— 否：`_100` 同样没有 `UpgradeTier`。
3. "缺 `RecipeMatch`（不是融合目标）" —— 否：`_100` 在官方基线里也没有 `RecipeMatch`。

另外排除：不是周期性刷新（静置 60 s 轮询两值纹丝不动），也不是"存档路径特殊"
（切场景后**内存里也被归零**了）。
</details>


#### ⑤ 商店数组的**空槽**是合法占位，不是坏数据（2026-10-07 实测）

商店里被买走的宝石会留下一条**全零记录**：

```
内存 CurrentStoreGems[140]  24 字节全 0
存档 CurrentStoreGems[140]  GemName = "None"
```

原先 `RecsShapeOk` 把"名索引 0"当形状异常，**进而废掉整个商店数组**
（152 条一条都拿不到），而且 `LocateByIdentity` 里任一组坏就整体 `return false`，
**把好好的背包一起连坐**——宝石页整个变空。三处已修：

1. `RecsShapeOk`：名索引 0 = 空槽，**合法**；仅当**整组全是空槽**才判异常
   （防"定位成功但背包为空"那个旧坑重现）。报错信息也加上**下标与地址**，
   否则"名索引 0x0"根本没法定位（实测就是为此多跑了一轮）。
2. `LocateByIdentity`：逐条校验**只作废出问题的那一组，绝不连坐**。
3. `ZipEquals` / `NameOne`：空槽特判——内存全零 ↔ 存档 `GemName="None"`，
   内存侧命名为 `(空槽)`、`Kind=Unknown`（界面上不可改）。
   ⚠️ **必须保留槽位**：丢一条，后面 141~151 全部错位，按位 zip 会全盘错。
   （存档那个简化解析器解不出空槽的 tier/cook，会留 `-1`，所以 `ZipEquals` 不特判就必失败。）

#### ⑥ 兜底序列扫描：两道闸从自检移进了产品代码

自检里早就有"命中数组过多"和"FName 索引撞名"两条断言，但**产品代码没有**
——典型的"自检报警而产品照跑"。实测商店 152 条时命中 11 个数组，且把同一个
FName 索引贴成两个不同名字。已把两道闸写进 `LocateBySaveSequence`：
命中数 > 6 判指纹不独特；名字↔索引撞车判对齐错位。两者都返回失败，
交回身份定位（主路径）。

---

## 九、本文件夹文件

| 文件 | 内容 |
|---|---|
| `README.md`（本文） | 入口、结论速查、进度、下一步 |
| `01_数值公式与融合机制.md` | 公式推导全过程、实测数据、跨端判定、存档直改的哈希破译记录 |
| `03_存档迁移.md` | **2026-10-07 新增**：`Cloud\` 读档路径实测证实、`LocalFileHeaderCache` 完整格式、`_CurrentSlot`/`CloudDocIndex` 语义、迁移配方 |
| `02_宝石发放链路（商店路线）.md` | 按名刷店命令、控制台注入方法、宝石发放矩阵、手术墙结论 |
| `04_药水（魔法剂）体系与发放.md` | **2026-10-10 新增**：两族「药水」的区分（A 族=宝石类的 `Potion_*` 魔法剂 / B 族=`SwordInventoryItemPotion`）、`PlayerGemData` 六字段布局、坩埚硬编码配方、`eTouchRewardActor` 全表、发放途径对比 |
| `参考资料/官方宝石模板_DefaultGems.ini.orig` | **官方原始宝石模板表**（改 ini 前的基线；只读它判断"哪些模板是官方有的"） |
| `参考资料/刷店函数字节码_SetPlayerCreateNewListOfStoreGems.txt` | 刷店函数反汇编（`bCheatHighEndGems` 只影响 tier 的证据在此） |
| `参考资料/刷店函数符号表.txt` | 同上函数的符号/字段表 |
| `参考资料/合成配方族.txt` | 三合一配方族清单 |
| `参考资料/模板名清单_db_tpls.txt` | 全部模板名（找宝石模板名用） |
| `参考资料/宝石数据库_ib3_gems.ini` | 训练器用的宝石数据库（含中文名） |
| `参考资料/宝石实例导出样本.txt` | 早期宝石实例导出样本（字段结构参考） |
| `参考资料/工具_gemdump.cs / .exe`、`工具_gemstate2.cs / .exe` | 宝石存档解析工具源码（⚠️ 其 `'None'` 字段解析不完整，读值时以本文第五节为准） |

**留在 `E:\ib3_re\` 的通用工具**（不在本文件夹，但常要用）：
`decsave.exe`（解密存档）、`aespack.py`（加解密）、`sendcmd.exe`（控制台注入）、
`ib3trainer_mem\memtest.exe`（内存扫描/读写）、`all_funcs.txt`（控制台函数签名表）。
