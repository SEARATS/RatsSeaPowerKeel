# R.A.T.S. Keel: building, layout and shared/

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
| `docs/` | Test plans and notes, loading window, code signing policy, [for mod authors](for-mod-authors.md) |
| `workshop/` | Workshop page text and pictures |

## shared/

- `shared/` is the common skeleton of the R.A.T.S. Sea Power mods, kept once in this repository. Keel itself does not use it.
- Each mod compiles these files into its own DLL as linked source, under its own `Rats.Shared` namespace. No mod references Keel's DLL, so every mod still runs under Keel or Anchor Chain alike.
- Mods that compile it in: Borders, Command Tools, Pop-Out, Radio, Sensor Views, Topo.
- A mod's csproj imports `shared/Rats.Mod.props`, which finds the folder through the workspace's `mods.json` (`-p:RatsShared=...` points at another copy).
- What it holds: the mod start (`ModEntry`), logging, the per-frame host, the hook registry, mission start and end events, the config file, the Mod Manager tick check, game-build detection, a Layers-menu entry, an Options-menu tab, and the package-script steps (`tools/RatsPackage.psm1`).
- A change there changes every mod at its next build: build and run `modtool compat` for every mod that links the file.
- File by file, and how to link a mod: [shared/README.md](../shared/README.md).
