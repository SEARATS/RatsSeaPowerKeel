# R.A.T.S. Keel: how it works and takeover

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
- How to write one (BepInEx plugins, Anchor Chain mods, start early, `keel.ini` for mods with one DLL per game build): [docs/for-mod-authors.md](for-mod-authors.md).

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
off lets Alpine start, the patcher updated itself while in use.

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
