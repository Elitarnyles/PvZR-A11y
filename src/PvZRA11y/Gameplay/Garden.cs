using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The Zen Garden: what is planted where, what each plant wants, and what you can do about it.
///
/// It runs on an ordinary Board, so most of the lawn machinery already applies — the grid
/// cursor walks it without changes, because the game's own cursor knows the garden's slot
/// table. What is different is everything else: the squares are not a rectangle of lawn but a
/// table of numbered pots, nothing attacks, and the verbs are tools rather than plants.
///
/// The shape of the interaction follows the original PvZ accessibility mod, at the player's
/// request: the cycle keys and the digits step through the tools you own, moving the cursor
/// says what is in the pot and what it wants, and one key uses the chosen tool on the pot you
/// are standing on.
/// </summary>
public static class Garden
{
    private const int Player = 0;

    /// <summary>Consumable counts are stored with this added, so a count of none reads as this.</summary>
    private const int PurchaseOffset = 1000;

    private static Board Board => Lawn.BoardRef;

    #region where we are

    private static ZenGarden Zen()
    {
        try { return Lawn.AppRef?.ZenGarden; }
        catch { return null; }
    }

    /// <summary>
    /// True while the Zen Garden is the thing on screen.
    ///
    /// Asked of the game's own mode rather than guessed from the background or from the
    /// absence of zombies. The mode is a single number the game sets itself, and every other
    /// test the mod tried for a mini-game turned out to be an inference that was wrong
    /// somewhere.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.GameMode == GameMode.ChallengeZenGarden; }
            catch { return false; }
        }
    }

    /// <summary>Which of the gardens is on screen.</summary>
    public static GardenType Which()
    {
        try { return Zen()?.GardenType ?? GardenType.Main; }
        catch { return GardenType.Main; }
    }

    /// <summary>The garden's spoken name.</summary>
    public static string WhichName()
    {
        GardenType which = Which();
        string key = "garden.name." + which;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(which.ToString());
    }

    #endregion

    #region the slots

    /// <summary>One pot position: where it is in the table, and where it is on screen.</summary>
    public readonly record struct Slot(int Index, int GridX, int GridY, int PixelX, int PixelY);

    private static List<Slot> _slots;
    private static BackgroundType _slotsFor = (BackgroundType)(-1);

    /// <summary>
    /// Every pot position in this garden, read from the game rather than assumed.
    ///
    /// The main garden is a real eight by four grid; the mushroom garden and the aquarium are
    /// eight pots in a single line whose screen order is not their slot order. Hard-coding
    /// either would be inventing a shape, and the game hands the table out on request.
    /// </summary>
    public static List<Slot> Slots()
    {
        BackgroundType background = Background();

        if (_slots != null && background == _slotsFor) return _slots;

        var found = new List<Slot>();
        ZenGarden zen = Zen();

        if (zen != null)
        {
            try
            {
                var table = zen.GetSpecialGridPlacements(out int count);

                if (table != null)
                    for (int i = 0; i < count && i < table.Length; i++)
                    {
                        var entry = table[i];
                        if (entry == null) continue;
                        found.Add(new Slot(i, entry.GridX, entry.GridY, entry.PixelX, entry.PixelY));
                    }
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"[garden] could not read the slot table: {ex.Message}");
            }
        }

        _slots = found;
        _slotsFor = background;

        if (found.Count > 0)
            Core.Log.Msg($"[garden] slot table for {background}: {found.Count} slots," +
                         $" grid {found[0].GridX},{found[0].GridY} to" +
                         $" {found[^1].GridX},{found[^1].GridY}");

        return found;
    }

    private static BackgroundType Background()
    {
        try { return Board?.mBackground ?? BackgroundType.Day; }
        catch { return BackgroundType.Day; }
    }

    /// <summary>How many rows of pots this garden has, counted from the table.</summary>
    public static int RowCount()
    {
        int rows = 0;
        foreach (Slot slot in Slots())
            if (slot.GridY + 1 > rows) rows = slot.GridY + 1;
        return rows;
    }

    /// <summary>The slot the cursor is standing on, or null when it is between the pots.</summary>
    public static Slot? SlotUnderCursor()
    {
        if (!Lawn.TryGetPosition(out int x, out int y)) return null;

        foreach (Slot slot in Slots())
            if (slot.GridX == x && slot.GridY == y) return slot;

        return null;
    }

    #endregion

    #region what is in a pot

    /// <summary>A pot's contents, or nothing when the pot is empty.</summary>
    public readonly record struct Occupant(PottedPlant Potted, Plant Plant, SeedType Type,
                                           PottedPlantAge Age, PottedPlantNeed Need);

    /// <summary>
    /// What is growing in a slot.
    ///
    /// The chain is the game's own: the board holds a Plant at the grid position, the plant
    /// remembers which potted plant it belongs to, and the potted plant is the thing that
    /// carries the age and the wishes. Asking the board for the plant with the zen tool
    /// priority is exactly what the game does when a watering can lands on a pot.
    /// </summary>
    public static Occupant? Occupied(int gridX, int gridY)
    {
        if (Board == null) return null;

        try
        {
            Plant plant = Board.GetTopPlantAt(gridX, gridY, PlantPriority.ZenToolOrder);
            if (plant == null) return null;

            ZenGarden zen = Zen();
            PottedPlant potted = zen?.PottedPlantFromIndex(plant.mPottedPlantIndex);
            if (potted == null) return null;

            return new Occupant(potted, plant, potted.mSeedType, potted.mPlantAge, NeedOf(potted));
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not read the pot at {gridX},{gridY}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// What a plant is asking for, taken from the game.
    ///
    /// The game weighs several things the mod cannot see from outside — the calendar day, the
    /// minutes since the last feeding, a few seconds of grace after a watering — and it is the
    /// same answer the tools themselves check before doing anything. Working it out
    /// independently would give the mod a second opinion that can disagree with the one that
    /// decides whether the watering can does anything, which is the shape of every wrong
    /// answer this mod has shipped.
    /// </summary>
    public static PottedPlantNeed NeedOf(PottedPlant potted)
    {
        if (potted == null) return PottedPlantNeed.None;
        try { return ZenGarden.GetPlantsNeed(potted); }
        catch { return PottedPlantNeed.None; }
    }

    /// <summary>The spoken name of a need, or null when there is nothing to say.</summary>
    public static string NeedName(PottedPlantNeed need)
    {
        if (need == PottedPlantNeed.None) return null;
        string key = "garden.need." + need;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(need.ToString());
    }

    /// <summary>The spoken name of a growth stage.</summary>
    public static string AgeName(PottedPlantAge age)
    {
        string key = "garden.age." + age;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(age.ToString());
    }

    /// <summary>
    /// A plant standing in the wrong garden, which is a thing to fix rather than a wish.
    ///
    /// A night plant left in the main garden and a water plant left out of the aquarium are
    /// both asleep: the game gives them no wishes at all, so they never ask for anything and
    /// never grow. Without a word for it, such a plant is indistinguishable from a contented
    /// one, and the player would wait for a wish that is never coming.
    /// </summary>
    public static string Misplaced(Occupant occupant)
    {
        if (Which() != GardenType.Main) return null;

        try
        {
            if (Plant.IsNocturnal(occupant.Type)) return Strings.T("garden.nocturnal");
            if (Plant.IsAquatic(occupant.Type)) return Strings.T("garden.aquatic");
        }
        catch { /* the game will not say; do not guess */ }

        return null;
    }

    /// <summary>
    /// What to say about a slot when the cursor lands on it.
    ///
    /// The need comes before the name, following the original PvZ accessibility mod: what
    /// stops you is worth hearing before what it is called, and on a walk across thirty-two
    /// pots the first word is the one that decides whether you stop.
    /// </summary>
    public static string Describe(Slot slot, bool withPosition)
    {
        var parts = new List<string>(3);

        Occupant? occupant = Occupied(slot.GridX, slot.GridY);

        if (occupant == null)
        {
            parts.Add(Strings.T("garden.empty"));
        }
        else
        {
            string wrongPlace = Misplaced(occupant.Value);
            string need = wrongPlace ?? NeedName(occupant.Value.Need);

            if (!string.IsNullOrEmpty(need)) parts.Add(need);
            parts.Add(Lawn.PlantName(occupant.Value.Type));
        }

        if (withPosition) parts.Add(PositionOf(slot));

        return string.Join(", ", parts);
    }

    /// <summary>Where a slot is, said the way this garden's shape allows.</summary>
    public static string PositionOf(Slot slot)
    {
        // A single line of pots has no rows to speak of, and the aquarium's slot order is not
        // its order across the screen - so "slot three of eight" is the only honest reading
        // there. Inventing a second row to save a few key presses would describe a shape that
        // is not on the screen.
        return RowCount() > 1
            ? Strings.T("garden.position", slot.GridY + 1, slot.GridX + 1)
            : Strings.T("garden.slot", slot.Index + 1, Slots().Count);
    }

    /// <summary>The full report of a slot, for the key that asks for it.</summary>
    public static string Report(Slot slot)
    {
        Occupant? found = Occupied(slot.GridX, slot.GridY);
        if (found == null) return $"{Strings.T("garden.empty")}, {PositionOf(slot)}";

        Occupant occupant = found.Value;
        var parts = new List<string>(5)
        {
            Lawn.PlantName(occupant.Type),
            AgeName(occupant.Age),
        };

        string wrongPlace = Misplaced(occupant);
        if (!string.IsNullOrEmpty(wrongPlace)) parts.Add(wrongPlace);
        else parts.Add(NeedName(occupant.Need) ?? Strings.T("garden.happy"));

        // How far off the next stage is, which is the one number that tells you whether this
        // plant is nearly done or barely started.
        try
        {
            int fed = occupant.Potted.mTimesFed;
            int needed = occupant.Potted.mFeedingsPerGrow;
            if (needed > 0 && occupant.Age != PottedPlantAge.Full)
                parts.Add(Strings.T("garden.fed", fed, needed));
        }
        catch { /* the count is a courtesy */ }

        parts.Add(PositionOf(slot));
        return string.Join(", ", parts);
    }

    /// <summary>How many plants in this garden want something.</summary>
    public static int NeedyCount()
    {
        int needy = 0;
        foreach (Slot slot in Slots())
        {
            Occupant? occupant = Occupied(slot.GridX, slot.GridY);
            if (occupant == null) continue;
            if (occupant.Value.Need != PottedPlantNeed.None || Misplaced(occupant.Value) != null) needy++;
        }
        return needy;
    }

    /// <summary>How many pots are planted, out of how many there are.</summary>
    public static (int Planted, int Total) Census()
    {
        var slots = Slots();
        int planted = 0;
        foreach (Slot slot in slots)
            if (Occupied(slot.GridX, slot.GridY) != null) planted++;
        return (planted, slots.Count);
    }

    #endregion

    #region the tools

    /// <summary>One entry in the row of things you can be holding.</summary>
    public readonly record struct Tool(ReloadedObjectType What, CursorType Cursor, string Name);

    /// <summary>
    /// The tools you can actually use, asked of the game.
    ///
    /// The original PvZ accessibility mod worked this out from the store: it read how many
    /// fertilizers had been bought, took a thousand off, and included the tool if what was
    /// left was not negative. It had to, because it was reading raw memory. Here the game
    /// answers directly, which also means the list stays right if an update changes what a
    /// tool costs or when it unlocks.
    /// </summary>
    public static List<Tool> Tools()
    {
        var tools = new List<Tool>(9);
        if (Board == null) return tools;

        Add(tools, ReloadedObjectType.WateringCan, CursorType.WateringCan, "garden.tool.watering_can", StoreItem.None);
        Add(tools, ReloadedObjectType.Fertilizer, CursorType.Fertilizer, "garden.tool.fertilizer", StoreItem.Fertilizer);
        Add(tools, ReloadedObjectType.BugSpray, CursorType.BugSpray, "garden.tool.bug_spray", StoreItem.BugSpray);
        Add(tools, ReloadedObjectType.Phonograph, CursorType.Phonograph, "garden.tool.phonograph", StoreItem.None);
        Add(tools, ReloadedObjectType.Chocolate, CursorType.Chocolate, "garden.tool.chocolate", StoreItem.Chocolate);
        Add(tools, ReloadedObjectType.Glove, CursorType.Glove, "garden.tool.glove", StoreItem.None);
        Add(tools, ReloadedObjectType.MoneySign, CursorType.MoneySign, "garden.tool.sell", StoreItem.None);
        Add(tools, ReloadedObjectType.Wheelbarrow, CursorType.WheeelBarrow, "garden.tool.wheelbarrow", StoreItem.None);
        Add(tools, ReloadedObjectType.TreeFood, CursorType.TreeFood, "garden.tool.tree_food", StoreItem.None);

        // Last on purpose: it is the one entry that is not a tool at all, and putting it at
        // the end means stepping right from the last tool reaches it, the way it does on
        // screen.
        Add(tools, ReloadedObjectType.NextGarden, CursorType.Normal, "garden.tool.next_garden", StoreItem.None);

        return tools;
    }

    private static void Add(List<Tool> tools, ReloadedObjectType what, CursorType cursor,
                            string key, StoreItem counted)
    {
        try { if (!Board.CanUseGameObject(what)) return; }
        catch { return; }

        string name = Strings.T(key);

        // A consumable says how many are left. Standing in front of thirty-two plants with
        // one fertilizer in the drawer is worth knowing before you spend it, not after.
        if (counted != StoreItem.None)
        {
            int left = Stock(counted);
            if (left >= 0) name = Strings.T("garden.tool.count", name, left);
        }

        // The wheelbarrow is worth naming by what is standing in it, since that is the whole
        // reason to look at it.
        if (what == ReloadedObjectType.Wheelbarrow)
        {
            string carried = InWheelbarrow();
            if (!string.IsNullOrEmpty(carried)) name = Strings.T("garden.tool.wheelbarrow_holding", carried);
        }

        tools.Add(new Tool(what, cursor, name));
    }

    /// <summary>How many of a bought item are left, or -1 when it cannot be read.</summary>
    public static int Stock(StoreItem item)
    {
        try
        {
            IUserService user = Lawn.UserServiceRef();
            if (user == null) return -1;

            int held = user.GetPurchases(item);
            return held < PurchaseOffset ? 0 : held - PurchaseOffset;
        }
        catch { return -1; }
    }

    /// <summary>The plant standing in the wheelbarrow, or null.</summary>
    public static string InWheelbarrow()
    {
        try
        {
            PottedPlant carried = Zen()?.GetPottedPlantInWheelbarrow();
            return carried == null ? null : Lawn.PlantName(carried.mSeedType);
        }
        catch { return null; }
    }

    #endregion

    #region doing something

    /// <summary>
    /// Uses a tool on a slot, the way a mouse click would.
    ///
    /// Two steps, because that is what the game does: pick the tool up, then click the pot.
    /// The click position matters - the game turns it back into grid coordinates and looks up
    /// the plant standing there - so it goes to the slot's own pixel from the table rather
    /// than to anything the mod worked out itself.
    ///
    /// The game only applies a tool that matches what the plant is asking for. Anything else
    /// plays its animation, comes off your stock and changes nothing. The mod does not stop
    /// you: that is how the game works for everyone, and second-guessing the player would
    /// mean deciding for him which tool he meant.
    /// </summary>
    public static bool Use(Tool tool, Slot slot)
    {
        if (Board == null) return false;

        try
        {
            if (!Board.PickUpTool(tool.What))
            {
                Core.Log.Msg($"[garden] the game would not hand over {tool.What}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not pick up {tool.What}: {ex.Message}");
            return false;
        }

        try
        {
            Core.Log.Msg($"[garden] {tool.What} on slot {slot.Index + 1}" +
                         $" (grid {slot.GridX},{slot.GridY}) at pixel {slot.PixelX},{slot.PixelY}");

            Board.MouseDownWithTool(slot.PixelX, slot.PixelY, 1, tool.Cursor, Player);
            Board.MouseUp(slot.PixelX, slot.PixelY, 1, Player);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not use {tool.What}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clicks a pot with whatever is already in hand.
    ///
    /// The glove and the wheelbarrow are two-step: the first press lifts a plant onto the
    /// cursor, and the second has to put it down. Reaching for the tool again on that second
    /// press would drop what you were carrying and pick the empty tool back up, which is a
    /// way of losing a plant with no way to notice. A plain click is what the mouse does, and
    /// the game's own dispatch already knows what a carried plant means.
    /// </summary>
    public static bool Place(Slot slot)
    {
        if (Board == null) return false;

        try
        {
            Core.Log.Msg($"[garden] placing what is in hand on slot {slot.Index + 1}" +
                         $" at pixel {slot.PixelX},{slot.PixelY}");

            Board.MouseDown(slot.PixelX, slot.PixelY, 1, Player);
            Board.MouseUp(slot.PixelX, slot.PixelY, 1, Player);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not put the plant down: {ex.Message}");
            return false;
        }
    }

    /// <summary>Moves on to the next garden, and says which one arrived.</summary>
    public static bool NextGarden()
    {
        ZenGarden zen = Zen();
        if (zen == null) return false;

        GardenType before = Which();

        try { zen.GotoNextGarden(); }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not move to the next garden: {ex.Message}");
            return false;
        }

        // The table belongs to the garden that has just gone.
        _slots = null;
        _slotsFor = (BackgroundType)(-1);

        Core.Log.Msg($"[garden] next garden: {before} -> {Which()}");
        return true;
    }

    /// <summary>
    /// Wakes Stinky, or feeds him when something edible is in hand, and says what happened.
    ///
    /// He picks coins up off the floor for you, which on a garden of thirty-two pots is the
    /// difference between collecting and hunting. Asleep he does nothing, and asleep is his
    /// resting state - so the key wakes him, and only feeds him when chocolate is deliberately
    /// in hand, because chocolate costs money and a reflex should not spend it.
    /// </summary>
    public static string Stinky(bool feeding)
    {
        ZenGarden zen = Zen();
        if (zen == null) return null;

        try { if (!zen.HasPurchasedStinky()) return Strings.T("garden.no_stinky"); }
        catch { /* if the game will not say, try anyway */ }

        if (feeding)
        {
            try
            {
                zen.FeedStinky();
                Core.Log.Msg("[garden] fed Stinky");
                return Strings.T("garden.stinky_fed");
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"[garden] could not feed Stinky: {ex.Message}");
                return null;
            }
        }

        bool asleep;
        try { asleep = zen.IsStinkySleeping(); }
        catch { asleep = false; }

        if (!asleep)
        {
            Core.Log.Msg("[garden] Stinky is already awake");
            return Strings.T("garden.stinky_awake");
        }

        try
        {
            zen.WakeStinky();
            Core.Log.Msg("[garden] woke Stinky");
            return Strings.T("garden.stinky_woken");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not wake Stinky: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region diagnostics

    /// <summary>Everything about the garden, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- zen garden ---");
        sb.AppendLine($"  active         : {IsActive}");
        if (!IsActive) { sb.AppendLine(); return; }

        sb.AppendLine($"  garden         : {Which()} ({WhichName()})");
        sb.AppendLine($"  background     : {Background()}");

        var slots = Slots();
        sb.AppendLine($"  slots          : {slots.Count}, {RowCount()} row(s)");

        Lawn.TryGetPosition(out int cx, out int cy);
        Slot? under = SlotUnderCursor();
        sb.AppendLine($"  cursor         : grid {cx},{cy}" +
                      $" ({(under == null ? "not on a slot" : "slot " + (under.Value.Index + 1))})");

        (int planted, int total) = Census();
        sb.AppendLine($"  planted        : {planted} of {total}, {NeedyCount()} want something");
        sb.AppendLine($"  wheelbarrow    : {InWheelbarrow() ?? "<empty>"}");

        sb.AppendLine("  tools          :");
        foreach (Tool tool in Tools())
            sb.AppendLine($"      {tool.What,-14} cursor {tool.Cursor,-14} \"{tool.Name}\"");

        sb.AppendLine("  pots           :");
        foreach (Slot slot in slots)
        {
            Occupant? occupant = Occupied(slot.GridX, slot.GridY);
            string what = occupant == null
                ? "<empty>"
                : $"{occupant.Value.Type} {occupant.Value.Age} need={occupant.Value.Need}" +
                  $" misplaced={Misplaced(occupant.Value) ?? "no"}";

            sb.AppendLine($"      [{slot.Index,2}] grid {slot.GridX},{slot.GridY}" +
                          $" pixel {slot.PixelX,4},{slot.PixelY,4}  {what}");
        }

        sb.AppendLine();
    }

    #endregion
}
