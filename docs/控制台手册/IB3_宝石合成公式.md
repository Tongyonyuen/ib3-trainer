# IB3 宝石合成（熔炼）公式表

> 数据来源：`DefaultGems.ini` 的 RecipeMatch / MPParent / Restrict 字段 + `CheckForGemRecipes` 字节码
> （函数签名 `CheckForGemRecipes(Byte CookVar, Str RecipeGemName, Array Gems)`，内含硬编码特殊配方）。

## 合成机制（代码确认）

- 合成在**大本营的宝石熔炉**（GemScene，内部叫 cooker）进行：放入三颗同族宝石 → 计时 → 完成（三合一）。
- 匹配规则：**三颗同配方族宝石（ini 里 RecipeMatch 值相同）放入熔炉三槽 → 融合，产物 = 该族的 MPParent**（CheckForUberGemCreation 字节码遍历 Gems[0/1/2] 三槽确认，与游戏内三合一一致）。
- `Restrict=1 / 2` = 熔炉槽位标记（In/Out 试剂被 ShouldDisable 禁止正常装备，仅供熔炼；配方档位计算 `GetRecipeBoostedGemTier` 会读取该字段）。
- `RecipeBoostAmount` = 稀有宝石（Uber 系）的合成加成参数。
- 熔炉有烹饪费用与计时（`CalculateGemCookCost` / `CompleteCookingGems`），可付费加速。
- `CheckForGemRecipes` 里硬编码了 `"UberElementalGem1"` —— 元素隐藏链的入口（见下文特殊配方）。

## 一、12 条标准稀有宝石配方（试剂可在商人处购买）

通用模式：**X·合成原料（Restrict=1） + X·合成产物（Restrict=2） + 任意同族X宝石 → 稀有X宝石（UberX）**（三合一）

| 配方族 | 输入1（Restrict=1） | 输入2（Restrict=2） | 产物（MPParent） | 产物效果 |
|---|---|---|---|---|
| AttackGem | AttackGemIn（27.5万） | AttackGemOut（23万） | **UberAttackGem** 稀有攻击宝石 | 伤害+250，Boost=100 |
| HealthGem | HealthGemIn（29.5万） | HealthGemOut（21.5万） | **UberHealthGem** | 生命系，Boost=20 |
| ShieldGem | ShieldGemIn（28万） | ShieldGemOut（16万） | **UberShieldGem** | 盾牌系，Boost=50 |
| MagicGem | MagicGemIn（24万） | MagicGemOut（14万） | **UberMagicGem** | 魔法系，Boost=50 |
| FireGem | FireGemIn（35万） | FireGemOut（27.5万） | **UberFireGem** | 火元素 |
| IceGem | IceGemIn（35万） | IceGemOut（27.5万） | **UberIceGem** | 冰元素 |
| ElecGem | ElecGemIn（35万） | ElecGemOut（27.5万） | **UberElecGem** | 雷元素 |
| PoisonGem | PoisonGemIn（35万） | PoisonGemOut（27.5万） | **UberPoisonGem** | 毒元素 |
| LightGem | LightGemIn（35万） | LightGemOut（27.5万） | **UberLightGem** | 光元素 |
| DarkGem | DarkGemIn（35万） | DarkGemOut（27.5万） | **UberDarkGem** | 暗元素 |
| WaterGem | WaterGemIn（35万） | WaterGemOut（27.5万） | **UberWaterGem** | 水元素 |
| WindGem | WindGemIn（35万） | WindGemOut（27.5万） | **UberWindGem** | 风元素 |

（In/Out 试剂无本地化显示名，商人界面按程序化名称显示；模板名即上表。）

## 二、特殊与隐藏配方

| 配方 | 输入 | 产物 | 说明 |
|---|---|---|---|
| 完美招架 | PerfectParryGem_1/2 ×3 | **GreatParryAllGem**（完美招架全部宝石）| 两个变体模板共享同族（三合一） |
| Boss 强化 | BossBoostGem + BossBoostGem | **UberBossBoostGem** | Boss 等级 +25 → **+1000**（1500 万金）|
| 元素隐藏链 | 两种不同元素 Uber 系宝石 | **UberElementalGem1**（全元素抗性 +1）| `CheckForGemRecipes` 字节码硬编码 "UberElementalGem1" |
| 元素链升级 | UberElementalGem_N ×2 | UberElementalGem_(N+1) | MPParent 链：1→2→3→4→**Max**（全元素抗性 +100）|
| 终极攻击 | 元素 Uber 攻击宝石系 | **UberElementalAttackGem** 稀有暗火宝石（900 万金）| ImmuneToAllBonus=500，带专属特效 |

（UberElementalGem1~Max 均为 unique+ItemRare=100，全元素抗性 1/…/100 递增。）

## 三、普通宝石的同族融合

基础宝石（AttackGem、FireGem 等，价格 180~300 金）与其 In/Out 试剂共享同一 RecipeMatch 族。
普通宝石两两同族融合的档位提升（+1 攻 → +2 攻……）由运行时档位系统处理
（模板里 `MaxRandomAddPct` / `UpgradeTier` 字段定义随机浮动与档位价格），模板本身不存具体档位。

## 四、控制台相关（配合测试）

- 发放任意宝石：`item <模板名>`（如 `item attackgemin`、`item uberelementalgem1`）
- 相关调试：`dumpitemnames`（列物品名）、`checkforgemrecipes` 本身也是 exec，可直接调用
  （参数 = CookVar、RecipeGemName、Gems 数组 —— 数组参数控制台传不了，仅内部使用）
- 完整宝石字段见《IB3_宝石列表.csv》（含 RecipeMatch/MPParent 列）。

## 五、暗火/光谱宝石（UberElemental 系）的同系升级规则

**函数链**（字节码确认）：`CheckForGemRecipes` → `CheckForUberGemCreation`（创造）
→ `CheckForImproveableUberGem(UberGemIndex, RecipeGemName, Gems)`（升级）→ `IsImprovableUberGem` / `SpecialUberElementalCheck`

**数值模型**：
- 基础模板 UberElementalAttackGem：全抗 500（即"暗火+500"）
- 升级模板 UberElementalAttackGem_100：全抗 1000，**RecipeBoostAmount=500**（每次成功升级融合 +500）
- 实测链：+500 → +1000 → +1500 →（+2000？）—— 每次融合增值 = RecipeBoostAmount

**升级融合的限制**（代码结构 + 你观察到的现象一致）：
1. 升级走 `CheckForImproveableUberGem` 专用分支，**不是"暗火+暗火"的同名融合**
   （同名融合走 `SameNameRecipeGemCheck`，三颗暗火必须同档（完整生成名一致）才匹配，+1500/+1000/+500 混放 → 拒绝）
2. 融合按 **CookVar 阶段变量**（0~3，存档里每颗宝石的 CookedGemVar）分支处理：
   不同阶段的宝石（+1500 与 +1000/+500）处于不同 CookVar → 不满足配对条件 → 拒绝
3. 代码里有 `GetModVarBool(83)`/写回调用 —— 升级进度还受一个游戏进度变量（存档 ModVar #83）门控
4. `CheckForGemRecipes` 的 CookVar==3 特殊分支 + `Gems[2]/Gems[3]` 槽位访问 = 顶阶宝石的特殊处理
   （暗火 +1500 可能已接近链顶，能配对的对象极其有限）

**建议的下一步实验**：用"暗火+1500"配一颗**元素 Uber 宝石**（稀有火焰/冰霜/闪电宝石等，
`item uberfiregem` 可发）进熔炉 —— 升级配方很可能是"暗火 + 元素 Uber 催化剂"而非"暗火+暗火"。
若成功应 +500 变 +2000；若仍拒绝，则 +1500 已是该链上限（CookVar==3 顶阶分支）。

## 六、自定义升级链（已实施，2026-10-05）

用户发现暗火/光谱无融合配方后，已在 `DefaultGems.ini` 中人为定义升级链（ini 备份：
`E:\ib3_re\DefaultGems.ini.orig`，164 节 → 166 节）：

```
[UberElementalAttackGem]        += MPParent=UberElementalAttackGem_100   ← 3×暗火+500 → +1000
[UberElementalAttackGem_100]    += MPParent=UberElementalAttackGem_200   ← 3×暗火+1000 → +2000
[UberElementalAttackGem_200]    = 新模板（全抗 2000，2000 万金）
[RainbowElementalAttackGem]     += MPParent=RainbowElementalAttackGem_100
[RainbowElementalAttackGem_100] += MPParent=RainbowElementalAttackGem_200
[RainbowElementalAttackGem_200] = 新模板（全抗 2000）
```

机制依据：MPParent = NameProperty（按名引用宝石数据库模板）；融合 = 同名三合一 → 产物 = MPParent。
重启游戏后（ini 启动时加载），**原有的三颗暗火+1000 直接可融合出 +2000**。
继续扩链：复制 _200 段改名为 _400 并把 _200 的 MPParent 指过去即可（任意档位/任意数值可自定义）。
新模板发放：`item uberelementalattackgem_200`（道具界面内执行）。

## 七、暗火升级配方（用户实测确认，2026-10-05）

**配方：任意暗火 + 任意元素攻击宝石 ×2 → 下一档暗火**

- 催化剂 = 两颗**元素攻击系 Uber 宝石**（稀有火焰/冰霜/闪电/毒/光/暗/水/风——**元素种类不限、可混搭**）
- 每次成功融合：暗火全抗 +500（RecipeBoostAmount=500）
- 用户实测：暗火+400 与任意两颗元素攻击宝石融合 → 升级为下一档 ✓

**机制对应**：`CheckForImproveableUberGem(Int UberGemIndex, Str RecipeGemName, Array Gems)`
- UberGemIndex = 暗火在三槽中的位置；RecipeGemName = 元素攻击宝石名；Gems = 三槽宝石
- 函数内部调用 `GetMasteryXP` / `GetMaxItemLevel` / `GetAbsolutleMaxItemLevel` / `eCurrentPlayerType`
- 元素种类"任意"= 检查的是元素攻击属性而非具体元素（GetElementalTypeAndValue 系）

**重要修正**（推翻本文件此前第五节的部分推断）：
- ❌ "暗火+暗火+暗火 三合一" —— 不是配方（用户实测拒绝）
- ❌ "火500+冰500+暗火" 组合 —— 不是配方（用户实测拒绝）
- ❌ "进度变量门控导致新档无法融合" —— 不成立（原存档上同样被拒，被拒原因是配方组合本身）
- ✅ 正确配方 = 暗火本尊 + 两颗任意元素攻击系 Uber 宝石
- ❓ 光谱（Rainbow）宝石：大概率同配方（光谱 + 2×元素攻击 Uber → 下一档），未实测

**Ini 定义的影响**：暗火/光谱无 RecipeMatch/MPParent 字段——它们的升级配方是
`CheckForImproveableUberGem` 的**运行时硬编码逻辑**，不受 ini 字段控制。
（ini 的 MPParent 链对十二族标准宝石有效；对暗火系无效果——除非通过第六节的
自定义新模板方式人为构建。）
