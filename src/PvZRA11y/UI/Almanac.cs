using System.Text;
using Il2CppReloaded.DataModels;
using Il2CppTekly.DataModels.Binders;
using Il2CppTekly.DataModels.Models;
using Il2CppTekly.PanelViews;
using Il2CppTMPro;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// The Suburban Almanac: four screens, and the only place in this game where a control
/// carries no text of any kind.
///
/// An entry tile is a Button with two Image children and nothing else. Every tile on a page
/// is cloned from one prefab, so they all share a GameObject name — which means even the
/// ui.* override has nothing to bite on, the first screen where that is true. What a tile
/// *is* lives only in the data model, and the tile carries a BinderContainer that knows its
/// own absolute key. That is the bridge: focused object, to its container, to the entry, to
/// a spoken name.
///
/// No index arithmetic anywhere. The grid binds a filtered subset of the entries while the
/// data behind it holds all of them, and one tile hides itself in place, so counting
/// siblings would quietly point at the wrong plant.
///
/// The prose is the game's own and is never copied into this mod. It is read from the
/// string table by key, with the label on screen as a fallback — the table first because it
/// is translated and complete, the label second because the shipped asset behind it is
/// English in every build and one entry's copy of it is the literal text "???".
/// </summary>
public static class Almanac
{
    public const string IndexPanel = "almanac";
    public const string PlantsPanel = "almanacPlants";
    public const string ZombiesPanel = "almanacZombies";
    public const string ArchivePanel = "almanacArchive";

    // Found by GameObject name rather than by path, because the two grid pages are not
    // built the same: the plants page puts its body text under one parent and the zombies
    // page under another, and the zombies page has no cost or recharge label at all.
    private const string NameLabel = "SelectedItemName";
    private const string CostLabel = "SelectedItemCostLabel";
    private const string RechargeLabel = "SelectedItemRechargeLabel";
    private const string BodyLabel = "SelectedItemInfoLabel";

    public static bool IsAlmanacPanel(string id)
        => id is IndexPanel or PlantsPanel or ZombiesPanel or ArchivePanel;

    /// <summary>True anywhere in the almanac, including the index and the archive.</summary>
    public static bool IsActive
        => IsAlmanacPanel(ScreenTracker.CurrentId) || IsAlmanacPanel(PanelScope.FrontPanelId);

    /// <summary>Which of the two pages that hold entries is in front, or null.</summary>
    public static string GridPanelId()
    {
        // The screen tracker derives this from the panel owning the focused control, which
        // is the steadier of the two. The front panel is the fallback for the frames before
        // focus has landed anywhere.
        string id = ScreenTracker.CurrentId;
        if (id is PlantsPanel or ZombiesPanel) return id;

        id = PanelScope.FrontPanelId;
        return id is PlantsPanel or ZombiesPanel ? id : null;
    }

    public static bool IsOnGrid => GridPanelId() != null;

    #region Naming a tile

    /// <summary>
    /// What to call an almanac tile, or null when this control is not one.
    ///
    /// Called from UiText.GetLabel on every focus change, so it has to be cheap and must
    /// never throw.
    /// </summary>
    public static string LabelFor(Selectable selectable)
    {
        if (selectable == null) return null;

        string panel = GridPanelId();
        if (panel == null) return null;

        try { if (PanelScope.PanelIdOf(selectable) != panel) return null; }
        catch { return null; }

        // The grid container is itself a Selectable in this game, so it turns up among the
        // tiles as an unlabelled phantom. Naming it here is safer than teaching the panel
        // filter to exclude a type that every grid screen depends on.
        if (UiText.SafeTypeName(selectable).Contains("NavigationContainer"))
            return Strings.T("almanac.grid");

        BinderContainer container = ContainerOn(selectable);
        if (container == null) return null;

        string raw = ModelValue(container, "*.name");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (IsHidden(raw)) return Strings.T("almanac.locked");

        string name = ResolveName(raw);
        if (string.IsNullOrEmpty(name)) return null;

        // A locked entry resolves through the table to "???". Never speak that: a screen
        // reader says "question question question", and naming a locked plant would give
        // away what the game is deliberately withholding.
        if (name.Contains("???")) return Strings.T("almanac.locked");

        string sun = ModelValue(container, "*.sunCost");
        if (double.TryParse(sun, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double cost)
            && cost >= 1d)
        {
            return name + ", " + Strings.T("almanac.sun", (int)Math.Round(cost));
        }

        return name;
    }

    #endregion

    #region Reading an entry

    /// <summary>
    /// Reads the entry in full: name, cost, recharge and the whole encyclopaedia text.
    /// On the fourth question key, which elsewhere reads the screen.
    /// </summary>
    public static void AnnounceEntry()
    {
        string panel = GridPanelId();
        if (panel == null)
        {
            Speech.SayVerbatim(Strings.T("almanac.no_entry"), "almanac entry");
            return;
        }

        var parts = new List<string>(4);
        Add(parts, PanelLabel(panel, NameLabel));
        Add(parts, PanelLabel(panel, CostLabel));
        Add(parts, PanelLabel(panel, RechargeLabel));
        Add(parts, Describe(panel));

        Speech.SayVerbatim(parts.Count == 0
            ? Strings.T("almanac.unreadable")
            : string.Join(". ", parts), "almanac entry");
    }

    private static void Add(List<string> parts, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parts.Add(value.Trim());
    }

    /// <summary>
    /// The description, from whichever source has it.
    ///
    /// The string table first: translated, complete, and readable at any moment because
    /// nothing has to have been bound yet. The label on screen second: it is by definition
    /// what a sighted player sees, but the asset behind it is English in every build.
    /// </summary>
    private static string Describe(string panelId)
    {
        string stem = EntryKeyStem(panelId);

        string fromTable = Clean(FromStringTable(stem));
        string onScreen = Clean(RawPanelText(panelId, BodyLabel));

        if (Settings.VerboseLogging.Value)
            Core.Log.Msg($"[almanac] stem \"{stem ?? "<none>"}\"" +
                         $"\n          table    : {Short(fromTable)}" +
                         $"\n          on screen: {Short(onScreen)}");

        if (LooksLikeProse(fromTable)) return fromTable;
        if (LooksLikeProse(onScreen)) return onScreen;

        Core.Log.Warning("[almanac] neither source gave a readable description");
        return null;
    }

    private static string FromStringTable(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return null;

        // Both spellings, for the same reason the zombie notes ask twice: the translator
        // strips brackets but knows nothing about a leading dollar, and which form it wants
        // was never worth a play session to settle.
        string header = GameText.Resolve("$" + stem + "_DESCRIPTION_HEADER")
                        ?? GameText.Resolve(stem + "_DESCRIPTION_HEADER");
        string body = GameText.Resolve("$" + stem + "_DESCRIPTION")
                      ?? GameText.Resolve(stem + "_DESCRIPTION");

        // Plenty of entries have no header at all, so a missing one is normal.
        if (string.IsNullOrWhiteSpace(header)) return body;
        if (string.IsNullOrWhiteSpace(body)) return header;

        // Joined with a newline rather than a space: the cleaner works a line at a time and
        // will give the header its own full stop, which is what the unpunctuated ones need.
        return header + "\n" + body;
    }

    /// <summary>
    /// The entry's key without the leading dollar, such as "PEASHOOTER". Taken from the
    /// tile under focus when there is one, otherwise from the page's own selection.
    /// </summary>
    private static string EntryKeyStem(string panelId)
    {
        string raw = ModelValue(ContainerOn(FocusedSelectable()), "*.name");
        if (string.IsNullOrWhiteSpace(raw)) raw = SelectedNameFromModel(panelId);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        raw = raw.Trim();
        return raw.StartsWith("$", StringComparison.Ordinal) ? raw[1..] : raw;
    }

    private static string SelectedNameFromModel(string panelId)
    {
        string side = panelId == ZombiesPanel ? "almanac.zombies" : "almanac.plants";

        RootModel root;
        try { root = RootModel.Instance; }
        catch { return null; }
        if (root == null) return null;

        string index = ReadModel(root, side + ".selected");
        if (string.IsNullOrWhiteSpace(index)) return null;

        return ReadModel(root, side + ".all." + index.Trim() + ".name");
    }

    private static string ReadModel(RootModel root, string key)
    {
        try
        {
            IModel model = null;
            return root.TryGetModel(key, out model) ? ModelValue(model) : null;
        }
        catch (Exception ex)
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.Msg($"[almanac] \"{key}\" could not be read: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Opening an entry

    private static int _announceIn;

    /// <summary>
    /// Called after a control has been pressed. Opening an almanac tile fills the panel but
    /// does not move focus, so without this the player hears nothing at all at the moment
    /// the screen fills with the thing he pressed for.
    /// </summary>
    public static void NoteActivated(Selectable selectable)
    {
        if (GridPanelId() == null) return;
        if (ContainerOn(selectable) == null) return;

        // Not this frame. The binders run after the press, the same lesson the speech
        // bubbles taught: reading now returns the previous entry, which sounds like a
        // working feature and is worse than saying nothing.
        _announceIn = 4;
    }

    /// <summary>Once per frame from Core.OnUpdate.</summary>
    public static void Tick()
    {
        if (_announceIn <= 0) return;
        if (--_announceIn > 0) return;

        string panel = GridPanelId();
        if (panel == null) return;

        var parts = new List<string>(3);
        Add(parts, PanelLabel(panel, NameLabel));
        Add(parts, PanelLabel(panel, CostLabel));
        Add(parts, PanelLabel(panel, RechargeLabel));

        if (parts.Count == 0) return;

        // The short form on purpose. The essay is on a key you press, so walking the grid
        // stays quick and the long text is something you ask for.
        Speech.Say(string.Join(", ", parts), interrupt: true, context: "almanac entry opened");
    }

    #endregion

    #region The markup

    /// <summary>
    /// The word tokens the almanac's descriptions carry. Every one is a colour switch in
    /// the original game and means nothing once spoken.
    /// </summary>
    private static readonly string[] MarkupTokens =
    {
        "SHORTLINE", "KEYWORD", "KEYMETAL", "STAT", "METAL", "FLAVOR", "AQUATIC", "NOCTURNAL",
    };

    private static readonly HashSet<string> ReportedTokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Turns one raw almanac description into something a screen reader can read.
    ///
    /// A line at a time, because collapsing the whole thing first fuses "Damage: normal"
    /// and "Range: lobbed" into one unpunctuated run with no pause between them.
    ///
    /// Scoped to almanac text deliberately. Run over the whole string table this would eat
    /// tokens that are real substitutions elsewhere.
    /// </summary>
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string text = StripTokens(raw.Replace("\r\n", "\n").Replace('\r', '\n'));

        var sb = new StringBuilder(text.Length + 16);
        foreach (string line in text.Split('\n'))
        {
            // Collapse also strips the rich-text colour tags the on-screen copy is full of.
            string clean = UiText.Collapse(line);
            if (string.IsNullOrEmpty(clean)) continue;

            if (sb.Length > 0) sb.Append(' ');
            sb.Append(clean);

            char last = clean[^1];
            if (last != '.' && last != '!' && last != '?' && last != '"') sb.Append('.');
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string StripTokens(string text)
    {
        var sb = new StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') { sb.Append(text[i]); continue; }

            int close = text.IndexOf('}', i);
            if (close < 0) { sb.Append(text[i]); continue; }

            string inner = text[(i + 1)..close];
            i = close;

            if (IsAllDigits(inner))
            {
                // A zombie's hit points, always trailing its toughness: "high {1370}". The
                // space in front goes too, or the line ends "high ." with a gap before it.
                while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;

                if (Settings.SayAlmanacHitPoints.Value)
                    sb.Append(' ').Append(Strings.T("almanac.hitpoints", inner));

                continue;
            }

            if (Array.IndexOf(MarkupTokens, inner) >= 0) continue;

            // Something added since this was written. Drop it rather than speak it, and say
            // so once, so it turns up in a log instead of in his ear.
            if (ReportedTokens.Add(inner))
                Core.Log.Warning($"[almanac] unknown markup token \"{{{inner}}}\"; dropped");
        }

        return sb.ToString();
    }

    private static bool IsAllDigits(string value)
    {
        if (value.Length == 0) return false;
        foreach (char c in value) if (!char.IsDigit(c)) return false;
        return true;
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// Everything about the entry in front, for the dump key. This is what settles which
    /// source the page is really showing without spending another play session on it.
    /// </summary>
    public static void Dump(StringBuilder sb)
    {
        sb.AppendLine("--- almanac ---");

        string panel = GridPanelId();
        sb.AppendLine($"  grid page    : {panel ?? "<not on a grid page>"}");
        sb.AppendLine($"  screen id    : {ScreenTracker.CurrentId}");
        sb.AppendLine($"  front panel  : {PanelScope.FrontPanelId ?? "<none>"}");
        sb.AppendLine($"  hit points   : {(Settings.SayAlmanacHitPoints.Value ? "spoken" : "dropped")}");

        Selectable focused = FocusedSelectable();
        BinderContainer container = ContainerOn(focused);
        sb.AppendLine($"  focused tile : {UiText.SafeName(focused)}  ({UiText.SafeTypeName(focused)})");

        if (container == null)
        {
            sb.AppendLine("  binder       : <none on the focused control>");
        }
        else
        {
            string full;
            try { full = container.ResolveFullKey(); }
            catch (Exception ex) { full = "<threw: " + ex.Message + ">"; }

            sb.AppendLine($"  binder key   : {full}");
            foreach (string field in new[] { "*.name", "*.sunCost", "*.recharge", "*.locked", "*.imitater" })
                sb.AppendLine($"  {field,-13}: {ModelValue(container, field) ?? "<null>"}");
        }

        sb.AppendLine($"  spoken label : {(focused == null ? "<none>" : LabelFor(focused) ?? "<null>")}");

        if (panel == null) { sb.AppendLine(); return; }

        sb.AppendLine($"  selection    : {SelectedNameFromModel(panel) ?? "<null>"}");
        sb.AppendLine($"  key stem     : {EntryKeyStem(panel) ?? "<null>"}");

        foreach (string label in new[] { NameLabel, CostLabel, RechargeLabel, BodyLabel })
        {
            string raw = RawPanelText(panel, label);
            sb.AppendLine($"  {label}");
            sb.AppendLine($"      raw  : {(raw == null ? "<not found>" : Short(raw))}");
            sb.AppendLine($"      read : {Short(Clean(raw))}");
        }

        string stem = EntryKeyStem(panel);
        if (!string.IsNullOrEmpty(stem))
        {
            sb.AppendLine($"  table header : {Short(GameText.Resolve("$" + stem + "_DESCRIPTION_HEADER"))}");
            sb.AppendLine($"  table body   : {Short(GameText.Resolve("$" + stem + "_DESCRIPTION"))}");
        }

        sb.AppendLine($"  would speak  : {Short(Describe(panel))}");
        sb.AppendLine();
    }

    private static string Short(string value)
    {
        if (string.IsNullOrEmpty(value)) return "<empty>";
        string one = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return one.Length <= 600 ? one : one[..600] + " ...";
    }

    #endregion

    #region Plumbing

    private static Selectable FocusedSelectable()
    {
        try
        {
            GameObject go = Focus.CurrentSelection();
            return go == null ? null : go.GetComponent<Selectable>();
        }
        catch { return null; }
    }

    private static BinderContainer ContainerOn(Selectable selectable)
    {
        if (selectable == null) return null;

        try
        {
            BinderContainer direct = selectable.GetComponent<BinderContainer>();
            if (direct != null) return direct;
        }
        catch { /* no such component here */ }

        try { return selectable.GetComponentInParent<BinderContainer>(); }
        catch { return null; }
    }

    /// <summary>One value out of a tile's own model, such as "*.name". Null on anything odd.</summary>
    private static string ModelValue(BinderContainer container, string relativeKey)
    {
        if (container == null) return null;

        try
        {
            IModel model = null;
            return container.TryGet(relativeKey, out model) ? ModelValue(model) : null;
        }
        catch (Exception ex)
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.Msg($"[almanac] \"{relativeKey}\" could not be read: {ex.Message}");
            return null;
        }
    }

    private static string ModelValue(IModel model)
    {
        if (model == null) return null;

        // The interface covers strings, numbers and booleans alike; the concrete casts
        // below are the fallback for when that cast does not survive interop.
        try
        {
            IValueModel value = model.TryCast<IValueModel>();
            if (value != null) return value.ToDisplayString();
        }
        catch { }

        try
        {
            StringValueModel text = model.TryCast<StringValueModel>();
            if (text != null) return text.OnToDisplayString();
        }
        catch { }

        try
        {
            NumberValueModel number = model.TryCast<NumberValueModel>();
            if (number != null) return number.OnToDisplayString();
        }
        catch { }

        return null;
    }

    private static bool IsHidden(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return true;

        try
        {
            string hidden = AlmanacEntryModel.NOT_ENCOUNTERED_YET_ID;
            if (!string.IsNullOrWhiteSpace(hidden)
                && string.Equals(rawName.Trim(), hidden.Trim(), StringComparison.Ordinal))
                return true;
        }
        catch { /* the constant moved; the text test below still stands */ }

        return rawName.Contains("???");
    }

    private static string ResolveName(string raw)
    {
        string key = raw.Trim();

        string text = GameText.Resolve(key) ?? GameText.Resolve("$" + key);
        if (!string.IsNullOrWhiteSpace(text)) return UiText.Collapse(text);

        // A key that would not translate still reads better split into words than spoken as
        // an identifier, and the log then names exactly which key to go and look at.
        return UiText.Prettify(key.StartsWith("$", StringComparison.Ordinal) ? key[1..] : key);
    }

    private static string PanelLabel(string panelId, string objectName)
        => UiText.Collapse(RawPanelText(panelId, objectName));

    private static string RawPanelText(string panelId, string objectName)
    {
        try
        {
            PanelView panel = PanelWithId(panelId);
            if (panel == null) return null;

            // false: only what is switched on. The zombies page has no cost or recharge
            // label at all, and a disabled one elsewhere would be stale.
            var texts = panel.GetComponentsInChildren<TMP_Text>(false);
            if (texts == null) return null;

            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null) continue;

                string name;
                try { name = text.gameObject.name; } catch { continue; }
                if (!string.Equals(name, objectName, StringComparison.Ordinal)) continue;

                try { return text.text; } catch { return null; }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read \"{objectName}\" on the almanac: {ex.Message}");
        }

        return null;
    }

    private static PanelView PanelWithId(string panelId)
    {
        try
        {
            foreach (PanelView candidate in PanelScope.ShownPanels())
                if (PanelScope.SafeId(candidate) == panelId) return candidate;
        }
        catch { /* the enumeration logs its own failures */ }

        return null;
    }

    /// <summary>
    /// Whether a string is prose rather than a placeholder. The game ships "???" as the
    /// description of anything not yet encountered.
    /// </summary>
    private static bool LooksLikeProse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Replace("?", string.Empty).Trim().Length == 0) return false;
        return value.Contains(' ');
    }

    #endregion
}
