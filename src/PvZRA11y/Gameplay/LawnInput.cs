using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// What the arrow keys and the status keys do once play has started.
///
/// Kept apart from the menu key handling because the same keys mean different things in
/// the two places: in a menu an arrow moves between buttons, on the lawn it moves across
/// grid squares. Which handler runs is decided by whether a board exists, not by guessing
/// from what happens to have focus.
///
/// Every cursor move produces a tone first and speech second. The tone is instant and
/// carries the position on its own, so a player moving quickly can outrun the narration
/// without losing track of where they are — the speech simply gets interrupted, and that
/// is fine.
/// </summary>
public static class LawnInput
{
    private static int _lastX = -1;
    private static int _lastY = -1;

    private static int _lastSeedIndex = int.MinValue;
    private static int _sunTicks;

    /// <summary>
    /// Frames between sun sweeps. Roughly three times a second — often enough that sun is
    /// never missed, rare enough that it costs nothing.
    /// </summary>
    private const int SunSweepInterval = 20;

    /// <summary>
    /// Per-frame work on the lawn: notice the plant in hand changing, and gather sun.
    /// </summary>
    public static void Update()
    {
        if (!Lawn.IsOnBoard) return;

        // The Zen Garden runs on a board, so everything here would otherwise run there too.
        // It did: the digits chose a garden tool AND asked the seed bank for a slot, so every
        // press announced the tool and was then talked over by "No slot 1" three milliseconds
        // later. The garden has its own handler for the same keys.
        if (Garden.IsActive) return;

        TickPickupTrace();
        TickPickupVerify();
        TickNumberKeys();
        TickSeedSelection();
        TickSunCollection();
        TickGameSpeed();
    }

    /// <summary>
    /// Announces the plant in hand whenever it changes.
    ///
    /// Polling rather than hooking the packet, for the same reason focus is polled in the
    /// menus: it catches every route in — the number keys, the cycle keys, a mouse click,
    /// or the game clearing the cursor after planting — without a patch for each.
    /// </summary>
    /// <summary>Reads the whole seed bank aloud, changing nothing.</summary>
    public static bool AnnounceBank()
    {
        if (!Lawn.IsOnBoard) return false;

        // The ground and the bank are separate facts and both get read. Tying the ground
        // readout to "the bank is empty" was the mistake: Vase Breaker has a one-slot bank,
        // so the test never once came out true on the level it was written for.
        var lying = new List<string>();
        foreach (Lawn.Pickup p in Lawn.Pickups())
            lying.Add(Strings.T("pickup.at", p.Label, p.Row + 1, p.Column + 1));

        string bank = Seeds.DescribeBank();

        if (lying.Count > 0)
        {
            string ground = Strings.T("pickup.on_the_lawn", string.Join(", ", lying));
            Speech.SayVerbatim(string.IsNullOrEmpty(bank) ? ground : $"{ground}. {bank}", "bank");
            return true;
        }

        if (string.IsNullOrEmpty(bank)) return false;

        Speech.SayVerbatim(bank, "seed bank");
        return true;
    }

    private static int _pendingDigitSlot = -1;
    private static int _pendingDigitFrom = int.MinValue;

    /// <summary>
    /// Notices the game's own number keys picking a plant.
    ///
    /// The digits belong to the game and are left to it. A pick that works is announced by
    /// the watcher above; a pick the game refuses — the packet still refreshing, or not
    /// enough sun — changes nothing at all, so the watcher stays quiet and you get silence
    /// that could mean any of three different things.
    ///
    /// The answer is deferred a frame so the game has acted first: if the selection moved,
    /// nothing more is needed, and if it did not, the slot is described and the description
    /// says why it was refused.
    /// </summary>
    private static void TickNumberKeys()
    {
        if (_pendingDigitSlot >= 0)
        {
            int slot = _pendingDigitSlot;
            _pendingDigitSlot = -1;

            if (Seeds.SelectedIndex() != _pendingDigitFrom) return;

            // A digit that changes nothing is usually a packet still refreshing or sun you
            // do not have. But it is also what a stuck tool looks like, and that one is a
            // dead end: the game refuses every plant while something is in hand, so the
            // digits stay dead for the rest of the level with nothing to say why. Cheap to
            // rule out, and it puts the player back in a working state rather than
            // explaining a broken one.
            if (Lawn.CursorKind() is Il2CppReloaded.Gameplay.CursorType.Shovel
                                   or Il2CppReloaded.Gameplay.CursorType.Hammer)
            {
                Core.Log.Msg("[lawn] a digit was refused while a tool was in hand; putting it down");
                Lawn.PutToolDown();
                if (Seeds.Select(slot)) return;
            }

            string description = Seeds.Describe(slot);
            // allowRepeat: pressing the same digit twice is two presses, and the second one
            // deserves the same answer as the first. Without it the repeat is swallowed and
            // the key reads as dead — which is the exact silence this whole branch exists
            // to remove.
            Speech.Say(string.IsNullOrEmpty(description) ? Strings.T("seeds.no_such_slot", slot + 1) : description,
                       interrupt: true, context: "seed refused", allowRepeat: true);
            return;
        }

        UnityEngine.InputSystem.Keyboard kb;
        try { kb = UnityEngine.InputSystem.Keyboard.current; }
        catch { return; }
        if (kb == null) return;

        for (int digit = 0; digit < 10; digit++)
        {
            var key = digit == 0
                ? UnityEngine.InputSystem.Key.Digit0
                : UnityEngine.InputSystem.Key.Digit1 + (digit - 1);

            bool pressed;
            try { pressed = kb[key]?.wasPressedThisFrame == true; }
            catch { continue; }
            if (!pressed) continue;

            // The game numbers the bank one to ten with zero at the end, matching the keys.
            int wanted = digit == 0 ? 9 : digit - 1;

            // Past the end of the bank the digits carry on into what is lying on the lawn,
            // so one row of keys covers everything a player can pick up. On an ordinary level
            // there is nothing lying about and the numbering is unchanged.
            int slots = Seeds.SlotCount();
            if (wanted >= slots)
            {
                var pickups = Lawn.Pickups();
                int onGround = wanted - slots;

                if (onGround < pickups.Count)
                {
                    _pickupCursor = onGround;
                    TakeAndAnnounce(pickups[onGround]);
                }
                else
                {
                    Speech.Say(Strings.T("seeds.no_such_slot", wanted + 1),
                               interrupt: true, context: "seed refused", allowRepeat: true);
                }

                return;
            }

            _pendingDigitSlot = wanted;
            _pendingDigitFrom = Seeds.SelectedIndex();
            return;
        }
    }

    private static void TickSeedSelection()
    {
        int index = Seeds.SelectedIndex();
        if (index == _lastSeedIndex) return;

        bool hadSomething = _lastSeedIndex >= 0;
        _lastSeedIndex = index;

        if (index < 0)
        {
            // Going empty-handed right after planting is expected and not worth saying.
            if (hadSomething && Settings.SayEmptyHands.Value)
                Speech.Say(Strings.T("seeds.nothing_held"), interrupt: false, context: "seed cleared");
            return;
        }

        string description = Seeds.Describe(index);
        if (!string.IsNullOrEmpty(description))
            Speech.Say(description, interrupt: true, context: "seed selected");
    }

    private static float? _lastSpeed;
    private static bool _warnedNoSpeed;

    /// <summary>
    /// Says when the game's speed changes under you.
    ///
    /// The fast-forward key belongs to the game and the mod deliberately leaves it there, so
    /// the speed can change with nothing to announce it. At eight times normal a row is
    /// overrun in less time than it takes to read one square aloud, which makes an
    /// unannounced change the difference between playing and guessing.
    ///
    /// The first reading of a level is taken as the starting state, not as a change.
    /// </summary>
    private static void TickGameSpeed()
    {
        float? speed = Lawn.SpeedMultiplier();
        if (speed == null)
        {
            // Said once. A speed reader that cannot find the control would otherwise be
            // silent in exactly the same way as a speed that never changes, and there would
            // be nothing in the log to tell the two apart.
            if (!_warnedNoSpeed)
            {
                _warnedNoSpeed = true;
                Core.Log.Warning("[speed] no fast-forward control found on this board");
            }
            return;
        }

        if (_lastSpeed != null && Math.Abs(speed.Value - _lastSpeed.Value) < 0.01f) return;

        bool first = _lastSpeed == null;
        _lastSpeed = speed;

        Core.Log.Msg($"[speed] x{speed.Value:0.##}  label=\"{Lawn.SpeedLabel()}\"" +
                     $"  setting={Lawn.SpeedSetting()}  timeScale={UnityEngine.Time.timeScale:0.##}");

        if (first) return;

        Speech.Say(Strings.T(SpeedKey(speed.Value), Format(speed.Value)),
                   interrupt: false, context: "game speed");
    }

    /// <summary>
    /// Normal speed gets its own sentence; everything else is read as a multiple.
    ///
    /// A multiple rather than a name, because the game ships whatever speeds it likes in
    /// that control and a fixed list of names would go quietly wrong the moment one changed.
    /// </summary>
    private static string SpeedKey(float multiplier)
        => Math.Abs(multiplier - 1f) < 0.01f ? "speed.normal"
            : multiplier < 1f ? "speed.slower"
            : "speed.faster";

    /// <summary>
    /// The multiplier as a spoken number.
    ///
    /// Invariant, not local: on a machine whose decimal mark is a comma this would otherwise
    /// come out as "1,5", which an English screen reader reads as something else entirely.
    /// </summary>
    private static string Format(float multiplier)
        => multiplier == Math.Floor(multiplier)
            ? ((int)multiplier).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void TickSunCollection()
    {
        if (!Settings.AutoCollectSun.Value && !Settings.AutoCollectItems.Value) return;

        // Deliberately not gated on the lawn having the keyboard. The prize for winning a
        // level lands while the game is wrapping up and windows are appearing, which is
        // exactly the moment this must keep working — and sweeping an empty lawn costs
        // nothing.
        if (_sweepFailures >= MaxSweepFailures) return;
        if (++_sunTicks < SunSweepInterval) return;
        _sunTicks = 0;

        if (Lawn.VacuumPickups(Settings.AutoCollectSun.Value, Settings.AutoCollectItems.Value))
        {
            _sweepFailures = 0;
            return;
        }

        // The board can be half torn down between levels, and retrying three times a
        // second fills the log with the same exception. Give up until the next level.
        if (++_sweepFailures >= MaxSweepFailures)
            Core.Log.Warning("[lawn] automatic collection kept failing; standing down until the next level");
    }

    private static int _sweepFailures;
    private const int MaxSweepFailures = 3;

    /// <summary>Steps through the seed slots. Announcement comes from the selection watcher.</summary>
    private static int _pickupCursor;
    private static Lawn.Pickup? _pendingPickup;
    private static int _pendingPickupFrames;

    /// <summary>How long to give the game to hand over a plant before calling it a miss.</summary>
    private const int PickupVerifyFrames = 10;

    /// <summary>
    /// Takes one plant off the ground and says so once the game agrees it happened.
    ///
    /// The click can miss — a coin moves while it falls, and its clickable box is smaller
    /// than the square it sits on. Announcing the pickup before checking would tell the
    /// player they are carrying a plant they are not, which is worse than silence.
    /// </summary>
    private static void TakeAndAnnounce(Lawn.Pickup taken)
    {
        _pendingPickup = null;

        if (!Lawn.TakePickup(taken))
        {
            Speech.Say(Strings.T("pickup.cannot_take"), context: "pickup");
            return;
        }

        // A reward never reaches the cursor - it goes straight into what you own - so there
        // is nothing to wait for and waiting would report a false failure.
        if (taken.Kind != Lawn.PickupKind.Plant)
        {
            AnnounceTaken(taken);
            return;
        }

        if (Lawn.HeldSeed() != Il2CppReloaded.Gameplay.SeedType.None)
        {
            AnnounceTaken(taken);
            return;
        }

        // Not in hand yet. It may simply need a frame, so wait rather than judge.
        _pendingPickup = taken;
        _pendingPickupFrames = 0;
    }

    private static void AnnounceTaken(Lawn.Pickup taken) =>
        Speech.Say(Strings.T("pickup.took", taken.Label, taken.Row + 1, taken.Column + 1),
                   interrupt: true, context: "pickup", allowRepeat: true);

    private static int _lastPickupCount = -1;

    /// <summary>
    /// Logs how many plants are lying on the lawn whenever that changes.
    ///
    /// This is the one line that settles whether a vase really drops a collectable rather
    /// than filling the seed bank. Without it the mod's silence is ambiguous: it could mean
    /// nothing came out, or that the mod is looking in the wrong place.
    /// </summary>
    private static void TickPickupTrace()
    {
        int count;
        try { count = Lawn.Pickups().Count; }
        catch { return; }

        if (count == _lastPickupCount) return;
        _lastPickupCount = count;
        Core.Log.Msg($"[lawn] plants lying on the lawn: {count}");
    }

    private static void TickPickupVerify()
    {
        if (_pendingPickup == null) return;

        Lawn.Pickup taken = _pendingPickup.Value;

        if (Lawn.HeldSeed() != Il2CppReloaded.Gameplay.SeedType.None)
        {
            _pendingPickup = null;
            AnnounceTaken(taken);
            return;
        }

        if (++_pendingPickupFrames < PickupVerifyFrames) return;

        _pendingPickup = null;
        Core.Log.Warning($"[lawn] clicked \"{taken.Label}\" at row {taken.Row + 1}," +
                         $" column {taken.Column + 1} but nothing reached the cursor" +
                         $" within {PickupVerifyFrames} frames");
        Speech.Say(Strings.T("pickup.cannot_take"), context: "pickup");
    }

    /// <summary>
    /// Takes the next plant lying on the lawn.
    ///
    /// The keys that cycle the deck reach for the ground first, because a dropped plant
    /// times out and a bank slot does not. This is where the original PvZ accessibility mod
    /// puts it too.
    /// </summary>
    private static bool CyclePickup(int delta)
    {
        var pickups = Lawn.Pickups();
        if (pickups.Count == 0) return false;

        _pickupCursor = ((_pickupCursor + delta) % pickups.Count + pickups.Count) % pickups.Count;
        TakeAndAnnounce(pickups[_pickupCursor]);
        return true;
    }

    public static bool CycleSeed(int delta)
    {
        if (!Lawn.HasInput) return false;

        // Anything lying on the lawn comes first, because it is the thing that disappears:
        // a dropped plant times out, a bank slot does not.
        if (CyclePickup(delta)) return true;

        int before = Seeds.SelectedIndex();

        if (!Seeds.Cycle(delta))
        {
            Speech.Say(Strings.T("seeds.no_bank"), context: "seed cycle");
            return true;
        }

        // The watcher that normally announces the new plant only speaks when the slot
        // changes. Landing back on the slot you already hold — a bank with one plant in it,
        // which is every early level — was therefore total silence, indistinguishable from
        // a dead key or a dead mod.
        int after = Seeds.SelectedIndex();
        if (after == before)
        {
            string description = after < 0 ? Strings.T("seeds.nothing_held") : Seeds.Describe(after);
            if (!string.IsNullOrEmpty(description))
                Speech.Say(description, interrupt: true, context: "seed cycle");
        }

        return true;
    }

    /// <summary>Plants the held plant on the current square, or explains why it cannot.</summary>
    public static bool Plant()
    {
        if (!Lawn.HasInput) return false;

        // What is in hand is the only thing that decides this, and Lawn.HeldSeed asks the
        // cursor. The old test also consulted the bank's "this slot is chosen" mark, which
        // survives the cursor emptying - so one press of the cycle key left the mod believing
        // a plant was in hand for the rest of the level, and Enter stopped breaking vases.
        if (Lawn.HeldSeed() == Il2CppReloaded.Gameplay.SeedType.None)
        {
            // Empty hands, in the order a player would expect: take what is lying under you,
            // otherwise break what is standing there.
            Lawn.Pickup? lying = Lawn.PickupUnderCursor();
            if (lying != null)
            {
                TakeAndAnnounce(lying.Value);
                return true;
            }

            // What comes out of a vase announces itself: the mod reads the vase before the
            // blow lands and says what was inside.
            if (Lawn.BreakVaseAtCursor(out string broke))
            {
                Speech.Say(Strings.T("lawn.broke", broke), interrupt: true,
                           context: "vase", allowRepeat: true);
                return true;
            }

            Speech.Say(Strings.T("seeds.nothing_held"), context: "plant");
            return true;
        }

        // The board announces a successful planting itself, through AddPlant.
        if (!Lawn.PlantAtCursor())
            Speech.Say(Strings.T("planting.blocked"), context: "plant");

        return true;
    }

    /// <summary>
    /// Stops or restarts the clock.
    ///
    /// Not gated on the lawn holding the keyboard, so a frozen game can always be
    /// unfrozen — a freeze you cannot undo would be a trap rather than a feature.
    /// </summary>
    public static bool ToggleFreeze()
    {
        if (!Lawn.IsOnBoard) return false;

        if (!Lawn.ToggleFreeze())
        {
            Speech.Say(Strings.T("lawn.no_board"), context: "freeze");
            return true;
        }

        Speech.SayVerbatim(Strings.T(Lawn.Frozen ? "lawn.frozen" : "lawn.unfrozen"), "freeze");
        return true;
    }

    /// <summary>Digs up the plant on the current square.</summary>
    public static bool Shovel()
    {
        if (!Lawn.HasInput) return false;

        // On the mallet mini-game the same key swings instead. There is nothing planted to
        // dig up there, and the original PvZ accessibility mod puts the mallet here too, so
        // anyone arriving from it already has the habit.
        if (Lawn.IsWhackAZombieLevel) return Whack();

        // Remembered before the dig, because digging takes whatever was in hand out of it.
        int heldBefore = Seeds.SelectedIndex();

        bool dug = Lawn.ShovelAtCursor(out string removed);

        // Always, dug or not: the tool is picked up by the attempt, not by the success.
        Lawn.PutToolDown();

        // Back to the plant you were holding. Digging is something you do in the middle of
        // planting, and having to find your plant again afterwards is a tax on every use.
        // It can legitimately fail - the plant may have become unaffordable while you dug -
        // and then it is worth a word, because otherwise the next key press plants nothing
        // and says nothing.
        if (heldBefore >= 0 && Seeds.SelectedIndex() != heldBefore && !Seeds.Select(heldBefore))
        {
            Core.Log.Msg($"[lawn] could not put slot {heldBefore + 1} back in hand after digging");
            Speech.Say(Strings.T("seeds.nothing_held"), interrupt: false, context: "shovel");
        }

        if (!dug)
        {
            Speech.Say(Strings.T("lawn.nothing_to_dig"), context: "shovel");
            return true;
        }

        Speech.Say(string.IsNullOrEmpty(removed)
            ? Strings.T("lawn.dug_up_something")
            : Strings.T("lawn.dug_up", removed), interrupt: true, context: "shovel");
        return true;
    }

    /// <summary>
    /// One swing of the mallet.
    ///
    /// Says what was there rather than whether it died: a zombie takes several hits, and
    /// reporting the hit itself is what tells you the swing landed on the square you meant.
    /// </summary>
    private static bool Whack()
    {
        bool swung = Lawn.HammerAtCursor(out string target);

        // The mallet is deliberately NOT put down here, unlike the shovel. It is the
        // player's tool for the whole mini-game, and taking it away after every swing left
        // him swinging an empty hand from the second press onward. Nothing else on this
        // level wants the cursor, so nothing is stranded by leaving it held.

        if (!swung)
        {
            Speech.Say(Strings.T("lawn.cannot_swing"), context: "mallet");
            return true;
        }

        if (!string.IsNullOrEmpty(target))
        {
            Speech.Say(Strings.T("lawn.swing_hit", target),
                       interrupt: true, context: "mallet", allowRepeat: true);
            return true;
        }

        // A miss that only says "nothing there" leaves you no better off than before you
        // swung. Where the nearest one is turns it into a direction to walk in.
        string nearest = null;
        try
        {
            if (Lawn.TryGetPosition(out int cx, out int cy)) nearest = Sonar.NearestZombieFrom(cy, cx);
        }
        catch { /* the miss is still worth saying without it */ }

        Speech.Say(string.IsNullOrEmpty(nearest)
                ? Strings.T("lawn.swing_missed")
                : Strings.T("lawn.swing_missed_near", nearest),
            interrupt: true, context: "mallet", allowRepeat: true);
        return true;
    }

    /// <summary>Says what is in hand, without changing it.</summary>
    public static void AnnounceHeld()
    {
        Speech.SayVerbatim(Seeds.DescribeHeld(), "held plant");
    }

    /// <summary>Handles a direction press. Returns false when there is no lawn to move on.</summary>
    public static bool Move(int dx, int dy)
    {
        if (!Lawn.HasInput) return false;

        switch (Lawn.Move(dx, dy))
        {
            case Lawn.MoveOutcome.Moved:
                AnnounceSquare(interrupt: true);
                break;

            case Lawn.MoveOutcome.Edge:
                // Named, not just tolled. A note that means "you did not move" leaves you
                // to work out which of the four walls you are against, and that is exactly
                // the thing you pressed the key to find out.
                Lawn.PlayEdgeCue();
                Speech.SayVerbatim(Strings.T(EdgeKey(dx, dy)), "lawn edge");
                break;

            default:
                Core.Log.Warning("[lawn] the grid cursor could not be read");
                Speech.SayVerbatim(Strings.T("lawn.cursor_lost"), "lawn edge");
                break;
        }

        return true;
    }

    /// <summary>
    /// Which side you just walked into.
    ///
    /// Taken from the key you pressed rather than from the cursor's position, so it stays
    /// right on pool and roof levels where the rows are not evenly spaced.
    /// </summary>
    private static string EdgeKey(int dx, int dy) =>
        dy < 0 ? "lawn.edge_top"
        : dy > 0 ? "lawn.edge_bottom"
        : dx < 0 ? "lawn.edge_left"
        : "lawn.edge_right";

    /// <summary>Describes the square under the cursor. Also bound to a key of its own.</summary>
    public static void AnnounceSquare(bool interrupt)
    {
        if (!Lawn.TryGetPosition(out int x, out int y)) return;

        _lastX = x;
        _lastY = y;

        Lawn.PlayPositionCue(x, y);

        string description = Lawn.DescribeSquare(x, y);
        if (!string.IsNullOrEmpty(description))
            Speech.Say(description, interrupt, "lawn square");
    }

    /// <summary>Full report on the current square, including the things not said on every move.</summary>
    public static void AnnounceSquareDetail()
    {
        if (!Lawn.TryGetPosition(out int x, out int y))
        {
            Speech.SayVerbatim(Strings.T("lawn.no_board"), "lawn detail");
            return;
        }

        var parts = new List<string>(4)
        {
            Strings.T("lawn.position", y + 1, x + 1),
        };

        string description = Lawn.DescribeSquare(x, y);
        if (!string.IsNullOrEmpty(description)) parts.Add(description);

        // Always stated here even at full health, unlike the running commentary, because
        // this key is the one you press when you want the whole picture of a square.
        string condition = Lawn.PlantConditionAt(x, y);
        if (!string.IsNullOrEmpty(condition)) parts.Add(condition);

        parts.Add(Strings.T(Lawn.RowHasMower(y) ? "lawn.mower_present" : "lawn.mower_gone"));

        Speech.SayVerbatim(string.Join(". ", parts), "lawn detail");
    }

    /// <summary>Announces the sun total, the number every decision on the lawn depends on.</summary>
    public static void AnnounceSun()
    {
        int sun = Lawn.SunCount();
        if (sun < 0)
        {
            Speech.SayVerbatim(Strings.T("lawn.no_board"), "sun");
            return;
        }

        Speech.SayVerbatim(Strings.T("lawn.sun", sun), "sun");
    }

    /// <summary>Announces how far through the level you are.</summary>
    public static void AnnounceProgress()
    {
        Speech.SayVerbatim(Lawn.LevelProgress(), "progress");
    }

    /// <summary>Announces the level layout: how many rows, and which of them are water.</summary>
    public static void AnnounceLayout()
    {
        if (!Lawn.IsOnBoard)
        {
            Speech.SayVerbatim(Strings.T("lawn.no_board"), "layout");
            return;
        }

        int rows = Lawn.SafeRowCount();
        var parts = new List<string>(3) { Strings.T("lawn.rows", rows) };

        var mowerless = new List<int>();
        for (int y = 0; y < rows; y++)
            if (!Lawn.RowHasMower(y)) mowerless.Add(y + 1);

        if (mowerless.Count > 0)
            parts.Add(Strings.T("lawn.rows_without_mower", string.Join(", ", mowerless)));

        Speech.SayVerbatim(string.Join(". ", parts), "layout");
    }

    /// <summary>Forgets remembered state, so the next level announces afresh.</summary>
    public static void Reset()
    {
        // Each level reads its speed afresh, so the opening reading is treated as the
        // starting state rather than as a change worth announcing.
        _lastSpeed = null;
        _warnedNoSpeed = false;
        _pendingDigitSlot = -1;

        _lastX = -1;
        _lastY = -1;
        _lastSeedIndex = int.MinValue;
        _sunTicks = 0;
        _sweepFailures = 0;
    }
}
