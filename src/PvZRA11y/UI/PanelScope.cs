using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTekly.Common.Presentables;
using Il2CppTekly.PanelViews;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Decides which controls the player can actually reach right now.
///
/// This matters more than it sounds. Unity's list of every Selectable returns a hundred
/// controls when the screen in front of you has six, because the game keeps whole
/// sections built and active while they are not on display. Walking into one of them is
/// not merely confusing: pressing a level tile belonging to a carousel that was never
/// opened throws inside the game.
///
/// Panel state alone does not sort this out. The main menu, level select, options,
/// achievements and the survival mode buttons all live inside a single PanelView called
/// "mainMenu" and are swapped by some other means, so every one of them reports the same
/// state. The question that does have a reliable answer is the one the player would ask:
/// can I see this, and can I click it? That is what gets tested here.
///
/// Three filters run in order, cheapest first:
///   1. panel state    — excludes genuinely hidden screens
///   2. screen bounds  — excludes anything slid off-screen or scaled away
///   3. hit test       — excludes anything covered by something else
///
/// Screen bounds turns out to do nearly all the work here, because the main menu hides
/// its sections by moving them rather than by disabling them. The hit test is off by
/// default: on this game it rejected every control it was asked about, so it contributes
/// nothing but risk. It stays available behind a setting, with a safety net that discards
/// its verdict outright when it rejects everything — announcing too many controls is a
/// nuisance, announcing none leaves the player unable to do anything at all.
/// </summary>
public static class PanelScope
{
    /// <summary>Why a control was or was not considered reachable. Surfaces in the diagnostic dump.</summary>
    public readonly record struct Reach(bool Reachable, string PanelId, string PanelState, string Reason);

    /// <summary>Counts from the most recent filter run, for the dump to report.</summary>
    public readonly record struct FilterStats(int Total, int AfterBasics, int AfterPanel, int AfterBounds, int AfterHitTest, bool FellBack);

    public static FilterStats LastStats { get; private set; }

    private const int MaxDepth = 64;

    /// <summary>A control smaller than this on screen is treated as hidden rather than tiny.</summary>
    private const float MinScreenSize = 2f;

    /// <summary>
    /// Narrows a raw list of controls down to the ones the player can reach.
    ///
    /// If the hit test rejects everything, its result is thrown away and the pre-hit-test
    /// list is used instead. Announcing too many controls is a nuisance; announcing none
    /// leaves the player with no way to do anything at all, so when the strict filter
    /// looks wrong it loses.
    /// </summary>
    public static List<Selectable> Filter(IReadOnlyList<Selectable> candidates)
    {
        int total = candidates.Count;

        var basics = new List<Selectable>(total);
        foreach (Selectable s in candidates)
        {
            if (s == null) continue;
            if (!UiText.IsVisible(s)) continue;
            if (!UiText.IsInteractable(s)) continue;
            basics.Add(s);
        }

        var onPanel = new List<Selectable>(basics.Count);
        foreach (Selectable s in basics)
            if (PanelAllows(s)) onPanel.Add(s);

        var onScreen = new List<Selectable>(onPanel.Count);
        foreach (Selectable s in onPanel)
            if (TryScreenRect(s, out _)) onScreen.Add(s);

        // A dialog opening over a screen leaves everything underneath it on screen and
        // perfectly clickable as far as Unity is concerned, so without this you can walk
        // straight out of an open dialog into the menu behind it.
        onScreen = ScopeToTopPanel(onScreen);

        if (!Config.Settings.UseHitTest.Value)
        {
            LastStats = new FilterStats(total, basics.Count, onPanel.Count, onScreen.Count, onScreen.Count, false);
            return onScreen;
        }

        var clickable = new List<Selectable>(onScreen.Count);
        foreach (Selectable s in onScreen)
            if (TryScreenRect(s, out Rect rect) && PassesHitTest(s, rect)) clickable.Add(s);

        bool fellBack = clickable.Count == 0 && onScreen.Count > 0;
        if (fellBack)
            Core.Log.Warning($"[scope] hit test rejected all {onScreen.Count} on-screen controls; ignoring it this time");

        LastStats = new FilterStats(total, basics.Count, onPanel.Count, onScreen.Count, clickable.Count, fellBack);

        return fellBack ? onScreen : clickable;
    }

    /// <summary>Order in which panels were last shown. Highest number is the one on top.</summary>
    private static readonly Dictionary<string, long> ShowOrder = new(StringComparer.Ordinal);
    private static long _showCounter;

    /// <summary>Panels currently on display, most recently shown last.</summary>
    private static readonly List<string> Open = new();

    /// <summary>Records a panel opening, so we can tell which of several is in front.</summary>
    public static void NoteShown(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId)) return;
        ShowOrder[panelId] = ++_showCounter;

        Open.Remove(panelId);
        Open.Add(panelId);
    }

    /// <summary>Records a panel closing.</summary>
    public static void NoteHidden(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId)) return;
        Open.Remove(panelId);
    }

    /// <summary>
    /// The panel in front, tracked from the game's own show and hide notifications rather
    /// than recomputed, so it is accurate every frame without costing anything.
    /// </summary>
    public static string FrontPanelId => Open.Count == 0 ? null : Open[^1];

    /// <summary>The id of the panel the player is currently working in, or null.</summary>
    public static string TopPanelId { get; private set; }

    /// <summary>
    /// Narrows a list of on-screen controls to the panel in front.
    ///
    /// Controls belonging to no panel — a persistent HUD, an overlay — always survive,
    /// since they are not part of any screen and stay usable regardless of what is open.
    /// If everything on screen belongs to one panel, nothing is dropped.
    /// </summary>
    /// <summary>
    /// Panels whose own controls stay reachable while one of their pages is in front.
    ///
    /// The almanac's way out — back to the index, and out to the menu — lives on the
    /// almanac panel itself, while the page you are reading is a separate panel shown on
    /// top of it. Scoping strictly to the top panel therefore left a player inside a page
    /// with no control that leads anywhere, which is a trap rather than an inconvenience
    /// for someone who cannot see a mouse pointer.
    ///
    /// Deliberately a short, explicit table rather than a rule. Widening the scope by
    /// guesswork is how this mod once put every level tile of an unopened carousel into
    /// the control list.
    /// </summary>
    private static readonly Dictionary<string, string> ParentPanels = new(StringComparer.Ordinal)
    {
        ["almanacPlants"] = "almanac",
        ["almanacZombies"] = "almanac",
        ["almanacArchive"] = "almanac",
    };

    private static List<Selectable> ScopeToTopPanel(List<Selectable> controls)
    {
        if (controls.Count == 0) return controls;

        var byPanel = new Dictionary<string, List<Selectable>>(StringComparer.Ordinal);
        var unowned = new List<Selectable>();

        foreach (Selectable s in controls)
        {
            string id = PanelIdOf(s);
            if (string.IsNullOrEmpty(id)) { unowned.Add(s); continue; }

            if (!byPanel.TryGetValue(id, out List<Selectable> group))
                byPanel[id] = group = new List<Selectable>();
            group.Add(s);
        }

        if (byPanel.Count == 0) { TopPanelId = null; return controls; }
        if (byPanel.Count == 1)
        {
            foreach (string only in byPanel.Keys) TopPanelId = only;
            return controls;
        }

        string topId = null;
        long topOrder = long.MinValue;
        foreach (KeyValuePair<string, List<Selectable>> pair in byPanel)
        {
            long order = ShowOrder.TryGetValue(pair.Key, out long value) ? value : 0L;
            if (order > topOrder) { topOrder = order; topId = pair.Key; }
        }

        if (topId == null) { TopPanelId = null; return controls; }

        TopPanelId = topId;

        var result = new List<Selectable>(byPanel[topId].Count + unowned.Count);
        result.AddRange(byPanel[topId]);

        // The page's own controls first, then the frame around it, so walking a grid does
        // not start on a button that leaves the screen.
        if (ParentPanels.TryGetValue(topId, out string parentId)
            && byPanel.TryGetValue(parentId, out List<Selectable> parent))
        {
            result.AddRange(parent);
        }

        result.AddRange(unowned);
        return result;
    }

    /// <summary>Full explanation for one control. Used by the dump, not on the hot path.</summary>
    public static Reach Evaluate(Selectable selectable)
    {
        if (selectable == null) return new Reach(false, null, null, "control is null");

        PanelView panel = NearestPanel(selectable);
        string panelId = SafeId(panel);
        string panelState = panel == null ? null : SafeState(panel).ToString();

        if (!UiText.IsVisible(selectable))
            return new Reach(false, panelId, panelState, "inactive");

        if (!UiText.IsInteractable(selectable))
            return new Reach(false, panelId, panelState, "not interactable");

        if (!PanelAllows(selectable))
            return new Reach(false, panelId, panelState, $"panel is {panelState}");

        if (!TryScreenRect(selectable, out Rect rect))
            return new Reach(false, panelId, panelState, "off screen or zero size");

        if (Config.Settings.UseHitTest.Value && !PassesHitTest(selectable, rect))
            return new Reach(false, panelId, panelState, "covered by something else");

        return new Reach(true, panelId, panelState, "reachable");
    }

    public static bool IsReachable(Selectable selectable) => Evaluate(selectable).Reachable;

    /// <summary>
    /// Whether the control's own panel is on display, and nothing on the way up has been
    /// faded out or had its canvas switched off.
    /// </summary>
    private static bool PanelAllows(Selectable selectable)
    {
        try
        {
            Transform t = selectable.transform;
            PanelView panel = null;

            for (int depth = 0; t != null && depth < MaxDepth; depth++, t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group != null && group.alpha <= 0.01f) return false;

                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return false;

                if (panel == null)
                {
                    var found = t.GetComponent<PanelView>();
                    if (found != null) panel = found;
                }
            }

            // Controls outside any panel — persistent HUD, overlays — are left alone.
            if (panel == null) return true;

            return SafeState(panel) is PresentableState.Shown or PresentableState.Showing;
        }
        catch
        {
            // Better to let a control through than to silence it over a hierarchy quirk.
            return true;
        }
    }

    /// <summary>
    /// The control's rectangle in screen pixels, if it lands on screen at a usable size.
    ///
    /// This is what catches the main menu's hidden sections: they stay active and fully
    /// opaque, but sit outside the viewport or are scaled down to nothing.
    /// </summary>
    /// <summary>
    /// Where a control's children land, for controls that have no size of their own.
    ///
    /// Only ever widens: it is consulted after the control's own rectangle has already been
    /// judged too small to be real. Anything still empty afterwards is genuinely not there.
    /// </summary>
    private static bool TryChildrenRect(RectTransform parent, Camera camera, out Rect rect)
    {
        rect = default;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;

        try
        {
            var children = parent.GetComponentsInChildren<RectTransform>(false);
            if (children == null) return false;

            var corners = new Il2CppStructArray<Vector3>(4);

            for (int c = 0; c < children.Length; c++)
            {
                RectTransform child = children[c];
                if (child == null) continue;

                child.GetWorldCorners(corners);

                for (int i = 0; i < 4; i++)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                    any = true;
                }
            }
        }
        catch { return false; }

        if (!any) return false;

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    public static bool TryScreenRect(Selectable selectable, out Rect rect)
    {
        rect = default;

        try
        {
            var rt = selectable.transform.TryCast<RectTransform>();
            if (rt == null) return true; // not a UI rect; not ours to judge

            var corners = new Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(corners);

            Camera camera = CameraFor(selectable);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);

            // A control whose own rectangle is nothing may still be perfectly visible: the
            // button is an empty container and the thing you see is a child of it. The
            // almanac's close button is exactly that, and rejecting it left a blind player
            // inside a screen with no way out of it.
            if (rect.width < MinScreenSize || rect.height < MinScreenSize)
            {
                if (!TryChildrenRect(rt, camera, out rect)) return false;
                if (rect.width < MinScreenSize || rect.height < MinScreenSize) return false;
            }

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            return rect.Overlaps(screen);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Whether a click at the control's centre would actually land on it.
    ///
    /// This is what separates a control that is merely on screen from one the player can
    /// use: anything sitting underneath an open sub-screen fails here.
    /// </summary>
    private static bool PassesHitTest(Selectable selectable, Rect rect)
    {
        try
        {
            EventSystem es = EventSystem.current;
            if (es == null) return true;

            var data = new PointerEventData(es) { position = ClampToScreen(rect.center) };
            var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(data, results);

            if (results.Count == 0) return false;

            GameObject top = results[0].gameObject;
            return top != null && IsSelfOrDescendant(selectable.transform, top.transform);
        }
        catch
        {
            return true;
        }
    }

    private static Vector2 ClampToScreen(Vector2 p)
        => new(Mathf.Clamp(p.x, 0f, Screen.width - 1f), Mathf.Clamp(p.y, 0f, Screen.height - 1f));

    private static bool IsSelfOrDescendant(Transform root, Transform candidate)
    {
        int rootId = root.GetInstanceID();
        Transform t = candidate;
        for (int depth = 0; t != null && depth < MaxDepth; depth++, t = t.parent)
            if (t.GetInstanceID() == rootId) return true;
        return false;
    }

    private static Camera CameraFor(Selectable selectable)
    {
        try
        {
            var canvas = selectable.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            Canvas root = canvas.rootCanvas ?? canvas;
            // An overlay canvas draws straight in screen space, so world corners are
            // already screen coordinates and passing a camera would skew them.
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
        catch { return null; }
    }

    private static PanelView NearestPanel(Selectable selectable)
    {
        try { return selectable.GetComponentInParent<PanelView>(); }
        catch { return null; }
    }

    /// <summary>The id of the panel a control belongs to, or null when it belongs to none.</summary>
    public static string PanelIdOf(Selectable selectable)
        => selectable == null ? null : SafeId(NearestPanel(selectable));

    public static string PanelIdOf(GameObject go)
    {
        if (go == null) return null;
        try { return SafeId(go.GetComponentInParent<PanelView>()); }
        catch { return null; }
    }

    /// <summary>Every panel currently on display. Used to name the screen when nothing has focus.</summary>
    public static List<PanelView> ShownPanels()
    {
        var result = new List<PanelView>();
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<PanelView>();
            if (all == null) return result;

            for (int i = 0; i < all.Length; i++)
            {
                PanelView p = all[i];
                if (p == null) continue;
                if (SafeState(p) is PresentableState.Shown or PresentableState.Showing)
                    result.Add(p);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not enumerate panels: {ex.Message}");
        }
        return result;
    }

    /// <summary>Longest body text we will read out in one go.</summary>
    private const int MaxBodyLength = 400;

    /// <summary>
    /// The prose inside a panel: what a dialogue box actually says, as opposed to what its
    /// buttons are labelled.
    ///
    /// Text belonging to a control is skipped, because focus announces those already and
    /// hearing "NEXT LEVEL" twice in a row is worse than not hearing it at all. What is
    /// left is the part with no other way in — Crazy Dave's lines, the explanation in a
    /// confirmation box, the note on an award screen.
    /// </summary>
    /// <summary>
    /// Panels whose loose text is furniture rather than prose: the score, the speed
    /// multiplier, the greeting on the main menu. Reading it aloud is pure chatter.
    /// Getting this list wrong only costs noise, never access.
    /// </summary>
    private static readonly HashSet<string> NoBodyPanels = new(StringComparer.Ordinal)
    {
        "gameplay",
        "acceleration",
        "mainMenu",
        "splash1",
        // The banner over the lawn is announced by the message hook, which fires once with
        // the right timing. Reading the panel as well said everything twice: once as
        // "Message Widget. Your house" and once as "Your house".
        "messageWidget",

        // The almanac pages carry a whole encyclopaedia entry as loose text. Read on every
        // screen change it would arrive unasked, at whatever length the selected entry
        // happens to be. It is on a question key instead. The index panel is deliberately
        // not here: its loose text is one short heading.
        "almanacPlants",
        "almanacZombies",
        "almanacArchive",
    };

    /// <summary>Every panel currently on display, comma separated. For the log.</summary>
    public static string ShownPanelIds()
    {
        var ids = new List<string>();
        try
        {
            foreach (PanelView p in ShownPanels())
            {
                string id = SafeId(p);
                if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
            }
        }
        catch (Exception ex)
        {
            return "unreadable: " + ex.Message;
        }
        return string.Join(", ", ids);
    }

    public static string BodyTextOf(string panelId, bool ignoreSuppression = false)
    {
        if (string.IsNullOrEmpty(panelId)) return null;
        if (!ignoreSuppression && NoBodyPanels.Contains(panelId)) return null;

        try
        {
            PanelView panel = null;
            foreach (PanelView candidate in ShownPanels())
                if (SafeId(candidate) == panelId) { panel = candidate; break; }

            if (panel == null) return null;

            // TMP_Text rather than TextMeshProUGUI: the latter is only the canvas flavour,
            // and a screen that mixes in a world-space label would have it silently skipped.
            var texts = panel.GetComponentsInChildren<TMP_Text>(false);
            if (texts == null || texts.Length == 0) return null;

            var parts = new List<string>();
            int total = 0;

            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null) continue;
                if (BelongsToControl(text)) continue;

                string clean = UiText.Collapse(text.text);
                if (string.IsNullOrWhiteSpace(clean)) continue;
                if (parts.Contains(clean)) continue;

                parts.Add(clean);
                total += clean.Length;
                if (total >= MaxBodyLength) break;
            }

            return parts.Count == 0 ? null : string.Join(". ", parts);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the text in panel \"{panelId}\": {ex.Message}");
            return null;
        }
    }

    /// <summary>True when this text is a control's own label rather than body prose.</summary>
    private static bool BelongsToControl(Component text)
    {
        try { return text.GetComponentInParent<Selectable>() != null; }
        catch { return false; }
    }

    public static string SafeId(PanelView panel)
    {
        if (panel == null) return null;
        try { return panel.Id; }
        catch { return null; }
    }

    public static PresentableState SafeState(PanelView panel)
    {
        if (panel == null) return PresentableState.Hidden;
        try { return panel.State; }
        catch { return PresentableState.Hidden; }
    }
}
