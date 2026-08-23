# Changelog

## 0.1.0 — unreleased

First public version. Everything below is new.

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
