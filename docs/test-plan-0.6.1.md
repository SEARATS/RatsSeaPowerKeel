# Test plan 0.6.1 - BepInEx downloaded by the setup

Starts: **0 game starts** (every row is setup only, game closed). Template: `test-plan-0.5.0.md`.

## Before

- Test VM (clean game, no BepInEx) or a copy of a game folder without `winhttp.dll` and `BepInEx\`
- Package staged with `tools\package.ps1`: `dist\SeaPowerKeel` holds no `BepInEx_win_x64_*.zip`

## Rows

| # | Check | Pass when |
|---|---|---|
| 1 | Setup on a game without BepInEx, online | status says it will download BepInEx 5.4.23.2; Install shows "Downloading..." then "Done. Added BepInEx. ..."; `winhttp.dll` + `BepInEx\core` there |
| 2 | Setup on a game without BepInEx, network off | Install says it could not download, names the zip to put next to KeelSetup.exe; nothing written to the game folder; window stays usable |
| 3 | Same, `BepInEx_win_x64_5.4.23.2.zip` (official) put next to KeelSetup.exe | Install works without network |
| 4 | Same, a wrong file under that name | Install refuses ("not the official BepInEx release"), nothing written |
| 5 | Setup on a game that has BepInEx | no download, status says "no internet access" |
| 6 | Restore after row 1 | game folder as before (BepInEx removed) |

## After

- Put the game folder / VM back (`reset-vm.ps1 -Yes` or delete the copy)

## Done offline (2026-11-18, this PC)

- Download from the built setup: 637 157 bytes, SHA-256 f752ce4e... matches, ~1.2 s
- Wrong pinned hash: refused with the checksum text; missing release (404): the download text with the offline hint
- Full Install into an empty fake game folder: BepInEx core + Doorstop, stub, patcher written
