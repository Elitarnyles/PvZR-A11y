using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using Il2CppReloaded.TreeStateActivities;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using UnityEngine.InputSystem.LowLevel;

namespace PvZRA11y.UI;

/// <summary>
/// The Mini-Games, Puzzle and Survival pages.
///
/// All three are the same screen with a different list in it, and none of them has a single
/// control the mod can walk. The page opens correctly, its heading and trophy count read
/// fine, and then there is nothing: the entries are a scroll view with its own grid
/// navigation and a pair of controller prompts, exactly like the Zen Garden's tool bar and
/// like the shop when the game thinks a controller is in use. Measured, not guessed - a dump
/// of the open page found zero controls belonging to it and seventeen belonging to the menu
/// behind it.
///
/// So the mod reads the list from the game's own level data, which is a list of entries with
/// a name, a mode and a type, and moves the game's selection with a controller it makes up
/// for the purpose.
/// </summary>
public static class Challenges
{
    /// <summary>The three pages, by the panel the game shows for each.</summary>
    private static readonly Dictionary<string, ChallengeEntryType> Pages =
        new(StringComparer.Ordinal)
        {
            ["minigames"] = ChallengeEntryType.MiniGame,
            ["puzzle"] = ChallengeEntryType.Puzzle,
            ["survival"] = ChallengeEntryType.Survival,
        };

    /// <summary>True while one of the three challenge pages is in front.</summary>
    public static bool IsActive => Page() != ChallengeEntryType.None;

    /// <summary>Which of the three is open, or None.</summary>
    public static ChallengeEntryType Page()
    {
        string front = PanelScope.FrontPanelId;
        if (string.IsNullOrEmpty(front)) return ChallengeEntryType.None;
        return Pages.TryGetValue(front, out ChallengeEntryType page) ? page : ChallengeEntryType.None;
    }

    /// <summary>The page's spoken name.</summary>
    public static string PageName()
    {
        ChallengeEntryType page = Page();
        string key = "challenges.page." + page;
        return Strings.Has(key) ? Strings.T(key) : UiText.Prettify(page.ToString());
    }

    #region reaching the game

    /// <summary>
    /// The activity that owns the level data and the current selection.
    ///
    /// The lawn's copy is only set while a board exists, and these pages live in the menu
    /// where there is none - so it is looked up in the scene as well. Which route answered is
    /// logged once, because "the list is empty" and "the mod cannot reach the game" are
    /// different problems that read identically.
    /// </summary>
    private static GameplayActivity _activity;
    private static bool _reportedRoute;

    private static GameplayActivity Activity()
    {
        try { if (_activity != null) return _activity; }
        catch { _activity = null; }

        try
        {
            GameplayActivity fromLawn = Gameplay.Lawn.AppRef;
            if (fromLawn != null)
            {
                _activity = fromLawn;
                Report("the board");
                return _activity;
            }
        }
        catch { /* try the scene */ }

        try
        {
            _activity = UnityEngine.Object.FindObjectOfType<GameplayActivity>();
            Report(_activity == null ? "nowhere" : "the scene");
            return _activity;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[challenges] could not find the activity: {ex.Message}");
            return null;
        }
    }

    private static void Report(string where)
    {
        if (_reportedRoute) return;
        _reportedRoute = true;
        Core.Log.Msg($"[challenges] reached the game through {where}");
    }

    /// <summary>
    /// The level data, from the one place that has it without a game in progress.
    ///
    /// The activity found in the menu is real but its injected services are not filled in
    /// until a game starts, so asking it for the data returned nothing and the page listed
    /// nought entries. ReloadedUtils carries the same service as a static, which is what the
    /// game itself uses from screens that are not a level.
    /// </summary>
    private static IDataService Data()
    {
        try
        {
            IDataService global = Il2CppSource.Utils.ReloadedUtils.DataService;
            if (global != null) return global;
        }
        catch { /* fall back to the activity */ }

        try { return Activity()?.m_dataService; }
        catch { return null; }
    }

    private static IUserService _user;
    private static bool _reportedUserRoute;

    /// <summary>
    /// Who knows what is locked and what has been beaten.
    ///
    /// Same trap as the level data: the gameplay activity holds one but has not been given it
    /// outside a level, so every entry read as unlocked - which on this save is wrong for
    /// thirty of the thirty-three. The main menu's own activity has the same service and is
    /// alive on exactly the screens where these pages live.
    /// </summary>
    private static IUserService User()
    {
        try { if (_user != null) return _user; }
        catch { _user = null; }

        _user = FromActivity("the gameplay activity", () => Activity()?.UserService)
             ?? FromActivity("the main menu",
                    () => UnityEngine.Object.FindObjectOfType<MainMenuActivity>()?.m_userService)
             ?? FromActivity("the frontend",
                    () => UnityEngine.Object.FindObjectOfType<FrontendActivity>()?.m_userService);

        if (_user == null && !_reportedUserRoute)
        {
            _reportedUserRoute = true;
            Core.Log.Warning("[challenges] nothing would say which entries are locked");
        }

        return _user;
    }

    private static IUserService FromActivity(string where, Func<IUserService> read)
    {
        IUserService found;
        try { found = read(); }
        catch { return null; }

        if (found == null) return null;

        if (!_reportedUserRoute)
        {
            _reportedUserRoute = true;
            Core.Log.Msg($"[challenges] locked and beaten come from {where}");
        }

        return found;
    }

    #endregion

    #region the list

    private static List<LevelEntryData> _entries;
    private static ChallengeEntryType _entriesFor = ChallengeEntryType.None;

    /// <summary>
    /// Everything on the open page, in the order the game holds it.
    ///
    /// Read from the data rather than from the screen, because the screen has nothing on it
    /// to read. The same list drives the level select, so the names, the modes and the types
    /// are the game's own.
    /// </summary>
    public static List<LevelEntryData> Entries()
    {
        ChallengeEntryType page = Page();

        if (_entries != null && page == _entriesFor) return _entries;

        var found = new List<LevelEntryData>();
        IDataService data = Data();

        if (data != null && page != ChallengeEntryType.None)
        {
            try
            {
                var all = data.AllLevelsData;
                // The interop wrapper for the game's read-only list is bare: no Count, no
                // indexer, nothing to enumerate. What is behind it is an ordinary list, so it
                // is asked for that and the failure is logged rather than guessed at.
                var list = all?.TryCast<Il2CppSystem.Collections.Generic.List<LevelEntryData>>();

                if (all == null)
                {
                    Core.Log.Warning("[challenges] no level data: the game would not hand any over");
                }
                else if (list == null)
                {
                    Core.Log.Warning("[challenges] the level data is not a list this mod can read");
                }
                else
                {
                    int total = list.Count;
                    for (int i = 0; i < total; i++)
                    {
                        try
                        {
                            LevelEntryData entry = list[i];
                            if (entry != null && entry.EntryType == page) found.Add(entry);
                        }
                        catch { /* one bad entry must not cost the rest */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"[challenges] could not read the level data: {ex.Message}");
            }
        }

        _entries = found;
        _entriesFor = page;

        Core.Log.Msg($"[challenges] {PageName()}: {found.Count} entries");
        return found;
    }

    /// <summary>What to say about one entry: its name, and whether you can play it.</summary>
    public static string Describe(LevelEntryData entry)
    {
        if (entry == null) return null;

        var parts = new List<string>(3);

        string name = null;
        try { name = GameText.Resolve(entry.LevelName); }
        catch { }
        parts.Add(string.IsNullOrWhiteSpace(name) ? UiText.Prettify(entry.LevelName ?? "?") : name);

        IUserService user = User();
        if (user != null)
        {
            try { if (user.IsLocked(entry, false)) parts.Add(Strings.T("challenges.locked")); }
            catch { }

            try { if (user.HasBeatenChallenge(entry.GameMode)) parts.Add(Strings.T("challenges.beaten")); }
            catch { }
        }

        return string.Join(", ", parts);
    }

    /// <summary>Reads the whole page, without touching what is selected.</summary>
    public static bool AnnouncePage()
    {
        var entries = Entries();
        if (entries.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("challenges.empty", PageName()), "challenges");
            return true;
        }

        var parts = new List<string>(entries.Count + 1)
        {
            Strings.T("challenges.header", PageName(), entries.Count),
        };

        foreach (LevelEntryData entry in entries)
        {
            string line = Describe(entry);
            if (!string.IsNullOrEmpty(line)) parts.Add(line);
        }

        Speech.SayVerbatim(string.Join(". ", parts), "challenges");
        return true;
    }

    #endregion

    #region moving and choosing

    // The list as it stands on screen: one tile per entry, laid out by a GridLayoutGroup
    // inside the page's scroll view. Each tile carries a binder bound to the game's own
    // LevelEntryModel, whose child keys - read out of the shipped code, not guessed - are
    // "name", "locked", "completed", "longestStreak" and "select", the last being the very
    // button the mouse would press.
    //
    // So the mod does not move the game's grid highlight at all. It keeps its own cursor
    // over the tiles, reads each one from its binder, and Enter activates the tile's own
    // button model - the same proven path as the shop's back button. The dpad presses this
    // page also listens for are left alone: they moved something the mod could never read
    // back, which made every arrow silent.

    private static int _cursor;
    private static ChallengeEntryType _cursorFor = ChallengeEntryType.None;

    /// <summary>The tiles on the open page, in the order they are laid out.</summary>
    private static List<UnityEngine.Transform> Tiles(out int columns)
    {
        columns = 1;
        var tiles = new List<UnityEngine.Transform>();

        try
        {
            var grids = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.GridLayoutGroup>();
            if (grids == null) return tiles;

            foreach (var grid in grids)
            {
                if (grid == null) continue;

                // The page's own grid, told apart by the prefab it lives in. There is one
                // challenge panel per page and its name carries the word.
                UnityEngine.Transform t = grid.transform;
                bool ours = false;
                for (UnityEngine.Transform up = t; up != null; up = up.parent)
                    if (up.name.Contains("ChallengePanel")) { ours = true; break; }
                if (!ours) continue;

                try
                {
                    if (grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount
                        && grid.constraintCount > 0)
                        columns = grid.constraintCount;
                }
                catch { }

                int count = t.childCount;
                for (int i = 0; i < count; i++)
                {
                    UnityEngine.Transform child = t.GetChild(i);
                    if (child == null) continue;

                    try { if (!child.gameObject.activeInHierarchy) continue; }
                    catch { continue; }

                    // The template the clones were stamped from sits in the same grid and
                    // still carries its design-time text. A tile with no model behind it is
                    // the template, not an entry.
                    if (ReadTile(child, "*.name") == null) continue;

                    tiles.Add(child);
                }

                break;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[challenges] could not read the tiles: {ex.Message}");
        }

        return tiles;
    }

    private static string ReadTile(UnityEngine.Transform tile, string key)
    {
        try
        {
            var container = tile.GetComponentInChildren<Il2CppTekly.DataModels.Binders.BinderContainer>();
            return ModelText.Value(container, key);
        }
        catch { return null; }
    }

    private static bool TileFlag(UnityEngine.Transform tile, string key)
    {
        string value = ReadTile(tile, key);
        return value != null && value.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What to say about one tile, from the model that draws it.</summary>
    private static string DescribeTile(UnityEngine.Transform tile, int index, int count)
    {
        var parts = new List<string>(4);

        string name = ReadTile(tile, "*.name");
        parts.Add(GameText.ResolveOrKeep(name) ?? Strings.T("msg.unlabelled"));

        if (TileFlag(tile, "*.locked")) parts.Add(Strings.T("challenges.locked"));
        if (TileFlag(tile, "*.completed")) parts.Add(Strings.T("challenges.beaten"));

        if (TileFlag(tile, "*.showLongestStreak"))
        {
            string streak = ReadTile(tile, "*.longestStreak");
            if (!string.IsNullOrWhiteSpace(streak)) parts.Add(GameText.ResolveOrKeep(streak));
        }

        parts.Add(Strings.T("challenges.position", index + 1, count));
        return string.Join(", ", parts);
    }

    /// <summary>Walks the mod's own cursor across the tiles and says what it lands on.</summary>
    public static bool Move(int dx, int dy)
    {
        SyncCursor();

        var tiles = Tiles(out int columns);
        if (tiles.Count == 0)
        {
            Speech.Say(Strings.T("challenges.empty", PageName()), context: "challenges");
            return true;
        }

        int step = dx + dy * columns;
        int target = _cursor + step;

        if (target < 0 || target >= tiles.Count)
        {
            Speech.Say(Strings.T("challenges.edge"), context: "challenges edge");
            return true;
        }

        _cursor = target;
        Speech.Say(DescribeTile(tiles[_cursor], _cursor, tiles.Count),
                   interrupt: true, context: "challenges", allowRepeat: true);
        return true;
    }

    /// <summary>Presses the button behind the tile the cursor is on.</summary>
    public static bool Choose()
    {
        SyncCursor();

        var tiles = Tiles(out _);
        if (tiles.Count == 0 || _cursor < 0 || _cursor >= tiles.Count)
        {
            Speech.Say(Strings.T("challenges.empty", PageName()), context: "challenges");
            return true;
        }

        UnityEngine.Transform tile = tiles[_cursor];
        string name = GameText.ResolveOrKeep(ReadTile(tile, "*.name")) ?? "?";

        if (TileFlag(tile, "*.locked"))
        {
            // Said rather than tried: the game would refuse anyway, and "Locked" with the
            // name is more useful than whatever its refusal looks like.
            Speech.Say(Strings.T("challenges.still_locked", name),
                       interrupt: true, context: "challenges", allowRepeat: true);
            return true;
        }

        Il2CppTekly.DataModels.Models.ButtonModel button = null;
        try
        {
            var container = tile.GetComponentInChildren<Il2CppTekly.DataModels.Binders.BinderContainer>();
            if (container != null && container.TryGet("*.select", out Il2CppTekly.DataModels.Models.IModel model))
                button = model?.TryCast<Il2CppTekly.DataModels.Models.ButtonModel>();
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[challenges] could not reach the button on {name}: {ex.Message}");
        }

        if (button == null)
        {
            Speech.Say(Strings.T("challenges.cannot_start", name),
                       interrupt: true, context: "challenges", allowRepeat: true);
            return true;
        }

        try
        {
            Core.Log.Msg($"[challenges] starting {name} through its own button model");
            button.Activate(0);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[challenges] the button on {name} refused: {ex.Message}");
            Speech.Say(Strings.T("challenges.cannot_start", name),
                       interrupt: true, context: "challenges", allowRepeat: true);
        }

        return true;
    }

    /// <summary>Leaves the page. The back action is a controller binding, and it works.</summary>
    public static bool Leave()
    {
        if (Input.VirtualPad.PressThenHandBack(UnityEngine.InputSystem.LowLevel.GamepadButton.East))
            return true;

        Speech.Say(Strings.T("challenges.no_pad"), context: "challenges");
        return true;
    }

    private static void SyncCursor()
    {
        ChallengeEntryType page = Page();
        if (page == _cursorFor) return;
        _cursorFor = page;
        _cursor = 0;
    }

    /// <summary>Per-frame upkeep: only the reset when the page closes.</summary>
    public static void Tick()
    {
        if (IsActive) return;

        _entries = null;
        _entriesFor = ChallengeEntryType.None;
        _cursorFor = ChallengeEntryType.None;
        _cursor = 0;
        _user = null;
        _reportedUserRoute = false;
    }

    #endregion

    /// <summary>The page's state, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- challenge pages ---");
        sb.AppendLine($"  front panel    : {PanelScope.FrontPanelId ?? "none"}");
        sb.AppendLine($"  page           : {Page()}");

        if (!IsActive) { sb.AppendLine(); return; }

        var entries = Entries();
        sb.AppendLine($"  entries        : {entries.Count}");
        var tiles = Tiles(out int columns);
        sb.AppendLine($"  tiles on screen: {tiles.Count}, {columns} columns, cursor at {_cursor}");

        foreach (LevelEntryData entry in entries)
        {
            string mode;
            try { mode = entry.GameMode.ToString(); } catch { mode = "?"; }
            sb.AppendLine($"      {Describe(entry)}  (mode {mode})");
        }

        sb.AppendLine();
    }
}
