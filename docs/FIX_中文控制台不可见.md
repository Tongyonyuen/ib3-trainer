# IB3 PC 移植版 — 中文模式下控制台不可见 修复说明

## 结论
**根因**：移植组为 PC 版启用了原版 UE3 控制台（`ConsoleClassName=Engine.Console`），其渲染字体来自
`Engine.GetSmallFont()`（即 `SmallFontName=EngineFonts.SmallFont` 配置指向的对象）。
移植组把 `EngineFonts.SmallFont/TinyFont` 两个字体对象**只添加进了英文包 `Startup_LOC_INT.upk`**，
中文包 `Startup_LOC_CHN.upk`（以及 JPN/KOR/RUS/THA）里完全没有。
于是选中文进入时 `EngineFonts.SmallFont` 解析失败 → Engine.SmallFont = NULL → 控制台文字全部不画
（背景/绿色分隔线用 DrawTile 画，不依赖字体，所以仍然可见）。

**IB2 没有这个问题**，因为移植组在 IB2 的所有语言 loc 包（含 IB2_LOC_CHN）里都补了这两个字体对象。
即：这是移植组在 IB3 上的疏漏，IB3 的日/韩/俄/泰语模式控制台同样是坏的。

**修复方式**：按移植组在 IB2 上的同款做法，把 `EngineFonts` 包对象（SmallFont/TinyFont + 两个 TFC 纹理引用）
从 INT 包**植入** `Startup_LOC_CHN.upk`（新增 5 个导出对象 + 10 个名字表项，改写摘要计数与偏移）。
该文件为"多语言通用修复"：实际上也顺带修复了 JPN/KOR/RUS/THA 模式（只要把同样步骤套在对应 loc 包上）。

## 改动的文件
- `E:\IB3\SwordGame\CookedPCConsole\Startup_LOC_CHN.upk` — 唯一被修改的游戏文件（5724036 字节，原版 5711735 字节解压态）。
  原版备份：`E:\ib3_re\orig\Startup_LOC_CHN.upk`（压缩态，md5 8f24c76af32bfb7f642ee954baa7159e）。
- 配置零改动（`SmallFontName` 保持 `EngineFonts.SmallFont`）。

## 植入内容（v868 包格式）
- 新增名字表项 10 个：B_Fonts, EngineFonts, MipTailBaseIdx, NameProperty, SmallFont, Startup_LOC_INT, TextureFileCacheName, TinyFont, XPadding, YPadding
- 新增导入表：无（INT/CHN 的 6 项导入表完全一致，引用零改动）
- 新增导出对象 5 个（0-based 序号 89..93）：
  - 89 `Font'EngineFonts.SmallFont'`（数据自 INT 包重映射拷贝；Textures 引用 15→93）
  - 90 `Font'EngineFonts.TinyFont'`（16→94）
  - 91 `Package'EngineFonts'`
  - 92 `Texture2D'EngineFonts.SmallFont.Texture2D_28'`（外层=SmallFont；像素数据在 Textures.tfc，已存在于游戏目录）
  - 93 `Texture2D'EngineFonts.TinyFont.Texture2D_29'`
- 摘要更新：NameCount 136→146、ExportCount 89→94、HeaderSize/ImportOffset/ExportOffset/DependsOffset 平移、
  generation 表 {89,136}→{94,146}；全部 89 个旧导出的 SerialOffset += 590。
- v868 导出条目结构（实测标定）：`cls,sup,pkg,nameIdx,nameExt,d1,d2,flags,size,off`（10×4B）+ GUID(16B) + 3×4B 尾部 = 68B 定长；
  例外：INT exp6（Package 对象）= 76B。名字表中 None 终结符为 8B（nameIdx+ext）。
- 载荷级重映射坑：NameProperty / 带枚举的 ByteProperty（size=8）的**载荷本身是 FName**，
  必须重映射（Format=PF_DXT5、TextureFileCacheName="Textures"、LODGroup、MipGenSettings 四处），否则引擎加载 TFC 失败直接退出。

## 验证记录（游戏内实测）
- 中文模式：`-` 打开控制台，输入 `stat fps` → 输入行、自动补全提示（"Stat FPS (Shows FPS counter)"）、
  回车后历史行 `>>> stat fps <<<` 全部正常渲染（截图 out/final_chn*.png、fixtest_v9d/e.png）。
- 英文模式：无回归（`test` 输入 + TestLevel 自动补全正常，out/baseline3_zoom.png）。
- 中文 UI 无回归：B_Fonts_CHN 字体对象字节级未改动（只追加对象）。

## 工具
- `E:\ib3_re\pkgmerge4.cs/.exe` — 植入工具（可 SKIPADDEXP=1 做无导出校验版）。
  对其他语言：`pkgmerge4.exe <该语言loc解压包> <INT解压包> <输出>`，然后把输出转回游戏目录
  （注意：本工具输出的是解压态包，游戏可直接加载；同名替换即可）。
- 过程截图与分析中间产物均在 `E:\ib3_re\out\`。
