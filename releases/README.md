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
| `使用说明.txt` | 使用说明（与 [docs/使用说明_r13.md](../docs/使用说明_r13.md) 同源） |

压缩包解压后还会自动生成 `ib3_addrs.ini` / `ib3_paths.ini` / `ib3_ui.ini` / `ib3_mailbox.json`
等本机状态文件 —— 这些**不要**在用户之间传递。

## 历史版本

r1–r13 是打包期的滚动构建，只在原作者本机留存。从 **v1.0.0** 起才正式打标签、走 GitHub Release。
逐版变更见 [CHANGELOG.md](../CHANGELOG.md)。
