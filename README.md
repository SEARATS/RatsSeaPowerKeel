# Sea Power: Keel

Keel (by R.A.T.S.) is the mod loader for Sea Power's code mods and carries on Anchor Chain: it finds the mod DLLs, checks them against your game version, starts them and tells you what happened.

| | |
| --- | --- |
| Questions | [R.A.T.S. Discord](https://discord.gg/CzuHd4f8Yf): help, ideas, talk; the first place to ask |
| Version | `0.8.0` (newer work on `main` under Unreleased) |
| Game | Sea Power 0.9.3 (public and beta) tested in the game with 0.6.0 (2026-11-18); `_info.ini` declares 0.9.0 up to, not including, 0.10.0 |
| Needs | [BepInEx 5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2) (KeelSetup adds it); Keel is the loader itself and needs no other one |
| Get it | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3835527459) |
| Settings | `BepInEx\config\dev.sewerlabs.seapowerkeel.cfg`, see [docs/settings.md](docs/settings.md) |
| Bug reports | [R.A.T.S. feedback board](https://hub.sewerlabs.dev/s/keel) |
| Roadmap | [docs/community/ROADMAP.md](docs/community/ROADMAP.md): where Keel goes next, also in Discord #roadmap |
| Community | [docs/community](docs/community/README.md): how we work, partner mods, open topics |
| Video | [Install on a clean game](https://youtu.be/m66tiCyWSNw) (more: [when a mod breaks](https://youtu.be/JrRR7d8NbhU), [switching loaders](https://youtu.be/vc_UddZHn0g)) |
| Source | R.A.T.S. Source License, see `LICENSE` |

![Keel's cards in the main menu](workshop/screenshots/01-cards-whats-new.jpg)

Mods made for [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757) run under Keel as they are: Keel is the one loader a player needs.

## Features

- Loads the plugin DLLs of the mods switched on in Mods, in dependency order; a mod that throws is marked and the next one starts.
- Runs plain BepInEx plugins and Anchor Chain mods as they are; one DLL for the game's current main version.
- A mod can name the Workshop items it needs: a missing one shows on the card with a Get it button, an installed one that is off is switched on ([for mod authors](docs/for-mod-authors.md)).
- [Early preparation](docs/for-mod-authors.md#early-preparation-in-keel) runs file work on a background worker during the splash.
- The BepInEx starter can start [standalone preloaders](docs/for-mod-authors.md#standalone-preloaders) with its own opt-in manifest entries.
- Checks every DLL against the game's `Managed` assemblies and leaves out one that could stop missions from loading, saying why.
- For out-of-date mods, flags Harmony targets and method lookups affected by changes in the game ([method history](docs/method-history.md)).
- Report window (Ctrl+Shift+K): every mod running, with errors, or not started and why; the same in `BepInEx\Keel-report.txt`.
- Journals every step, so after a crash the next start names the mod whose code was running and keeps it out until it is updated.
- `KeelWatcher.exe` shows a window the moment the game dies, naming the mod.
- *Save logs* and *Send report* pack your logs for a bug report; *Send report* replaces your Windows user name first.
- *Send report* tells you when the crash is a known one: fixed in a newer version, or already being looked at ([docs/crash-json.md](docs/crash-json.md)).
- A timestamped log of the game, BepInEx and every mod (`BepInEx\Keel-log.txt`).
- What's new card in the main menu after a start where mods changed, with the authors' change notes.
- Loading window while the game starts: the mod being started and how long it took ([docs/loading-window.md](docs/loading-window.md)).
- While Keel is on in Mods, it is the only loader that runs; Anchor Chain's and Alpine's files stay untouched ([docs/how-it-works.md](docs/how-it-works.md)).

Full detail: [docs/features.md](docs/features.md). How it works and where it differs from Anchor Chain: [docs/how-it-works.md](docs/how-it-works.md).

## Install

| Way | For | Steps |
|---|---|---|
| KeelSetup.exe | Windows | [docs/install.md](docs/install.md#keelsetupexe) |
| By hand | Windows, without running any `.exe` | [docs/install.md](docs/install.md#by-hand-no-setup-program) |
| Linux Steam Proton | Linux | [docs/install.md](docs/install.md#linux-steam-proton) |

- Each way ends the same: a starter file in `BepInEx\plugins` and Keel's takeover patcher in `BepInEx\patchers`.
- Restart after every change to the mod list; switching from Anchor Chain or Alpine needs nothing deleted ([docs/install.md](docs/install.md)).

## Settings

- In the game: Options > *Keel* tab (most settings apply at the next start).
- The ones players change most: `Loading.GameVersionCheck` (`Standard`), `Loading.VersionQuestion` (`Auto`), `Loading.TakeOver` (`true`), `WhatsNew.Show` (`true`), `Loading window.Show` (`true`), `Report.Hotkey` (`Ctrl+Shift+K`).
- All settings with defaults: [docs/settings.md](docs/settings.md); the file is `BepInEx\config\dev.sewerlabs.seapowerkeel.cfg`, made on the first start.

## For mod authors

- Your mod loads as built; list Keel as the required item (3835527459). Guide: [docs/for-mod-authors.md](docs/for-mod-authors.md).

## Building

- Needs the .NET SDK (8+) and a Sea Power install; `-p:GameDir=...` for another install, `-p:Deploy=true` copies into `StreamingAssets\SeaPowerKeel`.
- `tools/package.ps1` (or `python tools/modtool.py package keel --yes` in the workspace) stages `dist\SeaPowerKeel\` for the Workshop.

```
dotnet build src/SeaPowerKeel/SeaPowerKeel.csproj -c Release
dotnet build src/SeaPowerKeel.Stub/SeaPowerKeel.Stub.csproj -c Release
```

Offline check, self-tests and layout: [docs/building.md](docs/building.md).

## Credits and license

- [R.A.T.S. Source License](LICENSE): read it, learn from it, fork it, send fixes; no re-uploads, no reuse in other projects.
- Releases before 2026-11-18 stay MIT. agm-114's changes of 2026-11-14 (PR #3) came in under MIT and keep that notice: [lib/THIRD-PARTY.txt](lib/THIRD-PARTY.txt).
- The name and the look are not part of that. "R.A.T.S.", the name "Keel" for a Sea Power mod loader, the logo, the Workshop pictures and texts and the plugin ids `dev.sewerlabs.seapowerkeel*` belong to the author; all rights reserved.
- Keel carries on [Anchor Chain](https://github.com/SeaPower-Modders/AnchorChain) (MIT), the community chainloader Sea Power's code mods are built on, made by [JustOneOther](https://github.com/JustOneOther) and [agm-114](https://github.com/agm-114) of the [SeaPower-Modders](https://github.com/SeaPower-Modders). Its history is part of this repository, its source is in [`anchor-chain/`](anchor-chain/) under its own license ([anchor-chain/LICENSE.md](anchor-chain/LICENSE.md)).
- Keel's own code is written from scratch; it reads Anchor Chain mods through their public attributes and interface. Anchor Chain's library (`lib/`, MIT) and BepInEx keep their own licenses: [lib/THIRD-PARTY.txt](lib/THIRD-PARTY.txt).
- Code signing and privacy: [docs/code-signing-policy.md](docs/code-signing-policy.md).

*R.A.T.S. - Ratte's Assorted Tactical Systems*
