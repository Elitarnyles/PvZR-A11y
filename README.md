# PvZ Replanted Accessibility

A screen-reader mod for
[Plants vs. Zombies: Replanted](https://store.steampowered.com/app/3654560/) on Steam. It makes
the menus and the lawn readable, so the game can be played without seeing it.

Built for keyboard play with NVDA. No gamepad and no numeric keypad are needed anywhere.
English is the base language, and every line the mod speaks can be translated without touching
the code.

Everything the mod tells you, it says in words. A few optional tones exist, and every one of
them repeats something that was also spoken — turn them all off and you lose the redundancy,
not the information.

**Status: version 1.1.0.** The whole of Adventure mode has been played through from the first level to Dr Zomboss, on a keyboard, by a blind player, and the mini-games, the puzzle modes and the achievements are now reachable and read.

Done and played through: menus, choosing a player, level select, the plant chooser, the lawn,
planting, digging, the seed bank, the zombie scan, Crazy Dave's conversations and the things he
sells during them, the zombies' notes, the Suburban Almanac, the shop, the roof levels, Whack a
Zombie, Vase Breaker, the Zen Garden with all three of its gardens, the final fight with Dr
Zomboss, winning and the reward afterwards. Since 1.0.0: the Mini-Games, Puzzle and Survival
pages, I, Zombie with its brains, ZomBotany with its plant-headed zombies, and the achievements
list with what each one takes.

Not done: the Tree of Wisdom, Co-op and Versus, and the in-game narration of the mini-games
beyond the ones listed above — they start and can be played, but a mode with rules of its own
will not explain them.

Menus came first on purpose: reading the lawn is worth nothing if you cannot reach a level to
stand on.

## What it reads

In the menus: every control, its role, its state, and where it sits in a list. Screen changes
are announced as you arrive. Dialogue and message boxes are read as they appear.

On the lawn: the square under the cursor, what is planted there, how damaged it is, what the
ground is, and whether a zombie is standing on it. Zombies are announced as they enter a row,
counted, and can be scanned row by row with their column, their state, and how much armour they
have left — a Bucket-head whose bucket has been shot off is announced as exposed, because by
then it is not a Bucket-head problem any more. Sun and level progress are on their own keys.

Before a level: which zombies the level can send, what kind of level it is, how many plant slots
you have, and a one-line description of every plant you can choose.

Elsewhere: Crazy Dave's dialogue, including the questions he asks and the price of what he is
selling; the notes the zombies leave, which are pictures of handwritten letters with no text on
screen at all; and the Suburban Almanac in full, every entry read out as the encyclopaedia it is.

## Requirements

- Plants vs. Zombies: Replanted on Steam, app id 3654560. Developed and tested against game
  version **1.5.1469**, Steam buildid 21398230.
- [MelonLoader](https://melonwiki.xyz/) **0.7.2**, the .NET 6 build. Other versions may work;
  only 0.7.2 is tested.
- Windows, and a screen reader. NVDA is what this is developed against. JAWS, Window-Eyes,
  SuperNova, System Access and ZoomText should work through Tolk, but nobody has confirmed it.
  If you use one, please say whether it worked.

A game update regenerates MelonLoader's interop assemblies and can invalidate a released build.
If the mod stops loading after an update, suspect that first.

## Installing

1. Install MelonLoader 0.7.2 into the game folder and run the game once, so it generates
   `MelonLoader\Il2CppAssemblies`.
2. Copy `PvZRA11y.dll` into `PVZ Replanted\Mods\`.
3. Copy `Tolk.dll` and `nvdaControllerClient64.dll` into `PVZ Replanted\UserLibs\`. Both are in
   the `lib` folder of this release. If files with those names are already there — another
   accessibility mod may have put them there — leave the existing ones alone.
4. Start your screen reader, then start the game.

You should hear "Accessibility mod version 1.1.0 ready." within a few seconds of the game window
appearing.

If you have another accessibility mod for this game installed, remove it first. Two mods talking
at once is unusable.

## If you hear nothing

Silence is the hardest failure to diagnose, because it sounds exactly like a mod that was never
installed. Work through this in order.

**Check the log first.** It is in `PVZ Replanted\MelonLoader\Logs\`, newest file last, and it
records what the mod tried to do even when it could not say a word. Every line from this mod is
tagged `[PvZ_Replanted_Accessibility]`.

If the log has no lines from the mod at all, MelonLoader did not load it. Check that
`PvZRA11y.dll` is in `Mods\`, and that MelonLoader itself started — its own banner is at the top
of the log.

If the log shows the mod starting but you heard nothing, it could not reach a screen reader.
Look for a line mentioning Tolk. The usual causes are `Tolk.dll` missing from `UserLibs\`, or the
screen reader having been started after the game.

If you have no screen reader running at all, the mod stays silent on purpose. Windows can speak
through SAPI instead, but that is off by default — set `AllowSapi = true` in the settings file
described below. It is off by default because a mod that quietly falls back to a robotic voice
hides the fact that your real reader is not connected, and that is worth knowing.

**Ctrl+Alt+A** toggles the mod's speech and works even while the mod is silent. If something
turned it off, this turns it back on.

**F11** runs a self-test and writes the result to the log. It asks the game about everything the
mod depends on and checks the answers rather than printing them, so the log names what is broken
instead of leaving you to compare numbers.

## Keys

The mod's keys sit alongside the game's rather than replacing them. Where the game already does
something useful with a key, it keeps it.

Four keys carry almost everything — F1 to F4 — and each asks one kind of question, with the
answer depending on where you are. This is the layout the original PvZ accessibility mod uses,
so if you know that one you already know this.

### In the menus

- **Enter** — activate the focused control.
- **Tab**, and **Shift+Tab** — next and previous control.
- **F1** — repeat the last thing said.
- **F2** — what is focused right now.
- **F3** — read the whole screen.
- **F4** — read the whole screen.
- **Left** and **Right** on a slider — move it, a tenth of the way per press, and hear where it
  ended up. The game's own sliders answer a mouse drag and a gamepad stick, and neither is on
  this keyboard.
- **Minus** and **Equals** — move through the carousel on the level select screen.

### On the lawn

- **Arrow keys** — move the cursor one square. Walking into a wall says which wall.
- **Enter** — plant what is in your hand. In Vase Breaker, with nothing in hand, it breaks
  the vase you are standing on.
- **Backspace** — dig up the plant under the cursor. Whatever you were holding goes back into
  your hand afterwards. In Whack a Zombie it swings the mallet instead, and says what you hit.
- **F5** — freeze and unfreeze the game. You can still look around while frozen.
- **F6** — read the whole seed bank, including the slots you cannot afford yet, without changing
  what is in your hand. On a level with no seed bank it lists what is lying on the lawn instead.
- **Minus** and **Equals** — cycle through the seed packets.
- **1** to **0** — the game's own seed selection. If the game refuses a pick, the mod says why.

A vase that holds a plant drops it on the ground rather than putting it in the seed bank, and
it does not wait for ever. **Enter** on the square it landed on picks it up. **Minus** and
**Equals** step through everything lying about before they reach the seed bank, and the
number keys carry on past the last bank slot into the same list. Squares announce what is
lying on them as you walk past, and **F6** reads the ground and the bank together.

Picking a plant up needs an empty hand — that is the game's own rule, not the mod's — so the
mod puts back whatever you were holding first, and says you are carrying the new plant only
once the game confirms it. A vase says what came out of it as it breaks, never before.

The one thing a vase does say in advance is what is painted on it. Most are plain and
could hold anything, but the game marks some with a leaf, and it only ever marks a vase
that has a plant inside — those read as "vase with a plant". A vase marked with a zombie
reads the same way. That is not knowing what is in a vase: the marking is on the outside,
the plant's name is not, and picking the marked ones out at a glance is most of how the
mode is played.
- **Tab** — the game's fast-forward. The mod announces the new speed.
- **F1** — scan the current row: how many zombies, and each one's column, kind and state.
  Pressed twice quickly, what is guarding that row: its mower, its pool cleaner or its roof
  sweeper — or in I, Zombie, whether the brain is still there.
- **F2** — detail of the square under the cursor.
- **F3** — how much sun you have. Pressed twice quickly, how many coins.
- **F4** — level progress. On the last level, Dr Zomboss instead; in I, Zombie, how many
  brains are left and which rows they are in; in Vase Breaker, how many vases are still
  standing and how many of those are marked as holding a plant. None of those three modes has
  waves for the usual line to report.

### In Last Stand

This one does not start when you leave the plant chooser. It gives you a budget of sun and an
empty lawn and waits: you spend the lot laying out a defence, then tell it to send the wave. It
waits again between stages, with whatever sun the last one left you.

- **F5** — send the wave. That key otherwise freezes the game, and there is nothing running to
  freeze while it is waiting for you. It is where the original mod put it too.
- **F4** — during planning, how much sun you have to spend; during the wave, the usual progress.

The mod says when the game starts waiting for you, because nothing else marks that moment and a
silence with no zombies in it is otherwise hard to read.

### In the Zombiquarium

The one where you keep the zombies alive instead of killing them. They give off sun, the sun
buys more of them, and a thousand sun buys the trophy that wins it — but they starve, and a
brain costs five sun, so every brain is sun not spent on a zombie and every zombie is another
mouth.

- **Minus** and **Equals** — step through the zombies, each one saying where in the tank it is
  swimming. The arrow keys do nothing here: the game refuses to move its grid cursor on this
  level at all, because a tank of open water has no squares to walk.
- **Enter** — drop a brain by the zombie you stepped to. They swim to the nearest one, so
  where the food goes is a decision worth making.
- **F4** — the sun you have towards the thousand, how many zombies are still swimming, and how
  many brains are in the water.

Five sun a brain, three brains in the water at once. If a brain will not go in, the mod says
which of those stopped it.

### In Beghouled and Beghouled Twist

Eight columns by five rows of plants, and the game is to line three of a kind up. Seventy-five
lines wins it. In Beghouled a move swaps two neighbours and only a swap that scores is allowed;
in Twist a move turns a block of four a quarter turn.

- **Minus** and **Equals** — step through the moves the board will take. Each one is read out
  in full: which plant, which row and column, and which way it goes.
- **Enter** — play the move you have stepped to.
- **F6** — how many moves there are, and the first three.
- **F4** — how many of the seventy-five lines you have cleared.
- Walking the board, a square that can be moved into a line says so, and which way.
- **Backspace does nothing here.** Every square holds a plant and none of them is yours to dig
  up — they are the puzzle.

The original mod played a tone when the plant under the cursor could be matched. This says the
whole move instead, and will make it for you.

### In the picture puzzles

Seeing Stars, Art Challenge Wall-nut and Art Challenge Sunflower are one mini-game in three
coats: a pattern is marked out on the lawn and you win by planting the right plant on every
marked square while the zombies come. The pattern is drawn on the ground in a colour, which is
to say it does not exist for a player who cannot see it.

- Walking the lawn, a square that is part of the picture says **wants a Star fruit**, or
  **part of the picture** once the plant is standing on it. A square with no part in the
  pattern sounds like an ordinary square, because that is what it is.
- **F6** — what is still to plant, grouped by plant and then by row. Pressed twice quickly, the
  seed bank, which you still need: Art Challenge Sunflower wants three different plants — star
  fruit, wall-nut and umbrella leaf — so knowing a square wants an umbrella leaf only helps
  alongside knowing which packet holds one.
- **F4** — how many of the picture's plants are in place.

The pattern is asked of the game square by square, so all three read the same way and a fourth
would need no new code.

### In the Slot Machine

The keys are the ordinary lawn keys. Two of them mean something else for the length of that
one level, because that level asks different questions.

- **F3 pressed twice** — pull the handle. It costs twenty-five sun, and the mod says what you
  have left. A single press is still the sun, which is the number that matters here; the coin
  count is what gives up its place for this level.
- **F6** — what the three reels are showing, and whether that is two of a kind, three of a
  kind, or nothing. The first three slots of the seed bank are the reels, not plants you could
  pick.
- **F4** — how much of the two thousand sun you have, and whether you can afford a pull.

When the reels stop after a pull you made, the mod says what they landed on without being
asked. What they pay lands on the lawn as sun, diamonds or seed packets; the sun and the money
collect themselves, and a packet is picked up the same way a Vase Breaker plant is.

### The last level

Dr Zomboss does not walk down a row, so nothing else in the mod sees him coming. He picks
what to throw and where to throw it before the attack lands, and the mod says so at that
moment:

- **Fireball, row 3** or **Iceball, row 3** — the row is picked at random each time, so
  there is no learning it. The fireball burns what is there; the iceball freezes it.
- **Stomping row 2** — the foot comes down on one row that still has something to crush.
- **Bungee zombies dropping**, **Dropping a machine**, **Sending zombies**.

**F4** asks the same question at any time: what he is about to throw, at which row, and how
much of him is left.

A ball already crossing the lawn is part of the row scan, the way a zombie is. **F1** on the
row it is in names it first, before the count of zombies, and pressed twice it lists that row
among the rows with something in them - a ball crossing an otherwise empty row still makes
that row one you need to know about.

### Choosing plants before a level

- **Left** and **Right** — one plant at a time. **Up** and **Down** — a row of eight.
- **Enter** — take the plant, or put it back.
- **F6** — start the level.
- **F1** — which zombies this level can send.
- **F2** — what kind of level this is.
- **F3** — read the screen.
- **F4** — what is selected, and how many slots are left.
- **Minus** and **Equals** — move through the plants you have chosen.

### In the shop

- **Arrow keys** and **Tab** — move between items. **Enter** — buy the one you are on.
- Each item is read by name, price, and whether it is sold out or not yet available.
- **F1** — how many coins you have.

### In the almanac

- **Arrow keys** — move between entries. **Enter** — open the one you are on.
- **F4** — read the whole entry: name, cost, recharge and the full description.
- **Tab** — reach the buttons around the page, including the way back out.

### In the achievements

- **Up** and **Down**, **Tab** and **Shift+Tab**, or **Minus** and **Equals** — move through
  the list. Each one says its name, whether you have earned it, and where it sits in the list.
- **F1** — say that one again.
- **Enter**, or **F2** — what that one takes, in the game's own words.
- **F3** — read the whole list.
- **F4** — how many you have earned out of how many there are.
- **F6** — every achievement you have not earned yet, each with what it takes. That is the
  question behind opening this screen when the goal is to finish the game.
- **Backspace** — back out.

### Anywhere

- **Left Ctrl** — stop speaking.
- **Ctrl+Alt+A** — turn the mod's speech off and on.
- **F10** — write everything on the current screen to the log. This is the useful thing to
  attach to a report about a control that reads wrongly.
- **F11** — run the self-test.

Only the left Ctrl and left Alt are accepted. On most screen readers both Ctrl keys already stop
speech at the system level, and a right-Ctrl binding would fight that.

## Settings

The settings file is written on first run to `PVZ Replanted\UserData\MelonPreferences.cfg`, under
`[PvZRA11y]`. Every entry has a comment above it saying what it does. Edit it with the game
closed.

The ones most worth knowing about:

- `Language` — which file in `UserData\PvZRA11y\lang\` to speak. Default `en`.
- `AllowSapi` — speak through Windows SAPI when no screen reader is found. Off by default; see
  the silence section above for why.
- `VerboseLogging` — writes everything the mod says, and a great deal about what it read from the
  game, to the log. Turn this on before reporting a problem.
- `SayTilePosition` — say the row and column on every cursor move. Off by default, because it
  roughly triples the length of every step across the lawn.
- `SayZombieArrivals` — announce zombies as they enter a row. On by default.
- `SayTripwire` and `TripwireColumn` — warn when a zombie gets past a column you choose.
- `AutoCollectSun` and `AutoCollectItems` — collect sun and prizes for you. Both on by default.
  Finding falling sun with a grid cursor is close to impossible, so leaving `AutoCollectSun` on is
  strongly recommended.
- Every key can be rebound. The names are the ones the Unity Input System uses: `F6`, `Backspace`,
  `LeftCtrl`, `Minus`, `Equals`.

## Translating

Every line the mod speaks comes from a text file. None of it is baked into the code.

On each start, the complete English set is written to
`PVZ Replanted\UserData\PvZRA11y\lang\en.default.txt`. That file is overwritten every launch, so
do not edit it — copy it to `pl.txt`, or whatever your language code is, translate the right side
of each line, and set `Language` to match.

The format is one `key = text` per line, with `#` starting a comment. Placeholders like `{0}` are
filled in by the mod and have to survive translation, though they can be reordered. Any key you
leave out falls back to English, so a half-finished translation still speaks in full sentences
rather than reading identifiers aloud.

There are 314 lines. Roughly a third are plant and zombie names.

## Reporting a problem

Issues: https://github.com/Elitarnyles/PvZR-A11y/issues

What actually helps:

1. What you did, what you expected to hear, and what you heard instead.
2. The log file from that session, from `PVZ Replanted\MelonLoader\Logs\`, newest one. Turn
   `VerboseLogging` on first and reproduce the problem — without it the log has very little.
3. If a control reads wrongly, press **F10** while it is on screen before you quit. That writes
   every control, what the mod would say about each one, and which ones it filtered out and why.

**Before you attach a log, know what is in it.** It records your player profile name, because the
game puts that on screen and the mod reads screens. It records nothing else about you. If your
profile name is your real name, rename it or edit the log first.

## Building

You need the .NET SDK — 8 or newer builds the net6.0 target fine — and a copy of the game with
MelonLoader installed, because the mod compiles against MelonLoader's generated interop
assemblies.

    dotnet build PvZRA11y.sln -c Release

The game path defaults to the usual Steam location. Override it if yours is elsewhere:

    dotnet build PvZRA11y.sln -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\PVZ Replanted\"

The trailing backslash is required. If the path is wrong, the build says so in one line instead of
producing several hundred errors about missing types.

A successful build copies the mod straight into the game's `Mods` folder. If the game is running
it holds that file open and the build fails loudly — deliberately. A silently skipped copy means
the next test session runs the previous build, and every conclusion drawn from it is wrong.

### tools/AsmDump

A small metadata reader for the game's interop assemblies. It answers "what is this class actually
called, and what can I call on it" without loading anything — which matters, because the interop
stubs cannot be loaded outside the game.

    dotnet run --project tools/AsmDump -- "<path to dll>" "<type name regex>" --members

The regex matches the full type name including its namespace, so `Gameplay\.Board$` rather than
`^Board$`.

Nearly every question about the game's API in this project was answered with this rather than
guessed. That is not a style preference: every bug in this mod's history came from assuming
something about the game instead of measuring it.

## How it works

The game is Unity IL2CPP, so the mod runs inside it as a MelonLoader mod and reads the game's own
objects directly — no memory scanning, no version-specific pointer tables.

Wherever possible it drives the game's own code rather than reimplementing it. Planting replays
the click a mouse would send, so sun is spent and every placement rule is enforced. Digging goes
through the shovel tool, so a pumpkin comes off before the plant inside it. Collecting uses the
vacuum. Moving the cursor drives the game's own grid cursor, which already knows about pool lanes
and roof slopes. None of that logic has to be duplicated here, and none of it can drift out of
step when the game updates.

Focus narration works by polling `EventSystem.currentSelectedGameObject` once per frame rather
than by patching `Selectable.OnSelect`. The game's navigation containers override `OnSelect`, and
an override that does not chain to base never reaches a patch on the base method. Polling sees
every focus change no matter what caused it.

Speech is queued and flushed once per frame, so no game callback ever blocks on the screen reader.
Repeated text inside a 300 ms window is dropped, because the game raises several events for one
widget in a single frame — but a caller that knows a repeat is a genuinely new event can say so,
which is how three zombies entering one row stay three zombies. Interrupting clears only what is
queued from the same source, so moving the cursor cannot delete a wave warning you have not heard
yet.

The per-frame work is a list of steps, each in its own try block. A mod that throws every frame
would otherwise be a mod that never speaks again while still appearing to be loaded, and for a
blind user that is indistinguishable from never having installed it.

Not every control on screen is reachable. The game keeps whole sections built and active while
they are off-screen: level select, the options screen and the achievements list all live inside
the single `mainMenu` panel and are hidden by being moved, not disabled. So reachability is decided
by measuring where a control actually lands on screen, which also prevents pressing a level tile
belonging to a carousel that was never opened — something that throws inside the game.

## Credits

Built by **Elitarny Les**, who is blind, plays the game, and decided what it should say. The code
was written in conversation with Claude.

Two existing mods made this a much shorter road, and both are MIT licensed:

- [PvZA11y](https://github.com/CG8516/PvZA11y) by Clark (CG8516) — the accessibility mod for the
  original Plants vs. Zombies, and the reference for what good gameplay narration sounds like. The
  zombie scan, the armour wording and the four-question key layout are its ideas, matched here on
  purpose so that anyone arriving from it already knows how this mod speaks.
- [PvZ-Replanted-A11y](https://github.com/game-a11y/PvZ-Replanted-A11y) by Chengyu HAN (inkydragon)
  — mapped out which of Replanted's classes and methods matter, which is the slowest part of
  modding an IL2CPP game. The verified control-name labels in the English string set come from that
  work.

[Tolk](https://github.com/dkager/tolk) by Davy Kager is what lets one mod speak through any screen
reader.

## Licence

MIT. See [LICENSE](LICENSE).

The bundled speech libraries are LGPL and remain under their own terms. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Plants vs. Zombies is a trademark of Electronic Arts Inc. and PopCap Games. This mod is unofficial
and unaffiliated, ships no game code or assets, and does nothing without a legally obtained copy of
the game.
