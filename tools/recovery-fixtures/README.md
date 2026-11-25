# Recovery fixtures

Disposable game tests for caught startup failures and runtime compatibility errors.
These plugins deliberately log errors. Never ship or enable them in normal play.

Build `Fixtures.csproj` and `Prepare/Prepare.csproj` with `-p:GameDir=<Sea Power>`.
Run Prepare with the built fixture DLL and a separate output DLL path. It changes
references in one unused method to an absent game type, giving the real preflight
check a missing type and method without changing any game assembly.

Install only the prepared DLL in a disposable enabled mod folder with a compatible
`_info.ini`. Use `GameVersionCheck=Standard` and `RemoveBrokenPatches=true`. The test
starts six plugins from one DLL and writes `KEEL_RECOVERY PASS` after checking:

- A Harmony failure the plugin catches and logs itself marks it with errors; its patch
  and component stay.
- The same failure escaping `Awake` marks the plugin failed, removes its newly
  installed patch and stops its component.
- A healthy sibling keeps its patches on both the failed startup plugin's target and the runtime missing-type target.
- An ordinary error log marks its plugin with errors without rolling it back.
- Missing-type and missing-method errors each occur once. The failing patch comes
  off, so the second invocation succeeds. These exceptions deliberately reproduce
  the report's error formats; they are not a reproduction of the native crash.

An unchanged 0.8.1 loader writes `KEEL_RECOVERY BASELINE`, with the uncaught failure's
startup patch still installed and both runtime exceptions occurring twice. Compare its
report with the updated loader's report, then remove the fixture and restore the
original mod selections, config and installed loader.

`GameFrames.csproj` is a separate fixture DLL. Prepare it the same way and install
it alongside the first. Load a disposable mission to exercise `GameMain.LateUpdate`.
Its transpiler throws inline, so the exception stack contains only game and Unity
frames. Keel should identify this DLL from its preflight findings and installed
patch, remove the patch after one error, and report `KEEL_GAME_FRAMES PASS`. The
verifier waits for the mission error before checking; the target does not run on
the main menu.
