using PvZRA11y.A11y;
using PvZRA11y.Localization;
using PvZRA11y.UI;
using UnityEngine;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Reads out the notes the zombies leave after certain levels.
///
/// These are the only story the game tells, and they were completely silent. The award screen
/// announced "You found a note:" and then nothing, because the note is not text on the screen
/// at all — it is a localised sprite, a picture of a handwritten letter drawn once per
/// language. There is no label to read, and no amount of care with the screen reader would
/// have found one.
///
/// The words do exist. The game keeps the text of every note in its own string table, under
/// the keys the pictures are localised from, and the award data says which note is which:
///
///     AwardScreenData-Note1       level 9    note 0    IMG_GAMEPLAY_NOTE_01
///     AwardScreenData-Note2       level 19   note 1    IMG_GAMEPLAY_NOTE_02
///     AwardScreenData-Note3       level 29   note 2    IMG_GAMEPLAY_NOTE_03
///     AwardScreenData-Note4       level 39   note 3    IMG_GAMEPLAY_NOTE_04
///     AwardScreenData-NoteFinal   level 49   note 4    IMG_GAMEPLAY_NOTE_FINAL
///     AwardScreenData-NoteBossEnd level 50   note 5    IMG_CREDITS_NOTE_01
///
/// So the note is spoken by asking the game for its own text, in whatever language the game
/// is running in. Nothing is copied into this mod: the words belong to the game, a mod that
/// shipped them would be redistributing its content, and they would go stale the moment a
/// line changed.
///
/// Which note is showing is found by asking rather than by patching. A Harmony patch on the
/// binder's BindNoteNumber failed to apply at all — "IL Compile Error", the whole patch class
/// rejected — and it would have been the wrong instinct anyway: the binder switches on one of
/// its note objects and leaves the rest off, so the answer is sitting in the scene.
/// </summary>
public static class Notes
{
    /// <summary>The game's string-table key for each note, in the order the binder holds them.</summary>
    private static readonly string[] Keys =
    {
        "IMG_GAMEPLAY_NOTE_01",
        "IMG_GAMEPLAY_NOTE_02",
        "IMG_GAMEPLAY_NOTE_03",
        "IMG_GAMEPLAY_NOTE_04",
        "IMG_GAMEPLAY_NOTE_FINAL",
        "IMG_CREDITS_NOTE_01",
    };

    /// <summary>The panel a note is shown on.</summary>
    private const string PanelId = "awardScreen";

    private static bool _saidThisScreen;

    public static void Reset() => _saidThisScreen = false;

    /// <summary>
    /// Speaks the note once, while the award screen is in front.
    ///
    /// Polled rather than driven by an event, so it cannot run before the screen has finished
    /// announcing itself and land in the middle of its own heading.
    /// </summary>
    public static void Tick()
    {
        if (PanelScope.FrontPanelId != PanelId)
        {
            _saidThisScreen = false;
            return;
        }

        if (_saidThisScreen) return;

        int number = ShowingNote();
        if (number < 0) return;

        _saidThisScreen = true;

        string text = TextFor(number);

        // Queued rather than interrupting, so it follows "You found a note:".
        Speech.Say(text ?? Strings.T("note.unreadable"), interrupt: false, context: "zombie note");
    }

    /// <summary>
    /// Which note the screen is showing, or -1 when it is not showing one.
    ///
    /// The binder keeps every note as a ready-made object and switches on the one it wants,
    /// so the index of the live one is the note number.
    /// </summary>
    public static int ShowingNote()
    {
        try
        {
            var binder = UnityEngine.Object.FindObjectOfType<Il2Cpp.ZombieNoteBinder>();
            if (binder == null) return -1;

            var notes = binder.m_notes;
            if (notes == null || notes.Length == 0) return -1;

            int live = -1;
            for (int i = 0; i < notes.Length; i++)
            {
                GameObject note = notes[i];
                if (note == null) continue;

                bool on;
                try { on = note.activeInHierarchy; }
                catch { continue; }

                if (Config.Settings.VerboseLogging.Value)
                    Core.Log.Msg($"[note] slot {i}: \"{note.name}\" {(on ? "ON" : "off")}");

                if (on && live < 0) live = i;
            }

            if (live < 0) Core.Log.Msg($"[note] the binder has {notes.Length} notes and none is showing");
            return live;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[note] could not find which note is showing: {ex.Message}");
            return -1;
        }
    }

    /// <summary>The note's words, from the game's own table, or null when they cannot be had.</summary>
    public static string TextFor(int number)
    {
        if (number < 0 || number >= Keys.Length)
        {
            Core.Log.Warning($"[note] no key for note {number}");
            return null;
        }

        string key = Keys[number];

        // Two spellings. The table stores its identifiers with a leading dollar; which form
        // the translator wants is not worth a round trip through a play session to settle,
        // and asking twice costs nothing.
        string text = GameText.Resolve(key) ?? GameText.Resolve("$" + key);

        if (text == null)
        {
            Core.Log.Warning($"[note] could not resolve \"{key}\" or \"${key}\"");
            return null;
        }

        // The notes are laid out as a letter, a few words to a line. Spoken, those breaks
        // become pauses in the middle of sentences.
        text = text.Replace("\r", " ").Replace("\n", " ");
        while (text.Contains("  ")) text = text.Replace("  ", " ");

        return text.Trim();
    }

    /// <summary>Every note key and whether the game will give up its text. For the self-test.</summary>
    public static void Check(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- zombie notes ---");
        for (int i = 0; i < Keys.Length; i++)
        {
            string text = TextFor(i);
            if (string.IsNullOrEmpty(text))
            {
                sb.AppendLine($"  note {i} ({Keys[i]}): NOT RESOLVED");
                continue;
            }

            string preview = text.Length > 70 ? text.Substring(0, 70) + " ..." : text;
            sb.AppendLine($"  note {i} ({Keys[i]}): ok, {text.Length} chars — \"{preview}\"");
        }
        sb.AppendLine();
    }
}
