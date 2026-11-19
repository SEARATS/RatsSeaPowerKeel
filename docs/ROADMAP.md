# R.A.T.S. Keel roadmap

Small wins first, every wave makes the next one easier. Each wave ships at once, with one update post. Steps marked **[promised]** are the
ones named in the plan we sent to the bigger mod teams.

This file is the plan and lives here on GitHub. It is discussed in Discord: one thread per wave in `#proposals`, and the
`#roadmap` post links here.

## Wave 0: ship what is already done

- [ ] Keel's loading window counts mods with errors again
- [ ] No flat-map warning for mods' other-map builds on 0.9.x
- [ ] Anchor Chain 1.2.0's FindFile works under Keel
- [ ] Install without an exe, with the by-hand and Linux steps **[promised]**
- [ ] An install video for the by-hand way
- [ ] Every release links its VirusTotal results; the GitHub release carries the same files and the source
- [ ] Answer the open Keel questions on the feedback board

## Wave 1: small wins

- [ ] Mod Manager Restart keeps the launch options (Linux loses `WINEDLLOVERRIDES`, Keel issue #4)
- [ ] Start with the first partner mods (list below): each one runs under Keel, and its author says what it needs
- [ ] A Tester role in Discord: players who try a Keel build before it ships, named in the release post if they want
- [ ] Contributors listed in Keel's README credits and in its report window

## Mods we start with

### I start with my own: Radio becomes the R.A.T.S. Sound Overhaul

My R.A.T.S. mods already run on Keel. The first real change is Radio:

- R.A.T.S. Radio becomes the **R.A.T.S. Sound Overhaul**, an addon for Keel.
- The Sound Overhaul only provides the endpoints: where sound goes in, when it plays, how it mixes with the game.
- Addons add whatever they want on top, each one its own item:
  - the R.A.T.S. Radio voices, with every language pack as its own addon
  - VOIP
  - other sound mods from other authors

### Official partner mods

The first official partner mods, and hopefully a lot more to come:

- **VOIP**: go from the author, invited to the Discord
- **Pilothouse**: go from the author, invited to the Discord
- **Euromod**: interested, full answer still open
- **One more mod team**: in talks, name follows

- Their needs set the order of Wave 2 and 3; nothing in their mods changes without them.

## Wave 2: Keel becomes the interface

- [ ] Anchor Chain carried on inside Keel; Anchor Chain 1.3 offers the switch (draft PR #5) **[promised]**
- [ ] Conflict finder: the report names two mods patching the same game method **[promised]**
- [ ] Per-mod start and frame cost in the report; safe start (game + Keel only) **[promised]**
- [ ] Declared dependencies and a load order Keel sorts itself **[promised]**
- [ ] Early start before the load screen (Keel PR #2 by agm-114, Anchor Chain issue #32)
- [ ] Compatibility list: which known mods run on which game build under Keel, filled from Keel's reports (with consent)
- [ ] After each game update a post in `#game-updates`: what changed and when Keel's update is out

## Wave 3: the stable mod API

- [ ] Game events from Keel: mission start/end, unit selected, contact detected, time compression **[promised]**
- [ ] Version adapters per game build, so a mod built for an older build keeps running; frame time measured from the start **[promised]**
- [ ] One shared settings page for all mods in the game's Options **[promised]**
- [ ] Shared UI bar and hotkey manager **[promised]**
- [ ] API promise: a versioned API; a part marked old keeps working for at least two game updates, announced ahead **[promised]**
- [ ] "Build on Keel" guide and one example mod; a porting checklist for moving a mod from game internals onto Keel's events **[promised]**
- [ ] Shared timestamped logging, and crash reports routed to the author, for mods that build on the API (only those can be told apart) **[promised]**

After Wave 3 a game update only needs a Keel update; the mods on its API keep running.

## How we work

### One home for each thing

- **Code, pull requests, issues**: GitHub (`struckl/SeaPowerKeel`)
- **Ideas and plans**: Discord `#proposals`, one thread per idea
- **Releases and news**: Discord `#announcements` + the Workshop change note, the same text
- **This plan**: this file on GitHub (linked from the Workshop page); discussed in Discord `#proposals`, one thread per wave

### How decisions are made

- A change that touches other authors' mods (the loader, the API, load order) starts as a `#proposals` thread.
- Nothing is removed or replaced for players without an opt-in and a way back (Keel's Restore).

### Anchor Chain is still theirs

- Anchor Chain is agm-114's and Ave's (JustOneOther) work, and it is still theirs: it moved into Keel, it did not change hands.
- Keel builds on it because Anchor Chain did not run on the 3D-globe beta, and players needed something that did.
- Keel is not a takeover: what happens with Anchor Chain is still agm-114's and Ave's call.

### What went wrong in November, and that was on me (Kanalratte)

- I have been modding for years, but I never published anything. This was the first time, and I moved as if it was still
  only me.
- I moved too fast. When I get overmotivated, I move faster than other people can follow, and I did not wait for them.
- The move from Anchor Chain to Keel came before the talking. It looked like a takeover, and I understand why.
- News came after changes instead of before.
- Keel's setup exe got flagged by scanners and Steam pulled the item, and I had not shown well enough that it is safe.

### What I do differently

- **Announce before, not after.** Anything that changes what players or other authors have is posted in `#announcements` and on
  the Workshop page ahead: what changes, who is affected, how to go back.
- **Slow down and ask first.** Plans go to the people they touch before code is written.
- **Never take over silently.** Keel only takes over another loader after the player says yes, and Restore always undoes it.
- **Show that it is safe.** Every release links its VirusTotal results; the source is on GitHub; Keel installs without an exe.
- **When something breaks, speak first.** A pinned post: what happened, who is affected, what to do now, when the fix comes.
- **Answer every report** on the board, even if only "seen, looking into it".

## Open for discussion

- The license: whether the R.A.T.S. Source License (read and fork, no re-uploads) is the right fit for Keel, or something else.
