using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Watches UI focus, announces it, and keeps it from going missing.
///
/// Announcing works by polling EventSystem.currentSelectedGameObject once per frame
/// rather than by patching Selectable.OnSelect. That looks lazier and is in fact more
/// reliable: the game's navigation containers override OnSelect, and an override that
/// does not chain to base never reaches a patch on the base method. Polling sees every
/// focus change no matter what caused it.
///
/// Two things beyond announcing turn out to be necessary. The game leaves screens with
/// nothing selected when it thinks you are on mouse and keyboard, which makes the whole
/// screen feel dead because arrows and Enter have no target; and it drops the selection
/// again at odd moments. So focus is placed when a screen opens, and put back when it
/// disappears.
/// </summary>
public static class Focus
{
    private static GameObject _lastAnnounced;

    /// <summary>Frames left in which to try giving a freshly opened screen an initial focus.</summary>
    private static int _focusAttemptsLeft;

    private static Selectable _lastGoodSelection;
    private static int _framesWithoutSelection;
    private static int _framesOutsideScreen;
    private static int _restoresThisScreen;

    /// <summary>How long focus may sit on an unreachable control before it is moved.</summary>
    private const int FollowAfterFrames = 15;


    /// <summary>How long to let the selection stay empty before putting it back.</summary>
    private const int RestoreAfterNullFrames = 20;

    /// <summary>Stop re-asserting after this many tries, so we never fight the game in a loop.</summary>
    private const int MaxRestoresPerScreen = 5;

    /// <summary>Controls named individually by "read screen" before it switches to a count.</summary>
    private const int ReadScreenLimit = 40;

    /// <summary>
    /// Asks for focus to be placed on the first control of the screen that is opening.
    ///
    /// A panel is not fully built on the frame it is shown, so this does not act
    /// immediately — it retries each frame until controls appear, then gives up. If the
    /// game sets its own focus in the meantime we leave it alone.
    /// </summary>
    public static void RequestInitialFocus()
    {
        _focusAttemptsLeft = 30; // roughly half a second at 60 fps
        _restoresThisScreen = 0;
        InvalidateCache();
    }

    /// <summary>Called once per frame from Core.OnUpdate.</summary>
    public static void Update()
    {
        TickInitialFocus();

        GameObject current = CurrentSelection();

        if (current == null)
        {
            _framesWithoutSelection++;
            _framesOutsideScreen = 0;
            TickRestore();
        }
        else
        {
            _framesWithoutSelection = 0;
            TickFollowScreen(current);
        }

        if (SameObject(current, _lastAnnounced)) return;

        _lastAnnounced = current;

        if (current == null)
        {
            if (Settings.VerboseLogging.Value) Core.Log.Msg("[focus] selection cleared");
            return;
        }

        Selectable selectable = SelectableOn(current);
        if (selectable != null) _lastGoodSelection = selectable;

        // A text field has to be told to start listening before it will accept anything.
        TextEntry.OnFocusEntered(selectable);

        if (Settings.VerboseLogging.Value)
            Core.Log.Msg($"[focus] -> \"{SafeName(current)}\"  (panel: {PanelScope.PanelIdOf(current) ?? "none"})");

        Announce(current, "focus changed");
    }

    /// <summary>Speaks a GameObject that just took focus, prefixed by the screen name when one is pending.</summary>
    public static void Announce(GameObject go, string context)
    {
        if (go == null) return;

        Selectable selectable = SelectableOn(go);
        string text;

        if (selectable != null)
        {
            var visible = CollectVisible();
            int index = IndexOf(visible, selectable);
            text = UiText.Describe(selectable, index, visible.Count);
        }
        else
        {
            text = UiText.Prettify(SafeName(go));
        }

        if (string.IsNullOrEmpty(text)) return;

        // A screen change that happened moments ago rides along, so the two are spoken
        // as one phrase instead of the control cutting the screen name off.
        string prefix = ScreenTracker.ConsumePrefix();
        if (!string.IsNullOrEmpty(prefix)) text = prefix + ". " + text;

        Speech.Say(text, interrupt: true, context: context);
    }

    /// <summary>
    /// Gives a newly opened screen an initial focus when the game left it with none.
    ///
    /// This is what makes the keyboard work at all: with nothing selected, arrow keys and
    /// Enter have no target, so the screen appears completely dead even though every
    /// control on it is perfectly reachable.
    /// </summary>
    private static void TickInitialFocus()
    {
        if (_focusAttemptsLeft <= 0) return;
        _focusAttemptsLeft--;

        if (!Settings.AutoFocusFirstControl.Value) { _focusAttemptsLeft = 0; return; }

        // While the lawn is in charge, focus is beside the point: the arrow keys move the
        // grid cursor, not a highlight. Chasing the heads-up display only produced chatter
        // about the fast-forward button every time it appeared.
        if (Gameplay.Lawn.HasInput) { _focusAttemptsLeft = 0; return; }

        // The game got there first. Its choice wins.
        if (CurrentSelection() != null) { _focusAttemptsLeft = 0; return; }

        var visible = CollectVisible();
        if (visible.Count == 0)
        {
            // Panel is probably still building. Try again next frame.
            if (_focusAttemptsLeft == 0)
                Core.Log.Msg("[focus] screen opened and never produced a reachable control");
            return;
        }

        Selectable target = PreferredFirst(visible);
        Core.Log.Msg($"[focus] screen opened with nothing focused; selecting \"{UiText.SafeName(target)}\" of {visible.Count} controls");
        SetSelection(target);
        _focusAttemptsLeft = 0;
    }

    /// <summary>
    /// Moves focus onto the screen in front when it is left behind on one that is no
    /// longer reachable — most often because a dialog just opened over it.
    ///
    /// The delay matters: during a screen transition the focused control is briefly
    /// unreachable while the new panel is still building, and reacting to that would
    /// bounce focus around for no reason.
    /// </summary>
    private static void TickFollowScreen(GameObject current)
    {
        if (!Settings.AutoFocusFirstControl.Value) return;
        if (_focusAttemptsLeft > 0) return;
        if (Gameplay.Lawn.HasInput) { _framesOutsideScreen = 0; return; }

        var visible = CollectVisible();
        if (visible.Count == 0) { _framesOutsideScreen = 0; return; }

        Selectable selectable = SelectableOn(current);
        if (selectable != null && IndexOf(visible, selectable) >= 0)
        {
            _framesOutsideScreen = 0;
            return;
        }

        if (++_framesOutsideScreen < FollowAfterFrames) return;

        Selectable target = PreferredFirst(visible);
        _framesOutsideScreen = 0;
        Core.Log.Msg($"[focus] \"{SafeName(current)}\" is no longer on the active screen; moving to \"{UiText.SafeName(target)}\"");
        SetSelection(target);
    }

    /// <summary>Puts the selection back after the game drops it mid-screen.</summary>
    private static void TickRestore()
    {
        if (!Settings.AutoFocusFirstControl.Value) return;
        if (_focusAttemptsLeft > 0) return;                   // opening sequence owns this
        if (_framesWithoutSelection < RestoreAfterNullFrames) return;
        if (_restoresThisScreen >= MaxRestoresPerScreen) return;

        var visible = CollectVisible();
        if (visible.Count == 0) return;

        Selectable target = IndexOf(visible, _lastGoodSelection) >= 0
            ? _lastGoodSelection
            : PreferredFirst(visible);

        _restoresThisScreen++;
        _framesWithoutSelection = 0;
        Core.Log.Msg($"[focus] selection was dropped; restoring \"{UiText.SafeName(target)}\" (attempt {_restoresThisScreen})");
        SetSelection(target);
    }

    /// <summary>
    /// Presses the focused control.
    ///
    /// Needed because the game may not wire its submit action while it thinks you are on
    /// mouse and keyboard, which is what makes Enter feel dead. Buttons are invoked through
    /// their own onClick, so anything the game does on click happens here too.
    /// </summary>
    public static bool ActivateCurrent()
    {
        GameObject go = CurrentSelection();
        if (go == null)
        {
            Speech.Say(Strings.T("msg.nothing_focused"), context: "activate");
            return false;
        }

        // There was a guard here that discarded a second press of the same control inside
        // 400 ms, added because the game's Adventure button re-runs its action rather than
        // ignoring a repeat. It had to go: pressing Enter twice on a level tile is how you
        // start a level — once to bring it to the middle, once to play it — and the guard
        // swallowed the second press. Suppressing a harmless quirk of the game is not worth
        // breaking an interaction the player needs, and mouse users live with the quirk too.

        Selectable s = SelectableOn(go);
        if (s == null)
        {
            Core.Log.Msg($"[activate] \"{SafeName(go)}\" is not a control");
            return false;
        }

        if (!UiText.IsInteractable(s))
        {
            Speech.Say(Strings.T("state.disabled"), context: "activate");
            return false;
        }

        // Refuse to press anything on a screen that is not on display. Doing so throws
        // inside the game, because those screens were never initialised.
        var reach = PanelScope.Evaluate(s);
        if (!reach.Reachable)
        {
            Core.Log.Warning($"[activate] refused \"{UiText.SafeName(s)}\": {reach.Reason} (panel {reach.PanelId ?? "none"})");
            Speech.Say(Strings.T("msg.not_available"), context: "activate");
            return false;
        }

        try
        {
            // Level tiles take two presses in this game, so they get their own handling.
            if (LevelSelect.TryActivate(s)) return true;

            var toggle = s.TryCast<Toggle>();
            if (toggle != null)
            {
                toggle.isOn = !toggle.isOn;
                Core.Log.Msg($"[activate] toggle \"{UiText.SafeName(s)}\" -> {toggle.isOn}");
                Speech.Say(Strings.T(toggle.isOn ? "state.checked" : "state.unchecked"), context: "activate");
                return true;
            }

            var button = s.TryCast<Button>();
            if (button != null)
            {
                Core.Log.Msg($"[activate] button \"{UiText.SafeName(s)}\" on panel {reach.PanelId ?? "none"}");
                button.onClick.Invoke();

                // Opening an almanac entry fills the panel but does not move focus, so
                // nothing would otherwise be said at the moment the screen changes.
                Almanac.NoteActivated(s);
                return true;
            }

            Core.Log.Msg($"[activate] no handler for {UiText.SafeTypeName(s)} \"{UiText.SafeName(s)}\"");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Activating \"{UiText.SafeName(s)}\" failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>Announces the current screen and whatever has focus. Bound to the "where am I" key.</summary>
    public static void AnnounceCurrent()
    {
        var parts = new List<string>(2);

        string screen = ScreenTracker.CurrentName;
        if (!string.IsNullOrEmpty(screen)) parts.Add(Strings.T("msg.screen_is", screen));

        GameObject go = CurrentSelection();
        if (go == null)
        {
            parts.Add(Strings.T("msg.nothing_focused"));
        }
        else
        {
            Selectable s = SelectableOn(go);
            var visible = CollectVisible();
            string desc = s != null
                ? UiText.Describe(s, IndexOf(visible, s), visible.Count)
                : UiText.Prettify(SafeName(go));
            if (!string.IsNullOrEmpty(desc)) parts.Add(desc);
        }

        Speech.SayVerbatim(string.Join(". ", parts), "where am I");
    }

    /// <summary>Reads out every control on the current screen. Bound to the "read screen" key.</summary>
    public static void ReadScreen()
    {
        var visible = CollectVisible();
        if (visible.Count == 0)
        {
            Speech.SayVerbatim(Strings.T(EmptyScreenReason()), "read screen");
            return;
        }

        var parts = new List<string>(Math.Min(visible.Count, ReadScreenLimit) + 3);

        string screen = ScreenTracker.CurrentName;
        if (!string.IsNullOrEmpty(screen)) parts.Add(Strings.T("msg.screen_is", screen));
        parts.Add(Strings.T("msg.controls_count", visible.Count));

        // Runs of identical controls are collapsed. A grid screen such as the almanac is
        // dozens of tiles reading the same words, and spelling each one out is minutes of
        // speech that says one thing. The limit below counts groups, not controls, so
        // collapsing buys real coverage rather than just shortening the sentence.
        int spoken = 0;
        int i = 0;

        while (i < visible.Count && spoken < ReadScreenLimit)
        {
            string line = LineFor(visible[i]);

            int run = 1;
            while (i + run < visible.Count && LineFor(visible[i + run]) == line) run++;

            parts.Add(run == 1 ? line : Strings.T("msg.repeated", line, run));
            i += run;
            spoken++;
        }

        if (i < visible.Count)
            parts.Add(Strings.T("msg.and_more", visible.Count - i));

        Speech.SayVerbatim(string.Join(". ", parts), "read screen");
    }

    private static bool _wasEmpty;

    /// <summary>
    /// Says when a screen that had nothing on it becomes usable.
    ///
    /// Screens in this game animate in, with their buttons switched off until they have
    /// finished. Without this the player is left pressing Tab into silence with no way to
    /// know the moment it starts working, which is most of the wait.
    /// </summary>
    public static void TickReadiness()
    {
        bool empty;
        try { empty = CollectVisible().Count == 0; }
        catch { return; }

        if (empty == _wasEmpty) return;
        _wasEmpty = empty;

        // Only the transition into usable is worth a word. Going quiet is what leaving a
        // screen sounds like, and that is already announced by the screen change.
        if (!empty && ScreenTracker.CurrentId != null)
            Speech.Say(Strings.T("msg.now_ready"), interrupt: false, context: "screen ready");
    }

    /// <summary>
    /// Moves focus to a control the mod has already spoken about, without the watcher
    /// announcing it a second time.
    ///
    /// For the screens the mod navigates itself, where the only job left is making what is
    /// on screen agree with what was said.
    /// </summary>
    public static void AdoptSelection(Selectable selectable)
    {
        if (selectable == null) return;

        SetSelection(selectable);
        try { _lastAnnounced = selectable.gameObject; }
        catch { }
    }

    /// <summary>
    /// Why a screen has nothing to walk: genuinely empty, or not usable yet.
    ///
    /// The two sound alike and mean opposite things. Crazy Dave's shop before the taco
    /// mini-game has three buttons that the game has switched off while the scene plays,
    /// and saying "no controls found" there reads as a broken screen when the right answer
    /// is "wait a moment". Told apart by asking why each control was rejected.
    /// </summary>
    private static string EmptyScreenReason()
    {
        try
        {
            var all = Selectable.allSelectablesArray;
            if (all == null) return "msg.empty_screen";

            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (s == null) continue;
                if (!UiText.IsVisible(s)) continue;

                // On screen, in a panel on display, and merely switched off. That is a
                // control which is going to become usable, not one that is not there.
                if (UiText.IsInteractable(s)) continue;
                if (!PanelScope.Evaluate(s).Reachable && PanelScope.PanelIdOf(s) != PanelScope.FrontPanelId)
                    continue;

                return "msg.not_ready";
            }
        }
        catch { /* fall through to the plainer answer */ }

        return "msg.empty_screen";
    }

    /// <summary>What one control contributes to a whole-screen readout.</summary>
    private static string LineFor(Selectable s)
    {
        string label = UiText.GetLabel(s);
        string state = UiText.GetState(s);
        return string.IsNullOrEmpty(state) ? label : $"{label} {state}";
    }

    /// <summary>
    /// Moves focus by <paramref name="delta"/> places through the controls on screen,
    /// wrapping at both ends. The focus watcher announces the result, so this stays silent.
    /// </summary>
    public static void Move(int delta)
    {
        var visible = CollectVisible();
        if (visible.Count == 0)
        {
            Speech.Say(Strings.T(EmptyScreenReason()), context: "focus walk");
            return;
        }

        int index = IndexOf(visible, SelectableOn(CurrentSelection()));
        int next = index < 0
            ? (delta > 0 ? 0 : visible.Count - 1)
            : ((index + delta) % visible.Count + visible.Count) % visible.Count;

        SetSelection(visible[next]);
    }

    public static void SetSelection(Selectable selectable)
    {
        if (selectable == null) return;
        try
        {
            EventSystem es = EventSystem.current;
            if (es == null) return;
            es.SetSelectedGameObject(selectable.gameObject);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not move focus: {ex.Message}");
        }
    }

    private static List<Selectable> _cachedVisible;
    private static int _cacheExpiresAtFrame = int.MinValue;

    /// <summary>
    /// Frames a collected list stays good for. Filtering runs a hit test per control, which
    /// is too costly to repeat every frame, and the UI does not change faster than this.
    /// </summary>
    private const int CacheLifetimeFrames = 5;

    /// <summary>Throws away the cached list, for when the screen is known to have changed.</summary>
    public static void InvalidateCache() => _cacheExpiresAtFrame = int.MinValue;

    /// <summary>
    /// Every control the player can currently reach, ordered the way the screen reads:
    /// top to bottom, then left to right. See <see cref="PanelScope"/> for what "reach"
    /// means here and why the obvious answer is not good enough.
    /// </summary>
    public static List<Selectable> CollectVisible()
    {
        int frame = SafeFrameCount();
        if (_cachedVisible != null && frame < _cacheExpiresAtFrame)
            return _cachedVisible;

        var candidates = new List<Selectable>();

        try
        {
            var all = Selectable.allSelectablesArray;
            if (all != null)
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null) candidates.Add(all[i]);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not enumerate controls: {ex.Message}");
            return _cachedVisible ?? new List<Selectable>();
        }

        List<Selectable> result = PanelScope.Filter(candidates);
        result.Sort(CompareByScreenOrder);

        _cachedVisible = result;
        _cacheExpiresAtFrame = frame + CacheLifetimeFrames;
        return result;
    }

    private static int SafeFrameCount()
    {
        try { return Time.frameCount; }
        catch { return 0; }
    }

    /// <summary>
    /// The control an opening screen should start on. A button is a far more useful
    /// landing place than the scroll bar that often happens to sit above it.
    /// </summary>
    private static Selectable PreferredFirst(List<Selectable> visible)
    {
        foreach (Selectable s in visible)
        {
            try { if (s.TryCast<Button>() != null) return s; }
            catch { /* not a button */ }
        }
        return visible[0];
    }

    /// <summary>Reading order: higher on screen first, then further left.</summary>
    private static int CompareByScreenOrder(Selectable a, Selectable b)
    {
        Vector3 pa = SafePosition(a);
        Vector3 pb = SafePosition(b);

        // Treat rows within a few pixels of each other as the same row.
        const float RowTolerance = 8f;
        if (Math.Abs(pa.y - pb.y) > RowTolerance)
            return pb.y.CompareTo(pa.y);

        return pa.x.CompareTo(pb.x);
    }

    private static Vector3 SafePosition(Selectable s)
    {
        try { return s?.transform?.position ?? Vector3.zero; }
        catch { return Vector3.zero; }
    }

    private static int IndexOf(List<Selectable> list, Selectable target)
    {
        if (target == null) return -1;
        int targetId = SafeInstanceId(target);
        for (int i = 0; i < list.Count; i++)
            if (SafeInstanceId(list[i]) == targetId) return i;
        return -1;
    }

    public static GameObject CurrentSelection()
    {
        try
        {
            EventSystem es = EventSystem.current;
            return es == null ? null : es.currentSelectedGameObject;
        }
        catch { return null; }
    }

    private static Selectable SelectableOn(GameObject go)
    {
        if (go == null) return null;
        try { return go.GetComponent<Selectable>(); }
        catch { return null; }
    }

    private static bool SameObject(UnityEngine.Object a, UnityEngine.Object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return a == null && b == null;
        return SafeInstanceId(a) == SafeInstanceId(b);
    }

    private static int SafeInstanceId(UnityEngine.Object o)
    {
        try { return o == null ? 0 : o.GetInstanceID(); }
        catch { return 0; }
    }

    private static string SafeName(GameObject go)
    {
        try { return go?.name ?? string.Empty; }
        catch { return string.Empty; }
    }
}
