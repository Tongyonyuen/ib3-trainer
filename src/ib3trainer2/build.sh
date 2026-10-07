#!/bin/sh
# IB3 训练器 2.0 构建脚本
# 用法: sh build.sh            — 编译主程序 IB3训练器2.exe
#       sh build.sh test       — 仅编译并运行引擎自检 enginetest2.exe
#       sh build.sh gemtest    — 宝石定位自检（跑的是 Tabs.Gems.cs 里的真代码，只读）
#       sh build.sh updatetest — 更新器离线夹具自测（不联网、不发 release）
#       sh build.sh probe      — 只读地址探针 addrprobe.exe
set -e
cd "$(dirname "$0")"
CSC="C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
SRC="Ib3Core.cs MemIO.cs I18n.cs Theme.cs Layout.cs Toast.cs ItemDb.cs GemDb.cs Launcher.cs Recipes.cs EngineCall.cs Ib3Trainer2.cs Tabs.Scan.cs Tabs.Combat.cs Tabs.Items.cs Tabs.Growth.cs Tabs.Gems.cs Tabs.Misc.cs Tabs.Save.cs BusyOverlay.cs AboutForm.cs Update.cs UpdateForm.cs"

# ★ 新增 .cs 必须手工加进上面的 SRC（这里是写死的 csc 调用，没有 MSBuild 的 glob）。
#   UpdateSelfTest.cs **故意不在 SRC 里** —— 它自带 Main，只在 updatetest 目标里编。

# 横幅图内嵌进 exe —— 原实现写死 @"E:\IB3\Official Image" 且「目录不存在就静默 return」，
# 换个机器必然读不到（外部用户反馈的「横幅不显示」）。内嵌后单个 exe 就能携带图片。
# 资源名 banner.<key>，与 Ib3Trainer2.LoadBannerImage() 的查找规则对应。
RES="-resource:image/isa.jpg,banner.isa -resource:image/raidiar.jpg,banner.raidiar -resource:image/siris.jpg,banner.siris -resource:image/hideout.jpg,banner.hideout"

if [ "$1" = "test" ]; then
  "$CSC" -target:exe -main:Ib3Trainer2.EngineTest2 -codepage:65001 -out:enginetest2.exe Ib3Core.cs MemIO.cs EngineTest2.cs
  ./enginetest2.exe
elif [ "$1" = "gemtest" ]; then
  # 宝石定位自检：跑的就是 Tabs.Gems.cs 里那份真代码（反射调私有方法），只读，
  # 不写目标进程内存、不写存档。游戏在跑才有实况项，否则记 SKIP。
  "$CSC" -target:exe -main:Ib3Trainer2.GemSelfTest -codepage:65001 -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:gemselftest.exe $SRC GemSelfTest.cs
  ./gemselftest.exe
elif [ "$1" = "updatetest" ]; then
  # 更新器离线自测：夹具全内联，**不联网、不发 release**，所以随时可跑。
  # 只编 I18n.cs（要 BuildInfo）+ Update.cs + 自测本体 —— 这条链路刻意不依赖
  # MainForm / Theme，正是因为 Update.cs 被写成了纯逻辑（见其文件头注释）。
  "$CSC" -target:exe -main:Ib3Trainer2.UpdateSelfTest -codepage:65001 -r:System.Windows.Forms.dll -out:updatetest.exe I18n.cs Update.cs UpdateSelfTest.cs
  ./updatetest.exe
elif [ "$1" = "probe" ]; then
  # 只读地址探针：验证「四项属性 = 真身 + 固定偏移」。不写目标进程内存。
  "$CSC" -target:exe -main:Ib3Trainer2.AddrProbe -codepage:65001 -out:addrprobe.exe Ib3Core.cs MemIO.cs AddrProbe.cs
  echo "OK: addrprobe.exe"
else
  # -win32manifest 必须在清单里声明 dpiAware：csc 不带该参数时会内嵌一份只有 asInvoker
  # 的默认清单，进程以「DPI 未感知」启动，高缩放屏上整个窗体被位图拉伸（糊 + 装不下）。
  "$CSC" -target:winexe -codepage:65001 -win32manifest:app.manifest -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:"IB3训练器2.exe" $RES $SRC
  echo "OK: IB3训练器2.exe"
fi
