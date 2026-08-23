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

    /// <summary>Remembers what the screen announcement already said, so it is not repeated.</summary>
    public static void NoteSpokenElsewhere(string text)
    {
        if (Lawn.DialogueInFront && !string.IsNullOrWhiteSpace(text)) _lastSpoken = text;
    }
}
