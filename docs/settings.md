# R.A.T.S. Keel: settings

`BepInEx\config\dev.sewerlabs.seapowerkeel.cfg` (made on the first start):

| Setting | Default | What |
|---|---|---|
| `Loading.AnchorChainMods` | `true` | Start Anchor Chain mods (Keel runs them itself) |
| `Loading.TakeOver` | `true` | While Keel is on, other mod loaders do not start ([Takeover](how-it-works.md#takeover)); `false`: an installed other loader starts its mods and Keel leaves those to it |
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
| `Loading window.Show` | `true` | The loading window ([docs/loading-window.md](loading-window.md)) |
| `Loading window.Spot` | `TopRight` | `Marker` (the blank band under the main menu's news box), `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` of the game's window |
| `Loading window.Scale` | `0` | Size; 0 = by the game window's height (1.5 on a 4K screen) |
| `Loading window.Opacity` | `0.92` | 0.4 to 1 |
| `Loading window.SecondsAfterLastMod` | `2` | The full window stays this long after every mod has started, then glides to the corner |
| `Loading window.LongestSeconds` | `300` | The full window is done this long after the game process started, whatever happens |
| `Loading window.ClickHides` | `true` | A click hides the full window; a click on the summary opens Keel's report, its x button collapses the summary to a Keel report button |
