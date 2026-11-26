# R.A.T.S. Keel: feature detail

Everything Keel does, in full. The short list is in the [README](../README.md).



## Loading

- Finds the plugin DLLs in the game's mod folders and loads only mods that are switched on in Mods.
- Works out duplicates (newest copy wins), switched-off ids, dependencies with version ranges and conflicts, then starts the mods in dependency order, otherwise in the game's mod-list order.
- Starts each mod on its own. A mod that throws is marked and the next one starts: no endless load loop, no frozen game.
- Runs plain BepInEx plugins and Anchor Chain mods:
  - every DLL is loaded and every Anchor Chain mod created before the first one starts
  - `TriggerEntryPoint` is called through `IAnchorChainMod` (explicit implementations work)
  - `[ACConfig]` config files are prepared
  - `AnchorChainLoader.FindFile` works for mods that call it
- Gives mods that wait for the end of the start-up load screen (to add menu entries) that signal once after Keel started them.
- Uses no game assembly: one DLL for the game's current main version.
- Works on the globe beta, where Anchor Chain's own preloader does not start.
- The hand-installed part never changes: the starter file in `BepInEx\plugins` only finds and starts Keel, and updates come through the Workshop.

## Checks before loading

- Checks every DLL against the game's `Managed` assemblies. A mod built for the other game build could stop every mission from loading; Keel leaves such a DLL out and says why.
- A mod's build for the other map kind beside its matching build (`XFlat.dll` beside `X.dll`) gets a note, not a warning.
- Smaller mismatches are warnings: Harmony patches and by-name reflection lookups (also of internal classes) whose target this build no longer has. A transpiler that no longer matches its target's code is not detected.
- Keeps out a patch whose parameters do not fit the game method, because it would corrupt memory.
- Lists empty or broken pictures and sounds in mods in the report.
- Names the Euromod stage that ran (Euromod, + AC Pack, + Extended Guidance) in the report's first lines, also in *Save logs* and *Send report*.
  - With the AC Pack on, the report window has a *Turn EG on/off* button (writes the AC Pack's own switch, counts from the next start)
  - An AC Pack older than its 0.9 fix is named

## When something breaks

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
  - shows the board's answer under the number: a known problem and the version that fixes it, or a new one
  - before you send, says when the mod the crash is pinned on has a fix in a newer version ("please update"); you can still send
- Every crash also leaves `BepInEx\Keel-crash.json`: the same crash as fields (exception or native module, frames, the mod), no paths or user names ([crash-json.md](crash-json.md))
- `BepInEx\Keel-log.txt` has every log line of the game, BepInEx and all mods with the time of day and the seconds since the game started.
  - An exception gets a line saying which mod it came from, with its stack trace
  - BepInEx's own log has no times and is overwritten at every start; Keel keeps the last sessions too (`Keel-log.1.txt`, `.2.txt`, ...)

## In the main menu

- **What's new** splits off the Keel card at the bottom left of the main menu after a start where mods changed.
  - It grows upwards and pushes the Keel card above it, never over EXIT.
  - It names mods that are new, updated (with the version jump), gone, or ran last time and fail now.
  - Each comes with the first lines of what the author wrote:
    - Workshop mods: the change notes written since your last start, read from the item's public Steam changelog page, only for mods that changed (`WhatsNew.WorkshopNotes = false` switches that off)
    - else the mod's `CHANGELOG.md`, the sections above the version you had
  - Its x button closes it. A click anywhere else on it opens the full list in the report window. The card goes when a mission starts.
  - Keel remembers the loaded mods in `BepInEx\Keel-seen.txt`; the first start with it says nothing (nothing to compare with yet).
- The loading window and the summary card are described in [Loading window](#loading-window).

## Taking over from other loaders

- While Keel is switched on in Mods, it is the only loader that runs. See [Takeover](how-it-works.md#takeover).
- Other loaders' files are not touched; switch Keel off in the Mods menu and they start again.

## Loading window

While the game starts, a small window of its own shows the start-up step, the mods as Keel starts them (newest eight, with the time each took), a
bar, and, when a mod holds the game up, its name and for how long ("Starting 17/19: VopSound 1.96.29  8 s"; when a mod keeps the game busy after
it was started, "Waiting 19 s after Dynamic Campaign Mod").

- It is a Windows window on its own thread, so it keeps going while a mod blocks the game's main thread. It opens before the slow steps.
- It sits on the main menu's blank band by default.
- It belongs to the game's window: it stays above the game but not above other programs, and goes when the game is minimized. It never takes the keyboard focus.
- When every mod has started it shrinks and glides to the bottom left, above the game's version text, as a summary card ("Keel 0.1.5 started 19 mods in 40 s", the time until the game was ready, skipped mods, problems or the slowest mod).
  - A click on the card opens Keel's report, its x button closes it; it stays until then.
- Keel logs a `STARTING i/n: name` line before each mod, so `Keel-log.txt` names the mod that was running when the game stood still.
- More on where the start-up time goes and how it works: [docs/loading-window.md](loading-window.md). `tools/loading-preview` draws the window from a log without the game.

## Keel carries on Anchor Chain


- Anchor Chain is the community chainloader Sea Power's code mods are built on. Keel takes over its job and runs every Anchor Chain mod unchanged.
- It was made by [JustOneOther](https://github.com/JustOneOther) and [agm-114](https://github.com/agm-114) of the [SeaPower-Modders](https://github.com/SeaPower-Modders).
- Its whole history is part of this repository: every Anchor Chain commit, under its author's name, joined to Keel's `main` as one chain.
- Its source as of the hand-over is in [`anchor-chain/`](../anchor-chain/) and stays under its own MIT license ([anchor-chain/LICENSE.md](../anchor-chain/LICENSE.md)).
- Original project: [SeaPower-Modders/AnchorChain](https://github.com/SeaPower-Modders/AnchorChain), [Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757).

## Compatibility

| | Status |
|---|---|
| Game versions | `_info.ini`: 0.9.0 up to but not including 0.10.0 |
| Run in the game | the 3D globe build, every release on the then current build |
| Last full run | 0.6.0 in the test VM on game 0.9.3, public and beta (2026-11-18): setup and Restore, cards, report, What's new, two missions, a game ended from outside and the start after it; every row green |
| Loaders | Keel runs Anchor Chain mods and BepInEx plugins; while it is on, Anchor Chain's and Alpine's preloaders do not start |
| Linux | one player's report (2026-11-10); not run by us |

- The Anchor Chain compatibility changes in the CHANGELOG's "Unreleased" are checked offline only; they have not been run in the game.
