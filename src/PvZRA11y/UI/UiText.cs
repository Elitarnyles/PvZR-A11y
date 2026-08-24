using System.Text;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using PvZRA11y.Config;
using PvZRA11y.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Turns a Unity UI control into a sentence a screen reader can read.
///
/// The output follows what NVDA users already expect from desktop software:
/// label first, then role, then state — "Vibration, check box, checked".
///
/// Finding the label is the hard part. The game labels most things with TextMeshPro,
/// but plenty of controls are bare sprites whose only identity is a GameObject name
/// like "P_BacicButton_Yes". Those go through, in order: an explicit override from the
/// translation file, then any text found in the control's children, then a cleaned-up
/// version of the object name. The last one is a guess, but a readable guess beats
/// silence and it tells us exactly which name to add an override for.
/// </summary>
public static class UiText
{
    /// <summary>Word endings that duplicate the spoken role, so they get trimmed off the label.</summary>
    private static readonly string[] RoleSuffixes =
    {
        "CheckBox", "Check Box", "Checkbox", "Button", "Slider", "Toggle",
        "Dropdown", "Drop Down", "ScrollBar", "Scroll Bar", "InputField", "Input Field",
    };

    /// <summary>
    /// Builds the full announcement for a control: label, role, state, and optionally
    /// its position within the current screen.
    /// </summary>
    public static string Describe(Selectable selectable, int index = -1, int total = -1)
    {
        if (selectable == null) return null;

        var parts = new List<string>(4);

        string label = GetLabel(selectable);
        if (!string.IsNullOrEmpty(label)) parts.Add(label);

        if (Settings.SpeakRoles.Value)
        {
            string role = GetRole(selectable);
            if (!string.IsNullOrEmpty(role)) parts.Add(role);
        }

        string state = GetState(selectable);
        if (!string.IsNullOrEmpty(state)) parts.Add(state);

        if (!IsInteractable(selectable)) parts.Add(Strings.T("state.disabled"));

        if (Settings.SpeakPositionInList.Value && index >= 0 && total > 0)
            parts.Add(Strings.T("state.position", index + 1, total));

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The control's name, without role or state.</summary>
    public static string GetLabel(Selectable selectable)
    {
        if (selectable == null) return null;

        string objectName = SafeName(selectable);

        // An explicit override always wins; it is how we fix anything the game
        // gets wrong, and it is translatable.
        if (Strings.TryUiLabel(objectName, out string mapped))
            return mapped;

        // Almanac tiles carry no text at all — two images and nothing else — and every one
        // of them is a clone sharing a single GameObject name, so the override above has
        // nothing to key on either. What they are lives only in the data model.
        string almanac = Almanac.LabelFor(selectable);
        if (!string.IsNullOrEmpty(almanac)) return almanac;

        // The shop is built the same way: its tiles read out as bare prices, with no idea
        // what they were the price of.
        string store = Store.LabelFor(selectable);
        if (!string.IsNullOrEmpty(store)) return store;

        string text = ReadAnyText(selectable);
        if (!string.IsNullOrWhiteSpace(text))
            return Collapse(text);

        // Nothing readable in the hierarchy. Make the object name pronounceable.
        string pretty = Prettify(objectName);
        return string.IsNullOrEmpty(pretty) ? Strings.T("msg.unlabelled") : pretty;
    }

    /// <summary>The spoken control type, e.g. "button" or "check box".</summary>
    public static string GetRole(Selectable selectable)
    {
        if (selectable == null) return null;

        if (selectable.TryCast<Toggle>() != null) return Strings.T("role.checkbox");
        if (selectable.TryCast<Slider>() != null) return Strings.T("role.slider");
        if (selectable.TryCast<Scrollbar>() != null) return Strings.T("role.scrollbar");
        if (selectable.TryCast<Dropdown>() != null) return Strings.T("role.dropdown");
        if (selectable.TryCast<TMP_Dropdown>() != null) return Strings.T("role.dropdown");
        if (selectable.TryCast<InputField>() != null) return Strings.T("role.textfield");
        if (selectable.TryCast<TMP_InputField>() != null) return Strings.T("role.textfield");
        if (selectable.TryCast<Button>() != null) return Strings.T("role.button");

        // The game's own navigation containers stand in for lists and grids.
        string typeName = SafeTypeName(selectable);
        if (typeName.Contains("GridNavigation")) return Strings.T("role.grid");
        if (typeName.Contains("ListNavigation")) return Strings.T("role.list");

        return Strings.T("role.selectable");
    }

    /// <summary>Anything dynamic worth hearing: checked, slider percentage, current option.</summary>
    public static string GetState(Selectable selectable)
    {
        if (selectable == null) return null;

        try
        {
            // A text field's contents are its most important state, and nothing else
            // reports them: the field is drawn by the game, so a screen reader sees nothing.
            var input = selectable.TryCast<TMP_InputField>();
            if (input != null)
            {
                string value = input.text;
                return string.IsNullOrEmpty(value) ? Strings.T("state.blank") : value;
            }

            var toggle = selectable.TryCast<Toggle>();
            if (toggle != null)
                return Strings.T(toggle.isOn ? "state.checked" : "state.unchecked");

            var slider = selectable.TryCast<Slider>();
            if (slider != null)
            {
                float span = slider.maxValue - slider.minValue;
                int percent = span <= 0f ? 0 : (int)Math.Round((slider.value - slider.minValue) / span * 100f);
                return Strings.T("state.percent", percent);
            }

            var scrollbar = selectable.TryCast<Scrollbar>();
            if (scrollbar != null)
                return Strings.T("state.percent", (int)Math.Round(scrollbar.value * 100f));

            var dropdown = selectable.TryCast<Dropdown>();
            if (dropdown != null && dropdown.captionText != null)
                return Collapse(dropdown.captionText.text);

            var tmpDropdown = selectable.TryCast<TMP_Dropdown>();
            if (tmpDropdown != null && tmpDropdown.captionText != null)
                return Collapse(tmpDropdown.captionText.text);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read control state: {ex.Message}");
        }

        return null;
    }

    public static bool IsInteractable(Selectable selectable)
    {
        try { return selectable != null && selectable.IsInteractable(); }
        catch { return true; }
    }

    public static bool IsVisible(Selectable selectable)
    {
        try
        {
            return selectable != null
                && selectable.gameObject != null
                && selectable.gameObject.activeInHierarchy
                && selectable.IsActive();
        }
        catch { return false; }
    }

    public static string SafeName(Selectable selectable)
    {
        try { return selectable?.gameObject?.name ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string SafeTypeName(Il2CppSystem.Object obj)
    {
        try { return obj?.GetIl2CppType()?.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>Full scene path of a GameObject, for the diagnostic dump.</summary>
    public static string PathOf(GameObject go)
    {
        if (go == null) return "<null>";
        try
        {
            var stack = new List<string>();
            Transform t = go.transform;
            while (t != null && stack.Count < 32)
            {
                stack.Add(t.name);
                t = t.parent;
            }
            stack.Reverse();
            return string.Join("/", stack);
        }
        catch { return go.name; }
    }

    /// <summary>First non-empty text found on the control or any of its children.</summary>
    private static string ReadAnyText(Selectable selectable)
    {
        try
        {
            var tmp = selectable.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text;
        }
        catch { /* component missing on this control */ }

        try
        {
            var legacy = selectable.GetComponentInChildren<Text>();
            if (legacy != null && !string.IsNullOrWhiteSpace(legacy.text)) return legacy.text;
        }
        catch { /* component missing on this control */ }

        return null;
    }

    /// <summary>
    /// Makes a GameObject name speakable: "P_BacicButton_RestartLevel" becomes
    /// "Restart Level". Cosmetic only — an override in the translation file is
    /// always better, and the dump key tells us which one to add.
    /// </summary>
    public static string Prettify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        string s = name.Trim();

        // Unity's duplicate marker: "Thing (1)"
        int paren = s.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && s.EndsWith(")", StringComparison.Ordinal)) s = s[..paren];

        // Prefixes the game's prefabs use for panel-scoped widgets.
        foreach (string prefix in new[] { "P_", "UI_", "Btn_", "m_" })
            if (s.StartsWith(prefix, StringComparison.Ordinal)) { s = s[prefix.Length..]; break; }

        s = s.Replace('_', ' ').Replace('-', ' ');

        // Split PascalCase and camelCase into words.
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool boundary = i > 0
                && char.IsUpper(c)
                && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1])));
            if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            sb.Append(c);
        }
        s = Collapse(sb.ToString());

        // The role is spoken separately, so drop a trailing copy of it.
        foreach (string suffix in RoleSuffixes)
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && s.Length > suffix.Length)
            {
                s = s[..^suffix.Length].TrimEnd();
                break;
            }
        }

        s = s.Trim();

        // Names like "mainMenu" split into "main Menu", which reads oddly.
        if (s.Length > 0 && char.IsLower(s[0]))
            s = char.ToUpperInvariant(s[0]) + s[1..];

        return s;
    }

    /// <summary>Strips rich-text tags and squeezes whitespace into single spaces.</summary>
    public static string Collapse(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        bool inTag = false;
        bool lastWasSpace = false;

        // Some labels carry escape sequences that were never turned into characters: the
        // main menu's almanac button is literally "the\nsuburban\nalmanac", backslash and
        // letter n, four characters that no whitespace test will ever match. Spoken, it came
        // out as "the backslash n suburban backslash n almanac".
        text = text.Replace("\\r\\n", " ")
                   .Replace("\\n", " ")
                   .Replace("\\r", " ")
                   .Replace("\\t", " ");

        foreach (char c in text)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (inTag) continue;

            // The game emphasises words with asterisks. Spoken aloud they become "star".
            if (c == '*') continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }
}
