# Method history

Keel names the game methods that changed under a code mod made for an older Sea Power version.

## When it runs

- The mod's `_info.ini` `[Compatibility]` names an older game version than the running one
- Keel bundles a snapshot of a build that version covers (`lib/method-history-v1/<version>/`); it takes the newest one
- No matching snapshot: only the ordinary game-version check runs

## What it reports

- Harmony patch targets and AccessTools lookups whose method changed since that build:
  - its code or metadata
  - its callers (anything that can reach it)
  - its callees (anything it can reach)
- Several recorded builds of one version: a change is named only when it differs from every one of them
- At most 12 lines per mod, then a count
- A warning means the code changed, not that the mod breaks; Standard and Strict still decide whether the mod starts

## What it cannot see

- Targets computed at run time (`TargetMethod()`, names built in code)
- Overloads Keel cannot pick for sure
- Reflection and virtual calls beyond the method named in the IL

## Cache

- `BepInEx/cache/method-history-v1`
  - `game/`: a snapshot of the installed `Seapower-Scripts.dll`, made once per build; older ones are deleted
  - `results/`: the full check per mod DLL; results unused for 30 days are deleted
- A result is reused only while these stay the same:
  - the mod DLL (SHA-256)
  - `Seapower-Scripts.dll` (SHA-256)
  - the other DLLs in `Managed` and `BepInEx/core` (size and write time)
  - the bundled snapshots and the Keel build

## Record a Sea Power build

- Once per release or beta build players need as a baseline
- Use the **full build string** from Sea Power's log

```powershell
git submodule update --init
./tools/snapshot-methods.ps1 -BuildVersion '0.9.3-261116' -GameDir 'C:/Games/Sea Power'
```

- `-ManagedDir`: read an archived game assembly instead
- `-BinariesRepo`: write into another checkout of SEARATS/binaries
- Commit `keel/method-history-v1/<version>/<sha256>.kh.gz` in SEARATS/binaries, then move Keel's `external/binaries` pin
- The package copies the catalog to `lib/method-history-v1`; a packaged baseline also works against a newer game build

## What a snapshot holds

- Per method: its signature, a SHA-256 of its normalized IL and metadata, hashes of its callers and callees, its direct call edges
- Metadata tokens and branch offsets are normalized before hashing; recursive methods are hashed as one group
- Signatures and hashes only: game assemblies never go into the binaries repo
