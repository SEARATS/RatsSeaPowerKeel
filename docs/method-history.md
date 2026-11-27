# Method history

When a code mod's `_info.ini` declares compatibility with an older Sea Power version, Keel uses the latest bundled snapshot covered by that declaration. It compares methods in the installed `Seapower-Scripts.dll` with the snapshot and reports changes to the methods the mod patches or looks up. A warning can name a changed method body, callers that can reach the method, or methods it can reach. If the bundled release has multiple recorded builds, Keel reports a category when it detects the change against each of those builds.

Keel resolves statically declared Harmony patches and unambiguous method lookups the existing preflight recognizes. Computed targets, unresolved overloads, reflective dispatch, and virtual dispatch beyond the IL's named target can escape the scan. A warning means the code changed. The existing Standard and Strict rules still decide whether to start the mod. If there is no matching historical snapshot, Keel uses its ordinary compatibility checks.

Keel reads the bundled history offline. It builds an installed-game snapshot once per game assembly SHA-256 as needed. For a comparison, it caches the full preflight result and the method warnings in `BepInEx/cache/method-history-v1`. The cache key includes the mod DLL's SHA-256, the installed game's version and managed DLL hashes, the BepInEx core DLL hashes, the bundled comparison data, and the Keel build.

## Record a Sea Power build

Initialize the binaries submodule, then run the snapshot script with the **full build string** shown in Sea Power's log. Run it for each release or beta build whose users need a baseline.

```powershell
git submodule update --init
./tools/snapshot-methods.ps1 -BuildVersion '0.9.3-261116' -GameDir 'C:/Games/Sea Power'
```

Pass `-ManagedDir` to read an archived game assembly. Pass `-BinariesRepo` to write into a separate checkout of SEARATS/binaries. Review and commit the generated `keel/method-history-v1/<version>/<sha256>.kh.gz` in that repository, then update Keel's `external/binaries` pin for the next package. The distribution script copies the catalog under `lib/method-history-v1`. An already packaged baseline works against a new locally installed game build.

Each entry contains the method signature, a SHA-256 of its normalized IL and method metadata, hashes of its transitive callers and callees, and its direct method-reference edges. The scanner normalizes metadata references and branch positions before hashing and groups recursive methods before hashing each direction. The snapshots contain signatures, edges, and digests. Keep the game assemblies out of the binaries repository.
