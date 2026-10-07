# 无尽之剑 III（PC 移植版）内置控制台手册

> 逆向来源：`SwordGame/CookedPCConsole/Engine.upk` + `SwordGame.upk` 的 UFunction 元数据
>（FUNC_Exec=0x200 过滤，参数按 CPF_Parm 排除局部变量），共 **621 条**。
> 参数格式为控制台自动补全/函数声明的权威顺序；`out` 参数不消耗 token 已剔除。

## 使用须知
- 打开控制台：按 `键盘 -` 键（`ConsoleKey=Underscore`，用户可在 `文档\My Games\Infinity Blade III\SwordGame\Config\SwordInput.ini` 修改）。
- 命令不区分大小写；参数以空格分隔；最后一个 Str 参数会吸收剩余文本。
- `Name` 参数写裸标识符（如 `AttackGem`）；`Bool` 写 `1`/`0`（或 true/false）；`Byte` 写十进制数。
- **CheatManager 类命令**（God/Fly/Ghost/Slomo 等）需要先执行 `EnableCheats` 生成 CheatManager；
  SwordPC/SwordPlayer 系列命令直接可用。
- 通用原生命令（无脚本声明，所有 UE3 内置）：`get <类> <属性>`、`set <类> <属性> <值>`、`exit`/`quit`、`flushlog`（exe 内已确认）、`stat <模块>`（stat fps 等，exe 内已确认帮助文本）。
- 物品/宝石发放：`Item <模板名>`、`GiveAndMasterItem <模板名>`（送并满级）、`SetPlayerGiveAllItems`（全物品）、
  `DumpItemNames`（在日志/屏幕列出全部物品名）。宝石模板名见《IB3_宝石列表.csv》。

### SwordPC（IB3 玩家控制器）— 291 条

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| mobilegamestartmatch | 开始一场移动端比赛(内部) | `mobilegamestartmatch` | 无 | `mobilegamestartmatch` |  |
| openclashmobmenu | 打开ClashMob菜单 | `openclashmobmenu` | 无 | `openclashmobmenu` |  |
| hidesubtitlesonfirstrun | 首次运行隐藏字幕 | `hidesubtitlesonfirstrun` | 无 | `hidesubtitlesonfirstrun` |  |
| resetsubtitlesafterfirstrun | 复位首次运行字幕状态 | `resetsubtitlesafterfirstrun` | 无 | `resetsubtitlesafterfirstrun` |  |
| hideallchoicenodes | 隐藏全部分支节点 | `hideallchoicenodes` | 无 | `hideallchoicenodes` |  |
| sworddisablesleep | 禁止系统休眠 | `sworddisablesleep` | 无 | `sworddisablesleep` |  |
| swordenablesleep | 允许系统休眠 | `swordenablesleep` | 无 | `swordenablesleep` |  |
| showdemovideo | 播放演示视频 | `showdemovideo` | 无 | `showdemovideo` |  |
| appledemo | Apple演示模式 | `appledemo <Type>` | type=整数：类型 | `appledemo 0` |  |
| appledemofull | 完整Apple演示模式 | `appledemofull` | 无 | `appledemofull` |  |
| pax | 进入PAX展会演示模式 | `pax` | 无 | `pax` |  |
| paxprog | 推进展会演示进度 | `paxprog <Progress>` | progress=整数：进度 | `paxprog 5` |  |
| setplayerdragonfight | 进入屠龙战 | `setplayerdragonfight <FixedStart> <ForceMap>` | fixedStart=整数：起始方式；forceMap=文本：强制地图 | `setplayerdragonfight 0 map` |  |
| checkfordragonpausedeath | 检查龙战暂停死亡(调试) | `checkfordragonpausedeath` | 无 | `checkfordragonpausedeath` |  |
| setupcollector | 进入收藏家模式 | `setupcollector <ForceWeaponStart>` | forceWeaponStart=整数：武器起点 | `setupcollector 0` |  |
| setplayerquicktimedifficulty | 设置QTE(快速反应)难度 | `setplayerquicktimedifficulty <SetValue>` | setValue=整数：难度值 | `setplayerquicktimedifficulty 0` |  |
| setforceswordclash | 强制触发剑刃对拼(QTE) | `setforceswordclash` | 无 | `setforceswordclash` |  |
| setplayercombatlog | 开关战斗日志输出 | `setplayercombatlog <bSet>` | bSet=0或1：是否开启 | `setplayercombatlog 1` |  |
| setplayercombolog | 开关连击日志输出 | `setplayercombolog <bSet>` | bSet=0或1：是否开启 | `setplayercombolog 1` |  |
| playambientmusic | 播放环境音乐 | `playambientmusic` | 无 | `playambientmusic` |  |
| pausesong | 暂停当前音乐 | `pausesong` | 无 | `pausesong` |  |
| resumeprevioussong | 恢复上一首音乐 | `resumeprevioussong` | 无 | `resumeprevioussong` |  |
| assistclashmobcurrentmap | 以协助模式打当前ClashMob地图 | `assistclashmobcurrentmap <LoopAssistCount> <LoopAssistTime> <GoalProgress>` | loopAssistCount=整数：次数；loopAssistTime=小数：时长；goalProgress=整数：目标进度 | `assistclashmobcurrentmap 5 60 100` |  |
| setplayerstopallmatinee | 停止全部过场动画 | `setplayerstopallmatinee` | 无 | `setplayerstopallmatinee` |  |
| setmastervolume | 设置主音量 | `setmastervolume <bNoTurnOffCheck> <fxvolume>` | bNoTurnOffCheck=0或1；fxvolume=小数：音量(0~1) | `setmastervolume 0 1` |  |
| updatemastervolume | 刷新主音量 | `updatemastervolume` | 无 | `updatemastervolume` |  |
| setinputdefenseholddelay | 设置防御输入延迟 | `setinputdefenseholddelay <DodgeHoldDelay> <BlockHoldDelay>` | dodgeHoldDelay=小数：闪避延迟；blockHoldDelay=小数：格挡延迟 | `setinputdefenseholddelay 0.1 0.1` |  |
| loadnextboss | 加载下一场Boss战 | `loadnextboss <MapName> <bNextClassType>` | mapName=文本：地图名；bNextClassType=0或1：是否下一形态 | `loadnextboss map_x 1` |  |
| sf | SetFight 的缩写，直接进入指定Boss战 | `sf <MapName> <FightMapVariation> <BossClass> <BossName>` | mapName=文本；fightMapVariation=整数；bossClass=整数；bossName=文本 | `sf map_x 0 1 titan` |  |
| setfight | 直接进入指定Boss战 | `setfight <MapName> <BattleMapVariation> <BossClass> <BossName>` | mapName=文本：地图名；battleMapVariation=整数：变体；bossClass=整数：Boss类别；bossName=文本：Boss名 | `setfight map_x 0 1 titan` |  |
| testfight | 以测试配置进入一场Boss战 | `testfight` | 无 | `testfight` |  |
| setboss | 设置Boss行为开关 | `setboss <bDontTakeDamage> <bDontBlock> <bDontInitiateAttack>` | bDontTakeDamage=0或1：Boss不受伤；bDontBlock=0或1：Boss不格挡；bDontInitiateAttack=0或1：Boss不主动攻击 | `setboss 0 1 1` |  |
| setbossattack | 强制Boss使用指定攻击动画 | `setbossattack <ForcedAttackAnim>` | forcedAttackAnim=标识符：动画名 | `setbossattack anim_x` |  |
| setbossidleanim | 设置Boss待机动画 | `setbossidleanim <NewIdle>` | newIdle=标识符：动画名 | `setbossidleanim anim` |  |
| setbossforceallowspecialattacks | 允许Boss使用特殊技 | `setbossforceallowspecialattacks` | 无 | `setbossforceallowspecialattacks` |  |
| setbossforcemagicanimrate | 设置Boss魔法动画速率 | `setbossforcemagicanimrate <Rate>` | rate=小数：倍率 | `setbossforcemagicanimrate 1` |  |
| setbossforceattackset | 强制Boss攻击组合 | `setbossforceattackset <ForcedAttackSet>` | forcedAttackSet=整数：组合编号 | `setbossforceattackset 1` |  |
| setbossforceparryreact | 强制Boss招架反应 | `setbossforceparryreact <ReactType>` | reactType=整数：反应类型 | `setbossforceparryreact 1` |  |
| setbossforceswordclash | 强制Boss触发剑刃对拼 | `setbossforceswordclash` | 无 | `setbossforceswordclash` |  |
| setbossforceparryattack | 强制Boss招架攻击 | `setbossforceparryattack` | 无 | `setbossforceparryattack` |  |
| setbosstagaskilled | 把当前Boss标记为已击杀 | `setbosstagaskilled` | 无 | `setbosstagaskilled` |  |
| setbosscinscale | 设置过场中Boss模型缩放 | `setbosscinscale <ForceBoss>` | forceBoss=标识符 | `setbosscinscale titan` |  |
| setbossmeshscale | 缩放当前Boss模型 | `setbossmeshscale <ForceBoss> <ForceScale>` | forceBoss=标识符：Boss名；forceScale=小数：缩放倍率 | `setbossmeshscale titan 2` |  |
| resetbossmeshscale | 恢复Boss模型缩放 | `resetbossmeshscale <ForceBoss>` | forceBoss=标识符：Boss名 | `resetbossmeshscale titan` |  |
| setbosselementalattack | 设置Boss的元素攻击类型 | `setbosselementalattack <ElementalAttackType>` | elementalAttackType=0~255整数：元素类型编号 | `setbosselementalattack 3` |  |
| setbosselementalresist | 设置Boss的元素抗性类型 | `setbosselementalresist <ElementalAttackType>` | elementalAttackType=0~255整数：元素类型编号 | `setbosselementalresist 3` |  |
| setbossmagicattack | 设置Boss魔法攻击特效 | `setbossmagicattack <MagicFXName>` | magicFXName=标识符：特效名 | `setbossmagicattack fx` |  |
| setbossdumpcurrentattacks | 转储Boss当前攻击列表 | `setbossdumpcurrentattacks` | 无 | `setbossdumpcurrentattacks` |  |
| setbosslevel | 设置当前Boss等级 | `setbosslevel <iLevel>` | ilevel=整数：等级 | `setbosslevel 50` |  |
| pawnpoints | 显示角色坐标点(调试) | `pawnpoints <B> <bDontFlush> <fScale>` | b=对象；bDontFlush=0或1；fScale=小数 | `pawnpoints none 1 1` |  |
| setbossnextweapon | 让Boss切换下一件武器 | `setbossnextweapon` | 无 | `setbossnextweapon` |  |
| setbossnextclass | Boss切换下一职业形态 | `setbossnextclass` | 无 | `setbossnextclass` |  |
| cyclebossonretry | 重试战斗时轮换Boss形态 | `cyclebossonretry` | 无 | `cyclebossonretry` |  |
| setbossdisarm | 缴械当前Boss(去掉武器) | `setbossdisarm` | 无 | `setbossdisarm` |  |
| forcebossweaponswitch | 强制Boss切换到指定武器类别 | `forcebossweaponswitch <ForceClassIndex>` | forceClassIndex=整数：类别编号 | `forcebossweaponswitch 2` |  |
| bossinfo | 在屏幕/日志显示当前Boss信息 | `bossinfo` | 无 | `bossinfo` |  |
| trainingprevattackset | 训练模式上一攻击组合 | `trainingprevattackset` | 无 | `trainingprevattackset` |  |
| trainingnextattackset | 训练模式下一攻击组合 | `trainingnextattackset` | 无 | `trainingnextattackset` |  |
| setbosscycleelemental | 轮换Boss元素 | `setbosscycleelemental` | 无 | `setbosscycleelemental` |  |
| setplayercycleelemental | 轮换玩家元素 | `setplayercycleelemental` | 无 | `setplayercycleelemental` |  |
| setplayerlevel | 设置玩家等级 | `setplayerlevel <iLevel>` | ilevel=整数：目标等级 | `setplayerlevel 50` |  |
| setplayergiveallperks | 解锁全部特长(Perks) | `setplayergiveallperks` | 无 | `setplayergiveallperks` |  |
| setplayerallstats | 将玩家全部属性设为同一值 | `setplayerallstats <ForceStatsTo>` | forceStatsTo=整数：属性值 | `setplayerallstats 1000` |  |
| setplayerskiptutorial | 跳过教程 | `setplayerskiptutorial` | 无 | `setplayerskiptutorial` |  |
| setplayergiverandomgem | 随机发放一颗宝石 | `setplayergiverandomgem <FavorSocketType> <GoldScale> <RewardLevel>` | favorSocketType=整数：偏好槽位形状；goldScale=小数：价值缩放；rewardLevel=整数：奖励等级 | `setplayergiverandomgem 0 1 50` |  |
| setplayergiveallitems | 一次性发放全套物品(全武器/防具/戒指/宝石等) | `setplayergiveallitems` | 无 | `setplayergiveallitems` |  |
| setplayermasterallitems | 将全部物品设为满熟练度/指定等级 | `setplayermasterallitems <SetAllToLevel> <MasterCount>` | setAllToLevel=整数：目标等级；masterCount=整数：数量 | `setplayermasterallitems 10 100` |  |
| setplayeritemlevel | 设置当前物品的等级 | `setplayeritemlevel <ForceLevel>` | forceLevel=整数：目标等级 | `setplayeritemlevel 10` |  |
| item | 发放指定模板名的物品到背包(模板名=物品ini节名,宝石见宝石列表) | `item <ItemName>` | itemName=文本：物品模板名 | `item attackgem / item sword_1` |  |
| setplayerrandomdropratios | 设置随机掉落比例(各类权重) | `setplayerrandomdropratios <Items> <Gems> <Potions> <Keys> <Gold>` | items=整数：物品；gems=整数：宝石；potions=整数：药水；keys=整数：钥匙；gold=整数：金币 | `setplayerrandomdropratios 50 50 30 10 60` |  |
| setplayerrandomitemdropratios | 设置各类装备的随机掉落比例 | `setplayerrandomitemdropratios <Magic> <Helmet> <Armor> <Shield> <Sword>` | magic=整数：魔法；helmet=整数：头盔；armor=整数：护甲；shield=整数：盾牌；sword=整数：剑 | `setplayerrandomitemdropratios 10 10 10 10 60` |  |
| setplayercustomfx | 设置自定义特效 | `setplayercustomfx <CustomFXName>` | customFXName=标识符：特效名 | `setplayercustomfx fx_x` |  |
| setplayerfootstepsurface | 设置脚步声材质类型 | `setplayerfootstepsurface <FootStepMaterialType>` | footStepMaterialType=0~255整数：材质编号 | `setplayerfootstepsurface 1` |  |
| approvefacebook | 批准Facebook关联 | `approvefacebook` | 无 | `approvefacebook` |  |
| postfacebookstats | 发布Facebook统计(无效) | `postfacebookstats <bAllowPopups>` | bAllowPopups=0或1 | `postfacebookstats 0` |  |
| dumpplayerxpstats | 转储玩家经验统计 | `dumpplayerxpstats <PlayThrough>` | playThrough=整数：周目数 | `dumpplayerxpstats 0` |  |
| dumpanimsetfxplayerusage | 转储玩家动画集特效使用 | `dumpanimsetfxplayerusage <OnlyAnimSet>` | onlyAnimSet=整数：动画集编号 | `dumpanimsetfxplayerusage 0` |  |
| dumpanimsetfxbossusage | 转储Boss动画集特效使用 | `dumpanimsetfxbossusage <OnlyAnimSet>` | onlyAnimSet=整数：动画集编号 | `dumpanimsetfxbossusage 0` |  |
| dumpitemstats | 向日志转储全部物品的属性数据 | `dumpitemstats` | 无 | `dumpitemstats` |  |
| dumpitemnames | 向日志转储全部物品模板名(配合日志文件查名) | `dumpitemnames` | 无 | `dumpitemnames` |  |
| setplayerstats | 一次性设置玩家四维属性 | `setplayerstats <Magic> <ShieldHealth> <Health> <Damage>` | magic=整数：魔法；shieldHealth=整数：护盾血量；health=整数：生命；damage=整数：伤害 | `setplayerstats 500 5000 5000 2000` |  |
| setplayerdodgevars | 设置闪避参数 | `setplayerdodgevars <DodgeRechargeSeconds> <DodgeCountMax>` | dodgeRechargeSeconds=整数：闪避充能秒数；dodgeCountMax=整数：闪避次数上限 | `setplayerdodgevars 0 99` |  |
| setplayermultistab | 设置连突刺参数 | `setplayermultistab <StabAnimSpeed> <StabAnimBlendOutPct>` | stabAnimSpeed=小数：动画速度；stabAnimBlendOutPct=小数：混合比例 | `setplayermultistab 2 0.5` |  |
| setplayerglobalanimspeed | 设置全局动画速度 | `setplayerglobalanimspeed <Speed>` | speed=小数：速度倍率(1=正常) | `setplayerglobalanimspeed 0.5` |  |
| setplayermaxhealth | 设置玩家生命上限 | `setplayermaxhealth <To>` | to=整数：上限值 | `setplayermaxhealth 10000` |  |
| setplayerhealth | 设置玩家当前生命值 | `setplayerhealth <To>` | to=整数：生命值 | `setplayerhealth 10000` |  |
| setplayergold | 设置玩家金币余额为指定值 | `setplayergold <Gold>` | gold=整数：要设置的金币数量 | `setplayergold 999999999` |  |
| setplayerchips | 设置玩家筹码(内部货币)余额 | `setplayerchips <Chips>` | chips=整数：筹码数量 | `setplayerchips 5000` |  |
| setplayerdisplaystatus | 开关HUD扩展显示 | `setplayerdisplaystatus <bShowExtraParryInfo> <bShowXP> <bShow>` | bShowExtraParryInfo=0或1：额外招架信息；bShowXP=0或1：经验；bShow=0或1：总开关 | `setplayerdisplaystatus 1 1 1` |  |
| playplayertagfx | 按标签播放玩家特效 | `playplayertagfx <FXTag>` | fXTag=标识符：特效标签 | `playplayertagfx fx_x` |  |
| playbosstagfx | 按标签播放Boss特效 | `playbosstagfx <FXTag>` | fXTag=标识符：特效标签 | `playbosstagfx fx_x` |  |
| setplayersword | 切换指定编号的剑(编号见 DumpItemNames 输出) | `setplayersword <SwordNumber>` | swordNumber=整数：武器编号 | `setplayersword 3` |  |
| setplayershield | 切换指定编号的盾牌 | `setplayershield <ShieldNumber>` | shieldNumber=整数：盾牌编号 | `setplayershield 2` |  |
| setplayerarmor | 切换指定编号的护甲 | `setplayerarmor <ArmorNumber>` | armorNumber=整数：护甲编号 | `setplayerarmor 1` |  |
| setplayerhelmet | 切换指定编号的头盔 | `setplayerhelmet <HelmetNumber>` | helmetNumber=整数：头盔编号 | `setplayerhelmet 1` |  |
| setplayermagic | 切换指定编号的魔法 | `setplayermagic <MagicNumber>` | magicNumber=整数：魔法编号 | `setplayermagic 1` |  |
| setplayernextweapon | 切换到下一件武器 | `setplayernextweapon` | 无 | `setplayernextweapon` |  |
| setplayercyclemagicspells | 轮换已装备的魔法组合 | `setplayercyclemagicspells` | 无 | `setplayercyclemagicspells` |  |
| setplayerboostmagicspelllevel | 提升当前魔法咒语的等级 | `setplayerboostmagicspelllevel` | 无 | `setplayerboostmagicspelllevel` |  |
| setplayerinputfilter | 屏蔽玩家的部分输入(调试用) | `setplayerinputfilter <NoMagic> <NoSuper> <NoDodge> <NoShield> <NoAttack>` | noMagic=0或1：禁魔法；noSuper=0或1：禁大招；noDodge=0或1：禁闪避；noShield=0或1：禁格挡；noAttack=0或1：禁攻击 | `setplayerinputfilter 1 0 0 0 0` |  |
| setbossbattlefx | 开关Boss战特效 | `setbossbattlefx <bCameraShake> <bScreenFlash> <bSound> <bAnimFX> <bFX>` | bCameraShake=0或1：镜头震动；bScreenFlash=0或1：闪屏；bSound=0或1：音效；bAnimFX=0或1：动画特效；bFX=0或1：特效 | `setbossbattlefx 0 0 0 0 0` |  |
| setbossrage | 设置Boss怒气值 | `setbossrage <Rage>` | rage=小数：怒气(0~1) | `setbossrage 1` |  |
| setplayerfilterslomo | 开关过滤式慢动作 | `setplayerfilterslomo <bSet>` | bSet=0或1：是否开启 | `setplayerfilterslomo 1` |  |
| exitbossfight | 立即退出当前Boss战 | `exitbossfight` | 无 | `exitbossfight` |  |
| forcefighttransition | 强制战斗转场 | `forcefighttransition` | 无 | `forcefighttransition` |  |
| forcesuperdodgeactive | 强制完美闪避生效 | `forcesuperdodgeactive` | 无 | `forcesuperdodgeactive` |  |
| killboss | 立即击杀当前Boss | `killboss` | 无 | `killboss` |  |
| setmpgodmode | 切换多人/挑战模式的上帝模式 | `setmpgodmode <bSet>` | bSet=0或1：是否开启 | `setmpgodmode 1` |  |
| setmpfullmeters | 将大招/魔法槽充满 | `setmpfullmeters <SpecialLevel>` | specialLevel=小数：槽位值 | `setmpfullmeters 999` |  |
| dobossspecial | 让Boss释放特殊技 | `dobossspecial` | 无 | `dobossspecial` |  |
| dobossspecialmash | 模拟Boss特殊技连打 | `dobossspecialmash` | 无 | `dobossspecialmash` |  |
| dobosssupermash | 模拟Boss大招连打 | `dobosssupermash` | 无 | `dobosssupermash` |  |
| dumpmpbossattackcheck | 转储多人Boss攻击判定 | `dumpmpbossattackcheck` | 无 | `dumpmpbossattackcheck` |  |
| showmenuitem | 显示指定物品的菜单 | `showmenuitem <ItemName>` | itemName=标识符：物品名 | `showmenuitem sword_1` |  |
| testcloudsave | 测试云存档写入 | `testcloudsave` | 无 | `testcloudsave` |  |
| testcloudload | 测试云存档读取 | `testcloudload` | 无 | `testcloudload` |  |
| dumpunencryptedsavefile | 将存档解密转储到日志 | `dumpunencryptedsavefile <bDeleteSave> <Index> <SavedFileName>` | bDeleteSave=0或1：是否删除；index=整数：编号；savedFileName=文本：文件名 | `dumpunencryptedsavefile 0 0 save.bin` |  |
| setsave | 写入指定编号的存档 | `setsave <SaveIndex>` | saveIndex=整数：存档编号 | `setsave 1` |  |
| loadsave | 读取指定编号的存档 | `loadsave <SaveIndex>` | saveIndex=整数：存档编号 | `loadsave 1` |  |
| showrebirthtime | 显示下次转生剩余时间 | `showrebirthtime` | 无 | `showrebirhttime` |  |
| showlocalizedtext | 在屏幕显示本地化文本(调试) | `showlocalizedtext <TimeToShow> <Key> <Section>` | timeToShow=小数：显示秒数；key=文本：键名；section=文本：节名 | `showlocalizedtext 5 SOME_KEY SwordGame` |  |
| loadchoicemodepoint | 读取分支模式存档点 | `loadchoicemodepoint` | 无 | `loadchoicemodepoint` |  |
| showclashmobpopup | 弹出ClashMob活动提示 | `showclashmobpopup <EventID>` | eventID=文本：活动ID | `showclashmobpopup event_x` |  |
| forcedebugtouchactors | 强制调试触摸Actor | `forcedebugtouchactors <bAll>` | bAll=0或1：是否全部 | `forcedebugtouchactors 1` |  |
| forcealltouchrandom | 强制所有触摸点随机化 | `forcealltouchrandom <bDebugShow>` | bDebugShow=0或1：是否显示调试 | `forcealltouchrandom 1` |  |
| forcecyclealltouch | 轮换全部触摸点内容 | `forcecyclealltouch` | 无 | `forcecyclealltouch` |  |
| setshowsubtitles | 开关字幕 | `setshowsubtitles <bValue>` | bValue=0或1：是否显示 | `setshowsubtitles 1` |  |
| setcurrentsaveslot | 设置当前存档文件与槽位 | `setcurrentsaveslot <SaveFileIndex> <SaveSlotIndex>` | saveFileIndex=整数：文件编号；saveSlotIndex=整数：槽位编号 | `setcurrentsaveslot 0 1` |  |
| clearallsavegames | 删除全部存档(危险！不可恢复) | `clearallsavegames` | 无 | `clearallsavegames` |  |
| deletesavegame | 删除当前存档(危险！) | `deletesavegame` | 无 | `deletesavegame` |  |
| resetmap | 重置当前地图 | `resetmap` | 无 | `resetmap` |  |
| loadlastsavepoint | 读取最近存档点 | `loadlastsavepoint` | 无 | `loadlastsavepoint` |  |
| loadmakegameeasier | 读档并回退使游戏变简单 | `loadmakegameeasier <RollbackCount>` | rollbackCount=整数：回退次数 | `loadmakegameeasier 1` |  |
| rollbackgamefinishedcount | 回退通关计数 | `rollbackgamefinishedcount <RollbackCount>` | rollbackCount=整数：回退次数 | `rollbackgamefinishedcount 1` |  |
| loadstartnewbloodlinekilled | 以击杀指定稀有Boss的状态开新血脉 | `loadstartnewbloodlinekilled <UberBossIndex>` | uberBossIndex=整数：Boss编号 | `loadstartnewbloodlinekilled 1` |  |
| setplayermoney | 同时设置筹码与金币余额 | `setplayermoney <Chips> <Gold>` | chips=整数：筹码(内部货币)；gold=整数：金币数量 | `setplayermoney 0 999999999` |  |
| loadstartnewbloodline | 开始新血脉(转生重开) | `loadstartnewbloodline <ForceBloodline>` | forceBloodline=整数：血脉代数 | `loadstartnewbloodline 5` |  |
| loadstartib3finishgame | 以通关状态开始游戏 | `loadstartib3finishgame <bSkipTutorial>` | bSkipTutorial=0或1：是否跳过教程 | `loadstartib3finishgame 1` |  |
| loadstartnewgameplus | 开始二周目(New Game+) | `loadstartnewgameplus` | 无 | `loadstartnewgameplus` |  |
| tothenegativeworld | 进入负血脉世界 | `tothenegativeworld <NegativeBloodlineGeneration>` | negativeBloodlineGeneration=整数：负血脉代数 | `tothenegativeworld 5` |  |
| hidesaber | 隐藏武器(全部) | `hidesaber` | 无 | `hidesaber` |  |
| hidesaberl | 隐藏左手武器 | `hidesaberl` | 无 | `hidesaberl` |  |
| hidesaberr | 隐藏右手武器 | `hidesaberr` | 无 | `hidesaberr` |  |
| showsaber | 显示武器(全部) | `showsaber` | 无 | `showsaber` |  |
| showsaberl | 显示左手武器 | `showsaberl` | 无 | `showsaberl` |  |
| showsaberr | 显示右手武器 | `showsaberr` | 无 | `showsaberr` |  |
| saberon | 显示双持武器 | `saberon` | 无 | `saberon` |  |
| saber1on | 显示1号武器 | `saber1on` | 无 | `saber1on` |  |
| saber2on | 显示2号武器 | `saber2on` | 无 | `saber2on` |  |
| saberoff | 隐藏双持武器 | `saberoff` | 无 | `saberoff` |  |
| saber1off | 隐藏1号武器 | `saber1off` | 无 | `saber1off` |  |
| saber2off | 隐藏2号武器 | `saber2off` | 无 | `saber2off` |  |
| cleanuptrainingfight | 清理训练战 | `cleanuptrainingfight` | 无 | `cleanuptrainingfight` |  |
| ll | 快速加载指定关卡 | `ll <LevelToLoad>` | levelToLoad=文本：关卡名 | `ll a01_map` |  |
| explicitloadlevel | 显式加载指定关卡 | `explicitloadlevel <LevelStartIndex> <LevelToLoad>` | levelStartIndex=整数：起始点；levelToLoad=文本：关卡名 | `explicitloadlevel 0 a01_map` |  |
| loadlevel | 加载指定关卡 | `loadlevel <bExplicitMapName> <bSkipSave> <LevelStartIndex> <LevelToLoad>` | bExplicitMapName=0或1：是否显式地图名；bSkipSave=0或1：是否跳过存档；levelStartIndex=整数：起始点；levelToLoad=文本：关卡名 | `loadlevel 0 0 0 a01_map` |  |
| hideout | 返回大本营(主城) | `hideout` | 无 | `hideout` |  |
| forcerebirth | 强制转生指定次数 | `forcerebirth <RebirthCount>` | rebirthCount=整数：转生次数 | `forcerebirth 10` |  |
| killedgodkingnewworld | 触发击杀神王后的新世界流程 | `killedgodkingnewworld` | 无 | `killedgodkingnewworld` |  |
| slashlefthigh | 向左上方挥砍 | `slashlefthigh` | 无 | `slashlefthigh` |  |
| slashleftlow | 向左下方挥砍 | `slashleftlow` | 无 | `slashleftlow` |  |
| slashleft | 向左挥砍 | `slashleft <Dir>` | dir=整数：方向 | `slashleft 1` |  |
| slashcenterleft | 向中左挥砍 | `slashcenterleft` | 无 | `slashcenterleft` |  |
| slashcenterright | 向中右挥砍 | `slashcenterright` | 无 | `slashcenterright` |  |
| slashcenter | 向中挥砍 | `slashcenter <Dir>` | dir=整数：方向 | `slashcenter 1` |  |
| slashrighthigh | 向右上方挥砍 | `slashrighthigh` | 无 | `slashrighthigh` |  |
| slashrightlow | 向右下方挥砍 | `slashrightlow` | 无 | `slashrightlow` |  |
| slashright | 向右挥砍 | `slashright <Dir>` | dir=整数：方向 | `slashright 1` |  |
| slashdownleft | 向左下挥砍 | `slashdownleft` | 无 | `slashdownleft` |  |
| slashdownright | 向右下挥砍 | `slashdownright` | 无 | `slashdownright` |  |
| slashdown | 向下挥砍 | `slashdown <Dir>` | dir=整数：方向 | `slashdown 1` |  |
| superleft | 左方向大招斩 | `superleft` | 无 | `superleft` |  |
| supercenter | 中方向大招斩 | `supercenter` | 无 | `supercenter` |  |
| superright | 右方向大招斩 | `superright` | 无 | `superright` |  |
| superdown | 下方向大招斩 | `superdown` | 无 | `superdown` |  |
| swordstab | 执行突刺 | `swordstab` | 无 | `swordstab` |  |
| buttondodgeright | 按住右闪避 | `buttondodgeright` | 无 | `buttondodgeright` |  |
| dodgeright | 向右闪避 | `dodgeright` | 无 | `dodgeright` |  |
| releasebuttondodgeright | 松开右闪避 | `releasebuttondodgeright` | 无 | `releasebuttondodgeright` |  |
| buttondodgeleft | 按住左闪避 | `buttondodgeleft` | 无 | `buttondodgeleft` |  |
| dodgeleft | 向左闪避 | `dodgeleft` | 无 | `dodgeleft` |  |
| releasebuttondodgeleft | 松开左闪避 | `releasebuttondodgeleft` | 无 | `releasebuttondodgeleft` |  |
| buttondodgecenter | 按住后闪避 | `buttondodgecenter` | 无 | `buttondodgecenter` |  |
| dodgecenter | 向后闪避 | `dodgecenter` | 无 | `dodgecenter` |  |
| releasebuttondodgecenter | 松开后闪避 | `releasebuttondodgecenter` | 无 | `releasebuttondodgecenter` |  |
| buttonblockcenter | 按住中格挡 | `buttonblockcenter` | 无 | `buttonblockcenter` |  |
| releasebuttonblockcenter | 松开中格挡 | `releasebuttonblockcenter` | 无 | `releasebuttonblockcenter` |  |
| releaseblockcenter | 松开中格挡 | `releaseblockcenter` | 无 | `releaseblockcenter` |  |
| buttonblockleft | 按住左格挡 | `buttonblockleft` | 无 | `buttonblockleft` |  |
| releasebuttonblockleft | 松开左格挡 | `releasebuttonblockleft` | 无 | `releasebuttonblockleft` |  |
| releaseblockleft | 松开左格挡 | `releaseblockleft` | 无 | `releaseblockleft` |  |
| buttonblockright | 按住右格挡 | `buttonblockright` | 无 | `buttonblockright` |  |
| releasebuttonblockright | 松开右格挡 | `releasebuttonblockright` | 无 | `releasebuttonblockright` |  |
| releaseblockright | 松开右格挡 | `releaseblockright` | 无 | `releaseblockright` |  |
| pcdefendleft | 按住左侧防御 | `pcdefendleft` | 无 | `pcdefendleft` |  |
| releasepcdefendleft | 松开左防御 | `releasepcdefendleft` | 无 | `releasepcdefendleft` |  |
| pcdefendcenter | 按住中部防御 | `pcdefendcenter` | 无 | `pcdefendcenter` |  |
| releasepcdefendcenter | 松开中防御 | `releasepcdefendcenter` | 无 | `releasepcdefendcenter` |  |
| pcdefendright | 按住右侧防御 | `pcdefendright` | 无 | `pcdefendright` |  |
| releasepcdefendright | 松开右防御 | `releasepcdefendright` | 无 | `releasepcdefendright` |  |
| pcswordclashmash | 玩家侧对拼连打 | `pcswordclashmash` | 无 | `pcswordclashmash` |  |
| setbossplayercontrolled | 将当前Boss改为玩家可控 | `setbossplayercontrolled <bSet>` | bSet=0或1：是否接管 | `setbossplayercontrolled 1` |  |
| swordclashmash | 剑刃对拼连打模拟 | `swordclashmash` | 无 | `swordclashmash` |  |
| ongiveragebonus | 给予怒气加成(内部) | `ongiveragebonus <RageBonus>` | rageBonus=小数 | `ongiveragebonus 0.5` |  |
| showpendingtreasureaward | 显示待领取的宝藏奖励 | `showpendingtreasureaward` | 无 | `showpendingtreasureaward` |  |
| opengamewonscreen | 打开通关画面 | `opengamewonscreen` | 无 | `opengamewonscreen` |  |
| showplayerinfo | 显示玩家信息面板 | `showplayerinfo` | 无 | `showplayerinfo` |  |
| closeplayerinfo | 关闭玩家信息面板 | `closeplayerinfo` | 无 | `closeplayerinfo` |  |
| togglebosspopup | 开关Boss信息弹窗 | `togglebosspopup <bOnlyClose>` | bOnlyClose=0或1：是否仅关闭 | `togglebosspopup 0` |  |
| showcredits | 播放制作人员名单 | `showcredits <Mode>` | mode=文本：模式 | `showcredits full` |  |
| failhardcorequest | 使当前硬核任务失败 | `failhardcorequest` | 无 | `failhardcorequest` |  |
| completehardcorequest | 使当前硬核任务完成 | `completehardcorequest` | 无 | `completehardcorequest` |  |
| showcollectorprompt | 显示收藏家提示 | `showcollectorprompt` | 无 | `showcollectorprompt` |  |
| resetcontentpack | 重置内容包 | `resetcontentpack <To>` | to=整数：包编号 | `resetcontentpack 0` |  |
| showtutorial | 强制显示指定教程 | `showtutorial <bKillCameraAnims> <bCloseOnPauseExit> <TutorialName>` | bKillCameraAnims=0或1；bCloseOnPauseExit=0或1；tutorialName=文本：教程名 | `showtutorial 0 1 tut_x` |  |
| hidetutorial | 隐藏教程 | `hidetutorial <bClosedFromPause>` | bClosedFromPause=0或1 | `hidetutorial 0` |  |
| disabletutorial | 禁用教程系统 | `disabletutorial <bOnUnpause> <bDisable>` | bOnUnpause=0或1；bDisable=0或1 | `disabletutorial 0 1` |  |
| pausegame | 打开暂停菜单(带回调,慎用) | `pausegame <OnOpenScene> <bFade> <Mode> <PauseScene>` | onOpenScene=委托；bFade=0或1；mode=文本；pauseScene=类名 | `pausegame none 0 normal none` |  |
| unpausegame | 关闭暂停菜单 | `unpausegame <bFadeOut>` | bFadeOut=0或1 | `unpausegame 1` |  |
| opentitlemenu | 打开标题菜单 | `opentitlemenu <bDontPrompt> <bFinal>` | bDontPrompt=0或1；bFinal=0或1 | `opentitlemenu 1 0` |  |
| openloginmenu | 打开登录菜单 | `openloginmenu` | 无 | `openloginmenu` |  |
| showinfopage | 显示信息页 | `showinfopage` | 无 | `showinfopage` |  |
| multilineinput | 弹出多行文本输入框 | `multilineinput <bUrlEncode> <DefaultString> <Title>` | bUrlEncode=0或1；defaultString=文本：默认值；title=文本：标题 | `multilineinput 0 default title` |  |
| onmultilineinput | 处理多行输入回调(内部) | `onmultilineinput <msg>` | msg=文本 | `onmultilineinput msg` |  |
| promptforcharactername | 弹出改名输入框 | `promptforcharactername` | 无 | `promptforcharactername` |  |
| setbossquickstab | 设置Boss快速突刺 | `setbossquickstab <StabIndex>` | stabIndex=整数：突刺编号 | `setbossquickstab 0` |  |
| setcharactername | 设置角色名字 | `setcharactername <CharacterName>` | characterName=文本：名字 | `setcharactername 隆` |  |
| getcharactername | 在日志输出当前角色名 | `getcharactername` | 无 | `getcharactername` |  |
| swapcharacter | 切换角色(塞里斯/伊萨) | `swapcharacter` | 无 | `swapcharacter` |  |
| setisa | 切换为伊萨 | `setisa <bDontKeepChange>` | bDontKeepChange=0或1 | `setisa 0` |  |
| setsiris | 切换为塞里斯 | `setsiris <bDontKeepChange>` | bDontKeepChange=0或1 | `setsiris 0` |  |
| setplayerattachcrossbow | 给角色挂载/移除弩(装饰) | `setplayerattachcrossbow <bAttach>` | bAttach=0或1：是否挂载 | `setplayerattachcrossbow 1` |  |
| pauseifplayerdead | 玩家死亡时暂停(调试) | `pauseifplayerdead` | 无 | `pauseifplayerdead` |  |
| destroynonplayerpawns | 销毁全部非玩家角色 | `destroynonplayerpawns <bBlockRecreation> <PawnTypes>` | bBlockRecreation=0或1：是否禁止重建；pawnTypes=文本：类型过滤 | `destroynonplayerpawns 1 ` |  |
| updateendofbattletrackingachievements | 更新战斗结算成就(内部) | `updateendofbattletrackingachievements <Stats>` | stats=结构：统计 | `updateendofbattletrackingachievements none` |  |
| clearcheevodata | 清除成就数据 | `clearcheevodata` | 无 | `clearcheevodata` |  |
| unlockachievement | 解锁指定成就 | `unlockachievement <PercentComplete> <bSilent> <AchievementId>` | percentComplete=小数：进度；bSilent=0或1：是否静默；achievementId=0~255整数：成就ID | `unlockachievement 100 0 3` |  |
| unlockachievementingame | 在游戏中解锁成就 | `unlockachievementingame <Tier> <AchievementId>` | tier=整数：层级；achievementId=0~255整数：成就ID | `unlockachievementingame 1 3` |  |
| unlockachievmentinsystem | 系统级解锁成就 | `unlockachievmentinsystem <PercentComplete> <AchievementId>` | percentComplete=小数；achievementId=0~255整数 | `unlockachievmentinsystem 100 1` |  |
| securetime | 安全时间校验(内部) | `securetime <msg>` | msg=文本 | `securetime x` |  |
| clashmob | 执行ClashMob子命令 | `clashmob <Cmd>` | cmd=文本：子命令 | `clashmob help` |  |
| cm | ClashMob 缩写 | `cm <Cmd>` | cmd=文本：子命令 | `cm help` |  |
| mymob | MyClashMob 子命令 | `mymob <Cmd>` | cmd=文本：子命令 | `mymob help` |  |
| mm | MyClashMob 缩写 | `mm <Cmd>` | cmd=文本：子命令 | `mm help` |  |
| cloudavatar | 云端头像子命令 | `cloudavatar <Cmd>` | cmd=文本：子命令 | `cloudavatar help` |  |
| ca | 云端头像缩写 | `ca <Cmd>` | cmd=文本：子命令 | `ca help` |  |
| quest | 任务系统子命令 | `quest <Cmd>` | cmd=文本：子命令 | `quest help` |  |
| q | 任务系统缩写 | `q <Cmd>` | cmd=文本：子命令 | `q help` |  |
| debugsendgifttomobmembers | 调试:给活动成员发礼物 | `debugsendgifttomobmembers` | 无 | `debugsendgifttomobmembers` |  |
| debugqueryreceivedgifts | 调试:查询收到的礼物 | `debugqueryreceivedgifts` | 无 | `debugqueryreceivedgifts` |  |
| dumpiconusage | 转储图标使用情况 | `dumpiconusage` | 无 | `dumpiconusage` |  |
| posttweet | 发推(服务已停,无效) | `posttweet <Link> <msg>` | link=文本；msg=文本 | `posttweet url msg` |  |
| cloudgametwitter | 游戏推特关联(无效) | `cloudgametwitter` | 无 | `cloudgametwitter` |  |
| fbfriends | Facebook好友列表(无效) | `fbfriends` | 无 | `fbfriends` |  |
| postfb | Facebook发帖(无效) | `postfb <FriendID> <ImageURL> <LinkDesc> <LinkURL> <LinkCaption> <LinkName> <msg>` | friendID=文本；imageURL=文本；linkDesc=文本；linkURL=文本；linkCaption=文本；linkName=文本；msg=文本 | `postfb a b c d e f g` |  |
| posttext | 发送文本分享(无效) | `posttext <Message>` | message=文本 | `posttext hello` |  |
| postemail | 发邮件分享(无效) | `postemail <Message> <Subject> <Address>` | message=文本；subject=文本；address=文本 | `postemail hi hi a@b.c` |  |
| schedulelocalsystemnotify | 计划本地系统通知 | `schedulelocalsystemnotify <MessageBody> <SecondsFromNow> <Value> <Key>` | messageBody=文本；secondsFromNow=整数：延迟秒数；value=文本；key=文本 | `schedulelocalsystemnotify msg 60 v k` |  |
| clearlocalsystemnotify | 取消本地系统通知 | `clearlocalsystemnotify <Value> <Key>` | value=文本；key=文本 | `clearlocalsystemnotify v k` |  |
| loadleftbattle | 进入左侧战斗 | `loadleftbattle` | 无 | `loadleftbattle` |  |
| loadrighttbattle | 进入右侧战斗 | `loadrighttbattle` | 无 | `loadrighttbattle` |  |
| clashmobmapscene | 进入ClashMob地图场景 | `clashmobmapscene <ExitNumber>` | exitNumber=整数 | `clashmobmapscene 0` |  |
| mapscene | 进入大地图场景 | `mapscene <ExitNumber>` | exitNumber=整数：出口编号 | `mapscene 0` |  |
| potionscene | 进入药水场景 | `potionscene <ExitNumber>` | exitNumber=整数：出口编号 | `potionscene 0` |  |
| forgescene | 进入锻造场景 | `forgescene <ExitNumber>` | exitNumber=整数：出口编号 | `forgescene 0` |  |
| gemscene | 直接进入宝石合成/镶嵌场景 | `gemscene <ExitNumber>` | exitNumber=整数：出口编号 | `gemscene 0` |  |
| traderscene | 进入商店场景 | `traderscene <ExitNumber>` | exitNumber=整数：出口编号 | `traderscene 0` |  |
| trainerscene | 进入训练场景 | `trainerscene <ExitNumber>` | exitNumber=整数：出口编号 | `trainerscene 0` |  |
| cardsscene | 进入卡片场景 | `cardsscene <ExitNumber>` | exitNumber=整数：出口编号 | `cardsscene 0` |  |
| fishscene | 进入钓鱼场景 | `fishscene <ExitNumber>` | exitNumber=整数：出口编号 | `fishscene 0` |  |
| collectorscene | 进入收藏家场景 | `collectorscene` | 无 | `collectorscene` |  |
| logdlc | 列出DLC信息 | `logdlc` | 无 | `logdlc` |  |
| reloaddlc | 重新加载DLC | `reloaddlc <Path>` | path=文本：路径 | `reloaddlc path` |  |
| testcsreport | 测试客服上报 | `testcsreport <DetailText> <EmailSubject> <AccountAuth> <URL>` | detailText=文本；emailSubject=文本；accountAuth=文本；uRL=文本 | `testcsreport a b c d` |  |
| createmcptestmanager | 创建MCP测试管理器 | `createmcptestmanager` | 无 | `createmcptestmanager` |  |
| securetimeinit | 初始化安全时间 | `securetimeinit <bInitialize>` | bInitialize=0或1 | `securetimeinit 1` |  |
| texturerefdump | 转储纹理引用 | `texturerefdump <bClearOld>` | bClearOld=0或1：是否清旧数据 | `texturerefdump 1` |  |
| texturerefdebug | 纹理引用调试 | `texturerefdebug` | 无 | `texturerefdebug` |  |
| forceappgainfocus | 强制应用获得焦点 | `forceappgainfocus` | 无 | `forceappgainfocus` |  |
| forcecheevoupload | 强制上传成就 | `forcecheevoupload` | 无 | `forcecheevoupload` |  |
| exitkeybind | 退出按键绑定模式 | `exitkeybind` | 无 | `exitkeybind` |  |
| pausegamekeybind | 暂停菜单按键绑定 | `pausegamekeybind` | 无 | `pausegamekeybind` |  |
| acceptpopupkeybind | 确认弹窗按键绑定 | `acceptpopupkeybind` | 无 | `acceptpopupkeybind` |  |
| scrollup | 模拟向上滚动 | `scrollup` | 无 | `scrollup` |  |
| scrolldown | 模拟向下滚动 | `scrolldown` | 无 | `scrolldown` |  |
| scrollleft | 模拟向左滚动 | `scrollleft` | 无 | `scrollleft` |  |
| scrollright | 模拟向右滚动 | `scrollright` | 无 | `scrollright` |  |
| domagic0 | 施放1号魔法 | `domagic0` | 无 | `domagic0` |  |
| domagic1 | 施放2号魔法 | `domagic1` | 无 | `domagic1` |  |
| domagic2 | 施放3号魔法 | `domagic2` | 无 | `domagic2` |  |
| doautostab | 自动突刺 | `doautostab` | 无 | `doautostab` |  |

### SwordPlayer / SwordBasePC — 玩家命令

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| blockforasyncloads | 阻塞等待异步加载完成 | `blockforasyncloads` | 无 | `blockforasyncloads` | ✔ |
| loadsublevel | 加载指定子关卡 | `loadsublevel <bShouldBlockOnLoad> <SubLevelName>` | bShouldBlockOnLoad=0或1：是否阻塞等待；subLevelName=文本：子关卡名 | `loadsublevel 0 sub_x` | ✔ |
| unloadsublevel | 卸载指定子关卡 | `unloadsublevel <SubLevelName>` | subLevelName=文本：子关卡名 | `unloadsublevel sub_x` | ✔ |
| issublevelloaded | 查询指定子关卡是否已加载 | `issublevelloaded <SubLevelName>` | subLevelName=文本：子关卡名 | `issublevelloaded sub_x` | ✔ |
| setalwaystick | 强制Actor每帧Tick | `setalwaystick <bSet>` | bSet=0或1 | `setalwaystick 1` | ✔ |
| showloadingscreen | 显示加载画面 | `showloadingscreen <bPause>` | bPause=0或1：是否暂停 | `showloadingscreen 1` |  |
| hideloadingscreen | 隐藏加载画面 | `hideloadingscreen` | 无 | `hideloadingscreen` |  |
| testdisconnect | 模拟断线(调试) | `testdisconnect` | 无 | `testdisconnect` |  |
| closewaitingpopup | 关闭等待弹窗 | `closewaitingpopup <ErrorMsg> <Reason>` | errorMsg=文本；reason=文本 | `closewaitingpopup e r` |  |
| swordreportscores | 上报分数(服务已死,无效) | `swordreportscores <MaxBloodLine> <GodKingLevel> <TotalGold> <PawnLevel> <TotalKills>` | maxBloodLine=整数；godKingLevel=整数；totalGold=整数；pawnLevel=整数；totalKills=整数 | `swordreportscores 1 1 1 1 1` |  |
| swordreadscores | 读取排行榜(服务已死,无效) | `swordreadscores` | 无 | `swordreadscores` |  |
| swordreadfriendscores | 读取好友分数(服务已死,无效) | `swordreadfriendscores` | 无 | `swordreadfriendscores` |  |
| swordreadrangescores | 读取分数区间(服务已死,无效) | `swordreadrangescores <Length> <Start>` | length=整数；start=整数 | `swordreadrangescores 10 0` |  |
| swordreadscoresaroundplayer | 读取玩家附近排名(服务已死,无效) | `swordreadscoresaroundplayer <Num>` | num=整数 | `swordreadscoresaroundplayer 10` |  |
| swordreadachievements | 读取成就列表(服务已死,无效) | `swordreadachievements` | 无 | `swordreadachievements` |  |
| dopccheevos | 开关成就系统(调试) | `dopccheevos <bOff>` | bOff=0或1：是否关闭 | `dopccheevos 0` |  |
| unlockachievement | 解锁指定成就 | `unlockachievement <PercentComplete> <bSilent> <AchievementId>` | percentComplete=小数：进度；bSilent=0或1：是否静默；achievementId=0~255整数：成就ID | `unlockachievement 100 0 3` |  |
| testachievement | 测试指定成就 | `testachievement <PercentComplete> <Achievement>` | percentComplete=小数；achievement=整数 | `testachievement 100 1` |  |
| testachievementsall | 测试解锁全部成就 | `testachievementsall` | 无 | `testachievementsall` |  |
| setcheatdate | 设置作弊用日期(触发节日类内容) | `setcheatdate <Date> <Month>` | date=整数：日；month=整数：月 | `setcheatdate 25 12` |  |
| testcloudstep | 测试云存档步骤 | `testcloudstep <bFinishSteps>` | bFinishSteps=0或1 | `testcloudstep 1` |  |
| testcloudcleardelegates | 清理云存档委托 | `testcloudcleardelegates` | 无 | `testcloudcleardelegates` |  |
| testclouddebugfail | 模拟云存档失败 | `testclouddebugfail <Chance> <Step>` | chance=整数：概率；step=整数：步骤 | `testclouddebugfail 50 1` |  |
| cloudtest | 云存档综合测试 | `cloudtest` | 无 | `cloudtest` |  |
| clouddocs | 云存档文档目录 | `clouddocs` | 无 | `clouddocs` |  |
| cloudprint | 打印云存档状态 | `cloudprint` | 无 | `cloudprint` |  |
| setworlditemorderlist | 设置场景物品刷新序列 | `setworlditemorderlist <bClearExisting> <ForceAddCount> <ForceAddType>` | bClearExisting=0或1：是否清空；forceAddCount=整数：追加数量；forceAddType=0~255整数：类型 | `setworlditemorderlist 1 5 0` |  |
| setplayerkilleduberboss | 将指定稀有Boss标记为已击杀 | `setplayerkilleduberboss <UberBossIndex>` | uberBossIndex=整数：Boss编号 | `setplayerkilleduberboss 1` |  |
| disablecharacterbutton | 禁用角色切换按钮 | `disablecharacterbutton <bDisable>` | bDisable=0或1 | `disablecharacterbutton 1` |  |
| setgameflag | 置位指定游戏标志位 | `setgameflag <GameFlag>` | gameFlag=整数：标志位 | `setgameflag 1` |  |
| cleargameflag | 清除指定游戏标志位 | `cleargameflag <GameFlag>` | gameFlag=整数：标志位 | `cleargameflag 1` |  |
| setgameflagexplicit | 将游戏标志位设为指定值 | `setgameflagexplicit <Value> <GameFlag>` | value=整数：值；gameFlag=整数：标志位 | `setgameflagexplicit 1 2` |  |
| dorebalanceplayerstats | 按当前等级重新平衡玩家属性 | `dorebalanceplayerstats` | 无 | `dorebalanceplayerstats` |  |
| clearpurchasedperks | 清空已购买的特长 | `clearpurchasedperks` | 无 | `clearpurchasedperks` |  |
| setplayergodking | 将玩家标记为神王(通关状态) | `setplayergodking` | 无 | `setplayergodking` |  |
| clearplayergodking | 清除神王标记 | `clearplayergodking` | 无 | `clearplayergodking` |  |
| loadstartinglevel | 加载初始关卡 | `loadstartinglevel` | 无 | `loadstartinglevel` |  |
| dodemostartplayer | 演示模式玩家初始化 | `dodemostartplayer` | 无 | `dodemostartplayer` |  |
| giveandmasteritem | 发放指定物品并直接置为满熟练度 | `giveandmasteritem <ItemName>` | itemName=标识符：物品模板名 | `giveandmasteritem sword_2` |  |
| setplayerawakening | 设置玩家觉醒值 | `setplayerawakening <bAdjustWoker> <Awakening>` | bAdjustWoker=0或1：是否调整；awakening=整数：觉醒值 | `setplayerawakening 0 100` |  |
| setplayerupgradegemcarry | 升级宝石携带上限 | `setplayerupgradegemcarry` | 无 | `setplayerupgradegemcarry` |  |
| setplayergems | 按档位补一批宝石到背包 | `setplayergems <bLowGems>` | bLowGems=0或1：是否低档宝石 | `setplayergems 0` |  |
| setplayerpotions | 补满/发放药水 | `setplayerpotions` | 无 | `setplayerpotions` |  |
| setplayercreatenewlistofstoregems | 生成一批商店宝石列表 | `setplayercreatenewlistofstoregems <bCheatHighEndGems> <ForceCount> <bCreatePotions> <AllSameType> <bUseCheatGems>` | bCheatHighEndGems=0或1：是否高档宝石；forceCount=整数：数量；bCreatePotions=0或1：是否含药水；allSameType=文本：强制统一类型(可空)；bUseCheatGems=0或1：是否用作弊宝石 | `setplayercreatenewlistofstoregems 1 10 0  1` |  |
| forcemerchant | 强制商店出现 | `forcemerchant <AppearanceCount>` | appearanceCount=整数：出现次数 | `forcemerchant 1` |  |
| setplayerclearpotionlist | 清空药水列表 | `setplayerclearpotionlist` | 无 | `setplayerclearpotionlist` |  |
| setplayerhardcorecompletecount | 设置硬核模式完成次数 | `setplayerhardcorecompletecount <Count>` | count=整数：次数 | `setplayerhardcorecompletecount 5` |  |
| setarenacooldowntime | 设置竞技场冷却时间 | `setarenacooldowntime <Seconds>` | seconds=整数：秒数 | `setarenacooldowntime 0` |  |
| cleararenacooldown | 清除竞技场冷却 | `cleararenacooldown` | 无 | `cleararenacooldown` |  |
| setarenaround | 设置竞技场回合数 | `setarenaround <Round>` | round=整数：回合 | `setarenaround 3` |  |
| setupengagementnotifies | 设置接战通知(调试) | `setupengagementnotifies` | 无 | `setupengagementnotifies` |  |
| passtimemakingitems | 快进锻造/合成计时指定秒数 | `passtimemakingitems <Seconds>` | seconds=整数：秒数 | `passtimemakingitems 3600` |  |
| dodeathlessquestforgive | 宽恕一次无死亡任务失败 | `dodeathlessquestforgive` | 无 | `dodeathlessquestforgive` |  |
| setplayerclearitemstate | 清除全部物品状态(熟练度等) | `setplayerclearitemstate` | 无 | `setplayerclearitemstate` |  |
| setplayervalidlevel | 设置玩家有效等级标记 | `setplayervalidlevel <ValidLevel>` | validLevel=整数：等级 | `setplayervalidlevel 50` |  |
| dumpbattlechallengeinfo | 转储战斗挑战信息 | `dumpbattlechallengeinfo <Tier> <Count>` | tier=整数：层级；count=整数：数量 | `dumpbattlechallengeinfo 1 10` |  |
| consumeconsumable | 消耗一个消耗品 | `consumeconsumable <ConsumableObject> <Consumable>` | consumableObject=对象：对象；consumable=0~255整数：类型 | `consumeconsumable none 1` |  |
| addconsumable | 增加消耗品(药水等) | `addconsumable <bIgnoreMax> <AddCount> <Consumable>` | bIgnoreMax=0或1：是否忽略上限；addCount=整数：数量；consumable=0~255整数：消耗品类型 | `addconsumable 0 5 1` |  |
| setplayeraddconsumableforce | 强制添加指定消耗品 | `setplayeraddconsumableforce <Consumable>` | consumable=0~255整数：类型 | `setplayeraddconsumableforce 1` |  |
| forceequipinfinityblade | 强制装备无尽之剑 | `forceequipinfinityblade <bSpecialGiveItems> <bDo>` | bSpecialGiveItems=0或1：是否连带赠品；bDo=0或1：是否执行 | `forceequipinfinityblade 1 1` |  |
| showbattlechallenge | 显示战斗挑战横幅 | `showbattlechallenge <ChallengeName> <Tier> <eType> <StringToShow>` | challengeName=标识符：挑战名；tier=整数：层级；eType=0~255整数：类型；stringToShow=文本：文本 | `showbattlechallenge c 1 0 txt` |  |
| sethasusedkeyitem | 把指定钥匙物品标记为已使用 | `sethasusedkeyitem <KeyItemName>` | keyItemName=标识符：钥匙模板名 | `sethasusedkeyitem keyitem_x` |  |
| setgivekeyitem | 发放指定的钥匙物品 | `setgivekeyitem <KeyItemName>` | keyItemName=标识符：钥匙模板名 | `setgivekeyitem keyitem_x` |  |
| fillsuperandmagicmeters | 充满大招与魔法槽 | `fillsuperandmagicmeters` | 无 | `fillsuperandmagicmeters` |  |
| dosupermove | 直接释放大招 | `dosupermove <fGloryModeTime> <bStabEnter>` | fGloryModeTime=小数：荣耀模式时长；bStabEnter=0或1：是否突刺进入 | `dosupermove 10 0` |  |
| doquickslashsupermove | 快速斩大招 | `doquickslashsupermove <fGloryModeTime> <SlashDir>` | fGloryModeTime=小数：荣耀时长；slashDir=0~255整数：方向 | `doquickslashsupermove 10 0` |  |
| debugactivatemagicmode | 直接进入魔法模式(调试) | `debugactivatemagicmode` | 无 | `debugactivatemagicmode` |  |
| setplayeractivatesuperdodge | 触发完美闪避(调试) | `setplayeractivatesuperdodge <bTelegraph> <Type>` | bTelegraph=0或1；type=0~255整数：类型 | `setplayeractivatesuperdodge 1 0` |  |
| showsuperdodgeattackpoint | 显示完美闪避攻击点(调试) | `showsuperdodgeattackpoint` | 无 | `showsuperdodgeattackpoint` |  |
| clearsuperdodgeattackpoint | 清除完美闪避攻击点 | `clearsuperdodgeattackpoint` | 无 | `clearsuperdodgeattackpoint` |  |
| killself | 自杀(原地死亡) | `killself` | 无 | `killself` |  |
| dosecondchance | 触发一次第二次机会(复活) | `dosecondchance` | 无 | `dosecondchance` |  |
| debugbattleresults | 模拟结算一次战斗奖励 | `debugbattleresults <EffectXp2> <EffectXp1> <GoldScale> <RewardLevel> <GenericXP> <BossXP>` | effectXp2=整数；effectXp1=整数；goldScale=整数；rewardLevel=整数；genericXP=整数；bossXP=整数 | `debugbattleresults 1 1 1 50 100 100` |  |
| doaimedstabanim | 播放指定方向的突刺动画 | `doaimedstabanim <Y> <X>` | y=小数；x=小数 | `doaimedstabanim 0.5 0.5` |  |
| doblendedbodyanim | 播放混合身体动画(调试) | `doblendedbodyanim <HideWeapons> <NextActionDelayTime> <AnimSpeed> <bUpperBodyOnly> <BlendInTime> <BlendTarget> <BlendedAnim>` | hideWeapons=0~255整数；nextActionDelayTime=小数；animSpeed=小数；bUpperBodyOnly=0或1；blendInTime=小数；blendTarget=小数；blendedAnim=标识符：动画名 | `doblendedbodyanim 0 0 1 1 0.2 0 anim` |  |
| clearblendedanim | 清除混合动画 | `clearblendedanim <BlendOutTime>` | blendOutTime=小数：混合时间 | `clearblendedanim 0.2` |  |
| setplayerdoscreenspacestabtest | 屏幕空间突刺测试 | `setplayerdoscreenspacestabtest <ScreenDepth> <Y> <X>` | screenDepth=小数；y=小数；x=小数 | `setplayerdoscreenspacestabtest 1 0.5 0.5` |  |
| magicmodeearlyexit | 提前退出魔法模式 | `magicmodeearlyexit <bForceCloseTutorial>` | bForceCloseTutorial=0或1 | `magicmodeearlyexit 0` |  |
| domagicslot0 | 施放1号魔法 | `domagicslot0` | 无 | `domagicslot0` |  |
| domagicslot1 | 施放2号魔法 | `domagicslot1` | 无 | `domagicslot1` |  |
| domagicslot2 | 施放3号魔法 | `domagicslot2` | 无 | `domagicslot2` |  |
| givefullmagic | 立即回满魔法 | `givefullmagic` | 无 | `givefullmagic` |  |
| popbloodlineachievement | 弹出血脉成就 | `popbloodlineachievement <bFixMissing>` | bFixMissing=0或1：是否补发缺失 | `popbloodlineachievement 1` |  |
| easycheevotesting | 批量成就测试 | `easycheevotesting <Count>` | count=整数：数量 | `easycheevotesting 5` |  |
| setbosstestdynamicstabpointrange | 设置动态突刺点范围(调试) | `setbosstestdynamicstabpointrange <YMax> <YMin> <XMax> <XMin>` | yMax=小数；yMin=小数；xMax=小数；xMin=小数 | `setbosstestdynamicstabpointrange 1 0 1 0` |  |
| checksaveobjectchange | 检查存档对象变更(调试) | `checksaveobjectchange <IndexCheck>` | indexCheck=整数：索引 | `checksaveobjectchange 0` |  |
| setmaxcraftslots | 设置合成槽位数量上限 | `setmaxcraftslots <Num>` | num=整数：槽位数 | `setmaxcraftslots 5` |  |
| setcraftslotspurchased | 设置已购买的合成槽位数 | `setcraftslotspurchased <Num>` | num=整数：槽位数 | `setcraftslotspurchased 5` |  |
| colorizeplayerto | 将角色染成指定RGB色 | `colorizeplayerto <B> <G> <R>` | b=小数：蓝；g=小数：绿；r=小数：红(0~1) | `colorizeplayerto 0 0 1` |  |
| dosupermove | 直接释放大招 | `dosupermove <bStabEnter> <fGloryModeTime>` | fGloryModeTime=小数：荣耀模式时长；bStabEnter=0或1：是否突刺进入 | `dosupermove 10 0` |  |
| dosupermove | 直接释放大招 | `dosupermove <bStabEnter> <fGloryModeTime>` | fGloryModeTime=小数：荣耀模式时长；bStabEnter=0或1：是否突刺进入 | `dosupermove 10 0` |  |
| magicmodeearlyexit | 提前退出魔法模式 | `magicmodeearlyexit <bForceCloseTutorial>` | bForceCloseTutorial=0或1 | `magicmodeearlyexit 0` |  |
| dosecondchance | 触发一次第二次机会(复活) | `dosecondchance` | 无 | `dosecondchance` |  |
| dosupermove | 直接释放大招 | `dosupermove <bStabEnter> <fGloryModeTime>` | fGloryModeTime=小数：荣耀模式时长；bStabEnter=0或1：是否突刺进入 | `dosupermove 10 0` |  |
| domagicslot1 | 施放2号魔法 | `domagicslot1` | 无 | `domagicslot1` |  |
| domagicslot2 | 施放3号魔法 | `domagicslot2` | 无 | `domagicslot2` |  |
| domagicslot0 | 施放1号魔法 | `domagicslot0` | 无 | `domagicslot0` |  |

### CheatManager — 引擎作弊（需先 EnableCheats）

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| fxplay | 播放指定特效 | `fxplay <FXAnimPath> <aClass>` | fXAnimPath=文本：路径；aClass=类名：类 | `fxplay path class` |  |
| fxstop | 停止指定特效 | `fxstop <aClass>` | aClass=类名：类 | `fxstop class` |  |
| debugai | 开关AI调试显示 | `debugai <Category>` | category=标识符：类别 | `debugai combat` |  |
| editaibytrace | 编辑准星指向的AI | `editaibytrace` | 无 | `editaibytrace` |  |
| debugpause | 暂停AI/游戏调试 | `debugpause` | 无 | `debugpause` |  |
| listdynamicactors | 向日志列出全部动态Actor | `listdynamicactors` | 无 | `listdynamicactors` |  |
| freezeframe | 冻结游戏指定秒数 | `freezeframe <Delay>` | delay=小数：秒数 | `freezeframe 2` |  |
| writetolog | 向日志写入一条文本 | `writetolog <Param>` | param=文本：内容 | `writetolog hello` |  |
| killviewedactor | 杀死准星指向的Actor | `killviewedactor` | 无 | `killviewedactor` |  |
| teleport | 传送到准星指向位置 | `teleport` | 无 | `teleport` |  |
| changesize | 改变角色体型缩放 | `changesize <F>` | f=小数：倍率 | `changesize 2` |  |
| endpath | 结束路径记录 | `endpath` | 无 | `endpath` |  |
| amphibious | 水陆两栖模式 | `amphibious` | 无 | `amphibious` |  |
| fly | 飞行模式(可移动不受重力) | `fly` | 无 | `fly` |  |
| walk | 恢复正常行走(退出飞行/穿墙) | `walk` | 无 | `walk` |  |
| ghost | 穿墙模式(飞行+无碰撞) | `ghost` | 无 | `ghost` |  |
| allammo | 补满全部弹药(IB3无弹药,基本无效) | `allammo` | 无 | `allammo` |  |
| god | 切换无敌模式(不死) | `god` | 无 | `god` |  |
| slomo | 设置游戏时间流速 | `slomo <T>` | t=小数：倍率(1=正常,0.5=半速,2=双倍) | `slomo 0.5` |  |
| setjumpz | 设置跳跃初速度 | `setjumpz <F>` | f=小数：速度 | `setjumpz 500` |  |
| setgravity | 设置重力加速度 | `setgravity <F>` | f=小数：数值 | `setgravity 500` |  |
| setspeed | 设置移动速度乘数 | `setspeed <F>` | f=小数：倍率 | `setspeed 2` |  |
| killall | 杀死指定类的所有AI | `killall <aClass>` | aClass=类名：目标类(如 Engine.Pawn) | `killall engine.pawn` |  |
| killpawns | 杀死全部角色(Pawn) | `killpawns` | 无 | `killpawns` |  |
| avatar | 头像子命令(内部) | `avatar <ClassName>` | cmd=文本：子命令 | `avatar help` |  |
| summon | 召唤生成指定类名的Actor到准星处 | `summon <ClassName>` | className=类名：要生成的类 | `summon engine.pawn` |  |
| giveweapon | 发放指定武器(标准接口,IB3可能无效) | `giveweapon <WeaponClassStr>` | weaponTag=标识符：武器标签 | `giveweapon x` |  |
| playersonly | 冻结所有Actor(只走玩家逻辑) | `playersonly` | 无 | `playersonly` |  |
| suspendai | 暂停/恢复全部AI | `suspendai` | bSuspend=0或1：是否暂停 | `suspendai 1` |  |
| destroyfractures | 销毁全部可破坏碎块 | `destroyfractures <Radius>` | 无 | `destroyfractures` |  |
| fractureallmeshes | 触发全部可破坏网格破碎 | `fractureallmeshes` | bForce=0或1：是否强制 | `fractureallmeshes 1` |  |
| fractureallmeshestomaximizememoryusage | 触发全部破碎并最大化内存占用(压力测试) | `fractureallmeshestomaximizememoryusage` | 无 | `fractureallmeshestomaximizememoryusage` |  |
| rememberspot | 记住当前位置(寻路调试) | `rememberspot` | 无 | `rememberspot` |  |
| viewself | 视角切回自己 | `viewself <bQuiet>` | 无 | `viewself` |  |
| viewplayer | 视角跟随指定玩家 | `viewplayer <S>` | s=文本：玩家名 | `viewplayer p1` |  |
| viewactor | 视角跟随指定Actor | `viewactor <actorName>` | anActor=标识符：Actor名 | `viewactor x` |  |
| viewbot | 视角切换到AI Bot | `viewbot` | 无 | `viewbot` |  |
| viewclass | 视角切换到指定类的对象 | `viewclass <aClass>` | aClass=类名：目标类 | `viewclass engine.pawn` |  |
| loaded | 向日志输出当前已加载的包列表 | `loaded` | 无 | `loaded` |  |
| allweapons | 发放全部武器(标准接口,IB3可能无效) | `allweapons` | 无 | `allweapons` |  |
| streamlevelin | 流式加载指定关卡 | `streamlevelin <PackageName>` | packageName=标识符：包名 | `streamlevelin sub1` |  |
| onlyloadlevel | 只加载指定关卡(卸载其余) | `onlyloadlevel <PackageName>` | packageName=标识符：关卡包名 | `onlyloadlevel a01` |  |
| streamlevelout | 流式卸载指定关卡 | `streamlevelout <PackageName>` | packageName=标识符：包名 | `streamlevelout sub1` |  |
| testlevel | 测试当前关卡(性能) | `testlevel` | 无 | `testlevel` |  |
| dumponlinesessionstate | 转储联机会话状态 | `dumponlinesessionstate` | 无 | `dumponlinesessionstate` |  |
| setonlinedebuglevel | 设置联机调试级别 | `setonlinedebuglevel <DebugLevel>` | debugLevel=整数：级别 | `setonlinedebuglevel 1` |  |
| testnavmeshpath | 测试寻路(调试) | `testnavmeshpath <bDrawPath>` | 无 | `testnavmeshpath` |  |
| testpylonconnectivity | 测试导航柱连通性 | `testpylonconnectivity` | 无 | `testpylonconnectivity` |  |
| verbosepathdebug | 详细寻路调试 | `verbosepathdebug` | 无 | `verbosepathdebug` |  |
| logplaysoundcalls | 记录播放声音调用 | `logplaysoundcalls <bShouldLog>` | 无 | `logplaysoundcalls` | ✔ |
| logparticleactivatesystemcalls | 记录粒子系统调用 | `logparticleactivatesystemcalls <bShouldLog>` | 无 | `logparticleactivatesystemcalls` | ✔ |
| verifynavmeshobjects | 校验导航网格对象 | `verifynavmeshobjects` | 无 | `verifynavmeshobjects` | ✔ |
| drawunsupportingedges | 绘制导航网格边缘(调试) | `drawunsupportingedges <PawnClassName>` | 无 | `drawunsupportingedges` | ✔ |
| navmeshverification | 导航网格校验(调试) | `navmeshverification <interval>` | 无 | `navmeshverification` |  |
| printallpathobjectedges | 打印全部寻路边(调试) | `printallpathobjectedges` | 无 | `printallpathobjectedges` | ✔ |
| printnavmeshobstacles | 打印导航网格障碍 | `printnavmeshobstacles` | 无 | `printnavmeshobstacles` | ✔ |
| verifynavmeshcoverrefs | 校验导航掩码引用 | `verifynavmeshcoverrefs` | 无 | `verifynavmeshcoverrefs` | ✔ |
| toggleailogging | 开关AI日志 | `toggleailogging` | 无 | `toggleailogging` |  |
| debuginilocpatcher | 调试：ini本地化补丁 | `debuginilocpatcher` | 无 | `debuginilocpatcher` |  |
| debugdownloadtitlefile | 调试：下载标题文件 | `debugdownloadtitlefile <bFromCache> <Filename>` | bFromCache=0或1；filename=文本：文件名 | `debugdownloadtitlefile 0 x` |  |
| debugsavetitlefile | 调试：写标题文件 | `debugsavetitlefile <Filename>` | 无 | `debugsavetitlefile` |  |
| debugdeletetitlefiles | 调试：删除标题文件 | `debugdeletetitlefiles` | 无 | `debugdeletetitlefiles` |  |
| debugemsdownload | 调试：EMS内容下载 | `debugemsdownload` | 无 | `debugemsdownload` |  |
| dumpcoverstats | 转储掩体统计(调试) | `dumpcoverstats` | 无 | `dumpcoverstats` | ✔ |
| drawlocation | 在屏幕绘制当前坐标 | `drawlocation <Loc>` | bEnabled=0或1：是否开启 | `drawlocation 1` |  |
| drawlocationxyz | 在屏幕绘制指定坐标 | `drawlocationxyz <Z> <Y> <X>` | bEnabled=0或1；x=小数；y=小数；z=小数 | `drawlocationxyz 1 1 2 3` |  |
| debugnotification | 调试：弹出系统通知 | `debugnotification <SecondsFromNow> <MessageBody>` | 无 | `debugnotification` |  |
| debugqueryuserfiles | 调试：查询用户文件列表 | `debugqueryuserfiles <UserId>` | 无 | `debugqueryuserfiles` |  |
| debugwriteuserfile | 调试：写用户文件 | `debugwriteuserfile <Filename> <UserId>` | 无 | `debugwriteuserfile` |  |
| debugreaduserfile | 调试：读用户文件 | `debugreaduserfile <Filename> <UserId>` | 无 | `debugreaduserfile` |  |
| debugdeleteuserfile | 调试：删除用户文件 | `debugdeleteuserfile <Filename> <UserId>` | 无 | `debugdeleteuserfile` |  |
| testhttp | 发起一次HTTP测试请求 | `testhttp <bSendParallelRequest> <URL> <Payload> <Verb>` | bSendParallelRequest=0或1；uRL=文本；payload=文本；verb=文本 | `testhttp 0 http://x p GET` |  |
| sendanalyticsevent | 发送一条统计事件(服务已死,无效) | `sendanalyticsevent <AttributeValue> <AttributeName> <EventName>` | 按游戏内部定义 | `sendanalyticsevent` |  |
| sendanalyticsuserattributeevent | 上报用户属性事件(服务已死,无效) | `sendanalyticsuserattributeevent <AttributeValue> <AttributeName>` | 无 | `sendanalyticsuserattributeevent` |  |
| sendanalyticsitempurchaseevent | 上报物品购买事件(服务已死,无效) | `sendanalyticsitempurchaseevent <ItemQuantity> <PerItemCost> <Currency> <ItemId>` | 无 | `sendanalyticsitempurchaseevent` |  |
| sendanalyticscurrencypurchaseevent | 上报货币购买事件(服务已死,无效) | `sendanalyticscurrencypurchaseevent <PaymentProvider> <RealMoneyCost> <RealCurrencyType> <GameCurrencyAmount> <GameCurrencyType>` | 无 | `sendanalyticscurrencypurchaseevent` |  |
| sendanalyticscurrencygivenevent | 上报货币给予事件(服务已死,无效) | `sendanalyticscurrencygivenevent <GameCurrencyAmount> <GameCurrencyType>` | 无 | `sendanalyticscurrencygivenevent` |  |
| sendanalyticscachedevents | 发送缓存的统计事件(服务已死,无效) | `sendanalyticscachedevents` | 无 | `sendanalyticscachedevents` |  |
| setanalyticsuserid | 设置统计系统用户ID | `setanalyticsuserid <UserId>` | userId=文本 | `setanalyticsuserid x` |  |
| getanalyticsuserid | 输出统计系统用户ID | `getanalyticsuserid` | 无 | `getanalyticsuserid` | ✔ |
| analyticsstartsession | 开始统计会话(服务已死,无效) | `analyticsstartsession` | 无 | `analyticsstartsession` |  |
| analyticsendsession | 结束统计会话(服务已死,无效) | `analyticsendsession` | 无 | `analyticsendsession` |  |
| googleauth | Google授权(无效) | `googleauth` | 无 | `googleauth` |  |
| googlerevoke | 撤销Google授权(无效) | `googlerevoke` | 无 | `googlerevoke` |  |
| subscribetochairchannel | 订阅ChAIR频道(无效) | `subscribetochairchannel` | 无 | `subscribetochairchannel` |  |

### Engine 标准类（PlayerController/Actor/Controller/HUD 等）

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| flushdebugstrings | 刷新调试字符串 | `flushdebugstrings` | 无 | `flushdebugstrings` | ✔ |
| switchtobestweapon | 切换到最佳武器 | `switchtobestweapon <bForceNewWeapon>` | bForceNewWeapon=0或1 | `switchtobestweapon 1` |  |
| setaudiogroupvolume | 设置音频组音量 | `setaudiogroupvolume <Volume> <GroupName>` | volume=小数：音量；groupName=标识符：组名 | `setaudiogroupvolume 0.5 Music` | ✔ |
| enablecheats | 启用作弊:生成CheatManager,God/Fly等命令的前置条件 | `enablecheats` | 无 | `enablecheats` |  |
| settiltactive | 开关倾斜输入(移动端遗留) | `settiltactive <bActive>` | bActive=0或1 | `settiltactive 1` |  |
| talk | 语音(无效) | `talk` | 无 | `talk` |  |
| teamtalk | 队伍语音(无效) | `teamtalk` | 无 | `teamtalk` |  |
| fov | 设置视场角 | `fov <F>` | f=小数：角度(默认80) | `fov 100` |  |
| mutate | 向Mutator发送指令(单机无效) | `mutate <MutateString>` | mutateString=文本 | `mutate x` |  |
| say | 聊天广播(单机无效) | `say <msg>` | msg=文本 | `say hi` |  |
| teamsay | 队伍聊天(单机无效) | `teamsay <msg>` | msg=文本 | `teamsay hi` |  |
| camera | 切换相机模式 | `camera <NewMode>` | newMode=标识符：模式名 | `camera 3rdperson` |  |
| speech | 语音(无效) | `speech <Callsign> <Index> <Type>` | callsign=文本；index=整数；type=标识符 | `speech a 1 b` |  |
| restartlevel | 重启当前关卡 | `restartlevel` | 无 | `restartlevel` |  |
| localtravel | 切换到指定地图 | `localtravel <URL>` | uRL=文本：地图URL | `localtravel entry` |  |
| pause | 暂停/继续游戏 | `pause` | 无 | `pause` |  |
| utrace | 调试追踪(无实际效果) | `utrace` | 无 | `utrace` |  |
| throwweapon | 丢弃当前武器 | `throwweapon` | 无 | `throwweapon` |  |
| prevweapon | 切换上一件武器 | `prevweapon` | 无 | `prevweapon` |  |
| nextweapon | 切换下一件武器 | `nextweapon` | 无 | `nextweapon` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| stopfire | 停止开火 | `stopfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `stopfire 0` |  |
| startaltfire | 开始副开火 | `startaltfire <FireModeNum>` | fireModeNum=0~255整数 | `startaltfire 0` |  |
| stopaltfire | 停止副开火 | `stopaltfire <FireModeNum>` | fireModeNum=0~255整数 | `stopaltfire 0` |  |
| use | 使用/互动(准星目标) | `use` | 无 | `use` |  |
| suicide | 自杀 | `suicide` | 无 | `suicide` |  |
| setname | 设置玩家名字 | `setname <S>` | s=文本：名字 | `setname abc` |  |
| switchteam | 切换队伍(单机无效) | `switchteam` | teamName=文本 | `switchteam 0` |  |
| changeteam | 切换队伍(单机无效) | `changeteam <TeamName>` | teamName=文本 | `changeteam 0` |  |
| switchlevel | 切换关卡 | `switchlevel <URL>` | uRL=文本：地图名 | `switchlevel entry` |  |
| restartlevel | 重启当前关卡 | `restartlevel` | 无 | `restartlevel` |  |
| suicide | 自杀 | `suicide` | 无 | `suicide` |  |
| throwweapon | 丢弃当前武器 | `throwweapon` | 无 | `throwweapon` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| startaltfire | 开始副开火 | `startaltfire <FireModeNum>` | fireModeNum=0~255整数 | `startaltfire 0` |  |
| nextweapon | 切换下一件武器 | `nextweapon` | 无 | `nextweapon` |  |
| prevweapon | 切换上一件武器 | `prevweapon` | 无 | `prevweapon` |  |
| switchtobestweapon | 切换到最佳武器 | `switchtobestweapon <bForceNewWeapon>` | bForceNewWeapon=0或1 | `switchtobestweapon 1` |  |
| jump | 跳跃 | `jump` | 无 | `jump` |  |
| suicide | 自杀 | `suicide` | 无 | `suicide` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| suicide | 自杀 | `suicide` | 无 | `suicide` |  |
| throwweapon | 丢弃当前武器 | `throwweapon` | 无 | `throwweapon` |  |
| use | 使用/互动(准星目标) | `use` | 无 | `use` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| nextweapon | 切换下一件武器 | `nextweapon` | 无 | `nextweapon` |  |
| prevweapon | 切换上一件武器 | `prevweapon` | 无 | `prevweapon` |  |
| throwweapon | 丢弃当前武器 | `throwweapon` | 无 | `throwweapon` |  |
| startfire | 开始开火(按压) | `startfire <FireModeNum>` | fireModeNum=0~255整数：开火模式 | `startfire 0` |  |
| use | 使用/互动(准星目标) | `use` | 无 | `use` |  |
| jump | 跳跃 | `jump` | 无 | `jump` |  |
| causeevent | 触发一个Kismet远程事件 | `causeevent <EventName>` | eventID=标识符：事件名 | `causeevent myevent` |  |
| ce | CauseEvent 缩写 | `ce <EventName>` | eventID=标识符：事件名 | `ce myevent` |  |
| listconsoleevents | 列出控制台事件 | `listconsoleevents` | 无 | `listconsoleevents` |  |
| listce | 列出全部可用远程事件 | `listce` | 无 | `listce` |  |
| remoteevent | 触发远程事件(同CauseEvent) | `remoteevent <EventName>` | eventID=标识符：事件名 | `remoteevent myevent` |  |
| re | 远程事件重定向(RE xxxx:事件) | `re <EventName>` | conString=文本：连接串 | `re x` |  |
| showplayerstate | 在屏幕显示玩家状态 | `showplayerstate` | 无 | `showplayerstate` |  |
| showgamestate | 在屏幕显示游戏状态 | `showgamestate` | 无 | `showgamestate` |  |
| saveclassconfig | 把类默认值写入ini | `saveclassconfig <ClassName>` | aClassName=标识符：类名 | `saveclassconfig x` |  |
| saveactorconfig | 把Actor配置写入ini | `saveactorconfig <actorName>` | aClassName=标识符：类名 | `saveactorconfig x` |  |
| setshowsubtitles | 开关字幕 | `setshowsubtitles <bValue>` | bValue=0或1：是否显示 | `setshowsubtitles 1` | ✔ |
| consolekey | 查询或临时设置控制台开关键 | `consolekey <Key>` | key=标识符：键名 | `consolekey minus` |  |
| sendtoconsole | 向控制台转发一条命令 | `sendtoconsole <Command>` | command=文本：命令 | `sendtoconsole stat fps` |  |
| pathstep | 寻路调试：单步前进 | `pathstep <Cnt>` | 无 | `pathstep` |  |
| pathchild | 寻路调试：走到相邻节点 | `pathchild <Cnt>` | index=整数：节点号 | `pathchild 0` |  |
| pathclear | 寻路调试：清除路径 | `pathclear` | 无 | `pathclear` |  |
| debugcameraanims | 调试相机动画 | `debugcameraanims` | 无 | `debugcameraanims` |  |
| bugitgo | 传送到指定坐标并截图 | `bugitgo <Roll> <Yaw> <Pitch> <Z> <Y> <X>` | roll=小数：横滚；yaw=小数：偏航；pitch=小数：俯仰；z=小数：Z坐标；y=小数：Y坐标；x=小数：X坐标 | `bugitgo 0 0 0 0 0 0` |  |
| bugit | 截图+坐标写入BugIt报告文件 | `bugit <ScreenShotDescription>` | theLocation=文本：位置说明 | `bugit desc` |  |
| logloc | 向日志输出当前位置 | `logloc` | bEnabled=0或1：是否持续 | `logloc 1` |  |
| bugitai | AI版BugIt | `bugitai <ScreenShotDescription>` | theLocation=文本：位置说明 | `bugitai desc` |  |
| bugitstringcreator | 生成BugIt字符串 | `bugitstringcreator <LocString> <GoString> <ViewRotation> <ViewLocation>` | 无 | `bugitstringcreator` |  |
| dumponlinesessionstate | 转储联机会话状态 | `dumponlinesessionstate` | 无 | `dumponlinesessionstate` |  |
| dumpvoicemutingstate | 转储语音静音状态(无效) | `dumpvoicemutingstate` | 无 | `dumpvoicemutingstate` |  |
| dumppeers | 转储联机节点(无效) | `dumppeers` | 无 | `dumppeers` |  |
| admin | 管理员命令(联机无效) | `admin <CommandLine>` | cmd=文本：命令 | `admin x` |  |
| kickban | 封禁玩家(联机无效) | `kickban <S>` | playerName=文本 | `kickban x` |  |
| kick | 踢出玩家(联机无效) | `kick <S>` | playerName=文本 | `kick x` |  |
| playerlist | 列出玩家(联机无效) | `playerlist` | 无 | `playerlist` |  |
| restartmap | 重启地图(服务端) | `restartmap` | 无 | `restartmap` |  |
| switch | 切换地图(服务端) | `switch <URL>` | uRL=文本：地图 | `switch entry` |  |
| dotraveltheworld | 遍历加载全部地图(压力测试) | `dotraveltheworld` | 无 | `dotraveltheworld` |  |
| setbandwidthlimit | 设置带宽限制(联机) | `setbandwidthlimit <AsyncIOBandwidthLimit>` | bandwidth=整数：值 | `setbandwidthlimit 10000` | ✔ |
| beginbvt | 开始基础验证测试(内部) | `beginbvt <TagDesc>` | 无 | `beginbvt` |  |
| debugcreateplayer | 调试：创建一个本地玩家 | `debugcreateplayer <ControllerId>` | controllerIndex=整数：控制器编号 | `debugcreateplayer 1` |  |
| ssswapcontrollers | 交换分屏控制器 | `ssswapcontrollers` | 无 | `ssswapcontrollers` |  |
| debugremoveplayer | 调试：移除本地玩家 | `debugremoveplayer <ControllerId>` | controllerIndex=整数：控制器编号 | `debugremoveplayer 1` |  |
| setsplit | 设置分屏 | `setsplit <Mode>` | splitType=整数：分屏类型 | `setsplit 0` |  |
| showtitlesafearea | 显示安全区框线 | `showtitlesafearea` | 无 | `showtitlesafearea` |  |
| setconsoletarget | 设置控制台目标玩家 | `setconsoletarget <PlayerIndex>` | playerIndex=整数：玩家编号 | `setconsoletarget 0` |  |
| setprogresstime | 设置进度条显示时间 | `setprogresstime <T>` | time=小数：秒数 | `setprogresstime 5` |  |
| clearprogressmessages | 清除进度提示 | `clearprogressmessages` | 无 | `clearprogressmessages` |  |
| togglehud | 开关HUD | `togglehud` | 无 | `togglehud` |  |
| showhud | 开关HUD | `showhud` | 无 | `showhud` |  |
| showscores | 显示计分板 | `showscores` | 无 | `showscores` |  |
| setshowscores | 开关计分板 | `setshowscores <bNewValue>` | bInValue=0或1 | `setshowscores 1` |  |
| showdebug | 切换调试信息面板(循环多页) | `showdebug <DebugType>` | aClassName=类名：指定类(可空) | `showdebug` |  |
| toggledirectorinfohud | 开关导演信息HUD | `toggledirectorinfohud` | 无 | `toggledirectorinfohud` |  |
| toggledirectorinfodebug | 开关导演信息调试 | `toggledirectorinfodebug` | 无 | `toggledirectorinfodebug` |  |
| setbind | 绑定按键到命令 | `setbind <Command> <BindName>` | bindName=标识符：键名；command=文本：命令 | `setbind f slomo 0.5` |  |
| invertmouse | 翻转鼠标垂直方向 | `invertmouse` | bInvert=0或1：是否翻转 | `invertmouse 1` |  |
| invertturn | 翻转横向转向 | `invertturn` | bInvert=0或1：是否翻转 | `invertturn 1` |  |
| setsensitivity | 设置鼠标灵敏度 | `setsensitivity <F>` | f=小数：灵敏度 | `setsensitivity 2` |  |
| jump | 跳跃 | `jump` | 无 | `jump` |  |
| smartjump | 智能跳跃 | `smartjump` | 无 | `smartjump` |  |
| clearsmoothing | 清除鼠标平滑 | `clearsmoothing` | 无 | `clearsmoothing` |  |

### SwordPawn — 角色命令

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| hideweaponr | 隐藏右手武器 | `hideweaponr` | 无 | `hideweaponr` |  |
| showweaponr | 显示右手武器 | `showweaponr` | 无 | `showweaponr` |  |
| hideweaponl | 隐藏左手武器 | `hideweaponl` | 无 | `hideweaponl` |  |
| showweaponl | 显示左手武器 | `showweaponl` | 无 | `showweaponl` |  |
| hideweapons | 隐藏武器(角色侧) | `hideweapons` | 无 | `hideweapons` |  |
| showweapons | 显示武器(角色侧) | `showweapons` | 无 | `showweapons` |  |
| toggleselfshadow | 开关自身阴影 | `toggleselfshadow <bOn>` | bOn=0或1：是否开启 | `toggleselfshadow 1` |  |
| togglereflection | 开关反射效果 | `togglereflection <bOn>` | bOn=0或1：是否开启 | `togglereflection 1` |  |
| takeenemydamage | 让玩家受到指定伤害 | `takeenemydamage <DamageScalar>` | damageScalar=小数：伤害系数 | `takeenemydamage 100` |  |
| toggleselfshadow | 开关自身阴影 | `toggleselfshadow <bOn>` | bOn=0或1：是否开启 | `toggleselfshadow 1` |  |

### SwordHud* — 界面命令

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| hudpausegame | 暂停/继续游戏(HUD侧) | `hudpausegame` | 无 | `hudpausegame` |  |
| hudtimer | 定时执行命令 | `hudtimer <Seconds> <Cmd>` | seconds=整数：延迟秒数；cmd=文本：命令 | `hudtimer 5 slomo 1` |  |
| matineefastforward | 快进当前过场动画 | `matineefastforward` | 无 | `matineefastforward` |  |
| matineeresume | 恢复过场动画 | `matineeresume` | 无 | `matineeresume` |  |
| alwaysfastfoward | 过场动画总是快进 | `alwaysfastfoward` | 无 | `alwaysfastfoward` |  |
| disablefastforward | 禁用过场快进 | `disablefastforward <bDisable>` | bDisable=0或1 | `disablefastforward 1` |  |
| displayplayerevent | 在屏幕显示一条玩家事件文本 | `displayplayerevent <DrawColor> <FadeTime> <HoldTime> <SubEvent> <Event>` | drawColor=结构：颜色；fadeTime=小数：淡入秒；holdTime=小数：停留秒；subEvent=文本；event=文本 | `displayplayerevent (R=255,G=0,B=0,A=255) 1 5 sub ev` |  |
| gesturetest | 手势测试 | `gesturetest` | 无 | `gesturetest` |  |
| showbattlehud | 显示战斗HUD | `showbattlehud` | 无 | `showbattlehud` |  |
| huddrawenable | 启用HUD绘制 | `huddrawenable` | 无 | `huddrawenable` |  |
| huddrawdisable | 禁用HUD绘制 | `huddrawdisable` | 无 | `huddrawdisable` |  |
| displayvictorybanner | 显示胜利横幅 | `displayvictorybanner <bIsVictory>` | bIsVictory=0或1：是否胜利 | `displayvictorybanner 1` |  |
| showfixedslash | 显示固定挥砍方向提示 | `showfixedslash <FadeInTime> <ArrowDir>` | fadeTime=小数：淡入；arrowDir=0~255整数：方向 | `showfixedslash 1 0` |  |
| clearfixedslash | 清除挥砍方向提示 | `clearfixedslash <bSuccess> <FadeOutTime>` | bSuccess=0或1：是否成功；fadeTime=小数：淡出 | `clearfixedslash 1 1` |  |
| showachievement | 屏幕弹出成就横幅 | `showachievement <AchievementDesc> <AchievementName>` | achievementDesc=文本：描述；achievementName=文本：名称 | `showachievement desc name` |  |
| showfadedzones | 显示淡出区域(调试) | `showfadedzones` | 无 | `showfadedzones` |  |
| deactivatemagicmode | 退出魔法模式 | `deactivatemagicmode` | 无 | `deactivatemagicmode` |  |
| execmenu | 执行一条菜单命令 | `execmenu <msg>` | msg=文本：命令 | `execmenu x` |  |
| togglebatchrendering | 切换批渲染 | `togglebatchrendering` | 无 | `togglebatchrendering` |  |
| debugbossrecovery | 调试Boss恢复 | `debugbossrecovery` | 无 | `debugbossrecovery` |  |
| onbossswipezone | 模拟Boss横扫区域点击 | `onbossswipezone <bIsPress>` | 无 | `onbossswipezone` |  |
| bosssupermove | 让Boss释放大招 | `bosssupermove` | 无 | `bosssupermove` |  |
| bossspecialattack | 让Boss释放特殊攻击 | `bossspecialattack` | 无 | `bossspecialattack` |  |
| activatesupermode | 进入大招模式 | `activatesupermode` | 无 | `activatesupermode` |  |
| debugmagic | 魔法调试显示 | `debugmagic` | 无 | `debugmagic` |  |
| activatemagicmode | 进入魔法模式 | `activatemagicmode` | 无 | `activatemagicmode` |  |
| deactivatemagicmode | 退出魔法模式 | `deactivatemagicmode` | 无 | `deactivatemagicmode` |  |
| activatefinalstrikemode | 进入终结连击模式 | `activatefinalstrikemode` | 无 | `activatefinalstrikemode` |  |
| setsidezones | 设置侧向区域(调试) | `setsidezones <pos> <Scale>` | 无 | `setsidezones` |  |

### SwordGame — 游戏模式

| 命令 | 中文说明 | 标准格式 | 参数说明 | 示例 | 原生 |
|---|---|---|---|---|---|
| forcestartmatch | 强制开始比赛(内部) | `forcestartmatch` | 无 | `forcestartmatch` |  |

## 宝石列表（完整，167 条）

> 发放：控制台执行 `Item <模板名>` 或 `GiveAndMasterItem <模板名>`。
> 显示名缺中文时以英文为准；In/Out 为合成配对的半宝石，Uber 为稀有版。

| 模板名 | 中文显示名 | 英文显示名 | 分类 | 主属性 | 价格 |
|---|---|---|---|---|---|
| AttackGem | 攻击宝石(推断) |  | 攻击/伤害宝石 | DamageBonus=1 | 250 |
| AttackGemIn | 攻击宝石·合成原料(推断) |  | 攻击/伤害宝石 | DamageBonus=150 | 275000 |
| AttackGemOut | 攻击宝石·合成产物(推断) |  | 攻击/伤害宝石 | DamageBonus=150 | 230000 |
| UberAttackGem | 稀有攻击宝石 | RARE ATTACK GEM | 攻击/伤害宝石 | DamageBonus=250 | 5000000 |
| HealthGem | 生命宝石(推断) |  | 生命宝石 | HealthBonus=1 | 225 |
| HealthGemIn | 生命宝石·合成原料(推断) |  | 生命宝石 | HealthBonus=45 | 295000 |
| HealthGemOut | 生命宝石·合成产物(推断) |  | 生命宝石 | HealthBonus=45 | 215000 |
| UberHealthGem | 稀有健康宝石 | RARE HEALTH GEM | 生命宝石 | HealthBonus=60 | 4000000 |
| ShieldGem | 盾牌宝石(推断) |  | 盾牌宝石 | ShieldBonus=1 | 200 |
| ShieldGemIn | 盾牌宝石·合成原料(推断) |  | 盾牌宝石 | ShieldBonus=50 | 280000 |
| ShieldGemOut | 盾牌宝石·合成产物(推断) |  | 盾牌宝石 | ShieldBonus=50 | 160000 |
| UberShieldGem | 稀有防护宝石 | RARE SHIELD GEM | 盾牌宝石 | ShieldBonus=75 | 3000000 |
| MagicGem | 魔法宝石(推断) |  | 魔法宝石 | MagicBonus=1 | 180 |
| MagicGemIn | 魔法宝石·合成原料(推断) |  | 魔法宝石 | MagicBonus=150 | 240000 |
| MagicGemOut | 魔法宝石·合成产物(推断) |  | 魔法宝石 | MagicBonus=150 | 140000 |
| UberMagicGem | 稀有魔法宝石 | RARE MAGIC GEM | 魔法宝石 | MagicBonus=250 | 4500000 |
| FireGem | 火焰宝石(推断) |  | 元素宝石(Fire) | FireBonus=1 | 300 |
| FireGemIn | 火焰宝石·合成原料(推断) |  | 元素宝石(Fire) | FireBonus=300 | 350000 |
| FireGemOut | 火焰宝石·合成产物(推断) |  | 元素宝石(Fire) | FireBonus=300 | 275000 |
| UberFireGem | 稀有火焰宝石 | RARE FIRE GEM | 元素宝石(Fire) | FireBonus=500 | 8000000 |
| IceGem | 冰霜宝石(推断) |  | 元素宝石(Ice) | IceBonus=1 | 300 |
| IceGemIn | 冰霜宝石·合成原料(推断) |  | 元素宝石(Ice) | IceBonus=300 | 350000 |
| IceGemOut | 冰霜宝石·合成产物(推断) |  | 元素宝石(Ice) | IceBonus=300 | 275000 |
| UberIceGem | 稀有冰霜宝石 | RARE ICE GEM | 元素宝石(Ice) | IceBonus=500 | 8000000 |
| ElecGem | 闪电宝石(推断) |  | 元素宝石(Elec) | ElecBonus=1 | 300 |
| ElecGemIn | 闪电宝石·合成原料(推断) |  | 元素宝石(Elec) | ElecBonus=300 | 350000 |
| ElecGemOut | 闪电宝石·合成产物(推断) |  | 元素宝石(Elec) | ElecBonus=300 | 275000 |
| UberElecGem | 稀有震动宝石 | RARE SHOCK GEM | 元素宝石(Elec) | ElecBonus=500 | 8000000 |
| PoisonGem | 剧毒宝石(推断) |  | 元素宝石(Poison) | PoisonBonus=1 | 300 |
| PoisonGemIn | 剧毒宝石·合成原料(推断) |  | 元素宝石(Poison) | PoisonBonus=300 | 350000 |
| PoisonGemOut | 剧毒宝石·合成产物(推断) |  | 元素宝石(Poison) | PoisonBonus=300 | 275000 |
| UberPoisonGem | 稀有毒液宝石 | RARE POISON GEM | 元素宝石(Poison) | PoisonBonus=500 | 8000000 |
| LightGem | 光辉宝石(推断) |  | 元素宝石(Light) | LightBonus=1 | 300 |
| LightGemIn | 光辉宝石·合成原料(推断) |  | 元素宝石(Light) | LightBonus=300 | 350000 |
| LightGemOut | 光辉宝石·合成产物(推断) |  | 元素宝石(Light) | LightBonus=300 | 275000 |
| UberLightGem | 稀有明亮宝石 | RARE BRIGHT GEM | 元素宝石(Light) | LightBonus=500 | 8000000 |
| DarkGem | 暗黑宝石(推断) |  | 元素宝石(Dark) | DarkBonus=1 | 300 |
| DarkGemIn | 暗黑宝石·合成原料(推断) |  | 元素宝石(Dark) | DarkBonus=300 | 350000 |
| DarkGemOut | 暗黑宝石·合成产物(推断) |  | 元素宝石(Dark) | DarkBonus=300 | 275000 |
| UberDarkGem | 稀有暗宝石 | RARE DARK GEM | 元素宝石(Dark) | DarkBonus=500 | 8000000 |
| WaterGem | 水流宝石(推断) |  | 元素宝石(Water) | WaterBonus=1 | 300 |
| WaterGemIn | 水流宝石·合成原料(推断) |  | 元素宝石(Water) | WaterBonus=300 | 350000 |
| WaterGemOut | 水流宝石·合成产物(推断) |  | 元素宝石(Water) | WaterBonus=300 | 275000 |
| UberWaterGem | 稀有水宝石 | RARE WATER GEM | 元素宝石(Water) | WaterBonus=500 | 8000000 |
| WindGem | 风暴宝石(推断) |  | 元素宝石(Wind) | WindBonus=1 | 300 |
| WindGemIn | 风暴宝石·合成原料(推断) |  | 元素宝石(Wind) | WindBonus=300 | 350000 |
| WindGemOut | 风暴宝石·合成产物(推断) |  | 元素宝石(Wind) | WindBonus=300 | 275000 |
| UberWindGem | 稀有风宝石 | RARE WIND GEM | 元素宝石(Wind) | WindBonus=500 | 8000000 |
| UberElementalAttackGem | 稀有暗火宝石 | RARE DARKFIRE GEM | 稀有元素宝石 |  | 9000000 |
| RainbowElementalAttackGem | 稀有光谱宝石 | RARE SPECTRUM GEM | 稀有元素宝石 |  | 9000000 |
| UberElementalAttackGem_100 | 稀有暗火宝石 | RARE DARKFIRE GEM | 稀有元素宝石 |  | 10000000 |
| RainbowElementalAttackGem_100 | 稀有光谱宝石 | RARE SPECTRUM GEM | 稀有元素宝石 |  | 10000000 |
| StabAttackGem | 突刺攻击宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 对Boss额外伤害 · 突刺时 | 225 |
| SlashAttackGem | 挥砍攻击宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 对Boss额外伤害 · BT_On2HSlash | 275 |
| ParryChargeGem | 招架充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 受击伤害充能 · 招架时 | 6500 |
| BlockChargeGem | 格挡充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 受击伤害充能 · 格挡时 | 2500 |
| DodgeChargeGem | 闪避充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 受击伤害充能 · 闪避时 | 1500 |
| Gem3_1 | 圆形宝石1(推断) |  | 元素宝石(Fire) | FireBonus=1, 玩家元素防御 | 5000 |
| Gem3_2 | 圆形宝石2(推断) |  | 元素宝石(Ice) | IceBonus=1, 玩家元素防御 | 5000 |
| Gem3_3 | 圆形宝石3(推断) |  | 元素宝石(Elec) | ElecBonus=1, 玩家元素防御 | 5000 |
| Gem3_4 | 圆形宝石4(推断) |  | 元素宝石(Poison) | PoisonBonus=1, 玩家元素防御 | 5000 |
| Gem3_5 | 圆形宝石5(推断) |  | 元素宝石(Light) | LightBonus=1, 玩家元素防御 | 5000 |
| Gem3_6 | 圆形宝石6(推断) |  | 元素宝石(Dark) | DarkBonus=1, 玩家元素防御 | 5000 |
| Gem3_7 | 圆形宝石7(推断) |  | 元素宝石(Water) | WaterBonus=1, 玩家元素防御 | 5000 |
| Gem3_8 | 圆形宝石8(推断) |  | 元素宝石(Wind) | WindBonus=1, 玩家元素防御 | 5000 |
| UberElementalGemMax | 稀有元素宝石(最大)(推断) |  | 稀有元素宝石 | BattleEffectValue=100, 玩家元素防御 | 150000 |
| UberElementalGem1 | 稀有元素宝石1(推断) |  | 稀有元素宝石 | BattleEffectValue=5, 玩家元素防御 | 10000 |
| UberElementalGem2 | 稀有元素宝石2(推断) |  | 稀有元素宝石 | BattleEffectValue=10, 玩家元素防御 | 20000 |
| UberElementalGem3 | 稀有元素宝石3(推断) |  | 稀有元素宝石 | BattleEffectValue=25, 玩家元素防御 | 40000 |
| UberElementalGem4 | 稀有元素宝石4(推断) |  | 稀有元素宝石 | BattleEffectValue=50, 玩家元素防御 | 80000 |
| ItemDropGem_1 | 物品掉落宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2.0, 世界掉落率 · 被动 | 2000 |
| ItemDropGem_2 | 物品掉落宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2.0, 世界掉落率 · 被动 | 3000 |
| ItemDropGem_3 | 物品掉落宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2.0, 世界掉落率 · 被动 | 1000 |
| ItemDropGem_4 | 物品掉落宝石 #4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1.5, 世界掉落率 · 被动 | 3000 |
| DragonFightGem_1 | 龙战宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=5, 龙战时长+ · 被动 | 60000 |
| BlockGem_1 | 格挡宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 对Boss额外伤害 · 格挡时 | 2000 |
| BlockGem_2 | 格挡宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 · 格挡时 | 3500 |
| PerfectBlockGem_1 | 完美格挡宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 对Boss额外伤害 · 完美格挡时 | 1500 |
| PerfectBlockGem_2 | 完美格挡宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得生命 · 完美格挡时 | 1500 |
| ScratchGem_1 | 磨削宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得金币 · 磨削时 | 50000 |
| ScratchGem_2 | 磨削宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 · 磨削时 | 2000 |
| GloryModeHitGem_1 | 荣耀模式命中宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得金币 · 荣耀模式命中时 | 75000 |
| GloryModeHitGem_2 | 荣耀模式命中宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得经验 · 荣耀模式命中时 | 35000 |
| GloryModeHitGem_3 | 荣耀模式命中宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得生命 · 荣耀模式命中时 | 5000 |
| ComboGem_1 | 连击宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2, 获得护盾 · 连击时 | 2000 |
| ComboGem_2 | 连击宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 · 连击时 | 1000 |
| BonusComboGem_1 | 奖励连击宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2, 获得护盾 · 奖励连击时 | 1000 |
| BonusComboGem_2 | 奖励连击宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=20, 获得生命 · 奖励连击时 | 1250 |
| BonusComboGem_3 | 奖励连击宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 获得金币 · 奖励连击时 | 250000 |
| BonusComboGem_4 | 奖励连击宝石 #4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得经验 · 奖励连击时 | 25000 |
| BonusComboGem_5 | 奖励连击宝石 #5(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 大招充能 · 奖励连击时 | 300000 |
| BonusComboGem_6 | 奖励连击宝石 #6(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 魔法充能 · 奖励连击时 | 150000 |
| ParryGem_1 | 招架宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得生命 · 招架时 | 4000 |
| ParryGem_2 | 招架宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 对Boss额外伤害 · 招架时 | 6000 |
| GreatParryAllGem | 完美招架全部宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 完美招架次数+ · 被动 | 12500000 |
| PerfectParryGem_1 | 完美招架宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得生命 · 完美招架时 | 3000 |
| PerfectParryGem_2 | 完美招架宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 对Boss额外伤害 · 完美招架时 | 3000 |
| HitGem_1 | 命中宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 · 命中时 | 10000 |
| FinalHitGem_1 | 终结命中宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 对Boss额外伤害 · 终结技命中时 | 3500 |
| FinalHitGem_2 | 终结命中宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得生命 · 终结技命中时 | 2500 |
| FinalHitGem_3 | 终结命中宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=75, 获得金币 · 终结技命中时 | 125000 |
| FinalHitGem_4 | 终结命中宝石 #4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 获得经验 · 终结技命中时 | 27500 |
| FinalHitGem_5 | 终结命中宝石 #5(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 魔法充能 · 终结技命中时 | 175000 |
| BreakBossForHealthGem_1 | 破Boss回血宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 · 击破Boss时 | 1000 |
| BreakBossHitWindow_1 | 破Boss破绽窗口宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, BE_BossHitWindow1Off · 击破Boss时 | 82000 |
| BreakBossGetMagic_1 | 破Boss得魔法宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 魔法充能 · 击破Boss时 | 770000 |
| BreakBossGetSuper_1 | 破Boss得大招宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 大招充能 · 击破Boss时 | 980000 |
| GlobalScaleGoldGem_1 | 全局金币加成宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.05, 世界金币% · 被动 | 1000 |
| GlobalScaleXPGem_1 | 全局经验加成宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.25, 世界经验% · 被动 | 2000 |
| GlobalScaleRareGem_1 | 全局稀有加成宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.05, 世界稀有物品% · 被动 | 5000 |
| UberBossGem_1 | 稀有Boss宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 时限Boss破绽 · 被动 | 25000 |
| UberBossGem_2 | 稀有Boss宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2, 时限Boss破绽 · 被动 | 50000 |
| UberBossGem_3 | 稀有Boss宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=3.0, Boss眩晕 · 完美招架时 | 100000 |
| UberMagic_1 | 稀有魔法宝石1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得护盾 · 施放魔法时 | 2500 |
| UberMagic_2 | 稀有魔法宝石2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得生命 · 施放魔法时 | 5000 |
| UberGoldGem | 稀有黄金宝石 | RARE GOLD GEM | 特效宝石(BattleEffect) | BattleEffectValue=1000, 获得金币 · 命中时 | 20000000 |
| UberXPGem | 稀有经验宝石 | RARE XP GEM | 特效宝石(BattleEffect) | BattleEffectValue=1000, 获得经验 · 命中时 | 50000 |
| UberSwitchClass | 稀有换武器宝石(推断) |  | 特效宝石(BattleEffect) | 切换武器类型 · 释放大招时 | 250000 |
| UberTakeHitGem_1 | 稀有受击宝石 #1(推断) |  | 特效宝石(BattleEffect) | 切换武器类型 · 被泰坦击中时 | 850000 |
| UberTakeHitGem_2 | 稀有受击宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 魔法充能 · 被泰坦击中时 | 50000 |
| UberTakeHitGem_3 | 稀有受击宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.02, 大招充能 · 被泰坦击中时 | 80000 |
| BonusComboLenGem | 奖励连击长度宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 奖励连击上限+ · 被动 | 12555000 |
| BossBoostGem | Boss强化宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, Boss等级+ | 7500 |
| UberBossBoostGem | 稀有Boss强化宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1000, Boss等级+ | 15000000 |
| LightGem_1 | 光辉宝石 #1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得生命 · BT_OnCombo5 | 3000 |
| LightGem_2 | 光辉宝石 #2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得金币 · BT_OnCombo4 | 40000 |
| LightGem_3 | 光辉宝石 #3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 连击后重击 · BT_OnCombo5 | 300000 |
| LightGem_4 | 光辉宝石 #4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 时限内全格挡有效 · BT_OnCombo4 | 250000 |
| LightGem_5 | 光辉宝石 #5(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.90, Boss攻速修改 · 被动 | 750000 |
| LightGem_6 | 光辉宝石 #6(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.80, Boss攻速修改 · 被动 | 2500000 |
| LightGemMagic | 光辉宝石Magic(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.05, 魔法充能 · 完美招架时 | 500000 |
| UberParryChargeGem | 稀有招架充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 受击伤害充能 · 招架时 | 12500 |
| HeavyGem_1 | 重击宝石1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 获得生命 · BT_On2HSlash | 3000 |
| HeavyGem_2 | 重击宝石2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得金币 · BT_On2HSlash | 60000 |
| HeavyGem_3 | 重击宝石3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得经验 · BT_On2HSlash | 50000 |
| HeavyGem_4 | 重击宝石4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 对Boss额外伤害 · 格挡时 | 2500 |
| HeavyGem_5 | 重击宝石5(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=75, 对Boss额外伤害 · 完美格挡时 | 1250 |
| HeavyGem_6 | 重击宝石6(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1.0, 连击挥砍数修改 · 被动 | 2500000 |
| HeavyGem_7 | 重击宝石7(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.90, Boss攻速修改 · 被动 | 750000 |
| HeavyGem_8 | 重击宝石8(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.80, Boss攻速修改 · 被动 | 2500000 |
| HeavyGemMagic | 重击宝石(魔法)(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.05, 魔法充能 · 完美格挡时 | 500000 |
| UberBlockChargeGem | 稀有格挡充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=75, 受击伤害充能 · 格挡时 | 9500 |
| DualGem_1 | 双重宝石1(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得经验 · 招架时 | 35000 |
| DualGem_2 | 双重宝石2(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 获得金币 · 突刺时 | 40000 |
| DualGem_3 | 双重宝石3(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 时限Boss破绽 · 施放魔法时 | 250000 |
| DualGem_4 | 双重宝石4(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 获得经验 · 施放魔法时 | 10000 |
| DualGem_5 | 双重宝石5(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.90, Boss攻速修改 · 被动 | 750000 |
| DualGem_6 | 双重宝石6(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.80, Boss攻速修改 · 被动 | 2500000 |
| DualGem_7 | 双重宝石7(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=50, 获得生命 · 突刺时 | 2000 |
| DualGem_8 | 双重宝石8(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=25, 对Boss额外伤害 · 突刺时 | 4000 |
| DualGemMagic | 双重宝石(魔法)(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=0.05, 魔法充能 · 闪避时 | 500000 |
| UberDodgeChargeGem | 稀有闪避充能宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=40, 受击伤害充能 · 闪避时 | 11000 |
| Potion_HealthRegen | 生命回复药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 获得生命 | 25000 |
| Potion_ShieldRegen | 护盾回复药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 获得护盾 | 80000 |
| Potion_DoubleXP | 双倍经验药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=2.0, 世界经验% | 200000 |
| Potion_ParryAll | 全招架药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 时限内全招架 | 250000 |
| Potion_BlockAll | 全格挡药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 时限内全格挡有效 | 100000 |
| Potion_ElementalDefense | 元素防御药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=100, 玩家元素防御 | 100000 |
| Potion_SecondChance | 第二次机会药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=1, 第二次机会次数+ | 650000 |
| Potion_UnlimitedDodge | 无限闪避药剂宝石(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=5, 获得闪避 | 25000 |
| Spawn_SmallGoldTouch | 点金术(小额)(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=8, 触摸宝藏生成 · 立即生效 | 100000 |
| Spawn_LargeGoldTouch | 点金术(大额)(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=10, 触摸宝藏生成 · 立即生效 | 200000 |
| Spawn_RandomTouch | 随机触摸(推断) |  | 特效宝石(BattleEffect) | 触摸宝藏生成 · 立即生效 | 300000 |
| Spawn_RandomIngredientTouch | 随机原料触摸(推断) |  | 特效宝石(BattleEffect) | BattleEffectValue=6, 触摸宝藏生成 · 立即生效 | 500000 |
| TRA_GrabBag_SmallGem |  |  | 转轮(随机宝石包) | BattleEffectValue=100	; Reward Level | 100000 |
| TRA_GrabBag_MediumGem |  |  | 转轮(随机宝石包) | BattleEffectValue=600	; Reward Level | 1400000 |
| TRA_GrabBag_LargeGem |  |  | 转轮(随机宝石包) | BattleEffectValue=40000	; Reward Level | 1400000 |
