using PvZRA11y.A11y;
using PvZRA11y.Localization;
using PvZRA11y.UI;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Reads out the notes the zombies leave after certain levels.
///
/// These are the only piece of story the game tells, and they were completely silent. The
/// award screen announced "You found a note:" and then nothing, because the note is not text
/// on the screen at all — it is a localised sprite, a picture of a handwritten letter, one
/// per language. There is no label to read and no amount of care with the screen reader
/// would have found one.
///
/// The words do exist, though. The game keeps the text of each note in its own string table
/// under the keys the artists localise the pictures from, and the award data names which note
/// is which:
///
///     AwardScreenData-Note1       level 9    note 0    IMG_GAMEPLAY_NOTE_01
///     AwardScreenData-Note2       level 19   note 1    IMG_GAMEPLAY_NOTE_02
///     AwardScreenData-Note3       level 29   note 2    IMG_GAMEPLAY_NOTE_03
///     AwardScreenData-Note4       level 39   note 3    IMG_GAMEPLAY_NOTE_04
///     AwardScreenData-NoteFinal   level 49   note 4    IMG_GAMEPLAY_NOTE_FINAL
///     AwardScreenData-NoteBossEnd level 50   note 5    IMG_CREDITS_NOTE_01
///
/// So the note is spoken by asking the game for its own text, in whatever language the game
/// is running in. Nothing is copied into this mod: the words belong to the game, and a mod
/// that shipped them would be redistributing its content and would go stale the moment the
/// game changed a line.
/// </summary>
public static class Notes
{
    /// <summary>The game's string-table key for each note, indexed by the number it binds.</summary>
    private static readonly string[] Keys =
    {
        "IMG_GAMEPLAY_NOTE_01",
        "IMG_GAMEPLAY_NOTE_02",
        "IMG_GAMEPLAY_NOTE_03",
        "IMG_GAMEPLAY_NOTE_04",
        "IMG_GAMEPLAY_NOTE_FINAL",
        "IMG_CREDITS_NOTE_01",
    };

    /// <summary>The panel the note is shown on.</summary>
    private const string PanelId = "awardScreen";

    private static int _pending = -1;
    private static int _lastSpoken = -1;

    /// <summary>Called when the game binds a note to the award screen.</summary>
    public static void NoteBound(int number)
    {
        Core.Log.Msg($"[note] the game bound note {number}");
        _pending = number;
    }

    public static void Reset()
    {
        _pending = -1;
        _lastSpoken = -1;
    }

    /// <summary>
    /// Speaks the pending note once the award screen is actually in front.
    ///
    /// Waited for rather than spoken straight from the binding, because the binding lands
    /// before the screen has finished announcing itself and the note would arrive in the
    /// middle of its own heading.
    /// </summary>
    public static void Tick()
    {
        if (_pending < 0) return;
        if (PanelScope.FrontPanelId != PanelId) return;

        int number = _pending;
        _pending = -1;

        if (number == _lastSpoken) return;
        _lastSpoken = number;

        string text = TextFor(number);

        // Queued, not interrupting: it follows "You found a note:" rather than cutting it off.
        Speech.Say(text ?? Strings.T("note.unreadable"), interrupt: false, context: "zombie note");
    }

    /// <summary>The note's words, from the game's own table, or null when it cannot be had.</summary>
    public static string TextFor(int number)
    {
        if (number < 0 || number >= Keys.Length)
        {
            Core.Log.Warning($"[note] no key for note {number}");
            return null;
        }

        string key = Keys[number];

        // Two spellings. The table stores its identifiers with a leading dollar and the
        // translator is normally handed them without one; which of the two it wants is not
        // worth a round trip through someone else's play session to find out.
        string text = GameText.Resolve(key) ?? GameText.Resolve("$" + key);

        if (text == null)
        {
            Core.Log.Warning($"[note] could not resolve \"{key}\" or \"${key}\"");
            return null;
        }

        // The notes are laid out as a letter, one short line at a time. Spoken, those line
        // breaks turn into pauses in the middle of sentences, so they become spaces.
        text = text.Replace("\r", " ").Replace("\n", " ");
        while (text.Contains("  ")) text = text.Replace("  ", " ");

        return text.Trim();
    }
}
