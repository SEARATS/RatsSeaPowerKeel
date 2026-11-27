# Keel-crash.json

The crash as fields, for the R.A.T.S. feedback board (hub.sewerlabs.dev), so it can group reports without reading log text.

## Where and when

- `BepInEx\Keel-crash.json`, written next to `Keel-crash.txt`, always together with it:
  - by `KeelWatcher.exe` the moment the game dies without a normal quit (before the crash window can pack a report)
  - by Keel at the next start (`CrashGuard`), for the same session
- Removed again after a clean session, like `Keel-crash.txt`.
- *Save logs* and *Send report* pack it (`BepInEx/Keel-crash.json` in the zip), only when it belongs to the session in the journal.
- Code: `src/SeaPowerKeel/CrashJson.cs` (shared by Keel and the watcher), checks in the offline self-test.

## Schema 1

| Field | Type | What |
|---|---|---|
| `schema` | int | `1` |
| `exception` | string | managed exception type without namespace, e.g. `NullReferenceException` (only when the record names one) |
| `frames` | string[] | managed frames, top first, `Namespace.Type.Method` (no paths, no arguments), at most 20; always present, may be empty |
| `module` | string | native crash: the module's file name, e.g. `UnityPlayer.dll`; `unknown` when the catcher could not name it |
| `code` | string | native crash: `0xC0000005` |
| `mod` / `guid` / `modVersion` | string | the mod Keel pins the crash on (name as shown in the game) |
| `step` | string | `load` or `start` (Keel was loading or starting that mod), else `running` |
| `scene` | string | last scene |

- A field Keel does not know is left out.
- A pack (a mod without code) named only for a file it loaded is not pinned here: `mod` stays out.

## What it never holds

- No paths: a frame that still looks like one after its source spot and arguments are cut is left out.
- No user names, no arguments, no log text.
- Keel's own crash catcher frames are left out (they are on top of every caught stack).

## Example

```json
{
  "schema": 1,
  "frames": ["UnityEngine.Diagnostics.Utils.ForceCrash", "BetaMod.Hooks.Boom", "AlphaMod.Other.Run"],
  "module": "UnityPlayer.dll",
  "code": "0xC0000005",
  "mod": "R.A.T.S. Beta",
  "guid": "rats.beta",
  "modVersion": "1.2.0",
  "step": "running",
  "scene": "MainMenu"
}
```

## The board's side

- Groups by `exception` + top 3 frames outside `UnityEngine.`/`System.`/`Mono.` + `guid`, else by `module` + `code` + `guid` + `step`.
- Answers Send report with `message` (known problem, fixed in a version, new): the send window shows it under the report number.
- Before sending, the send window asks `GET /api/v1/known?mod=<mod>` (4 s timeout, only the mod's name goes out): a fix newer than
  `modVersion` shows as "Known problem, fixed in <mod> <version>: please update". Offline means no hint; Send stays possible.
