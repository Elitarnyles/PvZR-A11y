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

    /// <summary>
    /// True while the shop is up over the garden.
    ///
    /// The mode stays the garden's the whole time the shop is open, so without this the
    /// garden's keys would answer for the shop's - and Backspace, which leaves the garden
    /// here and leaves the shop there, would walk out of both at once.
    /// </summary>
    public static bool InStore()
    {
        try { return Zen()?.IsInStore ?? false; }
        catch { return false; }
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

    /// <summary>
    /// The numbers that move when a tool takes, so a success can be told from a miss.
    ///
    /// Not the need. A sprout wants three to five waterings before it wants fertilizer, so
    /// after a watering that plainly worked - the coin lands, the sound plays - it still
    /// wants water, and comparing needs called every one of those a failure. What actually
    /// moves is the count of feedings and the times the game stamps on the plant.
    /// </summary>
    public readonly record struct Progress(PottedPlantAge Age, int Fed, int Target,
                                           long Watered, long Fulfilled, long Fertilized);

    public static Progress? ProgressOf(int gridX, int gridY)
    {
        Occupant? occupant = Occupied(gridX, gridY);
        if (occupant == null) return null;

        PottedPlant potted = occupant.Value.Potted;

        try
        {
            return new Progress(potted.mPlantAge, potted.mTimesFed, potted.mFeedingsPerGrow,
                                potted.mLastWateredTime, potted.mLastNeedFulfilledTime,
                                potted.mLastFertilizedTime);
        }
        catch { return null; }
    }

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

    /// <summary>
    /// The bare noun for a need, for dropping into the middle of a sentence.
    ///
    /// The standalone line is a whole sentence - "Water needed" - because that is how it is
    /// heard when walking across the pots, and it is the wording the original PvZ
    /// accessibility mod uses. Inside another sentence it has to be a noun, or the mod says
    /// "Marigold wants Water needed".
    /// </summary>
    public static string WantName(PottedPlantNeed need)
    {
        if (need == PottedPlantNeed.None) return null;
        string key = "garden.wants." + need;
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

        // The one thing a keyboard cannot do by itself.
        //
        // A garden tool does NOT act on the position it is clicked at. MouseDownWithFeedingTool
        // walks the board looking for the first plant with mHighlighted set, and that flag is
        // written once a frame from the real mouse pointer's position. A player who never moves
        // the mouse therefore has nothing highlighted, and every watering can lands on nobody -
        // the tool is picked up, the click goes through, and not one drop reaches a plant.
        //
        // So the mod does what the pointer would have done, with the game's own method, in the
        // same call as the click: clear the flag everywhere, let the game set it from the slot's
        // pixel, and only then press.
        Highlight(slot);

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
    /// Points the game at one pot, the way a mouse hovering over it would.
    ///
    /// Cleared everywhere first so exactly one plant can answer. The game clears the flag on
    /// every plant each frame before setting it, and skipping that would leave a plant from a
    /// previous press still marked, which is a tool landing somewhere you are not standing.
    /// </summary>
    public static void Highlight(Slot slot)
    {
        if (Board == null) return;

        foreach (Slot other in Slots())
        {
            Occupant? occupant = Occupied(other.GridX, other.GridY);
            if (occupant == null) continue;
            try { occupant.Value.Plant.mHighlighted = false; } catch { }
        }

        try { Board.HighlightPlantsForMouse(slot.PixelX, slot.PixelY, Player); }
        catch (Exception ex) { Core.Log.Warning($"[garden] could not point at the pot: {ex.Message}"); }

        Occupant? target = Occupied(slot.GridX, slot.GridY);
        bool lit = false;
        if (target != null) try { lit = target.Value.Plant.mHighlighted; } catch { }

        Core.Log.Msg($"[garden] pointed at slot {slot.Index + 1}: plant highlighted = {lit}");
    }

    /// <summary>
    /// Presses one of the buttons along the top of the garden, by clicking where it is.
    ///
    /// Calling the method behind a button directly does not work here, and fails in the worst
    /// way: ZenGarden.OpenStore leaves the garden as its first act and then walks into
    /// something the click was supposed to have set up, so it throws halfway through and
    /// takes the garden with it. The game only ever reaches it from Board.MouseUp.
    ///
    /// So the mod does what a mouse does. Board.GetZenButtonRect gives each button's real
    /// position - it is the same call the game uses to decide whether a click landed on one -
    /// and a click at the middle of that goes through the game's own dispatch with everything
    /// it expects already in place.
    /// </summary>
    public static bool PressButton(ReloadedObjectType what)
    {
        if (Board == null) return false;

        try { if (!Board.CanUseGameObject(what)) { Core.Log.Msg($"[garden] {what} is not available"); return false; } }
        catch { /* ask for the rect anyway */ }

        UnityEngine.Rect rect;
        try { rect = Board.GetZenButtonRect(what); }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not find the {what} button: {ex.Message}");
            return false;
        }

        if (rect.width <= 0f || rect.height <= 0f)
        {
            Core.Log.Msg($"[garden] the {what} button has no position ({rect.width}x{rect.height})");
            return false;
        }

        int px = (int)Math.Round(rect.x + rect.width / 2f);
        int py = (int)Math.Round(rect.y + rect.height / 2f);

        try
        {
            Core.Log.Msg($"[garden] pressing {what} at pixel {px},{py}" +
                         $" (rect {rect.x:0},{rect.y:0} {rect.width:0}x{rect.height:0})");

            Board.MouseDown(px, py, 1, Player);
            Board.MouseUp(px, py, 1, Player);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] could not press {what}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens the garden's shop, by pressing the button the game listens for.
    ///
    /// There is no method to call. ZenGarden.OpenStore is left over from the 2009
    /// architecture and reaches for a store screen the remaster never builds - its
    /// GameplayActivity.ShowStoreScreen is a stub whose whole body returns nothing - so it
    /// throws, and it does so after tearing the garden down. And the ZenGarden/Shop action
    /// has no handler anywhere in the code: the wiring is in an interface prefab.
    ///
    /// What the game does listen for is a controller. The action map for the garden binds
    /// Shop to the secondary face button and to nothing else, so that is what the mod presses.
    /// </summary>
    public static bool OpenStore() =>
        Input.VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.West);

    /// <summary>
    /// Leaves the garden, by pressing the button the game listens for.
    ///
    /// Not ZenGarden.LeaveGarden, which sounds like the way out and is not: it is the tidying
    /// up that happens after the decision has been made elsewhere, so calling it on its own
    /// dismantles the garden without going anywhere. Like the shop, the real Back is a
    /// controller binding with no handler in the code to call instead.
    /// </summary>
    public static bool Leave() =>
        Input.VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.East);

    /// <summary>Moves to the next garden the way the game does, through its own button.</summary>
    public static bool NextGardenByPad() =>
        Input.VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.North);

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

    /// <summary>
    /// Advances Crazy Dave the way the garden needs, and reports whether it applied.
    ///
    /// The two marigolds you are given on your first visit are not handed over by the level
    /// or by arriving in the garden. They are handed over by one line of Dave's speech:
    /// ZenGarden.AdvanceCrazyDaveDialog advances his text and then, when his message index
    /// reaches 2102, builds two potted plants and adds them. Advancing him any other way says
    /// the same words and creates nothing, which leaves a first-time player standing in an
    /// empty greenhouse with no way to get the plants back.
    ///
    /// This is the third method of that name in the game. The cut scene's moves only words;
    /// the challenge's also lays out the next Vase Breaker stage; this one also gives plants.
    /// The mod has now been caught by two of the three.
    ///
    /// Safe to call when he is not talking: the game's own first test is whether there is a
    /// message at all, and it returns without doing anything when there is not.
    /// </summary>
    public static bool AdvanceDialog()
    {
        if (!IsActive) return false;

        ZenGarden zen = Zen();
        if (zen == null) return false;

        int before = Lawn.DaveMessageIndex();
        if (before < 0) return false;

        try { zen.AdvanceCrazyDaveDialog(); }
        catch (Exception ex)
        {
            Core.Log.Warning($"[garden] the garden would not advance Dave: {ex.Message}");
            return false;
        }

        Core.Log.Msg($"[garden] advanced the garden conversation, message {before} -> {Lawn.DaveMessageIndex()}");
        return true;
    }

    /// <summary>Moves on to the next garden, and says which one arrived.</summary>
    public static bool NextGarden()
    {
        ZenGarden zen = Zen();
        if (zen == null) return false;

        GardenType before = Which();

        // The controller route first, because it is the one the game wired: it checks the
        // garden really changed and plays the sound. Calling GotoNextGarden straight only
        // moves the state.
        if (!NextGardenByPad())
        {
            try { zen.GotoNextGarden(); }
            catch (Exception ex)
            {
                Core.Log.Warning($"[garden] could not move to the next garden: {ex.Message}");
                return false;
            }
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

    private static readonly char[] NewlineChars = { (char)13, (char)10 };

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        int cut = text.IndexOfAny(NewlineChars);
        return cut < 0 ? text : text[..cut];
    }

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

        string canShop;
        try { canShop = Lawn.AppRef == null ? "<no activity>" : Lawn.AppRef.CanShowStore().ToString(); }
        catch (Exception ex) { canShop = "<threw: " + FirstLine(ex.Message) + ">"; }
        sb.AppendLine($"  shop willing   : {canShop}");

        sb.AppendLine("  buttons        :");
        foreach (ReloadedObjectType what in new[]
                 {
                     ReloadedObjectType.StoreButton, ReloadedObjectType.MenuButton,
                     ReloadedObjectType.NextGarden, ReloadedObjectType.WateringCan,
                     ReloadedObjectType.Fertilizer, ReloadedObjectType.BugSpray,
                     ReloadedObjectType.Phonograph, ReloadedObjectType.Chocolate,
                     ReloadedObjectType.Glove, ReloadedObjectType.MoneySign,
                     ReloadedObjectType.Wheelbarrow, ReloadedObjectType.Stinky,
                 })
        {
            // Trimmed on purpose: this call throws "Unknown store item type" for the buttons
            // that are not store items, and the full IL2CPP stack trace for each one buried
            // the table it belongs to.
            string usable, where;
            try { usable = Board.CanUseGameObject(what).ToString(); }
            catch (Exception ex) { usable = "<threw: " + FirstLine(ex.Message) + ">"; }
            try
            {
                UnityEngine.Rect r = Board.GetZenButtonRect(what);
                where = $"{r.x:0},{r.y:0} {r.width:0}x{r.height:0}";
            }
            catch (Exception ex) { where = "<threw: " + ex.Message + ">"; }

            sb.AppendLine($"      {what,-14} usable={usable,-6} rect={where}");
        }

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
