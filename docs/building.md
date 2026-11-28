# Keel: building and layout

```
dotnet build src/SeaPowerKeel/SeaPowerKeel.csproj -c Release          # -p:Deploy=true copies it into StreamingAssets\SeaPowerKeel
dotnet build src/SeaPowerKeel.Stub/SeaPowerKeel.Stub.csproj -c Release
```

- `-p:GameDir=...` for another install.
- Run `git submodule update --init` before packaging to include the [method history snapshots](method-history.md).
- `tools/package.ps1` (or `python tools/modtool.py package keel --yes` in the workspace) stages `dist\SeaPowerKeel\` for the Workshop. Page text and picture are in `workshop/`.
- `tools/offline-check` runs Keel's scanner, game-version check and planner over mod folders on disk against a game build's `Managed` folder, without the game:

```
dotnet run -c Release --project tools/offline-check -- "<Managed>" "<Sea Power>\BepInEx\core" "<steamapps>\workshop\content\1286220"
```

Pass `--game-version 0.9.3-261116` before the folders to check version rules.
Hash rules use `Seapower-Scripts.dll` from the supplied `Managed` folder.

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
| `anchor-chain/`, `lib/` | Anchor Chain's source and library (MIT) |
| `tools/` | Packaging, BepInEx download, offline check, loading-window preview |
| `docs/` | Test plans and notes, loading window, code signing policy, [for mod authors](for-mod-authors.md) |
| `workshop/` | Workshop page text and pictures |
