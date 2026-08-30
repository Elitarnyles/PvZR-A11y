# Changelog

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
