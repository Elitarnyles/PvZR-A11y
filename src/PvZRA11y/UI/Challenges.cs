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

    private static IUserService User()
    {
        try { return Activity()?.UserService; }
        catch { return null; }
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

    /// <summary>
    /// Moves the game's own selection, because the mod has nothing of its own to move.
    ///
    /// The page answers only to a controller, so the arrow keys are turned into a controller's
    /// directional pad. What lands under the selection is then asked of the game rather than
    /// counted by the mod: keeping a second cursor in step with one that cannot be seen is
    /// exactly the kind of bookkeeping that goes quietly wrong.
    /// </summary>
    public static bool Move(int dx, int dy)
    {
        GamepadButton button =
            dy < 0 ? GamepadButton.DpadUp :
            dy > 0 ? GamepadButton.DpadDown :
            dx < 0 ? GamepadButton.DpadLeft : GamepadButton.DpadRight;

        int before = SelectedIndex();

        if (!Input.VirtualPad.Press(button))
        {
            Speech.Say(Strings.T("challenges.no_pad"), context: "challenges");
            return true;
        }

        _announceIn = AnnounceAfterFrames;
        _announcedFrom = before;
        return true;
    }

    /// <summary>Starts what is selected.</summary>
    public static bool Choose()
    {
        Core.Log.Msg($"[challenges] starting entry {SelectedIndex()}");

        if (Input.VirtualPad.PressThenHandBack(GamepadButton.South)) return true;

        Speech.Say(Strings.T("challenges.no_pad"), context: "challenges");
        return true;
    }

    /// <summary>Leaves the page.</summary>
    public static bool Leave()
    {
        if (Input.VirtualPad.PressThenHandBack(GamepadButton.East)) return true;

        Speech.Say(Strings.T("challenges.no_pad"), context: "challenges");
        return true;
    }

    /// <summary>Which entry the game says is selected, or -1.</summary>
    public static int SelectedIndex()
    {
        try { return Activity()?.GetCurrentChallengeIndex() ?? -1; }
        catch { return -1; }
    }

    private static int _announceIn;
    private static int _announcedFrom = -1;

    /// <summary>Long enough for the game to have moved before it is asked what moved.</summary>
    private const int AnnounceAfterFrames = 8;

    /// <summary>
    /// Says what the selection landed on, once the game has had time to move it.
    ///
    /// Logged either way, and with the index the game reports. Whether that index tracks the
    /// highlighted entry is the one thing about this screen that could not be settled without
    /// running it - so it is written down on every move rather than assumed, and if it turns
    /// out not to follow, the log says so on the first press instead of after a session of
    /// wondering why the names are wrong.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive)
        {
            _entries = null;
            _entriesFor = ChallengeEntryType.None;
            _announceIn = 0;
            return;
        }

        if (_announceIn <= 0) return;
        if (--_announceIn > 0) return;

        int index = SelectedIndex();
        var entries = Entries();

        Core.Log.Msg($"[challenges] selection {_announcedFrom} -> {index}" +
                     $" of {entries.Count} entries");

        if (index < 0 || index >= entries.Count)
        {
            // The index is not an index into this list, or the game does not keep one. Say the
            // page rather than an entry, so the key is never silent.
            Speech.Say(Strings.T("challenges.moved", PageName()),
                       interrupt: true, context: "challenges", allowRepeat: true);
            return;
        }

        Speech.Say(Describe(entries[index]), interrupt: true,
                   context: "challenges", allowRepeat: true);
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
        sb.AppendLine($"  selected index : {SelectedIndex()}");

        foreach (LevelEntryData entry in entries)
        {
            string mode;
            try { mode = entry.GameMode.ToString(); } catch { mode = "?"; }
            sb.AppendLine($"      {Describe(entry)}  (mode {mode})");
        }

        sb.AppendLine();
    }
}
