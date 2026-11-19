# Test 2026-11-18 - KeelSetup downloads BepInEx (plan: test-plan-0.6.1.md)

- **Where**: test sandbox VM (`SeaPowerSandbox`), 22:2x; build of commit 68dc53b (`tools\package.ps1`, `dist\SeaPowerKeel`, no BepInEx zip in it)
- **How**: fake game folders (a `Sea Power.exe` only) under `C:\sandbox\in\keel-bepinex\games`; a probe exe next to `KeelSetup.exe`
  calls the same methods as the Install and Restore buttons (`GetBepInExZip`, `SetupCore.Install` / `Restore`). The VM's Sea Power
  installs, Steam and the game were not touched
- **Game starts**: 0

| # | Check | Result | Seen |
|---|---|---|---|
| 1 | Game without BepInEx, online | GREEN | download 637 157 bytes; "Added BepInEx. Installed Keel's starter file. Installed Keel's takeover"; HasBepInEx, HasStub, CanRestore true; 25 files |
| 2 | Network adapter off | GREEN | refused: "could not be downloaded from GitHub (The remote name could not be resolved: 'github.com')" + offline hint; game folder untouched (1 file) |
| 3 | Network off, official zip next to KeelSetup.exe | GREEN | zip used (637 157 bytes), install done |
| 4 | Wrong file under the zip name | GREEN | refused: "is not the official BepInEx release. Delete it, and Install downloads the right one."; folder untouched |
| 5 | Game that has BepInEx | GREEN | no download (0 bytes), only starter file + takeover installed |
| 6 | Restore after row 1 | GREEN | only `Sea Power.exe` left, CanRestore false |
| - | Network back after the run | GREEN | adapter Up, github.com 200 |

## Not covered

- The window itself (status lines, the "Downloading..." text, buttons greyed during the download): not clicked in this run; the
  texts are the ones checked here, the UI code is plain
- A real game start after a setup that downloaded BepInEx: the downloaded zip is byte-identical to the one 0.6.0 shipped and tested
  (same SHA-256), so what lands in the game folder is unchanged

## Put back

- Test folder deleted in the VM, adapter on, `.game-lock` removed
