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
    /// This took three attempts, and the reason is worth writing down. The level's own
    /// ZombieTypes array declares Zombie, Flag and Cone-head for level 1-8, and the level
    /// sends Bucket-heads. Board.CanZombieSpawnOnLevel, asked about all forty-two types,
    /// answered with a subset of that same array — so the two were never independent sources
    /// at all, and merging them could only ever reproduce one short list.
    ///
    /// What finally sits outside that chain is the game's own zombie table: every
    /// ZombieDefinition carries the level it debuts on. Bucket-head's says level 8, which is
    /// where it was heard. So the candidates are gathered from every source the game will
    /// answer to, including that table, and then filtered by asking the board whether any row
    /// on it could actually hold each one — which is what keeps pool zombies off a day lawn.
    ///
    /// Each source is logged on its own line. Two of them have now been caught quietly
    /// returning a short list rather than failing, so "it answered" is not evidence that it
    /// answered fully, and the next disagreement should be visible without another play
    /// session spent finding it.
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

        // The level's own declared list. Never names a wrong zombie; known to miss some.
        sources.Add(Gather("level data", candidates, add =>
        {
            var declared = level?.ZombieTypes;
            if (declared == null) return false;
            for (int i = 0; i < declared.Length; i++) add(declared[i]);
            return true;
        }));

        // The activity keeps its own copy, which need not be the same array.
        sources.Add(Gather("activity", candidates, add =>
        {
            var types = app?.ZombieTypes;
            if (types == null) return false;
            for (int i = 0; i < types.Length; i++) add(types[i]);
            return true;
        }));

        // The game asked one type at a time, three different ways.
        sources.Add(Gather("level has", candidates, add =>
        {
            if (app == null || level == null) return false;
            ForEachType(t => { if (app.LevelHasZombieType(level, t)) add(t); });
            return true;
        }));

        sources.Add(Gather("can spawn", candidates, add =>
        {
            if (board == null) return false;
            ForEachType(t => { if (board.CanZombieSpawnOnLevel(t, levelNumber)) add(t); });
            return true;
        }));

        sources.Add(Gather("allowed", candidates, add =>
        {
            var allowed = board?.mZombieAllowed;
            if (allowed == null) return false;
            for (int i = 0; i < allowed.Length && i < TypeCount; i++)
                if (allowed[i]) add((ZombieType)i);
            return true;
        }));

        sources.Add(Gather("introduced", candidates, add =>
        {
            if (board == null) return false;
            add(board.GetIntroducedZombieType());
            return true;
        }));

        // The zombie table: each definition names the level its zombie first appears on.
        // The only source here that does not read the level's authored list.
        sources.Add(Gather("debut by level " + levelNumber, candidates, add =>
        {
            if (app == null || levelNumber <= 0) return false;
            ForEachType(t =>
            {
                ZombieDefinition def = app.GetZombieDefinition(t);
                if (def == null) return;

                // Weight zero means the picker never draws it. The ones that turn up anyway,
                // like the flag zombie, are named by one of the sources above.
                if (def.Weight <= 0) return;

                int debut = def.FirstLevel;
                if (debut > 0 && debut <= levelNumber) add(t);
            });
            return true;
        }));

        var kept = FilterToThisBoard(board, candidates, out var dropped);

        var names = new List<string>();
        foreach (ZombieType type in kept) names.Add(ShortZombieName(type));

        if (Config.Settings.VerboseLogging.Value)
        {
            string title = "?";
            try { title = level?.FullLevelName ?? "?"; } catch { }

            Core.Log.Msg($"[chooser] zombies for \"{title}\" (mLevel {levelNumber})");
            foreach (string line in sources) Core.Log.Msg("[chooser]   " + line);
            if (dropped.Count > 0)
                Core.Log.Msg($"[chooser]   no row can hold : {NamesOf(dropped)}");
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

    /// <summary>
    /// Drops anything no row of this board could hold.
    ///
    /// The zombie table says when a zombie debuts, not where, so on a daytime lawn it will
    /// happily offer the ones that swim. The board knows better and is asked directly.
    /// A type is kept whenever the board declines to answer: announcing one zombie too many
    /// costs a moment, and missing one costs the level.
    /// </summary>
    private static List<ZombieType> FilterToThisBoard(Board board, HashSet<ZombieType> candidates, out List<ZombieType> dropped)
    {
        dropped = new List<ZombieType>();

        var ordered = new List<ZombieType>(candidates);
        ordered.Sort((a, b) => ((int)a).CompareTo((int)b));

        int rows = 0;
        try { if (board != null) rows = board.GetNumRows(); } catch { }
        if (board == null || rows <= 0) return ordered;

        var kept = new List<ZombieType>();
        foreach (ZombieType type in ordered)
        {
            bool anyRow = false;
            bool asked = true;

            for (int row = 0; row < rows; row++)
            {
                try
                {
                    if (board.RowCanHaveZombieType(row, type)) { anyRow = true; break; }
                }
                catch { asked = false; break; }
            }

            if (!asked || anyRow) kept.Add(type);
            else dropped.Add(type);
        }

        // If the filter rejected everything, it is the filter that is wrong, not the level.
        return kept.Count == 0 ? ordered : kept;
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
            return;
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
    /// Read from the screen's own list rather than from the cards on display. The chooser
    /// recycles seven card objects for forty-odd plants, so the controls are a window onto
    /// the list, not the list itself — walking them reaches seven plants and stops.
    /// </summary>
    private static Il2CppSystem.Collections.Generic.List<ChosenSeed> Seeds()
    {
        try { return _screen?.mChosenSeeds; }
        catch { return null; }
    }

    /// <summary>Steps through the plants on offer and describes each one.</summary>
    public static bool Move(int delta)
    {
        if (!IsActive) return false;

        var seeds = Seeds();
        if (seeds == null || seeds.Count == 0)
        {
            Speech.Say(Strings.T("chooser.no_plants"), context: "chooser");
            return true;
        }

        // Step over anything this save has not unlocked. The list holds every plant in the
        // game, and the ones a player cannot take are not choices — offering them means
        // walking past plants that do nothing when picked.
        int count = seeds.Count;
        int from = _cursor < 0 ? (delta > 0 ? -1 : count) : _cursor;

        for (int step = 1; step <= count; step++)
        {
            int index = ((from + delta * step) % count + count) % count;
            if (!CanTake(seeds[index])) continue;

            _cursor = index;
            AnnounceCurrent();
            return true;
        }

        Speech.Say(Strings.T("chooser.no_plants"), context: "chooser");
        return true;
    }

    /// <summary>Describes the plant the cursor is on, without moving it.</summary>
    public static void AnnounceCurrent()
    {
        ChosenSeed seed = CurrentSeed();
        if (seed == null)
        {
            Speech.SayVerbatim(Strings.T("chooser.no_plants"), "chooser");
            return;
        }

        Speech.SayVerbatim(Describe(seed), "chooser plant");
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
    private static bool CanTake(ChosenSeed seed)
    {
        if (seed == null) return false;
        try
        {
            SeedType type = seed.mSeedType;

            // The chooser lists all forty-nine plants regardless of the save, and every one
            // of them reports SeedNotAllowedToPick as false — that flag means something
            // else. Whether this player actually owns the plant is the activity's question.
            var app = _screen.mApp;
            if (app != null && !app.HasSeedType(type)) return false;

            if (_screen.SeedNotAllowedToPick(type)) return false;
            if (_screen.SeedNotAllowedDuringTrial(type)) return false;
            return true;
        }
        catch { return true; }
    }

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
            return IsPicked(seed) ? Strings.T("chooser.picked", line) : line;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not describe a plant: {ex.Message}");
            return Strings.T("chooser.no_plants");
        }
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
