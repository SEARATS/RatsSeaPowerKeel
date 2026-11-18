# Test 2026-11-18: Anchor Chain 1.3, the switch to Keel (run 1)

- Test VM, game 0.9.3: beta copy (0.9.3-261116b) and Steam install (build 26712472). Plan: [test-plan-anchor-chain-1.3.md](test-plan-anchor-chain-1.3.md).
- Starts made: 2 of 4 (A on beta, B on public); C and D did not happen because the box never came.
- Results: 1 GREEN, 2 RED, 14 not run.

| # | Check | Result |
|---|---|---|
| 1 | Anchor Chain 1.3.0 loads the mods (beta) | GREEN: Anchor Chain 1.3.0, Radio 1.5.1 and Topographic Map 1.6.2 loaded |
| 2 | The box (beta) | RED: main menu without a box; no line of the bridge after more than a minute |
| 3 | Not now | not run: no box; nothing in BepInEx changed |
| 4 | The box (public) | RED: Cities and Borders, Pop-Out, Radio, Sensor Views and Topographic Map started under 1.3.0; no box, also after Options and back |
| 5-7 | Switch, files, Restart | not run: no box |
| 8-11 | Start C (Keel runs, Anchor Chain off, no box, errors) | not run; start B's log has no Error line |
| 12 | Restore | not run: no switch, so no Keel folder and no Keel files |
| 13 | Start D | not run |
| 14-17 | Cancel, failed download, Later, 0.8.7 | not run |

## Cause and fix

- **Cause:** the bridge waited for `LoadScreenView.Instance` to become null. The game sets it once, when the load screen is built, and never clears it, so the wait never ended.
- **Fix (run 2):** the main menu counts as up when the unit data is loaded, the start-up load screen says "Done" (the same test as Anchor Chain's preloader and Keel), the main menu view exists, and no mission or campaign scene is loaded. The bridge now logs "Waiting for the main menu", "Main menu is up", and a warning after five minutes without one.
