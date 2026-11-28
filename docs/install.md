# Keel: install

All the steps for the three ways. The short table is in the [README](../README.md).

Keel installs in three ways. Each one ends the same: a starter file in `BepInEx\plugins` and Keel's takeover patcher in `BepInEx\patchers`.

## KeelSetup.exe

1. Run `KeelSetup.exe` from Keel's Workshop folder, or download it from the GitHub release.
   - Steam: right-click Sea Power, Manage, Browse local files, then `..\..\workshop\content\1286220\3835527459`
   - The setup carries no other program inside: it copies the files next to it, so run it from Keel's folder.
2. It finds the game, adds BepInEx if needed, installs the starter file and the takeover patcher, and sets another loader's preloader aside.
3. **Restore** undoes it.

- Keel ships no BepInEx. When the game has none, the setup downloads the official BepInEx 5.4.23.2 from its GitHub release and checks it against a pinned hash before it unpacks anything.
- No internet on that PC: put `BepInEx_win_x64_5.4.23.2.zip` from that page next to `KeelSetup.exe`.
- It is not signed yet, so Windows may say "unknown publisher".

## By hand (no setup program)

The same result as the setup, without running any `.exe`:

1. Subscribe to Keel on the Workshop, start the game, switch Keel on in Mods, close the game.
2. BepInEx (skip if the game folder already has a `BepInEx` folder):
   - download `BepInEx_win_x64_5.4.23.2.zip` from [BepInEx's GitHub release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)
   - unzip it into the game folder, so `winhttp.dll`, `doorstop_config.ini` and `BepInEx\` sit next to `Sea Power.exe`
   - make the folder `BepInEx\plugins` if it is not there
3. Starter file: copy `SeaPowerKeel.Stub.dll` from `copy-to-BepInEx-plugins` in Keel's Workshop folder (or from the GitHub release) into `Sea Power\BepInEx\plugins`.
4. Start the game. Keel puts its takeover patcher in place itself. Anchor Chain or Alpine was installed: restart once.
5. Ctrl+Shift+K shows the report.

- Undo: delete `BepInEx\plugins\SeaPowerKeel.Stub.dll` and `BepInEx\patchers\SeaPowerKeel.Patcher.dll`.

## Linux (Steam Proton)

`KeelSetup.exe` is a Windows program: on Linux, install by hand. A player got Keel running this way (2026-11-10); we have not run it on Linux ourselves.

1. Subscribe to Keel on the Workshop, start the game, switch Keel on in Mods, close the game.
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

- Loader updates come through the Workshop. Copy the current starter file when an update requires a newer stub.
- Undo: delete `BepInEx/plugins/SeaPowerKeel.Stub.dll` and `BepInEx/patchers/SeaPowerKeel.Patcher.dll`. Remove the launch option if no other BepInEx mod needs it.

## After installing

- Mods that opt into `[Keel] Preload=` require stub 1.1.0 or later. Rerun KeelSetup or replace `BepInEx/plugins/SeaPowerKeel.Stub.dll` with the file under `copy-to-BepInEx-plugins` while the game is closed.
- The stub runs once, while the game starts, and reads the mod list then. Restart after every change to the mod list.
- Or let Keel follow: after the game's *Reload* in the Mods menu (Mods, Apply), Keel starts the code mods you switched on and takes switched-off ones out as far as the running game allows.
  - Their patches come off and a BepInEx plugin is stopped.
  - What a mod set up itself stays until a restart, as the game cannot unload code.
  - The same dialog has a *Restart* button (saves the list, closes the game and starts it again). The report window has *Restart the game* (click twice).
- If Keel was switched off at that moment, nothing loads, and switching it on in the running game does not start it: restart the game. The same goes for subscribing to, removing or reinstalling a mod, and for Steam re-downloading Keel's folder.
- If the game log says "Keel is switched off in the game's mod list", switch Keel on in Mods and restart.

## Switching from Anchor Chain or Alpine

- Nothing to delete. While Keel is on, its takeover keeps the other loader from starting (from the second start after installing Keel; see [Takeover](how-it-works.md#takeover)).
- You no longer need the Anchor Chain item: Keel ships Anchor Chain's library (MIT) and uses it when no Anchor Chain folder is switched on.
- Keel handles `[ACConfig]` files itself (checked offline; no mod in the game test used it).
- On Linux, see [Linux (Steam Proton)](#linux-steam-proton) for the Anchor Chain step.
