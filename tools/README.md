# tools/ — 逆向过程中沉淀的命令行工具

这些是研究《Infinity Blade III/II》的 UE3 脚本包与存档时写的一次性工具的**可复用部分**。
它们是**独立的命令行程序**，与训练器主程序无关（`src/ib3trainer2/build.sh` 不引用其中任何一个）。

每个工具都提供 `.cs` 源码与已编译的 `.exe`。要自己重新编译：

```sh
C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -target:exe -codepage:65001 -out:名字.exe 名字.cs
```

---

## 脚本包（.upk）工具

| 工具 | 用法 | 用途 |
|---|---|---|
| `patchflags2.exe` | `<upk> <el> <ul> <out> 函数名...` | **exec 位补丁**：给指定函数加上 `FUNC_Exec` 位（`0x200`），使其可从游戏控制台调用。`flags2` = 尾部搜索版，靠搜索函数尾部找到真实的 `FunctionFlags` dword，修正过 `GiveXp` 系函数的偏移问题 |
| `fn.exe` | `<upk> <extract_list.txt> <umodel_list.txt> [funcs\|funcbytes 函数名...]` | **函数/签名提取**：`funcs` 输出全部函数（`所属类\t函数名\t返回类型\t参数`）；`funcbytes` 输出指定函数的原始字节码（供反汇编/标定） |
| `paramd.exe` | `... 函数名...` | 转储指定函数的参数（含非 exec 函数） |
| `xscan.exe` | `<upk> <el> <ul> <exportIdx0based>` | 按导出索引扫描所有函数，找出引用该导出的字节码 |
| `vscan.exe` | `<upk> <el> <ul> <nameIdx>` | 按 FName 索引找**虚函数调用点** |
| `pkgmerge4.exe` | `<chnIn.upk> <int.upk> <out.upk>` | **包手术**：把 INT 本地化包的 `EngineFonts` 对象移植进 CHN 包（v868）—— 修「中文控制台不可见」的最终方案。见 [docs/FIX_中文控制台不可见.md](../docs/FIX_中文控制台不可见.md) |

> `<el>` / `<ul>` = umodel 导出的导出表与名称表文本（`extract_list.txt` / `umodel_list.txt`）。
> 这三个文件需要自己从游戏包生成，工具本身不产出它们。

## 存档与宝石工具

| 工具 | 用法 | 用途 |
|---|---|---|
| `decsave.exe` | `<in.bin> <out.bin>` | **云存档解密**：`Cloud\_SwordSaveX_0-0.bin` 的 `"yeK "` 头 + AES-256-ECB → 明文。这是做游戏内验证的推荐通道 |
| `saveparse2.exe` | 解密后的存档 | 全类型存档转储解析（属性流 / 物品块等） |
| `gemdump.exe` | 解密后的存档 | 抽取每个 `GemName` 块的模板名 + tier + `cookedvar` + boost |
| `gemstate2.exe` | 解密后的存档 | 走一遍每颗暗火宝石的完整 tagged property 块（隐藏状态） |
| `genitemdb.exe` | `<IB3游戏目录> <宝石CSV> <输出csv>` | **生成物品数据库**：解析游戏 7 个 `Default*.ini` 并合并宝石中文名 → `items.csv`（列：模板名,主分类,子分类,中文名,备注）。训练器的「物品发放」页读它 |
| `memtest.exe` | `scanpat <hex> <start> <end>` 等子命令 | **内存扫描/读写 CLI**，与训练器共用同一份 ScanCore 引擎。用于在活进程里定位地址（例如枚举所有带玩家 vtable 的实例）。源码是 `src/legacy/ib3trainer_mem/MemTestCli.cs` |

## 控制台工具

| 工具 | 用法 | 用途 |
|---|---|---|
| `checkcon.exe` | `<hwnd>` | **确保控制台已打开**：按绿色线像素判定当前状态，自动开关到打开态（最多试 3 次） |
| `sendcmd.exe` | `<命令...>` | **控制台注入**：复刻训练器的 `SendOne` 注入逻辑，命令行版。首字符用 VK 按键激活、其余走 `WM_CHAR` —— 刚打开的控制台会丢弃 `WM_CHAR`，只认 `VK` 按键事件 |

---

## 输入方法经验（踩过的坑）

- 控制台文字用 `WM_CHAR`（`postchar`），**支持下划线**；逐键 `postkey` 会丢字符
- **刚打开的控制台丢弃 `WM_CHAR`，只认 VK 按键事件**；且 VK 映射无视 shift（`:` 会变 `;`）。
  最终方案 = **首字符 VK 激活 + 其余 `WM_CHAR`**
- 真键盘 `keybd_event` 会被中文输入法拦截，不可用
- 控制台**不支持粘贴**（Ctrl+V 无效）

## 未随仓库分发的第三方工具

UE Viewer / umodel、`decompress.exe`、`extract.exe`（作者 Konstantin Nosov / Gildor，MIT 许可）
**没有**打包进本仓库 —— 它们是 8.5 MB 的第三方二进制且版本较旧。请自行从
<https://www.gildor.org/projects/umodel> 下载。
