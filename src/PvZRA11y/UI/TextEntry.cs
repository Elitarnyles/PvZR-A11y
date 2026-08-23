using Il2CppTMPro;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Makes the game's text fields usable from the keyboard, and audible while you type.
///
/// Two separate problems. First, the field never starts listening: the game's
/// ReloadedInputField overrides shouldActivateOnSelect, so selecting the field does not
/// put it into editing mode the way a normal Unity input field would. That is a sensible
/// choice for a gamepad, where selecting a field should not swallow every button press,
/// but it leaves the field looking focused while quietly discarding everything typed.
/// Calling ActivateInputField directly fixes it.
///
/// Second, nothing announces what you type. A screen reader can echo keystrokes in a real
/// edit control, but this field is drawn by the game and is invisible to it — so the echo
/// has to come from here. Each new character is spoken as it arrives, and any larger jump
/// falls back to reading the whole value.
/// </summary>
public static class TextEntry
{
    private static TMP_InputField _watched;
    private static string _lastText = string.Empty;

    /// <summary>The field currently being edited, or null.</summary>
    public static TMP_InputField Active => IsEditing(_watched) ? _watched : null;

    /// <summary>True when keystrokes are going into a text field rather than the game.</summary>
    public static bool IsTyping => Active != null;

    /// <summary>
    /// Called when focus lands on a control. If it is a text field, start editing it and
    /// place the caret at the end so typing appends rather than overwrites.
    /// </summary>
    public static void OnFocusEntered(Selectable selectable)
    {
        TMP_InputField field = AsField(selectable);

        if (field == null)
        {
            _watched = null;
            _lastText = string.Empty;
            return;
        }

        _watched = field;
        _lastText = SafeText(field);

        try
        {
            field.ActivateInputField();
            field.MoveTextEnd(false);
            Core.Log.Msg($"[text] editing \"{UiText.SafeName(selectable)}\", current value: \"{_lastText}\"");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not start editing \"{UiText.SafeName(selectable)}\": {ex.Message}");
        }
    }

    /// <summary>Speaks characters as they are typed. Called once per frame from Core.OnUpdate.</summary>
    public static void Update()
    {
        if (_watched == null) return;

        string current = SafeText(_watched);
        if (current == _lastText) return;

        string previous = _lastText;
        _lastText = current;

        Speech.Say(DescribeChange(previous, current), interrupt: true, context: "typing");
    }

    /// <summary>
    /// Handles Enter while a field is being edited. Runs the game's own submit path, so
    /// whatever a mouse user gets by pressing Enter in the field happens here too.
    /// </summary>
    public static bool HandleSubmit()
    {
        TMP_InputField field = Active;
        if (field == null) return false;

        string value = SafeText(field);
        Core.Log.Msg($"[text] submitting \"{value}\"");

        try
        {
            EventSystem es = EventSystem.current;
            if (es != null) field.OnSubmit(new BaseEventData(es));
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Submitting the text field failed: {ex.Message}");
            return false;
        }

        Speech.Say(string.IsNullOrEmpty(value) ? Strings.T("state.blank") : value,
            interrupt: true, context: "text submitted");
        return true;
    }

    /// <summary>
    /// Turns a change in the field's contents into something worth hearing.
    ///
    /// One character added is by far the common case and is spoken on its own, which keeps
    /// up with normal typing. Anything else — a deletion, a paste, a cleared field — is
    /// too varied to describe usefully, so the whole value is read back instead.
    /// </summary>
    private static string DescribeChange(string previous, string current)
    {
        if (string.IsNullOrEmpty(current)) return Strings.T("state.blank");

        if (current.Length == previous.Length + 1 && current.StartsWith(previous, StringComparison.Ordinal))
            return SpeakableCharacter(current[^1]);

        if (current.Length == previous.Length - 1 && previous.StartsWith(current, StringComparison.Ordinal))
            return Strings.T("msg.deleted", SpeakableCharacter(previous[^1]));

        return current;
    }

    /// <summary>A space read as a bare string says nothing at all, so name it.</summary>
    private static string SpeakableCharacter(char c)
        => c == ' ' ? Strings.T("char.space") : c.ToString();

    private static TMP_InputField AsField(Selectable selectable)
    {
        if (selectable == null) return null;
        try { return selectable.TryCast<TMP_InputField>(); }
        catch { return null; }
    }

    private static bool IsEditing(TMP_InputField field)
    {
        if (field == null) return false;
        try { return field.isFocused; }
        catch { return false; }
    }

    private static string SafeText(TMP_InputField field)
    {
        if (field == null) return string.Empty;
        try { return field.text ?? string.Empty; }
        catch { return string.Empty; }
    }
}
