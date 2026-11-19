# Sea Power: R.A.T.S. Keel

*R.A.T.S. - Reconnaissance, Acoustics, Terrain & Signals*

Keel is the mod loader for Sea Power's code mods. It finds the mod DLLs, checks them against your game version, starts them
and tells you what happened. It runs Anchor Chain mods and plain BepInEx plugins as they are.

- Workshop: [R.A.T.S. Keel](https://steamcommunity.com/sharedfiles/filedetails/?id=3835527459)
- Bugs, ideas, questions: [R.A.T.S. feedback board](https://hub.sewerlabs.dev/s/keel)
- Keel is the loader itself, so it needs no other one. Other mods need *a mod loader*: Keel or Anchor Chain, either one.

## Keel carries on Anchor Chain

- Anchor Chain is the community chainloader Sea Power's code mods are built on. Keel takes over its job and runs every Anchor Chain mod unchanged.
- It was made by [JustOneOther](https://github.com/JustOneOther) and [agm-114](https://github.com/agm-114) of the [SeaPower-Modders](https://github.com/SeaPower-Modders).
- Its whole history is part of this repository: every Anchor Chain commit, under its author's name, joined to Keel's `main` as one chain.
- Its source as of the hand-over is in [`anchor-chain/`](anchor-chain/) and stays under its own MIT license ([anchor-chain/LICENSE.md](anchor-chain/LICENSE.md)).
- Original project: [SeaPower-Modders/AnchorChain](https://github.com/SeaPower-Modders/AnchorChain), [Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757).

## What it does

### Loading

- Finds the plugin DLLs in the game's mod folders and loads only mods that are switched on in Mods.
- Works out duplicates (newest copy wins), switched-off ids, dependencies with version ranges and conflicts, then starts the mods in dependency order, otherwise in the game's mod-list order.
- Starts each mod on its own. A mod that throws is marked and the next one starts: no endless load loop, no frozen game.
- Runs plain BepInEx plugins and Anchor Chain mods:
  - every DLL is loaded and every Anchor Chain mod created before the first one starts
  - `TriggerEntryPoint` is called through `IAnchorChainMod` (explicit implementations work)
  - `[ACConfig]` config files are prepared
  - `AnchorChainLoader.FindFile` works for mods that call it
- Gives mods that wait for the end of the start-up load screen (to add menu entries) that signal once after Keel started them.
- Uses no game assembly, so one DLL serves both game builds (the flat map of 0.8.7 and the 3D globe build).
- Works on the globe beta, where Anchor Chain's own preloader does not start.
- The hand-installed part never changes: the starter file in `BepInEx\plugins` only finds and starts Keel, and updates come through the Workshop.

### Checks before loading

- Checks every DLL against the game's `Managed` assemblies. A mod built for the other game build could stop every mission from loading; Keel leaves such a DLL out and says why.
- A mod's build for the other map kind beside its matching build (`XFlat.dll` beside `X.dll`) gets a note, not a warning.
- Smaller mismatches are warnings: Harmony patches and by-name reflection lookups (also of internal classes) whose target this build no longer has. A transpiler that no longer matches its target's code is not detected.
- Keeps out a patch whose parameters do not fit the game method, because it would corrupt memory.
- Lists empty or broken pictures and sounds in mods in the report.
- Names the Euromod stage that ran (Euromod, + AC Pack, + Extended Guidance) in the report's first lines, also in *Save logs* and *Send report*.
  - With the AC Pack on, the report window has a *Turn EG on/off* button (writes the AC Pack's own switch, counts from the next start)
  - An AC Pack older than its 0.9 fix is named

### When something breaks

- Ctrl+Shift+K shows every mod: running, running with errors, not started and why (missing dependency and the version it needs, a conflict, the wrong game build). The same report is in `BepInEx\Keel-report.txt`.
- Errors a mod throws later are counted against it. A short notice appears after start-up when something went wrong.
- Keel journals every step to disk, because a crash in native code ends the game and nothing inside the process can catch it.
  - If the game dies while Keel loads or starts a mod, or a crash report shows a mod's code on its stack, the next start names that mod and keeps it out until the mod is updated (`BepInEx\Keel-held.txt`, details in `BepInEx\Keel-crash.txt`).
  - If the game was loading a mod's file (a picture or sound) just before the crash, the window names that mod.
  - A crash with no mod code on its stack and no mod file loading just before it is reported, but cannot be pinned on a mod.
- `KeelWatcher.exe` (in Keel's folder, nothing to install) shows a window the moment the game dies.
  - It names the mod when its code was running, has a tick box to keep it switched off for now, and offers a restart.
  - The report window's *Let held mods run* releases a held mod again.
  - The game itself cannot be kept alive.
- *Save logs* (crash window and report window) makes one zip of all logs on your Desktop, ready for a bug report. The logs contain file paths, so also your Windows user name.
- *Send report*, next to *Save logs*:
  - packs the same logs with your Windows user name replaced by `%USERNAME%` in every path
  - shows the file list and asks *Send logs to the R.A.T.S. developer?*, with room for a short note
  - sends only on *Send*, to the R.A.T.S. feedback board, where only the developer can open it
  - gives you a report number to mention in a post; no account needed
- `BepInEx\Keel-log.txt` has every log line of the game, BepInEx and all mods with the time of day and the seconds since the game started.
  - An exception gets a line saying which mod it came from, with its stack trace
  - BepInEx's own log has no times and is overwritten at every start; Keel keeps the last sessions too (`Keel-log.1.txt`, `.2.txt`, ...)

### In the main menu

- **What's new** splits off the Keel card at the bottom left of the main menu after a start where mods changed.
  - It grows upwards and pushes the Keel card above it, never over EXIT.
  - It names mods that are new, updated (with the version jump), gone, or ran last time and fail now.
  - Each comes with the first lines of what the author wrote:
    - Workshop mods: the change notes written since your last start, read from the item's public Steam changelog page, only for mods that changed (`WhatsNew.WorkshopNotes = false` switches that off)
    - else the mod's `CHANGELOG.md`, the sections above the version you had
  - Its × closes it. A click anywhere else on it opens the full list in the report window. The card goes when a mission starts.
  - Keel remembers the loaded mods in `BepInEx\Keel-seen.txt`; the first start with it says nothing (nothing to compare with yet).
- The loading window and the summary card are described in [Loading window](#loading-window).

### Taking over from other loaders

- While Keel is switched on in Mods, it is the only loader that runs. See [Takeover](#takeover).
- Other loaders' files are not touched; switch Keel off in the Mods menu and they start again.

## Install

Keel installs in three ways. Each one ends the same: a starter file in `BepInEx\plugins` and Keel's takeover patcher in `BepInEx\patchers`.

| Way | For | Needs |
|---|---|---|
| [KeelSetup.exe](#keelsetupexe) | Windows | the Workshop folder or the GitHub release |
| [By hand](#by-hand-no-setup-program) | Windows, without running any `.exe` | BepInEx 5.4.23.2 zip |
| [Linux](#linux-steam-proton) | Steam Proton | BepInEx 5.4.23.2 zip, a launch option |

### KeelSetup.exe

1. Run `KeelSetup.exe` from Keel's Workshop folder, or download it from the GitHub release.
   - Steam: right-click Sea Power, Manage, Browse local files, then `..\..\workshop\content\1286220\3835527459`
   - The setup carries no other program inside: it copies the files next to it, so run it from Keel's folder.
2. It finds the game, adds BepInEx if needed, installs the starter file and the takeover patcher, and sets another loader's preloader aside.
3. **Restore** undoes it.

- Keel ships no BepInEx. When the game has none, the setup downloads the official BepInEx 5.4.23.2 from its GitHub release and checks it against a pinned hash before it unpacks anything.
- No internet on that PC: put `BepInEx_win_x64_5.4.23.2.zip` from that page next to `KeelSetup.exe`.
- It is not signed yet, so Windows may say "unknown publisher".

### By hand (no setup program)

The same result as the setup, without running any `.exe`:

1. Subscribe to R.A.T.S. Keel on the Workshop, start the game, switch Keel on in Mods, close the game.
2. BepInEx (skip if the game folder already has a `BepInEx` folder):
   - download `BepInEx_win_x64_5.4.23.2.zip` from [BepInEx's GitHub release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
   - unzip it into the game folder, so `winhttp.dll`, `doorstop_config.ini` and `BepInEx\` sit next to `Sea Power.exe`
   - make the folder `BepInEx\plugins` if it is not there
3. Starter file: copy `SeaPowerKeel.Stub.dll` from `copy-to-BepInEx-plugins` in Keel's Workshop folder (or from the GitHub release) into `Sea Power\BepInEx\plugins`.
4. Start the game. Keel puts its takeover patcher in place itself. Anchor Chain or Alpine was installed: restart once.
5. Ctrl+Shift+K shows the report.

- Undo: delete `BepInEx\plugins\SeaPowerKeel.Stub.dll` and `BepInEx\patchers\SeaPowerKeel.Patcher.dll`.

### Linux (Steam Proton)

`KeelSetup.exe` is a Windows program: on Linux, install by hand. A player got Keel running this way (2026-11-10); we have not run it on Linux ourselves.

1. Subscribe to R.A.T.S. Keel on the Workshop, start the game, switch Keel on in Mods, close the game.
2. BepInEx (skip if the game folder already has a `BepInEx` folder):
   - download `BepInEx_win_x64_5.4.23.2.zip` from [BepInEx's GitHub release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2) - the Windows build, the game runs through Proton
   - unzip it into the game folder, so `winhttp.dll`, `doorstop_config.ini` and `BepInEx/` sit next to `Sea Power.exe`
   - make the folder `BepInEx/plugins` if it is not there
3. Starter file: copy `SeaPowerKeel.Stub.dll` from `copy-to-BepInEx-plugins` in Keel's Workshop folder (`steamapps/workshop/content/1286220/3835527459`) into `BepInEx/plugins`.
4. Launch option: Steam, right-click Sea Power, Properties, Launch Options: `WINEDLLOVERRIDES="winhttp=n,b" %command%`
   - without it Proton never loads BepInEx
5. Anchor Chain (only if you had it): switch it off in the game's Mods menu and unsubscribe from it on the Workshop.
   - Switched off in the Mods menu alone, Anchor Chain kept loading its mods for that player.
   - Keel's takeover should make this unneeded now; not checked on Linux.
6. Start the game. Anchor Chain or Alpine was installed: restart once.
7. Ctrl+Shift+K shows Keel's report.

- Updates come through the Workshop as on Windows; the hand-copied starter file never needs replacing.
- Undo: delete `BepInEx/plugins/SeaPowerKeel.Stub.dll` and `BepInEx/patchers/SeaPowerKeel.Patcher.dll`. Remove the launch option if no other BepInEx mod needs it.

### After installing

- The stub runs once, while the game starts, and reads the mod list then. Restart after every change to the mod list.
- Or let Keel follow: after the game's *Reload* in the Mods menu (Mods, Apply), Keel starts the code mods you switched on and takes switched-off ones out as far as the running game allows.
  - Their patches come off and a BepInEx plugin is stopped.
  - What a mod set up itself stays until a restart, as the game cannot unload code.
  - The same dialog has a *Restart* button (saves the list, closes the game and starts it again). The report window has *Restart the game* (click twice).
- If Keel was switched off at that moment, nothing loads, and switching it on in the running game does not start it: restart the game. The same goes for subscribing to, removing or reinstalling a mod, and for Steam re-downloading Keel's folder.
- If the game log says "R.A.T.S. Keel is switched off in the game's mod list", switch Keel on in Mods and restart.

### Switching from Anchor Chain or Alpine

- Nothing to delete. While Keel is on, its takeover keeps the other loader from starting (from the second start after installing Keel; see [Takeover](#takeover)).
- You no longer need the Anchor Chain item: Keel ships Anchor Chain's library (MIT) and uses it when no Anchor Chain folder is switched on.
- Keel handles `[ACConfig]` files itself (checked offline; no mod in the game test used it).
- On Linux, see [Linux (Steam Proton)](#linux-steam-proton) for the Anchor Chain step.

## Settings

`BepInEx\config\dev.sewerlabs.seapowerkeel.cfg` (made on the first start):

| Setting | Default | What |
|---|---|---|
| `Loading.AnchorChainMods` | `true` | Start Anchor Chain mods (Keel runs them itself) |
| `Loading.TakeOver` | `true` | While Keel is on, other mod loaders do not start ([Takeover](#takeover)); `false`: an installed other loader starts its mods and Keel leaves those to it |
| `Loading.GameVersionCheck` | `Standard` | `Standard`: leave out DLLs that would stop missions from loading, warn about the rest; `Strict`: leave out any DLL with a missing part (also a missing Harmony patch target or by-name lookup); `Off` |
| `Loading.RemoveBrokenPatches` | `true` | When the game throws 'Method not found' / 'Field not found' because a mod's Harmony patch uses something this build lacks, Keel takes that mod's patch off the failing game method again and names the mod; `false`: Keel only names the mod |
| `Loading.HoldOutCrashers` | `true` | Keep out a mod that took the game down, until it is updated; `false`: Keel only names it |
| `Loading.DisabledPlugins` | empty | Plugin ids Keel does not start although their folder is on |
| `Logging.TimestampedLog` | `true` | Write `BepInEx\Keel-log.txt` with times and the mod each exception came from |
| `Logging.KeepSessions` | `3` | Earlier sessions' logs to keep |
| `Report.Hotkey` | `Ctrl+Shift+K` | Report window |
| `Report.NoticeOnProblems` | `true` | Notice after start-up when a mod did not start or threw |
| `WhatsNew.Show` | `true` | The What's new card after a start where mods changed |
| `WhatsNew.WorkshopNotes` | `true` | Read updated Workshop mods' change notes from their public Steam changelog page (only for mods that changed); off: `CHANGELOG.md` or just the version |
| `WhatsNew.Seconds` | `0` | How long the card stays; 0 = until clicked or a mission starts |
| `Loading window.Show` | `true` | The loading window ([docs/loading-window.md](docs/loading-window.md)) |
| `Loading window.Spot` | `TopRight` | `Marker` (the blank band under the main menu's news box), `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` of the game's window |
| `Loading window.Scale` | `0` | Size; 0 = by the game window's height (1.5 on a 4K screen) |
| `Loading window.Opacity` | `0.92` | 0.4 to 1 |
| `Loading window.SecondsAfterLastMod` | `2` | The full window stays this long after every mod has started, then glides to the corner |
| `Loading window.LongestSeconds` | `300` | The full window is done this long after the game process started, whatever happens |
| `Loading window.ClickHides` | `true` | A click hides the full window; a click on the summary opens Keel's report, its × closes it |

## Loading window

While the game starts, a small window of its own shows the start-up step, the mods as Keel starts them (newest eight, with the time each took), a
bar, and, when a mod holds the game up, its name and for how long ("Starting 17/19: VopSound 1.96.29  8 s"; when a mod keeps the game busy after
it was started, "Waiting 19 s after Dynamic Campaign Mod").

- It is a Windows window on its own thread, so it keeps going while a mod blocks the game's main thread. It opens before the slow steps.
- It sits on the main menu's blank band by default.
- It belongs to the game's window: it stays above the game but not above other programs, and goes when the game is minimized. It never takes the keyboard focus.
- When every mod has started it shrinks and glides to the bottom left, above the game's version text, as a summary card ("Keel 0.1.5 started 19 mods in 40 s", the time until the game was ready, skipped mods, problems or the slowest mod).
  - A click on the card opens Keel's report, its × closes it; it stays until then.
- Keel logs a `STARTING i/n: name` line before each mod, so `Keel-log.txt` names the mod that was running when the game stood still.
- More on where the start-up time goes and how it works: [docs/loading-window.md](docs/loading-window.md). `tools/loading-preview` draws the window from a log without the game.

## How it works

1. The **stub** (`BepInEx\plugins\SeaPowerKeel.Stub.dll`) reads the game's mod list from `usersettings.ini`, finds `SeaPowerKeel.dll` in an enabled mod folder (newest version wins) and starts it.
2. Keel waits for the game's own mod list (on the globe build also for the end of the start-up load screen, as mods started during it can keep the unit data from loading).
3. It reads the plugin attributes of every DLL in the enabled folders without loading them (Mono.Cecil, which BepInEx ships), and checks each DLL against the game's `Managed` assemblies.
4. Duplicates, plugins already running from `BepInEx\plugins`, switched-off ids, dependencies and conflicts are worked out until nothing changes.
5. Every DLL is loaded and every Anchor Chain mod created. A mod that cannot be loaded, or has no `IAnchorChainMod.TriggerEntryPoint`, is FAILED and takes the mods that need it with it.
6. Only then the rest starts, in dependency order, otherwise in the game's mod-list order.

For mod authors: your mod loads as built, nothing to recompile or add.

- List R.A.T.S. Keel as your Workshop item's required item (3835527459). Keel brings Anchor Chain's library, so your players need no Anchor Chain item.
- When a game update breaks a mod, Keel's report names the missing method, field or type.
- How to write one (BepInEx plugins, Anchor Chain mods, start early, `keel.ini` for mods with one DLL per game build): [docs/for-mod-authors.md](docs/for-mod-authors.md).

## Takeover

While R.A.T.S. Keel is switched on in the game's Mods menu, it is the only mod loader that runs. Other loaders that are still
installed (Anchor Chain's preloader, Alpine's Mod Loader) are kept from starting, so every mod is started once, by Keel.

- **How:** Keel keeps a small BepInEx patcher, `BepInEx\patchers\SeaPowerKeel.Patcher.dll`. BepInEx runs patchers before any
  plugin; Keel's takes the other loaders out of the list of plugins BepInEx starts. It changes no game file and patches no
  game code.
- **Their files stay as they are.** Nothing is renamed, moved or deleted, and their code is not read: they are recognized by
  their plugin id. Switch Keel off in the Mods menu (or set `Loading.TakeOver = false`) and they start again at the next start.
- **Keel installs and updates the patcher itself** from its Workshop folder: nothing to copy by hand. A patcher in use is set aside as `.old` and replaced for the next start.
- **The first start after the update** still has the other loader running: Keel leaves that loader's mods to it once, so
  nothing starts twice, and says *restart the game once*. From the next start on Keel runs everything.
- **The report says what happened:** `BepInEx\Keel-takeover.txt` (written by the patcher at every start) and a line in Keel's
  report name the loader that was kept from starting.
- If the game folder cannot be written to, Keel says so; run `KeelSetup.exe` then.

Run in the game on 0.9.1 (2026-11-12): Alpine kept from starting with its file back in `BepInEx\plugins`, Keel switched
off lets Alpine start, the patcher updated itself while in use. Notes: [docs/test-2026-11-12-takeover.md](docs/test-2026-11-12-takeover.md).

## Differences from Anchor Chain's loader

Keel is meant to run the same mods. Where it deliberately does not behave like Anchor Chain 1.1.0 / 1.2.0, the report says so:

- Only the exact `AnchorChain.dll` and `AnchorChain.Preloader.dll` are left out (and Keel's own DLLs). Every DLL left out is
  listed with the reason in the report. A mod called `AnchorChainSomething.dll` is scanned like any other.
- Only public classes count as Anchor Chain plugins, as in Anchor Chain (`GetExportedTypes`); a non-public plugin class is listed as SKIPPED with that reason.
- `[ACIncompatibility]`:
  - Anchor Chain drops every mod that declares one, whether or not the named mod is installed (its `toRemove.Add` sits outside the `if`, in the released 1.1.0 and in the current source).
  - Keel skips a mod only when a named mod is really there. A mod that starts only because of this carries a warning in the report.
- `before` / `after` only order the start; an absent plugin named there does not keep the mod from starting (Anchor Chain treats them as dependencies).
- Switched-off mod folders are not loaded. Anchor Chain loads the DLLs of every folder the game lists, switched on or not; mods that check the Mod Manager themselves, like Euromod's, behave the same either way.
- `[ACConfig]` works like Anchor Chain's: default `<id>.ini` in the mod folder, the player's `<id>_user.ini` in `StreamingAssets\ACConfigs`, missing keys merged in, `[AnchorChain.ResetValues]` applied once.
  - The game's `IniHandler` is used by reflection; it has everything needed on 0.8.7, the globe build 26223125 and 261107 (`tools/offline-check --inicheck`).
  - Should a build lack a member, Keel copies the default file, says what it could not do, and starts the mod.
- `AnchorChainLoader.FindFile` (Euromod's Sonar Audio Loader uses it for audio in other mods' folders) works: Keel provides the one interface of Anchor Chain's preloader that Anchor Chain's DLL needs to load that class, and fills the class's folder list.

## Compatibility

| | Status |
|---|---|
| Game versions | `_info.ini`: 0.8.7 up to but not including 0.10.0 |
| Run in the game | the flat map (0.8.7, up to Keel 0.1.0) and the 3D globe build (every release since, on the then current build); no more 0.8.7 tests since 2026-11-08 |
| Last full run | 0.6.0 in the test VM on game 0.9.3, public and beta (2026-11-18): setup and Restore, cards, report, What's new, two missions, a game ended from outside and the start after it; every row green |
| Loaders | Keel runs Anchor Chain mods and BepInEx plugins; while it is on, Anchor Chain's and Alpine's preloaders do not start |
| Linux | one player's report (2026-11-10); not run by us |

- Test notes: [docs/test-2026-11-18-0.6.0.md](docs/test-2026-11-18-0.6.0.md), [docs/test-2026-11-07.md](docs/test-2026-11-07.md), [docs/test-2026-11-08-globe.md](docs/test-2026-11-08-globe.md).
- The Anchor Chain compatibility changes in the CHANGELOG's "Unreleased" are checked offline only; they have not been run in the game.

## Feedback

- [R.A.T.S. feedback board](https://hub.sewerlabs.dev/s/keel): bugs, ideas, questions.
- In the game, *Send report* sends your logs to the developer (see above).

## Building

```
dotnet build src/SeaPowerKeel/SeaPowerKeel.csproj -c Release          # -p:Deploy=true copies it into StreamingAssets\SeaPowerKeel
dotnet build src/SeaPowerKeel.Stub/SeaPowerKeel.Stub.csproj -c Release
```

- `-p:GameDir=...` for another install.
- `tools/package.ps1` (or `python tools/modtool.py package keel --yes` in the workspace) stages `dist\SeaPowerKeel\` for the Workshop. Page text and picture are in `workshop/`.
- `tools/offline-check` runs Keel's scanner, game-version check and planner over mod folders on disk against a game build's `Managed` folder, without the game:

```
dotnet run -c Release --project tools/offline-check -- "<Managed>" "<Sea Power>\BepInEx\core" "<steamapps>\workshop\content\1286220"
```

Keel's own tests (the scanner, entry point, start order, `[ACConfig]`, Anchor Chain hand-over ... on stand-in mods and a stand-in `IniHandler`; no game needed), and the check that the game's real `IniHandler` has what `[ACConfig]` needs (one build per run):

```
dotnet run -c Release --project tools/offline-check -- --selftest "<Managed>" "<Sea Power>\BepInEx\core"
dotnet run -c Release --project tools/offline-check -- --inicheck "<Managed>"
```

## Layout

| Path | What |
|---|---|
| `src/SeaPowerKeel/` | The loader (`SeaPowerKeel.dll`) |
| `src/SeaPowerKeel.Stub/` | The starter file for `BepInEx\plugins` |
| `src/SeaPowerKeel.Patcher/` | The takeover patcher |
| `src/SeaPowerKeel.Setup/` | `KeelSetup.exe` |
| `src/SeaPowerKeel.Watcher/` | `KeelWatcher.exe`, the crash window |
| `shared/` | Source that the other R.A.T.S. mods compile in, see below |
| `anchor-chain/`, `lib/` | Anchor Chain's source and library (MIT) |
| `tools/` | Packaging, BepInEx download, offline check, loading-window preview |
| `docs/` | Test plans and notes, loading window, code signing policy, [for mod authors](docs/for-mod-authors.md) |
| `workshop/` | Workshop page text and pictures |

### shared/

- `shared/` is the common skeleton of the R.A.T.S. Sea Power mods, kept once in this repository. Keel itself does not use it.
- Each mod compiles these files into its own DLL as linked source, under its own `Rats.Shared` namespace. No mod references Keel's DLL, so every mod still runs under Keel or Anchor Chain alike.
- Mods that compile it in: Borders, Command Tools, Pop-Out, Radio, Sensor Views, Topo.
- A mod's csproj imports `shared/Rats.Mod.props`, which finds the folder through the workspace's `mods.json` (`-p:RatsShared=...` points at another copy).
- What it holds: the mod start (`ModEntry`), logging, the per-frame host, the hook registry, mission start and end events, the config file, the Mod Manager tick check, game-build detection, a Layers-menu entry, an Options-menu tab, and the package-script steps (`tools/RatsPackage.psm1`).
- A change there changes every mod at its next build: build and run `modtool compat` for every mod that links the file.
- File by file, and how to link a mod: [shared/README.md](shared/README.md).

## Licenses and credits

- [R.A.T.S. Source License](LICENSE): read it, learn from it, fork it, send fixes; no re-uploads, no reuse in other projects.
- Releases before 2026-11-18 stay MIT. agm-114's changes of 2026-11-14 (PR #3) came in under MIT and keep that notice: [lib/THIRD-PARTY.txt](lib/THIRD-PARTY.txt).
- The name and the look are not part of that. "R.A.T.S.", "R.A.T.S. Keel", the logo, the Workshop pictures and texts and the plugin ids `dev.sewerlabs.seapowerkeel*` belong to the author; all rights reserved.
- Anchor Chain's library (`lib/`, MIT) and BepInEx keep their own licenses: [lib/THIRD-PARTY.txt](lib/THIRD-PARTY.txt).
- Keel's own code is written from scratch. Anchor Chain (SeaPower-Modders: JustOneOther, agm-114; MIT) is the community chainloader Sea Power code mods are built on; Keel reads Anchor Chain mods through their public attributes and interface so players can use both.
- Code signing and privacy: [docs/code-signing-policy.md](docs/code-signing-policy.md).

*R.A.T.S. - Ratte's Assorted Tactical Systems*
