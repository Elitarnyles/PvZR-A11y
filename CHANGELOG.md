# Changelog

## 1.1.0 — 2026-08-30

Everything outside Adventure mode. The mini-games, the puzzle modes and the achievements were
all reachable in 1.0.0 and none of them said anything; all three are read now. The board keys
also moved back to where the original PvZ accessibility mod has them, which is a change to
muscle memory and the one thing in this release worth reading before playing.

### Keys

- **F1 pressed twice** now says what is guarding the row you are standing in — its mower, pool
  cleaner or roof sweeper, or in I, Zombie whether the brain is still there. It used to list
  the rows that had zombies in them, which was this mod's own invention and is gone rather
  than moved.
- **F3 pressed twice** says how many coins you have. A single press is still the sun.
- Both match the original mod. A layout learned once should not have to be learned again for a
  second game.

### Mini-games, puzzles and survival

- The three pages list what is on them, which entries are locked and which you have beaten, and
  start the one you choose. They have no controls on them at all — the entries are picture
  tiles bound to the game's own data — so the mod walks its own cursor over them and presses
  each tile's own button.
- The plant chooser now says which zombies a mini-game will actually send. It was announcing
  "Zombie" and nothing else on every one of them, because a challenge level's declared list
  says only that; the real list is the one the challenge code fills in.
- The plant-headed zombies of ZomBotany, the Bobsled Team and nine other zombie types had no
  name at all and read as whatever their internal name split into. They are named after the
  head — "Pea-head", "Gatling-head" — because the short form used in a row scan drops the
  trailing "zombie", and "Peashooter" alone would be the plant you just put down.

### I, Zombie

- The brains: how many are left and which rows they are in, on the key that reports progress
  everywhere else. That key used to say "Wave 0 of 4" for the whole level, because there are no
  waves in this mode.
- A brain going is announced as it happens, with how many are left.
- An eaten brain no longer reads as a brain still waiting. More generally, a destroyed thing on
  a square is no longer reported for the frame before the game sweeps it away — a gravestone, a
  crater, a rake.
- The zombie packets are called what the rest of the game calls them. "Bucket-head", not
  "Zombie Pail".

### Achievements

- All thirty-seven, with the name, whether you have earned it, and what it takes in the game's
  own words. The screen is not a panel and has no text control on it — it is a section of the
  main menu slid in over the top — so it is read from the game's data model instead.
- **F6** reads every achievement you have not earned yet, each with what it takes. That is the
  question behind opening the screen when the goal is to finish the game.
- **F3** reads the whole list, **F4** the tally, **F1** repeats the one you are on.

### Fixes

- Locked challenges announced themselves as unlocked while refusing to start. Every flag on
  those tiles is a boolean, and the helper that turns a model value into words handled strings
  and numbers but not booleans, so all of them came back as nothing — and nothing and false
  look identical to a caller holding a string.
- A dotted key into the game's data model looked for one child named after the whole path, so
  it never found anything. This is why the achievements list read as empty, and it had also
  been quietly breaking one of the shop's three routes to the coin count since that was written.
- A lawn mower that has already been set off is no longer counted as protection. The game
  leaves it in its row while it charges across, and the row read as guarded at the exact moment
  that was most wrong.
- Mowers are told apart by kind. Announcing "mower" on a roof is a promise the lawn cannot keep.

## 1.0.0 — 2026-08-30

First release. Adventure mode has been played from the first level to Dr Zomboss on a keyboard
by a blind player, which is the whole of what this version claims.

Everything below is new.

### The last level

- Dr Zomboss says what he is about to do, at the moment he decides it rather than when it
  lands: a fireball or an iceball with the row it is aimed at, which row he is about to stomp,
  and when bungees, a machine or a wave are on their way. The row is picked at random each
  time, so there is nothing to learn and no way to plan around it without being told.
- A ball already crossing the lawn is part of the row scan, named before the count of zombies.
  Pressed twice, the scan lists its row among the rows worth knowing about — a ball crossing an
  otherwise empty row used to answer "all clear".
- F4, which reports progress through the waves on an ordinary level, asks about the boss here.
  There are no waves on that level for it to report.

### The Zen Garden

- The garden reads as a grid of pots: what is planted, how grown it is, and what it wants.
- Tools are stepped through with minus, equals and the digits, and used with Enter, following
  the original PvZ accessibility mod. The list is built from what the game says you can use, so
  it stays right as you buy things.
- A night plant left in the main garden and a water plant left out of the aquarium are asleep
  and will never ask for anything. They say so, rather than reading as contented.
- Stinky, selling, the glove and the wheelbarrow, and moving between the three gardens.
- The shop, the way out and the tool row answer only to a controller in this game — not one
  keyboard binding exists in the garden's whole action map. The mod adds a controller that no
  hardware backs for the length of a press, then hands the controls straight back to the
  keyboard, because a screen built for a controller has nothing on it a keyboard can reach.

### Mini-games

- Vase Breaker: vases read as vases and say what was inside once broken; the plants they drop
  are picked up from the ground with Enter, the cycle keys or the digits, since those levels
  have no deck to put them in.
- Whack a Zombie: the mallet.

### Fixed

- Rows were read wrongly on roof levels, and worse the further along the lawn you were. The
  roof slopes, and every row was being measured at the first column — the one place the old
  reading was right. Replaying the game's own formulas over all forty-five roof squares: thirty
  two were wrong, and none are now.
- The coin total was a tenth of the truth. The game counts money in units of ten and multiplies
  only when it draws the number.
- Digging while holding a plant killed that plant for the rest of the level.
- Crazy Dave's conversations are advanced by whichever screen owns them. Three different
  screens have a method of that name and each does something the others do not — one lays out
  the next Vase Breaker stage, one hands over your first two garden plants.



### Menus

- Every control read as you reach it: its label, its role, its state, and its position in a list.
- Screen changes announced on arrival, folded into the same sentence as the first control rather
  than said separately.
- Dialogue and message boxes read as they appear.
- Profile creation, including the name field, which does not activate on focus by itself.
- Level select, including the carousel, which reports a scroll offset rather than an index and
  had to be tracked through the game's own callback.

### The lawn

- Arrow keys walk the game's own grid cursor, so pool lanes and roof slopes need no special
  handling. Walking into a wall says which wall; a cursor that cannot be read says that instead,
  which used to sound identical.
- Enter plants and Backspace digs, both through the game's own code, so cost, cooldown and
  placement rules are enforced by the game rather than reimplemented. Digging puts back whatever
  was in your hand.
- Each square reports what is planted, how damaged it is, what the ground is, and what is
  standing on it.
- Zombies announced as they arrive, aggregated per row so a wave is one sentence with a count.
- Row scan with each zombie's column, kind, state and remaining armour. Pressed twice, it names
  the rows that have anything in them.
- Tripwire warning when a zombie passes a column you choose.
- Sun and level progress on their own keys. Sun and prizes collected automatically.
- Freeze, so the board can be read without the game moving underneath.
- The whole seed bank readable without changing what is in your hand, including the slots you
  cannot yet afford — the game's own navigation skips those, which is exactly backwards.
- Game speed announced when it changes, read from the game's own fast-forward control.

### Before a level

- The plant chooser, read as a list rather than through its controls: it recycles seven card
  objects for forty-nine plants, so walking the controls reaches seven and stops.
- Which zombies the level can send. The level's own list is incomplete — it names what a level
  introduces, not everything it can draw on — so this is assembled from the game's zombie table
  as well, and filtered by what the board could actually hold.
- What kind of level it is, how many slots are left, and a one-line description of every plant.

### Speech

- Queued and flushed once a frame, so no game callback blocks on the screen reader.
- Interrupting clears only what is queued from the same source, so moving the cursor cannot
  delete a wave warning that has not been spoken yet.
- Repeats inside 300 ms are dropped, except where the caller knows a repeat is a genuinely new
  event.
- 314 translatable lines, none baked into the code. The full English set is written out on every
  launch for translators to copy.

### Robustness

- Each per-frame step runs in its own try block. A mod that throws every frame would otherwise be
  a mod that never speaks again while still appearing to be loaded, which for a blind user is
  indistinguishable from never having installed it.
- The board pointer is checked against the game's own every frame. It used to survive a level
  restart and a level ending, after which everything read from it threw and the row scan reported
  the resulting emptiness as "all clear" — a confident answer about a lawn that no longer existed.
- A failed read is reported as a failed read rather than as an empty lawn, and a scan that lost
  entries says how many.
