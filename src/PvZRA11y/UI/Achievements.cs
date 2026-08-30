using Il2CppReloaded.DataModels;
using Il2CppTekly.DataModels.Models;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.UI;

/// <summary>
/// The achievements screen.
///
/// Thirty-seven entries, each a title, a sentence saying what it takes, and whether it has been
/// earned. A sighted player scrolls a list of picture tiles; there is no text control to walk,
/// no focus to follow, and the screen is not even a panel of its own — it is a section of the
/// main menu that slides in over the top, so every test the mod has for "which screen is in
/// front" answers "the main menu" throughout.
///
/// So none of the usual machinery applies, and the screen is read from the game's data model
/// instead. The whole list is registered at the root under "achievements", with the entries
/// beneath it keyed by their position, and every entry carries its own title, subheader and
/// earned flag. That is better than reading the tiles: the list scrolls, and a tile that has
/// scrolled away may not exist to be read, whereas the model holds all thirty-seven whether
/// they are on screen or not.
///
/// Whether the screen is open at all is the one thing the model will not say, and the game's
/// own view keeps a flag for exactly that.
/// </summary>
public static class Achievements
{
    /// <summary>Where the list lives in the game's data model.</summary>
    private const string Root = "achievements";

    /// <summary>A sane ceiling for probing, used only when the total will not be read.</summary>
    private const int MaxEntries = 256;

    #region is it open

    private static Il2CppUI.Scripts.AchievementsUI _view;

    /// <summary>The screen's own view, or null when it is not in the scene.</summary>
    private static Il2CppUI.Scripts.AchievementsUI View()
    {
        try { if (_view != null) return _view; }
        catch { _view = null; }

        try { _view = UnityEngine.Object.FindObjectOfType<Il2CppUI.Scripts.AchievementsUI>(); }
        catch { _view = null; }

        return _view;
    }

    /// <summary>
    /// True while the achievements list is showing.
    ///
    /// From the screen's own flag rather than from panel state, which cannot tell this screen
    /// apart from the menu it slides in over.
    /// </summary>
    public static bool IsActive
    {
        get
        {
            Il2CppUI.Scripts.AchievementsUI view = View();
            if (view == null) return false;

            try { return view.m_achievementsIsActive; }
            catch { return false; }
        }
    }

    #endregion

    #region the list

    /// <summary>One entry's model, or null.</summary>
    private static AchievementEntryModel Entry(int index)
    {
        if (index < 0) return null;

        try { return ModelText.ModelAt($"{Root}.all.{index}")?.TryCast<AchievementEntryModel>(); }
        catch { return null; }
    }

    /// <summary>
    /// How many there are.
    ///
    /// The model keeps a total, and it is asked first. Probing is the fallback rather than the
    /// method, because a count that stops at the first gap would quietly shorten the list.
    /// </summary>
    public static int Count()
    {
        string total = ModelText.FromRoot($"{Root}.total");
        if (!string.IsNullOrWhiteSpace(total)
            && float.TryParse(total, System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            && parsed > 0)
            return (int)parsed;

        int count = 0;
        while (count < MaxEntries && Entry(count) != null) count++;

        return count;
    }

    /// <summary>How many have been earned by the model's own tally, or -1 when it will not say.</summary>
    private static int UnlockedTally()
    {
        string value = ModelText.FromRoot($"{Root}.unlocked");

        return !string.IsNullOrWhiteSpace(value)
            && float.TryParse(value, System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? (int)parsed
            : -1;
    }

    /// <summary>
    /// How many have been earned, counted the hard way when the tally will not answer.
    ///
    /// Still -1 if even that fails, and callers have to respect it. Saying "0 of 37 earned" to
    /// a player who has earned twenty is worse than saying nothing about the count: it is a
    /// wrong answer in the voice of a right one, and the count is the whole reason this screen
    /// gets opened.
    /// </summary>
    private static int Unlocked(int count)
    {
        int tally = UnlockedTally();
        if (tally >= 0) return tally;

        if (count <= 0) return -1;

        int earned = 0;
        bool anyRead = false;

        for (int i = 0; i < count; i++)
        {
            if (Entry(i) == null) continue;
            anyRead = true;
            if (IsEarned(i)) earned++;
        }

        return anyRead ? earned : -1;
    }

    /// <summary>An entry's name, in words.</summary>
    public static string Title(int index)
    {
        AchievementEntryModel entry = Entry(index);

        if (entry != null)
        {
            try
            {
                string raw = entry.EntryData?.EntryTitle;
                string text = ModelText.Resolve(raw);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { /* the binder's copy below */ }
        }

        return ModelText.Resolve(ModelText.FromRoot($"{Root}.all.{index}.title"));
    }

    /// <summary>What it takes to earn, in the game's own words.</summary>
    public static string Requirement(int index)
    {
        AchievementEntryModel entry = Entry(index);

        if (entry != null)
        {
            try
            {
                string raw = entry.EntryData?.EntrySubheader;
                string text = ModelText.Resolve(raw);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { /* the binder's copy below */ }
        }

        return ModelText.Resolve(ModelText.FromRoot($"{Root}.all.{index}.subHeader"));
    }

    /// <summary>Whether it has been earned.</summary>
    public static bool IsEarned(int index)
    {
        AchievementEntryModel entry = Entry(index);

        if (entry != null)
        {
            try { return entry.Granted; }
            catch { /* the binder's copy below */ }
        }

        string flag = ModelText.FromRoot($"{Root}.all.{index}.granted");
        return flag != null && flag.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region moving and reading

    private static int _cursor;

    /// <summary>
    /// What to say about one entry as the cursor lands on it.
    ///
    /// Title, whether it is earned, and where it sits in the list. Not the requirement: the
    /// sentence saying what it takes is a whole line of speech, and hearing thirty-seven of
    /// them while looking for one is worse than pressing a key for the one you stopped at.
    /// </summary>
    private static string Describe(int index, int count)
    {
        var parts = new List<string>(3)
        {
            Title(index) ?? Strings.T("msg.unlabelled"),
            Strings.T(IsEarned(index) ? "achievements.earned" : "achievements.not_earned"),
            Strings.T("achievements.position", index + 1, count),
        };

        return string.Join(", ", parts);
    }

    /// <summary>Walks the list.</summary>
    public static bool Move(int step)
    {
        int count = Count();
        if (count <= 0)
        {
            Speech.Say(Strings.T("achievements.empty"), context: "achievements");
            return true;
        }

        int target = _cursor + step;
        if (target < 0 || target >= count)
        {
            Speech.Say(Strings.T("achievements.edge"), context: "achievements edge");
            return true;
        }

        _cursor = target;
        Speech.Say(Describe(_cursor, count), interrupt: true, context: "achievements", allowRepeat: true);
        return true;
    }

    /// <summary>Says the entry under the cursor again, without moving.</summary>
    public static bool AnnounceCurrent()
    {
        int count = Count();
        if (count <= 0)
        {
            Speech.SayVerbatim(Strings.T("achievements.empty"), "achievements");
            return true;
        }

        if (_cursor >= count) _cursor = count - 1;

        Speech.SayVerbatim(Describe(_cursor, count), "achievements");
        return true;
    }

    /// <summary>Says what the entry under the cursor takes.</summary>
    public static bool AnnounceRequirement()
    {
        int count = Count();
        if (count <= 0 || _cursor >= count)
        {
            Speech.SayVerbatim(Strings.T("achievements.empty"), "achievements");
            return true;
        }

        string requirement = Requirement(_cursor);
        string title = Title(_cursor) ?? Strings.T("msg.unlabelled");

        Speech.SayVerbatim(string.IsNullOrWhiteSpace(requirement)
            ? Strings.T("achievements.no_requirement", title)
            : title + ". " + requirement, "achievement detail");

        return true;
    }

    /// <summary>How the collection stands as a whole.</summary>
    public static bool AnnounceSummary()
    {
        int count = Count();
        if (count <= 0)
        {
            Speech.SayVerbatim(Strings.T("achievements.empty"), "achievements");
            return true;
        }

        int earned = Unlocked(count);

        Speech.SayVerbatim(earned < 0
            ? Strings.T("achievements.summary_unknown", count)
            : Strings.T("achievements.summary", earned, count), "achievements");

        return true;
    }

    /// <summary>
    /// Reads the ones still to earn, and nothing else.
    ///
    /// The question behind opening this screen at all when the goal is to finish the game: not
    /// what has been done, but what is left. Said with the requirement, because for a list of
    /// things still to do the name alone says nothing about how to do them.
    /// </summary>
    public static bool AnnounceRemaining()
    {
        int count = Count();
        if (count <= 0)
        {
            Speech.SayVerbatim(Strings.T("achievements.empty"), "achievements");
            return true;
        }

        var lines = new List<string>();
        for (int i = 0; i < count; i++)
        {
            if (IsEarned(i)) continue;

            string title = Title(i) ?? Strings.T("msg.unlabelled");
            string requirement = Requirement(i);

            lines.Add(string.IsNullOrWhiteSpace(requirement) ? title : title + ": " + requirement);
        }

        if (lines.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("achievements.all_earned"), "achievements");
            return true;
        }

        lines.Insert(0, Strings.T("achievements.still_to_do", lines.Count));
        Speech.SayVerbatim(string.Join(". ", lines), "achievements");
        return true;
    }

    /// <summary>
    /// Reads the list from top to bottom.
    ///
    /// What "read the whole screen" means here, and it is long on purpose: thirty-seven
    /// entries is what is on the screen. Left Ctrl stops it, as it stops anything.
    /// </summary>
    public static bool AnnounceAll()
    {
        int count = Count();
        if (count <= 0)
        {
            Speech.SayVerbatim(Strings.T("achievements.empty"), "achievements");
            return true;
        }

        int earned = Unlocked(count);

        var lines = new List<string>(count + 1)
        {
            earned < 0 ? Strings.T("achievements.summary_unknown", count)
                       : Strings.T("achievements.summary", earned, count),
        };

        for (int i = 0; i < count; i++)
            lines.Add((Title(i) ?? Strings.T("msg.unlabelled")) + ", "
                      + Strings.T(IsEarned(i) ? "achievements.earned" : "achievements.not_earned"));

        Speech.SayVerbatim(string.Join(". ", lines), "achievements");
        return true;
    }

    /// <summary>Leaves the screen by its own back button.</summary>
    public static bool Leave()
    {
        Il2CppUI.Scripts.AchievementsUI view = View();

        try
        {
            UnityEngine.UI.Button back = view?.m_backButton;
            if (back != null && back.interactable)
            {
                back.onClick.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[achievements] the back button refused: {ex.Message}");
        }

        // The controller's cancel, which is what every other page of this menu answers to.
        if (Input.VirtualPad.PressThenHandBack(UnityEngine.InputSystem.LowLevel.GamepadButton.East))
            return true;

        Speech.Say(Strings.T("achievements.no_way_back"), context: "achievements");
        return true;
    }

    #endregion

    #region upkeep

    private static bool _announced;

    /// <summary>
    /// Says the screen when it opens, and forgets where the cursor was when it closes.
    ///
    /// Announced by the mod because the screen has no heading control to focus and nothing
    /// else would say it had arrived.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive)
        {
            _announced = false;
            _cursor = 0;
            return;
        }

        if (_announced) return;
        _announced = true;

        int count = Count();
        int earned = Unlocked(count);

        Core.Log.Msg($"[achievements] open: {count} entries, {earned} earned");

        // Which way in answered, and what the first entry looks like. The list is read from
        // the data model rather than from anything on screen, so when it comes back empty
        // there is nothing visible to work backwards from.
        if (count <= 0)
        {
            foreach (string key in new[] { Root, Root + ".all", Root + ".total", Root + ".all.0",
                                           Root + ".all.0.title" })
                Core.Log.Msg($"[achievements]   \"{key}\" -> {(ModelText.ModelAt(key) == null ? "nothing" : "a model")}"
                             + $", value {ModelText.FromRoot(key) ?? "<none>"}");
        }

        if (count <= 0)
        {
            Speech.Say(Strings.T("achievements.empty"),
                       interrupt: true, context: "achievements", allowRepeat: true);
            return;
        }

        // The header, and then the entry the cursor is standing on. Without the second half
        // the player lands on an unnamed position and the first press of Down takes them to
        // the second entry, having never heard the first.
        _cursor = 0;

        string header = earned < 0
            ? Strings.T("achievements.opened_unknown", count)
            : Strings.T("achievements.opened", earned, count);

        Speech.Say(header + " " + Describe(_cursor, count),
                   interrupt: true, context: "achievements", allowRepeat: true);
    }

    /// <summary>The whole list, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- achievements ---");
        sb.AppendLine($"  screen open : {IsActive}");

        int count = Count();
        sb.AppendLine($"  entries     : {count}");
        sb.AppendLine($"  earned      : {Unlocked(count)} (tally says {UnlockedTally()})");

        if (count <= 0) { sb.AppendLine(); return; }

        for (int i = 0; i < count; i++)
            sb.AppendLine($"      {i + 1}. {Title(i)} — {(IsEarned(i) ? "earned" : "not earned")} — {Requirement(i)}");

        sb.AppendLine();
    }

    #endregion
}
