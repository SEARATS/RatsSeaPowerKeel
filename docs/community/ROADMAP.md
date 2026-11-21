# R.A.T.S. Keel roadmap

Small wins first, every wave makes the next one easier. Each wave ships at once, with one update post. Steps marked **[promised]** are the
ones named in the plan we sent to the bigger mod teams.

This file is the plan and lives here on GitHub. It is discussed in Discord: one thread per wave in `#proposals`, and the
`#roadmap` post links here.

## Wave 0: ship what is already done

- [x] Keel's loading window counts mods with errors again (0.7.0)
- [x] No flat-map warning for mods' other-map builds on 0.9.x (0.7.0)
- [x] Anchor Chain 1.2.0's FindFile works under Keel (0.7.0)
- [ ] Install without an exe, with the by-hand and Linux steps **[promised]**
- [ ] An install video for the by-hand way
- [ ] Every release links its VirusTotal results; the GitHub release carries the same files and the source
- [ ] Every release lists a SHA-256 checksum per file, so anyone can check the download and compare it with their own build
- [ ] Answer the open Keel questions on the feedback board

## Wave 1: small wins

- [ ] Mod Manager Restart keeps the launch options (Linux loses `WINEDLLOVERRIDES`, Keel issue #4)
- [ ] Start with the first partner mods ([partner-mods](partner-mods.md)): each one runs under Keel, and its author says what it needs
- [ ] A Tester role in Discord: players who try a Keel build before it ships, named in the release post if they want
- [ ] Contributors listed in Keel's README credits and in its report window
- [ ] Log lines name their mod: a warning or error in the game's log shows which mod wrote it (from the call's stack), and a
  "Method not found" names the mod whose DLL uses that member (Keel already checks every DLL against the running build)

## Mods we start with

Who we start with (partners, wanted modders): [partner-mods.md](partner-mods.md).

## Wave 2: Keel becomes the interface

- [ ] Anchor Chain carried on inside Keel; Anchor Chain 1.3 offers the switch (draft PR #5) **[promised]**
- [ ] Conflict finder: the report names two mods patching the same game method **[promised]**
- [ ] Per-mod start and frame cost in the report; safe start (game + Keel only) **[promised]**
- [ ] Declared dependencies and a load order Keel sorts itself **[promised]**
- [ ] Early start before the load screen (Keel PR #2 by agm-114, Anchor Chain issue #32)
- [ ] Compatibility list: which known mods run on which game build under Keel, filled from Keel's reports (with consent)
- [ ] Safety check at load: the report lists what each mod's code can reach (start programs, the network, files outside
  the game and its save folder, the registry, code loaded from bytes), and Keel asks once before such a mod starts the
  first time. A .NET mod cannot be fully fenced in inside the game: Keel makes this visible, it cannot make it impossible
- [ ] After each game update a post in `#game-updates`: what changed and when Keel's update is out

## Wave 3: the stable mod API

- [ ] Game events from Keel: mission start/end, unit selected, contact detected, time compression **[promised]**
- [ ] Version adapters per game build, so a mod built for an older build keeps running; frame time measured from the start **[promised]**
  - how adapters work (pass-through, permanent, timed with retirement, Keel extras): [diagrams](diagrams.md)
- [ ] One shared settings page for all mods in the game's Options **[promised]**; agm-114's settings and UI prototypes from his own mod are the starting point
- [ ] Shared UI bar and hotkey manager **[promised]**
  - one small toolbar with an icon per mod (set by the mod) that opens that mod's window
  - a hotkey page where players set every mod's keys; mods join on their own, but it may have to cover every mod that reads keys, since clashing keys cause too much odd behaviour
- [ ] API promise: a versioned API; a part marked old keeps working for at least two game updates, announced ahead **[promised]**
- [ ] "Build on Keel" guide and one example mod; a porting checklist for moving a mod from game internals onto Keel's events **[promised]**
- [ ] Shared timestamped logging, and crash reports routed to the author, for mods that build on the API (only those can be told apart) **[promised]**

- [ ] Crash hunting mode, off by default: a memory monitor and debug harness for access-violation crashes, because a mod
  named in `Keel-crash.txt` is not always the one at fault

After Wave 3 a game update only needs a Keel update; the mods on its API keep running. The model is
[KSP Realism Overhaul](https://github.com/KSP-RO), which has given Kerbal Space Program a stable shared base for twelve years.

## On the horizon

- A code signing certificate for KeelSetup.exe

## Community

How we work, the partner mods, what went wrong in November and the topics still open: [the community folder](README.md).
