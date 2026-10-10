# releases/

**这个目录故意是空的。** 成品压缩包不放仓库里，走 GitHub Releases 页面：

### → <https://github.com/Tongyonyuen/ib3-trainer/releases>

## 为什么不放仓库

- 每个成品包约 **4 MB**（其中 11.9 MB 的 `SwordGame.upk` 压缩后约占 3 MB），两个版本就是 8 MB+。
  **二进制一旦提交进 Git 历史就永久留存**，之后要瘦身必须重写历史
- 成品包里含**游戏数据**（`SwordGame.upk`、`ib3_gems.ini`），不该出现在源码树里。
  详见仓库根目录的 [LICENSE](../LICENSE) 与 [README](../README.md) 的「版权与许可」一节

## 成品包里有什么

| 文件 | 说明 |
|---|---|
| `IB3训练器2.exe` | 主程序，单文件，横幅图已内嵌 |
| `items.csv` | 物品数据库（949 条） |
| `ib3_gems.ini` | 宝石公式库 —— **运行时必需**，源码树里没有 |
| `SwordGame.upk` | 游戏脚本包改造版 —— **首次选「游戏目录…」时自动部署**，源码树里没有 |
| `使用说明.txt` | 使用说明（与 [docs/使用说明.md](../docs/使用说明.md) 同源） |

压缩包解压后还会自动生成 `ib3_addrs.ini` / `ib3_paths.ini` / `ib3_ui.ini` / `ib3_mailbox.json`
等本机状态文件 —— 这些**不要**在用户之间传递。

## 怎么出包（发版清单）

1. `I18n.cs` 的 `BuildInfo` 三处版本号一起改（`Version` / `Major.Minor.Patch`），**必须与要打的
   git tag 一致** —— 漏改会让更新器陷入无限更新循环（详见该处注释）。
2. `CHANGELOG.md` 开新版本段，正文 **首行必须是 `>` 引用摘要**（新增/修改/删除 各一行）——
   「每版首次运行」弹窗与「关于」页只取这几行。
3. `sh build.sh` 出 `src/ib3trainer2/IB3训练器2.exe`。
4. 拼包：单层目录 `IB3Trainer2_vX.Y.Z/`，放上表那 5 个文件。其中后三份数据文件**不在源码树里**
   （见 `.gitignore`），本机副本在 `E:\ib3_re\ib3trainer2\`（`SwordGame.upk`、`ib3_gems.ini`）
   与 `src/ib3trainer2/`（`items.csv`）；**未变更时可直接复用上一版包内的同名文件，发布前建议
   sha256 核对**。
5. `push main` → 打 **annotated** tag `vX.Y.Z` → `push origin vX.Y.Z` → `gh release create` 传
   **两个**资产：`IB3Trainer2_vX.Y.Z_full.zip` 与裸 exe `IB3Trainer2.exe`（把 `IB3训练器2.exe`
   复制一份改成这个名 —— 资产名取文件名）。
6. 发布说明照 v1.1.8 的形状：一段摘要 + `## 下载哪个` 两行资产表。**只有随包数据文件变了**才
   写 `DATA_REV: <n>` 并把 `BuildInfo.DataRev` +1。

### `使用说明.txt` 的生成规则

它就是 **`docs/使用说明.md` 去掉 `**` 与反引号 `` ` ``** 后的纯文本（UTF-8 无 BOM），其余一字不改
—— 2026-10-10 用「拿改动前的 md 转换后与 v1.1.8 包内那份对比」验证过，**逐字节相同**。

⚠ 这份 txt 曾在 **v1.1.5–v1.1.8 连续四版没有重新生成**（包里一直是 v1.1.4 时代的旧版）。
**每版都要重生成一次**，否则用户拿到的说明不含本版新功能。

## 历史版本

r1–r13 是打包期的滚动构建，只在原作者本机留存。从 **v1.0.0** 起才正式打标签、走 GitHub Release。
逐版变更见 [CHANGELOG.md](../CHANGELOG.md)。
