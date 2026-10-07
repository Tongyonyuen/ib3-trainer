# Infinity Blade III Trainer

> An **offline, single-player** trainer for the PC port of *Infinity Blade III*. Zero console injection,
> auto-attaches to the game process. Covers gold/chips, the four stats, level & skill points, item
> granting, gem tier & inventory, mastery upgrades, and a general memory-scanning workbench.
> Bilingual (Chinese/English) UI, one self-contained `.exe`, no installer.

`中文` → [README.md](README.md)

[![Release](https://img.shields.io/github/v/release/Tongyonyuen/ib3-trainer?label=release)](https://github.com/Tongyonyuen/ib3-trainer/releases)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

---

## ⚠️ Disclaimer — read this first

- This is an **offline, single-player** trainer, **for personal local use only**. This repository does
  not provide or endorse any multiplayer or online cheating use.
- The trainer **writes changes directly into your game save**, which is **irreversible**. **Back up your
  save first**: `Documents\My Games\Infinity Blade III\SwordGame\Cloud`
- The "mastery upgrade" feature **only raises item levels, never lowers them**. Test on a throwaway save first.
- Some changes **persist** (item level/XP, gold, gem tier and the fused flag); others (stats, player level,
  HP) are **temporary** and get recomputed by the game on reload. See the [manual](docs/使用说明.md) (Chinese).
- This project is **not affiliated with or endorsed by** Epic Games or Chair Entertainment.
  *Infinity Blade* is their trademark.
- **Make sure you own a legitimate copy of the game.**

---

## Features

| Tab | What it does |
|---|---|
| **Combat · Store** | God mode toggle, fill super/magic meters, kill current boss (in combat), direct gold/chip write, refresh the store with a round of rare gems, trigger dragon fight / collector |
| **Item Grant** | Browse every item by category; grants through the correct channel per type — equipment/treasure maps straight to inventory, gems into the portable store, materials/potions/keys/grab-bags as stack counts |
| **Gems · Inventory** | Lists every gem in the inventory and portable store (template / tier / fused flag / random bonus / displayed value / address); retarget to a displayed value or a tier |
| **Growth** | Read / write / lock stamina, shield, attack, magic, level, skill points, HP, max HP; mastery upgrade |
| **Discovery Mode** | General-purpose memory-scan workbench (first scan / filter / snapshot / diff / test write) for advanced troubleshooting |
| **Save Switch** | Detects a save swap and rebinds automatically |

**Highlights**

- **No console injection** — never opens the game console, never steals focus, never sends keystrokes
- **Auto-attach** — locates the real player object by structure and binds gold/chips/stats/level addresses for you
- **Safety gate** — re-locates on every single write; if an address went stale or the array is unloaded it
  **refuses the write and tells you why**, instead of reporting a fake success
- **Bilingual** — auto-detects the system language; one-click toggle in the title bar; the choice is persisted in `ib3_ui.ini`
- **One exe** — no installer, no runtime to ship (uses only the .NET Framework and `csc.exe` built into Windows)

---

## Download & install

Grab the latest package from **[Releases](https://github.com/Tongyonyuen/ib3-trainer/releases)**, extract it
anywhere (avoid special characters in the path), then:

1. Run **`IB3训练器2.exe`**
2. Click **「游戏目录…」 (Game folder…)** at the bottom left → pick the folder containing
   **`Infinity Blade Launcher.exe`** (usually `your game folder\Binaries`)
   - The trainer remembers the location and deploys the bundled `SwordGame.upk` to
     `game folder\SwordGame\CookedPCConsole\SwordGame.upk` (the original is backed up as `SwordGame.upk.orig`)
   - **Replacing the upk is required** — item granting and mastery upgrades depend on it. If it says the
     game is running, close the game and pick the folder again
3. Click **「启动游戏（中文）」 (Launch game, Chinese)** → the trainer **auto-attaches** once the game is up
   (the status bar shows 「已附着」) and you're ready to go

The full per-feature manual, hard constraints and FAQ are in **[docs/使用说明.md](docs/使用说明.md)** (Chinese).

---

## Building from source

No MSBuild, no NuGet, no Visual Studio — **just the .NET Framework compiler that ships with Windows**.

```sh
cd src/ib3trainer2
sh build.sh            # main program -> IB3训练器2.exe
sh build.sh test       # engine self-test (enginetest2.exe); live checks skip if the game isn't running
sh build.sh gemtest    # gem-locating self-test (gemselftest.exe); read-only, never writes the target process
sh build.sh probe      # read-only address probe (addrprobe.exe)
```

`build.sh` invokes `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (bundled with Windows).
The language level is capped at **C# 5**, so the sources use no string interpolation, no `?.`, no `out var`
and no expression-bodied members — that is deliberate, see the constraint comment at the top of `I18n.cs`.

**Build dependencies**: none. New `.cs` files must be added to the `SRC` list in `build.sh` by hand.

> **Note**: this repo's source tree does **not** contain `ib3_gems.ini` or `SwordGame.upk` — those are game
> data and ship only inside the Release package. `SwordGame.upk` is only needed when you pick the game folder;
> `ib3_gems.ini` is a **runtime dependency** (the gem formula library) and the app logs `⚠ 未找到 ib3_gems.ini`
> and degrades without it. For full functionality, drop `ib3_gems.ini` from the Release package next to the exe.

---

## Layout

```
src/ib3trainer2/     Main program (IB3 Trainer 2.0) — the only actively maintained version
src/legacy/          Two earlier standalone trainers, kept for reference
tools/               Reusable CLI tools from the RE work (exec-bit patcher, save parser, package surgery, item DB generator, ...)
docs/                Manual, RE research reports, console-command reference, gem mechanics
releases/            Points at the GitHub Releases page (no zips in the repo)
```

---

## Known limitations

- The trainer targets **one specific build of the game**. On a mismatched build it simply fails or refuses
  to act — it will not scribble over unrelated memory.
- Gem **names are aligned to the save file positionally**. Once the in-game inventory changes
  (fuse / buy / sell / equip), the list can slip out of alignment — meaning "you look at one gem and
  modify another". Save once in-game (change scene) before reading/applying.
- Changing a gem's tier also marks it **fused** (`CookedGemVar=50`). That's required for the game to honour
  the tier, but it is a **persistent** change to your save.
- `ib3_addrs.ini` is generated by the app; the addresses in it are memory locations that differ per machine
  and per run. **Never share that file between users** — it would make the other person's trainer write to the wrong place.

---

## Documentation

| Document | Contents |
|---|---|
| [使用说明 (manual)](docs/使用说明.md) | Per-feature guide, hard constraints, FAQ, tester feedback guide *(Chinese)* |
| [IB3 项目总参考](docs/IB3_项目总参考.md) | Master index: environment map, mechanics conclusions, tool list, **correction log** *(Chinese)* |
| [宝石研究](docs/宝石研究/README.md) | Gem number formulas, fusion mechanics, grant chains, save migration *(Chinese)* |
| [控制台手册](docs/控制台手册/IB3_控制台命令手册.md) | 621 game console commands with Chinese descriptions *(Chinese)* |
| [tools/](tools/README.md) | Purpose and usage of each CLI tool |

---

## License

The **source code and documentation authored by the project owner** are released under the
[MIT License](LICENSE).

**Game data and artwork are not covered**: `SwordGame.upk` (a modified copy of the game's script package),
`ib3_gems.ini` (the contents of the game's `DefaultGems.ini`), the game's localization text and template
names, and the promotional images under `image\` — all remain the property of Epic Games / Chair
Entertainment, and are redistributed only so that the program can run.
