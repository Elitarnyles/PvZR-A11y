---
name: Something reads wrongly, or does not read at all
about: Report a problem with what the mod says
title: ''
labels: ''
assignees: ''
---

<!--
Everything below is optional, and a report with none of it filled in is still better than no
report. But the first three questions are what usually decide whether a problem can be fixed
without a round of guessing.
-->

## What happened

Where you were, what you did, what you expected to hear, and what you heard instead.

If the answer is "nothing", say so explicitly. Silence is a real symptom and it is easy to
leave out of a description precisely because there is nothing to describe.

## Versions

- Mod version:
- Game version (Steam, under the game's Properties, or the "Replanted.exe" file version):
- MelonLoader version:
- Screen reader and version:
- Windows version:

## The log

`PVZ Replanted\MelonLoader\Logs\`, newest file.

The log is far more useful with `VerboseLogging = true` in
`PVZ Replanted\UserData\MelonPreferences.cfg`. If you can, turn it on, reproduce the problem,
then attach the log from that run.

**Check the log before you attach it.** It contains your player profile name, because the game
shows that on screen and the mod reads screens. It contains nothing else about you. If your
profile name is your real name, rename the profile or edit the file first.

## If a control reads wrongly

Press **F10** while that screen is up, before quitting. It writes every control on screen, what
the mod would say about each one, and which ones it filtered out and why. That is usually enough
to fix a wrong label without any back-and-forth.

## If something on the lawn is wrong

Press **F11** during a level with `VerboseLogging` on. It asks the game about everything the mod
depends on and checks the answers, so the log will say which part disagrees.
