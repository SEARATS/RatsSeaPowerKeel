# R.A.T.S. Keel roadmap

Small wins first, each one shippable on its own; every step makes the next one easier. Steps marked **[promised]** are the
ones named in the plan we sent to the bigger mod teams.

This file is the plan: it lives here on GitHub and in the Discord `#roadmap` channel, and both are updated together.

## Wave 0: ship what is already done

| # | Step |
|---|---|
| 0.1 | Keel's loading window counts mods with errors again |
| 0.2 | No flat-map warning for mods' other-map builds on 0.9.x |
| 0.3 | Anchor Chain 1.2.0's FindFile works under Keel |
| 0.4 | Install without an exe, with the by-hand and Linux steps **[promised]** |
| 0.5 | An install video for the by-hand way |
| 0.6 | Answer the open Keel questions on the feedback board |

## Wave 1: small wins

| # | Step |
|---|---|
| 1.1 | Mod Manager Restart keeps the launch options (Linux loses `WINEDLLOVERRIDES`, Keel issue #4) |
| 1.2 | Get Euromod's feedback on the plan (the message is out): their needs set the order of Wave 2 and 3 |

## Wave 2: Keel becomes the interface

| # | Step | **[promised]** item |
|---|---|---|
| 2.1 | Anchor Chain carried on inside Keel; Anchor Chain 1.3 offers the switch (draft PR #5) | Keel carries on Anchor Chain |
| 2.2 | Conflict finder: the report names two mods patching the same game method | conflict finder |
| 2.3 | Per-mod start and frame cost in the report; safe start (game + Keel only) | performance cost, safe start |
| 2.4 | Declared dependencies and a load order Keel sorts itself | dependencies and load order |
| 2.5 | Early start before the load screen (Keel PR #2 by agm-114, Anchor Chain issue #32) | |

## Wave 3: the stable mod API

| # | Step | **[promised]** item |
|---|---|---|
| 3.1 | Game events from Keel: mission start/end, unit selected, contact detected, time compression | stable mod API with game events |
| 3.2 | Version adapters per game build, so a mod built for an older build keeps running; frame time measured from the start | version adapters, backwards compatibility |
| 3.3 | One shared settings page for all mods in the game's Options | shared settings page |
| 3.4 | Shared UI bar and hotkey manager | UI bar, hotkeys |
| 3.5 | API docs and one example mod for other authors; all opt-in, existing mods unchanged | opt-in, few changes to existing mods |
| 3.6 | Shared timestamped logging, and crash reports routed to the author, for mods that build on the API (only those can be told apart) | shared logging, crash routing |

## How we work

### One home for each thing

| What | Where |
|---|---|
| Code, pull requests, issues | GitHub (`struckl/SeaPowerKeel`) |
| Ideas and plans | Discord `#proposals`, one thread per idea |
| Releases and news | Discord `#announcements` + the Workshop change note, the same text |
| This plan | this file on GitHub and Discord `#roadmap`, updated together; linked from the Workshop page |

### How decisions are made

- A change that touches other authors' mods (the loader, the API, load order) starts as a `#proposals` thread.
- Nothing is removed or replaced for players without an opt-in and a way back (Keel's Restore).

### Anchor Chain stays theirs

- Anchor Chain is agm-114's and Ave's (JustOneOther) mod, and it stays theirs.
- Keel builds on it because Anchor Chain did not run on the 3D-globe beta, and players needed something that did.
- Keel is not a takeover. Where the two meet, they decide about Anchor Chain.

### What went wrong in November, and that was on me (Kanalratte)

- I moved too fast. When I get overmotivated, I move faster than other people can follow, and I did not wait for them.
- The move from Anchor Chain to Keel came before the talking. It looked like a takeover, and I understand why.
- News came after changes instead of before.
- Keel's setup exe got flagged by scanners and Steam pulled the item, and I had not shown well enough that it is safe.

### What I do differently

- **Announce before, not after.** Anything that changes what players or other authors have is posted in `#announcements` and on
  the Workshop page ahead: what changes, who is affected, how to go back.
- **Slow down and ask first.** Plans go to the people they touch before code is written.
- **Say where things come from.** Anchor Chain by agm-114 and Ave, in every text that talks about it.
- **Never take over silently.** Keel only takes over another loader after the player says yes, and Restore always undoes it.
- **Show that it is safe.** Every release links its VirusTotal results; the source is on GitHub; Keel installs without an exe.
- **When something breaks, speak first.** A pinned post: what happened, who is affected, what to do now, when the fix comes.
- **Answer every report** on the board, even if only "seen, looking into it".

## Also part of the plan

### For mod authors

- **"Build on Keel" guide**: from an empty project to a mod on Keel's events, with the example mod from 3.5.
- **API promise**: versioned API; a part marked old keeps working for at least two game updates before it goes, announced in
  `#announcements` and `#keel-loader`.
- **Compatibility list**: which known mods run on which game build under Keel, filled from Keel's reports (with consent).
- **Help with porting**: `#modding-help` plus a checklist for moving a mod from game internals onto Keel's events.

### When the game updates

- Keel gets the update, the mods on its API keep running: that is the point of Wave 3.
- A post in `#game-updates`: what changed and when Keel's update is out.

### Testers

- A **Tester** role in Discord: players who try a Keel build before it ships.
- Testers are named in the release post (if they want).

### Trust and safety

- Every release: VirusTotal results, the GitHub release with the same files, and the source.
- Code signing once there is a sane route; until then the by-hand install keeps the exe optional.
- Nothing calls home: the only download is BepInEx from its official release, and reports are sent only by the player's click.

### Thanks and credit

- Contributors listed in Keel's README credits and in its report window.

### Keeping it sustainable

- Two people with access to the repo and the Workshop item, so nothing stops when one is away.
- Holidays and slow weeks are announced, not discovered.
- Small, finished steps over big promises: a wave is only announced once its first step is in test.

## Open for discussion

- The license: whether the R.A.T.S. Source License (read and fork, no re-uploads) is the right fit for Keel, or something else.
