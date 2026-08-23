using PvZRA11y.A11y;
using PvZRA11y.UI;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Keeps a conversation readable while it is going on.
///
/// A speech bubble is one panel that changes its text, not a new screen each time. The mod
/// announces screens when the panel in front changes, so the first line was read and every
/// line after it was silent — the conversation carried on and nothing said so.
///
/// It also carries no button at all. Advancing is a click anywhere, which is nothing for a
/// keyboard player to press, so Enter fell through to whatever had been focused on the
/// previous screen and answered "Not available from here". Between the two, a conversation
/// was something you could start and not finish.
/// </summary>
public static class Dialogue
{
    /// <summary>
    /// How long to wait before reading the new line.
    ///
    /// The text arrives through data binding, a frame or two after the request. Reading
    /// immediately gets the line that is already on screen — which is the one just finished
    /// with, so it would repeat rather than move on. The same delay that once made speech
    /// bubbles read a prefab placeholder.
    /// </summary>
    private const int FramesBeforeReading = 3;

    private static int _countdown = -1;
    private static string _lastSpoken;

    public static void Reset()
    {
        _countdown = -1;
        _lastSpoken = null;
    }

    /// <summary>Called after the conversation has been moved on.</summary>
    public static void NoteAdvanced() => _countdown = FramesBeforeReading;

    public static void Tick()
    {
        TickDialogBox();

        if (!Lawn.DialogueInFront)
        {
            // Cleared on leaving, so the same opening line is read again next conversation.
            _countdown = -1;
            _lastSpoken = null;
            return;
        }

        if (_countdown < 0) return;
        if (--_countdown > 0) return;
        _countdown = -1;

        string text = PanelScope.BodyTextOf(PanelScope.FrontPanelId, ignoreSuppression: true);
        if (string.IsNullOrWhiteSpace(text)) return;

        // The last line stays the last line when a conversation ends on it; saying it twice
        // would read as the key not having worked.
        if (text == _lastSpoken) return;
        _lastSpoken = text;

        Speech.Say(text, interrupt: true, context: "dialogue");
    }

    private static string _lastDialogBox;

    /// <summary>
    /// Announces a question the game puts up — a price, a confirmation, a pair of buttons.
    ///
    /// These are a different thing from a speech bubble: the bubble is one character
    /// talking, this is the game waiting for an answer. It has a header, body text and
    /// buttons, and none of it was being read.
    /// </summary>
    private static void TickDialogBox()
    {
        Il2CppReloaded.Gameplay.Dialog dialog;
        try { dialog = Il2CppReloaded.Gameplay.Dialog.CurrentDialog; }
        catch { return; }

        if (dialog == null)
        {
            _lastDialogBox = null;
            return;
        }

        string text;
        try
        {
            var parts = new List<string>(3);
            void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) parts.Add(UiText.Collapse(s)); }

            Add(dialog.mDialogHeader);
            Add(dialog.mDialogLines);
            Add(dialog.mDialogFooter);

            text = string.Join(". ", parts);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[dialogue] could not read the dialog box: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || text == _lastDialogBox) return;
        _lastDialogBox = text;

        int buttons = 0;
        try { buttons = dialog.mNumButtons; } catch { }

        Core.Log.Msg($"[dialogue] dialog box, {buttons} button(s): {text}");
        Speech.Say(text, interrupt: true, context: "dialog box");
    }

    /// <summary>Remembers what the screen announcement already said, so it is not repeated.</summary>
    public static void NoteSpokenElsewhere(string text)
    {
        if (Lawn.DialogueInFront && !string.IsNullOrWhiteSpace(text)) _lastSpoken = text;
    }
}
