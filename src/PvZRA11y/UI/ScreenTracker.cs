using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;

namespace PvZRA11y.UI;

/// <summary>
/// Works out which screen the player is actually on, and announces it when that changes.
///
/// Naming the screen after the most recent PanelView.Show call does not work: the game
/// opens several panels in one breath, re-shows the same panel repeatedly, and the last
/// one to open is often not the one in front of you. So the screen is derived from the
/// control that has focus — whichever panel owns it is, by definition, the screen you
/// are on — and only falls back to the topmost displayed panel when nothing has focus.
///
/// When the screen changes, the name is held as a prefix rather than spoken straight
/// away. Focus almost always moves a frame or two later, and a screen name followed by
/// an interrupting control announcement would simply be cut off. Holding it lets the two
/// come out as one sentence: "Level select. Level 1-1, button, 1 of 50."
/// </summary>
public static class ScreenTracker
{
    /// <summary>Raw panel id, e.g. "mainMenu". Empty until a screen is identified.</summary>
    public static string CurrentId { get; private set; } = string.Empty;

    /// <summary>The screen name to speak: a translation when we have one, otherwise a tidied id.</summary>
    public static string CurrentName => NameOf(CurrentId);

    private static string _announcedId = null;
    private static string _pendingId;
    private static int _pendingFramesLeft;

    /// <summary>Frames to wait for a focus change to absorb the screen name before speaking it alone.</summary>
    private const int PrefixGraceFrames = 12;

    /// <summary>Called once per frame from Core.OnUpdate, before focus is examined.</summary>
    public static void Poll()
    {
        string id = Detect();

        if (!string.IsNullOrEmpty(id)) CurrentId = id;

        if (!string.IsNullOrEmpty(id) && id != _announcedId)
        {
            _announcedId = id;

            if (Settings.SpeakScreenChanges.Value && !string.IsNullOrEmpty(NameOf(id)))
            {
                _pendingId = id;
                _pendingFramesLeft = PrefixGraceFrames;
                Core.Log.Msg($"[screen] now on \"{id}\"");
            }
        }

        // Focus never moved, so say the screen name on its own.
        if (_pendingId != null && --_pendingFramesLeft <= 0)
        {
            string prefix = BuildPrefix();
            _pendingId = null;
            if (!string.IsNullOrEmpty(prefix))
                Speech.Say(prefix, interrupt: false, context: "screen changed");
        }
    }

    /// <summary>
    /// Hands the pending screen name to whoever is about to speak, so it can be said as
    /// one phrase instead of being interrupted. Returns null when there is nothing pending.
    /// </summary>
    public static string ConsumePrefix()
    {
        if (_pendingId == null) return null;

        string prefix = BuildPrefix();
        _pendingId = null;
        return prefix;
    }

    /// <summary>
    /// Builds the announcement for the screen, at the last possible moment.
    ///
    /// The delay is the point. A dialogue box's text is not in the panel when it opens —
    /// the prefab carries a placeholder ("Speech bubble text...") and the real line is bound
    /// in a frame or two later. Reading on the opening frame reliably produced the
    /// placeholder, which sounds exactly like a working feature and is worse than silence.
    /// </summary>
    private static string BuildPrefix()
    {
        string id = _pendingId;
        if (id == null) return null;

        string name = NameOf(id);
        string body = PanelScope.BodyTextOf(id);

        if (string.IsNullOrEmpty(body)) return name;

        Core.Log.Msg($"[screen] \"{id}\" says: {body}");

        // So the conversation watcher does not read the opening line a second time.
        Gameplay.Dialogue.NoteSpokenElsewhere(body);
        return string.IsNullOrEmpty(name) ? body : name + ". " + body;
    }

    /// <summary>Records a panel opening. Only used to notice screens that have no controls at all.</summary>
    public static void NotePanelShown(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (!string.IsNullOrEmpty(CurrentId)) return;
        CurrentId = id.Trim();
    }

    private static string Detect()
    {
        // The panel that owns the focused control is the screen the player is on.
        string fromFocus = PanelScope.PanelIdOf(Focus.CurrentSelection());
        if (!string.IsNullOrEmpty(fromFocus)) return fromFocus;

        // Nothing focused. Fall back to whichever displayed panel holds reachable controls.
        var panels = PanelScope.ShownPanels();
        if (panels.Count == 0) return null;

        return PanelScope.SafeId(panels[^1]);
    }

    private static string NameOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        string key = "screen." + id;
        return Strings.Has(key) ? Strings.T(key) : UiText.Prettify(id);
    }
}
