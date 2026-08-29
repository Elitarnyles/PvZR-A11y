using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// What the keys do in the Zen Garden.
///
/// The shape follows the original PvZ accessibility mod, because that is what the player asked
/// for and because it is the scheme he already has in his hands: the cycle keys and the digits
/// step through the tools, moving the cursor says what is in the pot and what it wants, and
/// the activate key uses whatever tool is chosen on the pot you are standing on. Choosing
/// "next garden" and pressing the same key moves gardens instead.
///
/// The mod deliberately does not choose the tool for the player, even though it could: the
/// game exposes what each plant wants, so an automatic key would always be right. It would
/// also make it impossible to use anything the plant is not asking for - chocolate, the glove,
/// the money sign - and those are half of what the garden is for.
/// </summary>
public static class GardenInput
{
    private static int _tool;
    private static GardenType _toolsFor = (GardenType)(-1);
    private static int _lastAnnouncedTool = int.MinValue;

    /// <summary>Reset when the garden changes: the tools are not the same list.</summary>
    private static void SyncGarden()
    {
        GardenType which = Garden.Which();
        if (which == _toolsFor) return;

        _toolsFor = which;
        _tool = 0;
        _lastAnnouncedTool = int.MinValue;
    }

    #region moving

    /// <summary>Walks one slot and says what is there.</summary>
    public static bool Move(int dx, int dy)
    {
        SyncGarden();

        Lawn.MoveOutcome outcome = Lawn.Move(dx, dy);

        if (outcome == Lawn.MoveOutcome.CursorLost)
        {
            Speech.Say(Strings.T("garden.no_cursor"), context: "garden");
            return true;
        }

        if (outcome == Lawn.MoveOutcome.Edge)
        {
            // A garden of one row refuses every vertical press, and saying "top row" there
            // would describe a shape that is not on the screen.
            Speech.Say(Garden.RowCount() <= 1 && dy != 0
                           ? Strings.T("garden.single_row")
                           : Strings.T("garden.edge"),
                       context: "garden edge");
            return true;
        }

        Announce(withPosition: false);
        return true;
    }

    /// <summary>Says what the cursor is standing on.</summary>
    private static void Announce(bool withPosition)
    {
        Garden.Slot? slot = Garden.SlotUnderCursor();

        if (slot == null)
        {
            Speech.Say(Strings.T("garden.between_pots"), context: "garden slot");
            return;
        }

        Speech.Say(Garden.Describe(slot.Value, withPosition), interrupt: true, context: "garden slot");
    }

    #endregion

    #region tools

    /// <summary>Steps to the next or previous tool and says which it is.</summary>
    public static bool CycleTool(int delta)
    {
        SyncGarden();

        var tools = Garden.Tools();
        if (tools.Count == 0)
        {
            Speech.Say(Strings.T("garden.no_tools"), context: "garden tool");
            return true;
        }

        _tool = ((_tool + delta) % tools.Count + tools.Count) % tools.Count;
        AnnounceTool(tools);
        return true;
    }

    /// <summary>Picks a tool by its number, the way the digits do on the seed bank.</summary>
    public static bool PickTool(int index)
    {
        SyncGarden();

        var tools = Garden.Tools();
        if (index < 0 || index >= tools.Count)
        {
            Speech.Say(Strings.T("garden.no_such_tool", index + 1),
                       interrupt: true, context: "garden tool", allowRepeat: true);
            return true;
        }

        _tool = index;
        AnnounceTool(tools);
        return true;
    }

    private static void AnnounceTool(List<Garden.Tool> tools)
    {
        // allowRepeat: pressing the same digit twice is two presses and deserves two answers.
        // Without it the second one reads as a dead key.
        Speech.Say(Strings.T("garden.tool_chosen", tools[_tool].Name, _tool + 1, tools.Count),
                   interrupt: true, context: "garden tool", allowRepeat: true);

        _lastAnnouncedTool = _tool;
    }

    /// <summary>The tool in hand, kept inside the list even as the list changes under us.</summary>
    private static Garden.Tool? Chosen(List<Garden.Tool> tools)
    {
        if (tools.Count == 0) return null;
        if (_tool < 0) _tool = 0;
        if (_tool >= tools.Count) _tool = tools.Count - 1;
        return tools[_tool];
    }

    #endregion

    #region acting

    /// <summary>Uses the chosen tool on the pot under the cursor, or moves gardens.</summary>
    public static bool Use()
    {
        SyncGarden();

        var tools = Garden.Tools();
        Garden.Tool? chosen = Chosen(tools);

        if (chosen == null)
        {
            Speech.Say(Strings.T("garden.no_tools"), context: "garden");
            return true;
        }

        Garden.Tool tool = chosen.Value;

        // Something already in hand wins over the tool list. The glove and the wheelbarrow
        // both lift a plant onto the cursor and expect a second click to put it down; going
        // back to the tools on that second press would drop the plant and re-arm an empty
        // glove, losing it with nothing said.
        if (Lawn.HeldSeed() != SeedType.None)
        {
            Garden.Slot? target = Garden.SlotUnderCursor();
            if (target == null)
            {
                Speech.Say(Strings.T("garden.between_pots"), context: "garden");
                return true;
            }

            string carried = Lawn.PlantName(Lawn.HeldSeed());

            if (!Garden.Place(target.Value))
            {
                Speech.Say(Strings.T("garden.cannot_place", carried),
                           interrupt: true, context: "garden", allowRepeat: true);
                return true;
            }

            Speech.Say(Lawn.HeldSeed() == SeedType.None
                           ? Strings.T("garden.placed", carried, Garden.PositionOf(target.Value))
                           : Strings.T("garden.still_holding", carried),
                       interrupt: true, context: "garden", allowRepeat: true);
            return true;
        }

        // Not a tool at all, and it does not care where the cursor is standing.
        if (tool.What == ReloadedObjectType.NextGarden)
        {
            if (!Garden.NextGarden())
            {
                Speech.Say(Strings.T("garden.cannot_switch"), context: "garden");
                return true;
            }

            SyncGarden();
            var census = Garden.Census();
            Speech.Say(Strings.T("garden.arrived", Garden.WhichName(), census.Planted, census.Total),
                       interrupt: true, context: "garden", allowRepeat: true);
            return true;
        }

        Garden.Slot? slot = Garden.SlotUnderCursor();
        if (slot == null)
        {
            Speech.Say(Strings.T("garden.between_pots"), context: "garden");
            return true;
        }

        // Read before, so the change can be reported rather than guessed at. A watering that
        // takes leaves the plant with a different wish, and that is the one thing the player
        // needs to hear.
        Garden.Progress? before = Garden.ProgressOf(slot.Value.GridX, slot.Value.GridY);

        if (!Garden.Use(tool, slot.Value))
        {
            Speech.Say(Strings.T("garden.tool_refused", tool.Name),
                       interrupt: true, context: "garden", allowRepeat: true);
            return true;
        }

        // Not read back in the same breath. The game registers the watering on its own next
        // update, so asking straight away found the plant still thirsty and reported a
        // failure that had not happened - while the game's own advice was cheerfully saying
        // "keep watering your plants".
        _pendingTool = tool;
        _pendingSlot = slot.Value;
        _pendingBefore = before;
        _pendingFrames = 0;
        return true;
    }

    private static Garden.Tool? _pendingTool;
    private static Garden.Slot _pendingSlot;
    private static Garden.Progress? _pendingBefore;
    private static int _pendingFrames;

    /// <summary>
    /// How long to keep watching for a tool to take effect before calling it a miss.
    ///
    /// Generous on purpose, and watched rather than waited out. A garden tool does not act
    /// when it is clicked: the click puts a tool object on the plant, and the plant is only
    /// watered when that finishes its animation a second or more later. A short fixed wait
    /// read the plant while nothing had happened yet and reported a failure on every single
    /// watering that worked.
    /// </summary>
    private const int WatchForFrames = 240;

    private static void TickReport()
    {
        if (_pendingTool == null) return;

        Garden.Tool tool = _pendingTool.Value;
        Garden.Progress? now = Garden.ProgressOf(_pendingSlot.GridX, _pendingSlot.GridY);

        // Report the moment something moves, rather than at the end of a fixed wait. A plant
        // that answers in three frames should not be reported four seconds later.
        if (Moved(_pendingBefore, now))
        {
            _pendingTool = null;
            Report(tool, _pendingSlot, _pendingBefore);
            return;
        }

        if (++_pendingFrames < WatchForFrames) return;

        _pendingTool = null;
        Report(tool, _pendingSlot, _pendingBefore);
    }

    /// <summary>Whether anything about the plant changed since the tool was used.</summary>
    private static bool Moved(Garden.Progress? before, Garden.Progress? after)
    {
        if (before == null || after == null) return before != null || after != null;

        Garden.Progress was = before.Value;
        Garden.Progress now = after.Value;

        return now.Age != was.Age
            || now.Fed != was.Fed
            || now.Watered != was.Watered
            || now.Fulfilled != was.Fulfilled
            || now.Fertilized != was.Fertilized;
    }

    /// <summary>
    /// Says what the tool did, by looking at the numbers that move rather than at the need.
    ///
    /// The need is the wrong measure. A sprout wants three to five waterings before it wants
    /// anything else, so a watering that plainly worked leaves it still wanting water - and
    /// reporting on the need called every one of those a failure while the coin was landing
    /// on the floor. What moves is the count of feedings and the times the game stamps on
    /// the plant.
    /// </summary>
    private static void Report(Garden.Tool tool, Garden.Slot slot, Garden.Progress? before)
    {
        Garden.Progress? after = Garden.ProgressOf(slot.GridX, slot.GridY);
        Garden.Occupant? occupant = Garden.Occupied(slot.GridX, slot.GridY);

        // The pot emptied, which is what selling looks like from here.
        if (before != null && after == null)
        {
            Speech.Say(Strings.T("garden.gone"), interrupt: true, context: "garden", allowRepeat: true);
            return;
        }

        if (before == null || after == null || occupant == null)
        {
            Speech.Say(Strings.T("garden.nothing_happened"), context: "garden");
            return;
        }

        string name = Lawn.PlantName(occupant.Value.Type);
        Garden.Progress was = before.Value;
        Garden.Progress now = after.Value;

        if (now.Age != was.Age)
        {
            Speech.Say(Strings.T("garden.grew", name, Garden.AgeName(now.Age)),
                       interrupt: true, context: "garden", allowRepeat: true);
            return;
        }

        // One more feeding done. The count towards the next stage is the number worth having:
        // it is the only thing that says whether this plant is nearly ready for fertilizer.
        if (now.Fed != was.Fed)
        {
            Speech.Say(now.Target > 0
                           ? Strings.T("garden.fed_progress", name, now.Fed, now.Target)
                           : Strings.T("garden.tended", name),
                       interrupt: true, context: "garden", allowRepeat: true);
            return;
        }

        bool stamped = now.Watered != was.Watered
                    || now.Fulfilled != was.Fulfilled
                    || now.Fertilized != was.Fertilized;

        if (stamped)
        {
            Speech.Say(Strings.T("garden.tended", name),
                       interrupt: true, context: "garden", allowRepeat: true);
            return;
        }

        // Nothing moved at all. Almost always the wrong tool, which the game accepts in
        // silence and takes off your stock.
        Core.Log.Msg($"[garden] {tool.What} moved nothing on slot {slot.Index + 1}" +
                     $" (fed {was.Fed}/{was.Target}, need {occupant.Value.Need})");

        string wanted = Garden.WantName(occupant.Value.Need);
        Speech.Say(wanted == null
                       ? Strings.T("garden.wants_nothing", name)
                       : Strings.T("garden.wanted_instead", name, wanted),
                   interrupt: true, context: "garden", allowRepeat: true);
    }

    #endregion

    #region questions

    /// <summary>How many plants want something, and on a second press what the garden holds.</summary>
    public static bool AnnounceSurvey(bool detailed)
    {
        SyncGarden();

        if (detailed)
        {
            var census = Garden.Census();
            Speech.SayVerbatim(Strings.T("garden.census", Garden.WhichName(),
                                         census.Planted, census.Total), "garden");
            return true;
        }

        int needy = Garden.NeedyCount();
        Speech.SayVerbatim(needy == 1
                               ? Strings.T("garden.one_needy")
                               : Strings.T("garden.needy", needy), "garden");
        return true;
    }

    /// <summary>The whole report of the pot under the cursor.</summary>
    public static bool AnnounceSlot()
    {
        SyncGarden();

        Garden.Slot? slot = Garden.SlotUnderCursor();
        if (slot == null)
        {
            Speech.SayVerbatim(Strings.T("garden.between_pots"), "garden slot");
            return true;
        }

        Speech.SayVerbatim(Garden.Report(slot.Value), "garden slot");
        return true;
    }

    /// <summary>Wakes Stinky, or feeds him when chocolate is the tool in hand.</summary>
    public static bool AnnounceStinky()
    {
        SyncGarden();

        var tools = Garden.Tools();
        Garden.Tool? chosen = Chosen(tools);
        bool feeding = chosen != null && chosen.Value.What == ReloadedObjectType.Chocolate;

        string said = Garden.Stinky(feeding);
        if (string.IsNullOrEmpty(said)) return false;

        Speech.SayVerbatim(said, "garden");
        return true;
    }

    /// <summary>Reads every pot in order, without moving the cursor.</summary>
    public static bool AnnounceGarden()
    {
        SyncGarden();

        var slots = Garden.Slots();
        if (slots.Count == 0) return false;

        var parts = new List<string>(slots.Count + 1) { Garden.WhichName() };

        foreach (Garden.Slot slot in slots)
        {
            Garden.Occupant? occupant = Garden.Occupied(slot.GridX, slot.GridY);
            if (occupant == null) continue;   // an empty pot in a list of thirty-two is noise

            string need = Garden.Misplaced(occupant.Value) ?? Garden.NeedName(occupant.Value.Need);
            string one = Lawn.PlantName(occupant.Value.Type);
            if (!string.IsNullOrEmpty(need)) one = $"{need}, {one}";

            parts.Add($"{one}, {Garden.PositionOf(slot)}");
        }

        if (parts.Count == 1) parts.Add(Strings.T("garden.nothing_planted"));

        Speech.SayVerbatim(string.Join(". ", parts), "garden");
        return true;
    }

    #endregion

    /// <summary>
    /// Says when the garden changes under you.
    ///
    /// Standing still in a garden is not standing still: the calendar day turns over and every
    /// grown plant wants something again. Growth is the rare event and changes what the plant
    /// is, so it is worth interrupting for; a new wish is common and, over thirty-two pots,
    /// would talk over everything else.
    /// </summary>
    private static readonly Dictionary<int, PottedPlantAge> _ages = new();

    private static bool _dumpedHud;

    /// <summary>
    /// Writes out the garden's own top bar, once per visit.
    ///
    /// Its buttons - previous, next, visit other garden, shop, back - are on screen and their
    /// text is readable, but the mod finds exactly one control on that panel and it is a
    /// template. Board.GetZenButtonRect, which in the 2009 game gave each of them a position,
    /// returns nothing at all here: the bar was rebuilt in the interface layer and no longer
    /// exists as far as the board is concerned.
    ///
    /// So the dump goes in by itself rather than waiting for someone who cannot see the
    /// screen to think of pressing a diagnostic key at the right moment.
    /// </summary>
    private static bool _dumpedStore;

    /// <summary>
    /// Writes out the shop the first time it opens from the garden.
    ///
    /// It comes up empty, and there are two quite different reasons it could: either the item
    /// tiles are not there yet, or they are there and no longer reachable because the game
    /// rebuilt the screen for a controller the moment the mod pretended to plug one in. The
    /// dump tells those apart, and guessing between them has already cost this mod several
    /// rounds today.
    /// </summary>
    private static int _storeDumpIn;

    private static void TickStoreDump()
    {
        if (_dumpedStore) return;

        if (UI.PanelScope.FrontPanelId != "store") { _storeDumpIn = 0; return; }

        // Late on purpose. The controls are handed back to the keyboard a few frames after the
        // shop opens, and the screen is rebuilt when that happens - so a dump taken the moment
        // the panel appears describes the state we are trying to get out of.
        if (++_storeDumpIn < 30) return;

        _dumpedStore = true;
        Core.Log.Msg($"[garden] writing out the shop opened from the garden;" +
                     $" controls are {Input.VirtualPad.ControlType()}");

        try { Diagnostics.Probe.DumpCurrentScreen(); }
        catch (Exception ex) { Core.Log.Warning($"[garden] could not dump the shop: {ex.Message}"); }
    }

    private static void TickHudDump()
    {
        if (_dumpedHud) return;
        if (UI.PanelScope.FrontPanelId != "zenGardenHUD") return;

        _dumpedHud = true;
        Core.Log.Msg("[garden] writing out the garden bar, to find where its buttons live");

        try { Diagnostics.Probe.DumpCurrentScreen(); }
        catch (Exception ex) { Core.Log.Warning($"[garden] could not dump the bar: {ex.Message}"); }
    }


    private static bool _wasActive;

    public static void Tick()
    {
        bool active = Garden.IsActive;

        // Logged whichever way it goes, and before anything can fail. A feature that turns
        // out to be inert on its first test should say WHY from the log alone: whether the
        // mod even noticed the garden, whether there is a board under it, and whether the
        // keys were being handed to the interface at the time. Working that out afterwards
        // costs a round trip through someone who cannot see the screen.
        if (active != _wasActive)
        {
            _wasActive = active;
            Core.Log.Msg($"[garden] {(active ? "entered" : "left")} the Zen Garden:" +
                         $" board={Lawn.IsOnBoard} input={Lawn.HasInput}" +
                         $" panel={UI.PanelScope.FrontPanelId ?? "none"}" +
                         $" garden={Garden.Which()} slots={Garden.Slots().Count}" +
                         $" tools={Garden.Tools().Count}");

            if (active)
            {
                var census = Garden.Census();
                Speech.Say(Strings.T("garden.arrived", Garden.WhichName(),
                                     census.Planted, census.Total),
                           interrupt: false, context: "garden", allowRepeat: true);
            }
        }

        if (!active)
        {
            if (_ages.Count > 0) _ages.Clear();
            _toolsFor = (GardenType)(-1);
            _pendingTool = null;
            _dumpedHud = false;
            _dumpedStore = false;
            _storeDumpIn = 0;
            return;
        }

        TickReport();
        TickHudDump();
        TickStoreDump();

        foreach (Garden.Slot slot in Garden.Slots())
        {
            Garden.Occupant? occupant = Garden.Occupied(slot.GridX, slot.GridY);
            if (occupant == null) { _ages.Remove(slot.Index); continue; }

            PottedPlantAge age = occupant.Value.Age;

            if (!_ages.TryGetValue(slot.Index, out PottedPlantAge was))
            {
                _ages[slot.Index] = age;
                continue;
            }

            if (was == age) continue;
            _ages[slot.Index] = age;

            Core.Log.Msg($"[garden] slot {slot.Index + 1} grew: {was} -> {age}");
            Speech.Say(Strings.T("garden.grew", Lawn.PlantName(occupant.Value.Type),
                                 Garden.AgeName(age)),
                       interrupt: false, context: "garden growth", allowRepeat: true);
        }
    }
}
