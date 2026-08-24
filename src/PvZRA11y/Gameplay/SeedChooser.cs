using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppReloaded.TreeStateActivities;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using PvZRA11y.UI;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The screen where a level's plants are chosen.
///
/// Everything a sighted player uses here is on screen at once and none of it is text: the
/// row of zombie portraits along the top telling you what this level will send, the
/// backdrop telling you whether it is daytime, night or the pool, and the count of empty
/// slots left in the deck. All three decide which plants to take, and none of them can be
/// reached by walking the controls.
///
/// The wording follows the original PvZ accessibility mod, which puts the zombie list and
/// the level type on the same two keys used everywhere else.
/// </summary>
public static class SeedChooser
{
    /// <summary>The game's panel id for this screen.</summary>
    private const string PanelId = "seedChooser";

    private static int _lastRemaining = int.MinValue;
    private static int _lastDeckSize = -1;

    /// <summary>True while the plant chooser is the screen in front.</summary>
    public static bool IsActive => PanelScope.FrontPanelId == PanelId;

    /// <summary>
    /// Says which zombies this level will send.
    ///
    /// Taken from the level's own data rather than from what has spawned, because the point
    /// is to know before the level starts — that is the whole reason the portraits are shown
    /// on this screen.
    /// </summary>
    public static void AnnounceZombieTypes()
    {
        var names = ZombieTypesInLevel();

        Speech.SayVerbatim(names.Count == 0
            ? Strings.T("chooser.no_zombies")
            : string.Join(", ", names) + ",", "chooser zombies");
    }

    /// <summary>
    /// Every zombie this level can send.
    ///
    /// The level's own list is the answer, and it took two wrong turns to be sure of that.
    ///
    /// It looked incomplete: level 1-8 declares Zombie, Flag and Cone-head, and a Bucket-head
    /// seemed to turn up anyway. So the mod started adding every zombie whose definition said
    /// it had debuted by this level. That was wrong. Reading the shipped level table settles
    /// it — 1-7 and 1-9 do include Bucket-heads and 1-8 genuinely does not, and the sighting
    /// came from somewhere else. The debut rule then over-reported everywhere: it offered
    /// Cone-heads, Pole-vaulters and Bucket-heads on level 2-1, which sends Zombies, Flags
    /// and Newspapers and nothing else.
    ///
    /// Every level in the game carries its own hand-picked set. Nothing needs deriving.
    ///
    /// The other sources are still asked, and still logged, but no longer believed. Two of
    /// them return a subset of the level's list and two return something else entirely —
    /// GetIntroducedZombieType answers Pail on level 8, which is what sent this astray, and
    /// whatever it means it is not "what this level sends".
    /// </summary>
    private static List<string> ZombieTypesInLevel()
    {
        var candidates = new HashSet<ZombieType>();
        var sources = new List<string>();

        Board board = Lawn.BoardRef;
        GameplayActivity app = AppRef(board);

        LevelEntryData level = null;
        try { level = Lawn.LevelData(); } catch { /* reported as unavailable below */ }

        int levelNumber = 0;
        try { if (board != null) levelNumber = board.mLevel; } catch { }

        // --- believed ---------------------------------------------------------------
        // Three ways of asking for the same authored list. Merged rather than one picked,
        // so that a single one being unavailable cannot leave the player with nothing.

        sources.Add(Gather("level data", candidates, add =>
        {
            var declared = level?.ZombieTypes;
            if (declared == null) return false;
            for (int i = 0; i < declared.Length; i++) add(declared[i]);
            return true;
        }));

        sources.Add(Gather("activity", candidates, add =>
        {
            var types = app?.ZombieTypes;
            if (types == null) return false;
            for (int i = 0; i < types.Length; i++) add(types[i]);
            return true;
        }));

        sources.Add(Gather("level has", candidates, add =>
        {
            if (app == null || level == null) return false;
            ForEachType(t => { if (app.LevelHasZombieType(level, t)) add(t); });
            return true;
        }));

        // --- logged only ------------------------------------------------------------
        // Kept because a disagreement here is worth seeing. Deliberately not merged in:
        // the first two answer with a subset, and the last two with something that is not
        // this question.

        var ignored = new HashSet<ZombieType>();

        sources.Add("(not spoken) " + Gather("can spawn", ignored, add =>
        {
            if (board == null) return false;
            ForEachType(t => { if (board.CanZombieSpawnOnLevel(t, levelNumber)) add(t); });
            return true;
        }));

        sources.Add("(not spoken) " + Gather("allowed", ignored, add =>
        {
            var allowed = board?.mZombieAllowed;
            if (allowed == null) return false;
            for (int i = 0; i < allowed.Length && i < TypeCount; i++)
                if (allowed[i]) add((ZombieType)i);
            return true;
        }));

        sources.Add("(not spoken) " + Gather("introduced", ignored, add =>
        {
            if (board == null) return false;
            add(board.GetIntroducedZombieType());
            return true;
        }));

        var ordered = new List<ZombieType>(candidates);
        ordered.Sort((a, b) => ((int)a).CompareTo((int)b));

        var names = new List<string>();
        foreach (ZombieType type in ordered) names.Add(ShortZombieName(type));

        if (Config.Settings.VerboseLogging.Value)
        {
            string title = "?";
            try { title = level?.FullLevelName ?? "?"; } catch { }

            Core.Log.Msg($"[chooser] zombies for \"{title}\" (mLevel {levelNumber})");
            foreach (string line in sources) Core.Log.Msg("[chooser]   " + line);
            Core.Log.Msg($"[chooser]   spoken ({names.Count}): {string.Join(", ", names)}");
        }

        return names;
    }

    /// <summary>
    /// Runs one source and records what it contributed.
    ///
    /// The report separates "this source named these" from "this source could not be asked",
    /// because those two want opposite fixes and both otherwise look like an empty list.
    /// </summary>
    private static string Gather(string label, HashSet<ZombieType> into, Func<Action<ZombieType>, bool> source)
    {
        var mine = new List<ZombieType>();
        bool answered;

        try
        {
            answered = source(t =>
            {
                if (t == ZombieType.Invalid || (int)t < 0 || (int)t >= TypeCount) return;
                if (!mine.Contains(t)) mine.Add(t);
            });
        }
        catch (Exception ex)
        {
            return $"{label,-22}: could not be asked ({ex.GetType().Name}: {ex.Message})";
        }

        if (!answered) return $"{label,-22}: not available";

        var fresh = new List<ZombieType>();
        foreach (ZombieType t in mine) if (into.Add(t)) fresh.Add(t);

        string added = fresh.Count == 0 ? "" : $"   [+{NamesOf(fresh)}]";
        return $"{label,-22}: ({mine.Count}) {NamesOf(mine)}{added}";
    }

    private static void ForEachType(Action<ZombieType> body)
    {
        for (int i = 0; i < TypeCount; i++)
        {
            var type = (ZombieType)i;
            if (type == ZombieType.Invalid) continue;
            try { body(type); } catch { /* one bad type must not lose the rest */ }
        }
    }

    private static string NamesOf(IEnumerable<ZombieType> types)
    {
        var parts = new List<string>();
        foreach (ZombieType t in types)
        {
            try { parts.Add(t.ToString()); } catch { parts.Add("?"); }
        }
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    /// <summary>The gameplay activity, from whichever of the two objects has it this frame.</summary>
    private static GameplayActivity AppRef(Board board)
    {
        try { var a = board?.mApp; if (a != null) return a; } catch { }
        try { return _screen?.mApp; } catch { }
        return null;
    }

    private const int TypeCount = (int)ZombieType.NumZombieTypes;

    /// <summary>Says what kind of level this is — the backdrop a sighted player simply sees.</summary>
    public static void AnnounceLevelType()
    {
        LevelEntryData level = Lawn.LevelData();
        if (level == null)
        {
            Speech.SayVerbatim(Strings.T("chooser.no_level"), "chooser level");
            return;
        }

        try
        {
            var parts = new List<string>(2);

            string areaKey = "area." + level.GameArea;
            parts.Add(Strings.Has(areaKey) ? Strings.T(areaKey) : UiText.Prettify(level.GameArea.ToString()));

            string name = level.FullLevelName;
            if (!string.IsNullOrWhiteSpace(name)) parts.Insert(0, UiText.Collapse(name));

            Speech.SayVerbatim(string.Join(", ", parts), "chooser level");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the level type: {ex.Message}");
            Speech.SayVerbatim(Strings.T("chooser.no_level"), "chooser level");
        }
    }

    /// <summary>How many deck slots are still empty, or -1 when it cannot be worked out.</summary>
    public static int RemainingSlots()
    {
        // Counting empty entries in the packet array gave ten on a six-slot deck: the array
        // is allocated at the largest size the game supports and only partly used. The
        // screen knows how many this level actually asks for.
        try
        {
            if (_screen == null) return -1;

            // mNumSeedsToChoose reads zero on this screen, so the deck itself is asked how
            // many slots it has. Counting empty entries in the packet array does not work
            // either: that array is allocated at the largest size the game supports.
            int wanted = DeckSize();
            if (wanted <= 0) return -1;

            int picked = 0;
            var seeds = Seeds();
            if (seeds != null)
            {
                for (int i = 0; i < seeds.Count; i++)
                    if (IsPicked(seeds[i])) picked++;
            }

            return Math.Max(0, wanted - picked);
        }
        catch { return -1; }
    }

    /// <summary>
    /// Announces the deck filling up, once per change.
    ///
    /// Said as its own sentence rather than folded into whatever starts the level, because
    /// the moment it becomes possible to start is the thing worth hearing — waiting until
    /// you press the key to be told you cannot is a wasted press.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive)
        {
            _lastRemaining = int.MinValue;
            _lastDeckSize = -1;

            // Re-based per screen. The grid is rebuilt each time and the plants owned can
            // have changed since the last level, so a position carried over would point at
            // a different plant than it did.
            _slot = -1;
            _deckCursor = -1;
            _grid = null;
            _reportedCardCount = int.MinValue;
            return;
        }

        // Start on the first plant rather than nowhere, so the first arrow press is a step
        // and not a jump. Silent: the screen announcing itself is enough.
        if (_slot < 0)
        {
            var opening = Offered();
            if (opening.Count > 0)
            {
                _slot = 0;
                _cursor = opening[0];
                Core.Log.Msg($"[chooser] grid is {Columns()} columns, {opening.Count} plants owned," +
                             $" {Cards().Count} cards on screen");
            }
        }

        // The deck is not sized until the game has configured it for this level: asked too
        // early it answers ten, the largest it supports, and only later six. Announcing that
        // first answer told the player to pick four plants that do not exist.
        int size = DeckSize();
        if (size != _lastDeckSize)
        {
            if (_lastDeckSize > 0) Core.Log.Msg($"[chooser] deck resized {_lastDeckSize} -> {size}");
            _lastDeckSize = size;
            _lastRemaining = int.MinValue;
            return;
        }

        int remaining = RemainingSlots();
        if (remaining < 0 || remaining == _lastRemaining) return;

        bool first = _lastRemaining == int.MinValue;
        _lastRemaining = remaining;

        // The first reading is the screen simply opening; the plant that took focus is
        // announcement enough without a slot count on top of it.
        if (first) return;

        // The ready message names the key rather than saying "press Start", because there
        // is no Start here: Escape already belongs to the game's pause.
        Speech.Say(remaining == 0
            ? Strings.T("chooser.ready", Config.Settings.KeyStartLevel.Value)
            : Strings.T(remaining == 1 ? "chooser.need_one" : "chooser.need_more", remaining),
            interrupt: false, context: "deck status");
    }

    private static SeedChooserScreen _screen;

    public static void NoteScreen(SeedChooserScreen screen)
    {
        if (screen != null) _screen = screen;
    }

    /// <summary>Our own position in the list of plants, independent of any control.</summary>
    private static int _cursor = -1;

    /// <summary>
    /// Every plant this level offers, in the order the chooser lays them out.
    ///
    /// Read from the screen's own list rather than from the cards on display, because this
    /// list holds every plant in the game while the cards hold only the ones this save owns.
    /// The two are kept in step by Offered(), which filters this list down to exactly what
    /// is drawn.
    /// </summary>
    private static Il2CppSystem.Collections.Generic.List<ChosenSeed> Seeds()
    {
        try { return _screen?.mChosenSeeds; }
        catch { return null; }
    }

    /// <summary>Steps through the plants on offer and describes each one.</summary>
    private static UnityEngine.UI.GridLayoutGroup _grid;
    private static int _reportedCardCount = int.MinValue;

    /// <summary>
    /// The layout that arranges the cards.
    ///
    /// Found through the controls the mod can already see rather than by a path through the
    /// hierarchy, so it survives the game moving things about.
    /// </summary>
    private static UnityEngine.UI.GridLayoutGroup GridLayout()
    {
        try { if (_grid != null) return _grid; }
        catch { _grid = null; }

        List<UnityEngine.UI.Selectable> visible;
        try { visible = UI.Focus.CollectVisible(); }
        catch { return null; }

        for (int i = 0; i < visible.Count; i++)
        {
            UnityEngine.UI.Selectable s = visible[i];
            if (s == null) continue;

            try { if (UI.PanelScope.PanelIdOf(s) != PanelId) continue; }
            catch { continue; }

            UnityEngine.UI.GridLayoutGroup found = null;
            try { found = s.GetComponent<UnityEngine.UI.GridLayoutGroup>(); } catch { }
            if (found == null)
                try { found = s.GetComponentInParent<UnityEngine.UI.GridLayoutGroup>(); } catch { }

            if (found != null) { _grid = found; return found; }
        }

        return null;
    }

    /// <summary>How many cards to a row, asked of the live layout rather than assumed.</summary>
    private static int Columns()
    {
        var grid = GridLayout();
        if (grid == null) return FallbackColumns;

        try
        {
            if (grid.constraint != UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount)
                return FallbackColumns;

            int n = grid.constraintCount;
            return n > 0 ? n : FallbackColumns;
        }
        catch { return FallbackColumns; }
    }

    /// <summary>The card objects, in the order the layout arranges them.</summary>
    private static List<UnityEngine.UI.Selectable> Cards()
    {
        var cards = new List<UnityEngine.UI.Selectable>();

        var grid = GridLayout();
        if (grid == null) return cards;

        UnityEngine.Transform parent;
        int count;
        try { parent = grid.transform; count = parent.childCount; }
        catch { return cards; }

        for (int i = 0; i < count; i++)
        {
            UnityEngine.Transform child;
            try { child = parent.GetChild(i); }
            catch { continue; }
            if (child == null) continue;

            try { if (!child.gameObject.activeInHierarchy) continue; }
            catch { continue; }

            UnityEngine.UI.Selectable button = null;
            try { button = child.GetComponentInChildren<UnityEngine.UI.Selectable>(); }
            catch { }

            cards.Add(button);
        }

        return cards;
    }

    /// <summary>
    /// Moves the visible highlight to the card being spoken about.
    ///
    /// Only when the two lists agree. Pointing the highlight at the wrong plant would be
    /// worse than leaving it still — these sessions get recorded, and a sighted viewer
    /// would see one plant while hearing another.
    /// </summary>
    private static void SyncSelection(List<int> offered)
    {
        var cards = Cards();

        if (cards.Count != offered.Count)
        {
            if (_reportedCardCount != cards.Count)
            {
                _reportedCardCount = cards.Count;
                Core.Log.Msg($"[chooser] {cards.Count} cards on screen but {offered.Count} plants offered;" +
                             " leaving the highlight where it is");
            }
            return;
        }

        if (_slot < 0 || _slot >= cards.Count) return;

        try { UI.Focus.AdoptSelection(cards[_slot]); }
        catch (Exception ex) { Core.Log.Warning($"Could not move the chooser highlight: {ex.Message}"); }
    }

    /// <summary>Where we are in the grid of cards, counting across rows.</summary>
    private static int _slot = -1;

    /// <summary>Eight is what the shipped screen lays out; only a fallback if it will not say.</summary>
    private const int FallbackColumns = 8;

    /// <summary>
    /// The plants the screen actually lays out, as indices into its own list.
    ///
    /// Packed with no gaps, because that is how the cards are drawn: the grid binds the
    /// plants this save owns, and a plant that is not owned has no card at all.
    /// </summary>
    private static List<int> Offered()
    {
        var offered = new List<int>(49);

        var seeds = Seeds();
        if (seeds == null) return offered;

        for (int i = 0; i < seeds.Count; i++)
        {
            ChosenSeed seed;
            try { seed = seeds[i]; }
            catch { continue; }

            if (Owned(seed)) offered.Add(i);
        }

        return offered;
    }

    /// <summary>
    /// One cell across the grid the screen is drawing. dx and dy are -1, 0 or 1.
    ///
    /// The old version added eight to a position in the game's list of all forty-nine
    /// plants and then skipped the ones not owned, which is not a row at all: with
    /// twenty-one plants owned, one press of Down went from the bottom-left card to the
    /// top-right one and nothing said so. Down now means one row of cards, counted over
    /// the plants that actually have cards.
    /// </summary>
    public static bool Move(int dx, int dy)
    {
        if (!IsActive) return false;

        var offered = Offered();
        if (offered.Count == 0)
        {
            Speech.Say(Strings.T("chooser.no_plants"), context: "chooser");
            return true;
        }

        int columns = Columns();
        int rows = (offered.Count + columns - 1) / columns;

        if (_slot < 0 || _slot >= offered.Count)
        {
            _slot = 0;
            Land(offered, columns, rows, Detail.Row);
            return true;
        }

        int row = _slot / columns;
        int column = _slot % columns;

        if (dx != 0)
        {
            int target = column + dx;

            if (target < 0)
            {
                Speech.SayVerbatim(Strings.T("chooser.edge_left"), "chooser edge");
                return true;
            }

            if (target >= columns || row * columns + target >= offered.Count)
            {
                Speech.SayVerbatim(Strings.T("chooser.edge_right"), "chooser edge");
                return true;
            }

            _slot = row * columns + target;
            Land(offered, columns, rows, Detail.Plain);
            return true;
        }

        int wanted = row + dy;

        if (wanted < 0) { Speech.SayVerbatim(Strings.T("chooser.edge_top"), "chooser edge"); return true; }
        if (wanted >= rows) { Speech.SayVerbatim(Strings.T("chooser.edge_bottom"), "chooser edge"); return true; }

        int landing = wanted * columns + column;

        // The last row is usually short. Rather than refuse the step, land on its last
        // plant and say the whole position, because the column has moved as well as the row.
        bool shifted = landing >= offered.Count;
        if (shifted) landing = offered.Count - 1;

        _slot = landing;
        Land(offered, columns, rows, shifted ? Detail.Position : Detail.Row);
        return true;
    }

    /// <summary>How much position to say along with the plant.</summary>
    private enum Detail { Plain, Row, Position }

    private static void Land(List<int> offered, int columns, int rows, Detail detail)
    {
        _cursor = offered[_slot];
        SyncSelection(offered);

        ChosenSeed seed = CurrentSeed();
        if (seed == null)
        {
            Speech.SayVerbatim(Strings.T("chooser.no_plants"), "chooser");
            return;
        }

        string line = Describe(seed);
        int row = _slot / columns;

        // Said only when it changed. Repeating the column on every sideways step would put
        // two numbers on the end of every plant for no new information.
        if (detail == Detail.Row)
            line += ", " + Strings.T("chooser.row", row + 1, rows);
        else if (detail == Detail.Position)
            line += ", " + Strings.T("chooser.at", row + 1, rows, (_slot % columns) + 1, RowWidth(offered, columns, row));

        Speech.SayVerbatim(line, "chooser plant");
    }

    /// <summary>How many plants are really in a row. The last one is usually short.</summary>
    private static int RowWidth(List<int> offered, int columns, int row)
        => Math.Min(columns, offered.Count - row * columns);

    /// <summary>Describes the plant the cursor is on, without moving it.</summary>
    public static void AnnounceCurrent()
    {
        ChosenSeed seed = CurrentSeed();
        if (seed == null)
        {
            Speech.SayVerbatim(Strings.T("chooser.no_plants"), "chooser");
            return;
        }

        string line = Describe(seed);

        // Asked for on purpose, so it carries the whole position and the size of the grid —
        // the thing walking the rows does not repeat every step.
        var offered = Offered();
        if (offered.Count > 0 && _slot >= 0 && _slot < offered.Count)
        {
            int columns = Columns();
            int rows = (offered.Count + columns - 1) / columns;
            int row = _slot / columns;

            line += ", " + Strings.T("chooser.at", row + 1, rows, (_slot % columns) + 1,
                                     RowWidth(offered, columns, row));
            line += ". " + Strings.T("chooser.offered", offered.Count);
        }

        Speech.SayVerbatim(line, "chooser plant");
    }

    /// <summary>
    /// Takes or returns the plant under the cursor, through the game's own click handler so
    /// that everything it refuses — a plant already taken, one this level does not allow —
    /// is refused here too.
    /// </summary>
    public static bool Pick()
    {
        if (!IsActive) return false;

        ChosenSeed seed = CurrentSeed();
        if (seed == null) return false;

        bool wasPicked = IsPicked(seed);

        try
        {
            _screen.ClickedSeedInChooser(seed, 0);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not pick a plant: {ex.Message}");
            return false;
        }

        // Report what happened rather than what was attempted. The game silently refuses
        // plants this save has not unlocked, and announcing the attempt made those sound
        // taken when nothing had changed.
        bool nowPicked = IsPicked(seed);
        string name = Lawn.PlantName(SafeType(seed));

        if (nowPicked == wasPicked)
        {
            Core.Log.Msg($"[chooser] the game refused \"{name}\"");
            Speech.SayVerbatim(Strings.T("chooser.refused", name), "chooser pick");
            return true;
        }

        Speech.SayVerbatim(Strings.T(nowPicked ? "chooser.took" : "chooser.returned", name), "chooser pick");
        return true;
    }

    /// <summary>
    /// Whether the player may actually take this plant, asked of the game rather than
    /// guessed from the save. A locked plant is on the list like any other.
    /// </summary>
    /// <summary>
    /// Whether this save owns the plant at all, and so whether it has a card on screen.
    ///
    /// This, and only this, decides membership of the grid. The two questions used to be
    /// one, and that was wrong: a plant this level forbids still has a card, so excluding
    /// it shortened the mod's list below the number of cards and put every row out of step
    /// with the screen.
    /// </summary>
    private static bool Owned(ChosenSeed seed)
    {
        if (seed == null) return false;
        try
        {
            // The chooser lists all forty-nine plants regardless of the save, and every one
            // of them reports SeedNotAllowedToPick as false — that flag means something
            // else. Whether this player actually owns the plant is the activity's question.
            var app = _screen.mApp;
            return app == null || app.HasSeedType(seed.mSeedType);
        }
        catch { return true; }
    }

    /// <summary>
    /// Whether this level lets the plant be taken. Spoken as a qualifier, never used to
    /// hide a plant — its card is on screen either way.
    /// </summary>
    private static bool Allowed(ChosenSeed seed)
    {
        if (seed == null) return false;
        try
        {
            SeedType type = seed.mSeedType;
            if (_screen.SeedNotAllowedToPick(type)) return false;
            if (_screen.SeedNotAllowedDuringTrial(type)) return false;
            return true;
        }
        catch { return true; }
    }

    /// <summary>Kept for the places that only ask "could the player take this at all".</summary>
    private static bool CanTake(ChosenSeed seed) => Owned(seed) && Allowed(seed);

    /// <summary>Starts the level. The game refuses if the deck is not full, so we ask first.</summary>
    public static bool Start()
    {
        if (!IsActive) return false;

        int remaining = RemainingSlots();

        // A negative answer means we could not work out how many are needed. Starting anyway
        // let a level begin with an empty deck, so an unknown count now refuses.
        if (remaining != 0)
        {
            Core.Log.Msg($"[chooser] refusing to start, {remaining} slots reported outstanding");
            Speech.SayVerbatim(remaining < 0
                    ? Strings.T("chooser.cannot_start")
                    : Strings.T(remaining == 1 ? "chooser.need_one" : "chooser.need_more", remaining),
                "chooser start");
            return true;
        }

        try
        {
            _screen.OnStartButton();
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not start the level: {ex.Message}");
            return false;
        }
    }

    /// <summary>How many plants this level's deck holds.</summary>
    private static int DeckSize()
    {
        try
        {
            var infos = _screen?.m_seedBankInfos;
            if (infos == null || infos.Count == 0) return 0;
            return infos[0]?.mSeedBank?.NumPackets ?? 0;
        }
        catch { return 0; }
    }

    private static SeedType SafeType(ChosenSeed seed)
    {
        try { return seed.mSeedType; }
        catch { return SeedType.None; }
    }

    /// <summary>
    /// Writes the whole chooser to the log: every plant, its state, and what the game says
    /// about whether it may be taken. Which of those fields actually means "unlocked" is not
    /// documented anywhere, and guessing it wrong twice was enough.
    /// </summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("---- plant chooser ----");

        if (_screen == null) { sb.AppendLine("  not open"); return; }

        try
        {
            sb.AppendLine($"  deck size       : {DeckSize()}   (mNumSeedsToChoose reports {_screen.mNumSeedsToChoose})");
            sb.AppendLine($"  screen ready    : {_screen.mIsReady}");
            sb.AppendLine($"  remaining slots : {RemainingSlots()}");

            var seeds = Seeds();
            sb.AppendLine($"  plants listed   : {(seeds == null ? 0 : seeds.Count)}");
            if (seeds == null) return;

            for (int i = 0; i < seeds.Count; i++)
            {
                ChosenSeed seed = seeds[i];
                if (seed == null) continue;

                SeedType type = SafeType(seed);
                string notAllowed, trial;
                try { notAllowed = _screen.SeedNotAllowedToPick(type).ToString(); } catch { notAllowed = "?"; }
                try { trial = _screen.SeedNotAllowedDuringTrial(type).ToString(); } catch { trial = "?"; }

                string state, notSuggested;
                try { state = seed.mSeedState.ToString(); } catch { state = "?"; }
                try { notSuggested = seed.mNotSuggested.ToString(); } catch { notSuggested = "?"; }

                string owned;
                try { owned = (_screen.mApp?.HasSeedType(type)).ToString(); } catch { owned = "?"; }

                sb.AppendLine($"      {type,-18} owned={owned,-6} state={state,-20}" +
                              $" notAllowed={notAllowed,-6} trial={trial,-6} notSuggested={notSuggested}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  dump failed: {ex.Message}");
        }
    }

    private static ChosenSeed CurrentSeed()
    {
        var seeds = Seeds();
        if (seeds == null || _cursor < 0 || _cursor >= seeds.Count) return null;
        try { return seeds[_cursor]; }
        catch { return null; }
    }

    /// <summary>
    /// One plant, worded as the original PvZ accessibility mod words it:
    ///
    ///     Sunflower: 50: Gives you additional sun
    ///     Picked. Peashooter: 100: Shoots peas at the enemy
    ///
    /// The cost and the description come from the game's own plant data, so they are already
    /// in the player's language and cannot drift from what the game actually charges.
    /// </summary>
    private static string Describe(ChosenSeed seed)
    {
        try
        {
            SeedType type = seed.mSeedType;
            string name = Lawn.PlantName(type);

            var parts = new List<string>(3) { name };

            PlantDefinition definition = DefinitionFor(type);
            if (definition != null)
            {
                parts.Add(definition.SeedCost.ToString());

                // PlantToolTip is a key into the game's string table, not a sentence.
                string tip = DescriptionFor(type, definition);
                if (!string.IsNullOrWhiteSpace(tip)) parts.Add(UiText.Collapse(tip));
            }

            string line = string.Join(": ", parts);

            // Outermost first, so the thing that stops you is the first word you hear. A
            // plant the level forbids is worth knowing before its name, not after its
            // description.
            if (IsPicked(seed)) line = Strings.T("chooser.picked", line);
            if (NotSuggested(seed)) line = Strings.T("chooser.not_suggested", line);
            if (!Allowed(seed)) line = Strings.T("chooser.not_allowed", line);

            return line;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not describe a plant: {ex.Message}");
            return Strings.T("chooser.no_plants");
        }
    }

    /// <summary>
    /// The game's own hint that a plant is a poor fit for this level — an aquatic plant on
    /// a dry lawn, and so on. Read from the same field the diagnostic already prints, and
    /// spoken because a sighted player sees the card dimmed.
    /// </summary>
    private static bool NotSuggested(ChosenSeed seed)
    {
        try { return seed.mNotSuggested; }
        catch { return false; }
    }

    private static bool IsPicked(ChosenSeed seed)
    {
        try
        {
            return seed.mSeedState is ChosenSeedState.SeedInBank or ChosenSeedState.SeedFlyingToBank;
        }
        catch { return false; }
    }

    /// <summary>
    /// A one-line description of a plant.
    ///
    /// Written into this mod's own string table rather than taken from the game, after both
    /// of the game's sources turned out to be unusable here. Its tooltip strings resolve to
    /// nothing while this screen is open — the text is loaded in banks and that one is not
    /// among them — and the almanac holds the full encyclopaedia entry, several sentences of
    /// flavour, which is far too much when stepping through forty plants one at a time.
    ///
    /// The almanac is still used as a fallback for anything without an entry here, cut down
    /// to its first sentence. The original PvZ accessibility mod ships its own descriptions
    /// for the same reason.
    /// </summary>
    private static string DescriptionFor(SeedType type, PlantDefinition definition)
    {
        string key = "plant.tip." + type;
        if (Strings.Has(key)) return Strings.T(key);

        string almanac = FirstSentence(AlmanacDescription(type));
        if (Config.Settings.VerboseLogging.Value)
            Core.Log.Msg($"[chooser] {type} has no written description; almanac says \"{almanac ?? "<none>"}\"");

        return LooksLikeWords(almanac) ? almanac : null;
    }

    /// <summary>The first sentence of a longer passage, so a description stays one line.</summary>
    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string clean = UiText.Collapse(text);
        int stop = clean.IndexOf(". ", StringComparison.Ordinal);
        return stop > 0 ? clean[..(stop + 1)] : clean;
    }

    /// <summary>The almanac's own entry for a plant, which may be text or another key.</summary>
    private static string AlmanacDescription(SeedType type)
    {
        try
        {
            var data = _screen?.mApp?.m_dataService?.TryCast<Il2CppReloaded.Services.DataService>();
            var entries = data?.PlantAlmanacData;
            if (entries == null) return null;

            // The interop view of a read-only list offers indexing and nothing else — no
            // count — so it is walked until it runs out. The bound is a backstop, not a
            // guess at the size.
            const int Limit = 200;
            for (int i = 0; i < Limit; i++)
            {
                AlmanacEntryData entry;
                try { entry = entries[i]; }
                catch { break; }

                if (entry == null) break;
                if (entry.SeedType != type) continue;
                return entry.EntryDescription;
            }
        }
        catch { /* no almanac data */ }

        return null;
    }

    /// <summary>
    /// Whether a string is prose rather than an identifier. Keys in this game are upper case
    /// with underscores and no spaces, which prose never is.
    /// </summary>
    private static bool LooksLikeWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains(' ')) return true;
        return value != value.ToUpperInvariant();
    }

    /// <summary>The game's own record for a plant: its cost and the line the almanac shows.</summary>
    private static PlantDefinition DefinitionFor(SeedType type)
    {
        try
        {
            var data = _screen?.mApp?.m_dataService?.TryCast<Il2CppReloaded.Services.DataService>();
            return data?.GetPlantDefinition(type);
        }
        catch { return null; }
    }

    private static int _deckCursor = -1;

    /// <summary>
    /// Steps through the deck, saying what is in each slot and which are still empty.
    ///
    /// Picking plants is a two-sided job: what is left in front of you, and what you have
    /// already taken. The cards answer the first, and nothing answered the second — the deck
    /// is off to one side and never takes focus.
    /// </summary>
    public static bool CycleDeck(int delta)
    {
        if (!IsActive) return false;

        try
        {
            var packets = Lawn.BoardRef?.SeedBanks[0]?.SeedPackets;
            if (packets == null || packets.Length == 0)
            {
                Speech.Say(Strings.T("seeds.no_bank"), context: "deck");
                return true;
            }

            int count = packets.Length;
            _deckCursor = _deckCursor < 0
                ? (delta > 0 ? 0 : count - 1)
                : ((_deckCursor + delta) % count + count) % count;

            SeedPacket packet = packets[_deckCursor];
            SeedType type = packet == null ? SeedType.None : packet.PacketType;

            Speech.SayVerbatim(type == SeedType.None
                ? Strings.T("chooser.slot_empty", _deckCursor + 1)
                : Strings.T("chooser.slot_filled", _deckCursor + 1, Lawn.PlantName(type)),
                "deck");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the deck: {ex.Message}");
            return false;
        }
    }

    public static void Reset()
    {
        _lastRemaining = int.MinValue;
        _lastDeckSize = -1;
        _deckCursor = -1;
        _cursor = -1;
    }

    /// <summary>Zombie names read best without the trailing word in a list of eight.</summary>
    private static string ShortZombieName(ZombieType type)
    {
        string key = "zombie." + type;
        string name = Strings.Has(key) ? Strings.T(key) : UiText.Prettify(type.ToString());

        const string suffix = " zombie";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^suffix.Length];

        return name;
    }
}
