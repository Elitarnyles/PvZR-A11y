using System.Text;
using Il2CppTekly.PanelViews;
using PvZRA11y.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PvZRA11y.Diagnostics;

/// <summary>
/// Writes a complete picture of the current screen to the MelonLoader log.
///
/// This is the workflow tool for adding labels. When a control announces something
/// unhelpful, press the dump key and the log shows every control with its raw GameObject
/// name, its type, its owning panel, and exactly what the mod would say about it. The
/// "ui." line for each one can be pasted straight into a translation file.
///
/// Controls that were filtered out are listed too, with the reason. That is what makes
/// it possible to tell "the mod cannot see this control" apart from "the mod can see it
/// but reads it wrong" — two very different bugs.
///
/// The log lives in PVZ Replanted\MelonLoader\Logs\.
/// </summary>
public static class Probe
{
    /// <summary>Hidden controls listed before the rest are summarised as a count.</summary>
    private const int HiddenListLimit = 25;

    public static void DumpCurrentScreen()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("================ PvZRA11y screen dump ================");

        try
        {
            AppendContext(sb);
            if (Gameplay.SeedChooser.IsActive) Gameplay.SeedChooser.Dump(sb);
            if (UI.Almanac.IsActive) UI.Almanac.Dump(sb);
            if (UI.Store.IsActive) UI.Store.Dump(sb);
            AppendPanels(sb);
            AppendPanelText(sb);
            AppendReachable(sb);
            AppendFiltered(sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Dump failed partway through: {ex}");
        }

        sb.AppendLine("======================================================");
        Core.Log.Msg(sb.ToString());
    }

    private static void AppendContext(StringBuilder sb)
    {
        sb.AppendLine($"Screen id     : {Or(ScreenTracker.CurrentId, "<none identified>")}");
        sb.AppendLine($"Screen spoken : {Or(ScreenTracker.CurrentName, "<none>")}");

        EventSystem es = EventSystem.current;
        sb.AppendLine($"EventSystem   : {(es == null ? "<null>" : es.gameObject.name)}");

        GameObject sel = Focus.CurrentSelection();
        sb.AppendLine($"Focused       : {(sel == null ? "<nothing>" : UiText.PathOf(sel))}");
        sb.AppendLine();
    }

    /// <summary>Every panel in the scene and its state, so the scoping can be checked at a glance.</summary>
    private static void AppendPanels(StringBuilder sb)
    {
        sb.AppendLine("--- panels ---");
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<PanelView>();
            if (all == null || all.Length == 0)
            {
                sb.AppendLine("  (none found)");
            }
            else
            {
                for (int i = 0; i < all.Length; i++)
                {
                    PanelView p = all[i];
                    if (p == null) continue;
                    sb.AppendLine($"  {PanelScope.SafeState(p),-8} {Or(PanelScope.SafeId(p), "<no id>")}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  could not enumerate panels: {ex.Message}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Every piece of text on the front panel, including the ones switched off.
    ///
    /// Reading a screen only reports the text that is live and not part of a control, which
    /// is right for speech and useless for diagnosis: when something on screen is not being
    /// read, the question is whether the mod skipped it, whether it was inactive at the
    /// time, or whether there was never any text there at all and the thing is a picture.
    /// Those three want completely different fixes and sound identical from the outside.
    /// </summary>
    private static void AppendPanelText(StringBuilder sb)
    {
        string id = PanelScope.FrontPanelId;
        sb.AppendLine($"--- text on front panel \"{Or(id, "<none>")}\" ---");

        if (string.IsNullOrEmpty(id))
        {
            sb.AppendLine("  (no panel in front)");
            sb.AppendLine();
            return;
        }

        try
        {
            PanelView panel = null;
            var all = UnityEngine.Object.FindObjectsOfType<PanelView>();
            for (int i = 0; all != null && i < all.Length; i++)
                if (PanelScope.SafeId(all[i]) == id) { panel = all[i]; break; }

            if (panel == null)
            {
                sb.AppendLine("  (panel object not found)");
                sb.AppendLine();
                return;
            }

            // true: include the inactive ones. That is the whole point of this section.
            var texts = panel.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true);
            sb.AppendLine($"  TMP_Text components: {(texts == null ? 0 : texts.Length)}");

            for (int i = 0; texts != null && i < texts.Length; i++)
            {
                Il2CppTMPro.TMP_Text t = texts[i];
                if (t == null) continue;

                string live;
                try { live = t.gameObject.activeInHierarchy ? "on " : "OFF"; }
                catch { live = " ? "; }

                string body;
                try { body = t.text; } catch (Exception ex) { body = "<threw: " + ex.Message + ">"; }
                body = string.IsNullOrWhiteSpace(body) ? "<empty>" : UiText.Collapse(body);
                if (body.Length > 300) body = body.Substring(0, 300) + " ...";

                sb.AppendLine($"  [{live}] {UiText.PathOf(SafeGameObject(t))}");
                sb.AppendLine($"        \"{body}\"");
            }

            // Pictures matter too: a note drawn as a handwritten image has no text anywhere,
            // and that is a different problem with a different answer.
            var images = panel.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            int on = 0, off = 0;
            for (int i = 0; images != null && i < images.Length; i++)
            {
                try { if (images[i].gameObject.activeInHierarchy) on++; else off++; } catch { }
            }
            sb.AppendLine($"  Images: {on} active, {off} inactive");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  could not read the panel text: {ex.Message}");
        }

        sb.AppendLine();
    }

    private static GameObject SafeGameObject(UnityEngine.Component c)
    {
        try { return c?.gameObject; }
        catch { return null; }
    }

    private static void AppendReachable(StringBuilder sb)
    {
        Focus.InvalidateCache();
        var visible = Focus.CollectVisible();
        GameObject sel = Focus.CurrentSelection();

        var st = PanelScope.LastStats;
        sb.AppendLine("--- filter stages ---");
        sb.AppendLine($"  all Selectables      : {st.Total}");
        sb.AppendLine($"  active + interactable: {st.AfterBasics}");
        sb.AppendLine($"  panel on display     : {st.AfterPanel}");
        sb.AppendLine($"  within screen bounds : {st.AfterBounds}");
        sb.AppendLine($"  passes hit test      : {st.AfterHitTest}{(st.FellBack ? "   <-- rejected everything, ignored" : "")}");
        sb.AppendLine();

        sb.AppendLine($"--- reachable controls ({visible.Count}) ---");

        for (int i = 0; i < visible.Count; i++)
        {
            Selectable s = visible[i];
            string name = UiText.SafeName(s);
            bool focused = sel != null && SameObject(s, sel);

            sb.AppendLine($"[{i + 1,3}] {(focused ? "> " : "  ")}{name}");
            sb.AppendLine($"        type   : {UiText.SafeTypeName(s)}");
            sb.AppendLine($"        panel  : {Or(PanelScope.PanelIdOf(s), "none")}");
            sb.AppendLine($"        path   : {UiText.PathOf(SafeGameObject(s))}");
            sb.AppendLine($"        spoken : {UiText.Describe(s, i, visible.Count)}");
            sb.AppendLine($"        ui.{name} = {UiText.GetLabel(s)}");
        }
        sb.AppendLine();
    }

    /// <summary>Controls that exist but were excluded, and why.</summary>
    private static void AppendFiltered(StringBuilder sb)
    {
        var hidden = new List<string>();
        var near = new List<string>();
        string front = PanelScope.FrontPanelId;

        try
        {
            var all = Selectable.allSelectablesArray;
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    Selectable s = all[i];
                    if (s == null) continue;

                    string reason = null;
                    if (!UiText.IsVisible(s)) reason = "inactive";
                    else if (!UiText.IsInteractable(s)) reason = "not interactable";
                    else
                    {
                        var reach = PanelScope.Evaluate(s);
                        if (!reach.Reachable) reason = $"{reach.Reason} (panel {Or(reach.PanelId, "none")})";
                    }

                    if (reason == null) continue;

                    string panelId = PanelScope.PanelIdOf(s) ?? "";

                    // Controls belonging to the screen actually in front are listed in
                    // full and first, however long the list runs. Everything else is
                    // background - a menu still loaded behind an overlay contributes a
                    // hundred entries and buries the one that matters.
                    string line = $"  {UiText.SafeName(s)} — {reason}{RectOf(s)}";
                    if (panelId == front || PanelScope.FrontPanelId == panelId) near.Add(line);
                    else hidden.Add(line);
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"--- filtered out --- could not enumerate: {ex.Message}");
            return;
        }

        sb.AppendLine($"--- filtered out on \"{front ?? "<none>"}\" ({near.Count}) ---");
        if (near.Count == 0) sb.AppendLine("  (none - every control of this screen is reachable)");
        foreach (string line in near) sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine($"--- filtered out elsewhere ({hidden.Count}) ---");
        int shown = Math.Min(hidden.Count, HiddenListLimit);
        for (int i = 0; i < shown; i++) sb.AppendLine(hidden[i]);
        if (hidden.Count > shown) sb.AppendLine($"  ... and {hidden.Count - shown} more");
    }

    /// <summary>
    /// Where the bounds test thinks a control sits.
    ///
    /// Asks the test itself rather than measuring again. The first version of this measured
    /// separately, with a managed array where the interop one was needed, and reported every
    /// control as zero by zero — a diagnostic that agreed with the thing it was meant to
    /// check, which is the least useful kind of wrong.
    /// </summary>
    private static string RectOf(Selectable s)
    {
        try
        {
            bool ok = PanelScope.TryScreenRect(s, out Rect rect);
            return $"  [{rect.xMin:0},{rect.yMin:0} to {rect.xMax:0},{rect.yMax:0}" +
                   $"  {rect.width:0}x{rect.height:0}; screen {Screen.width}x{Screen.height}" +
                   $"; bounds {(ok ? "pass" : "fail")}]";
        }
        catch { return "  [rect unreadable]"; }
    }

    private static string Or(string value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;

    private static bool SameObject(Selectable s, GameObject go)
    {
        try { return s != null && go != null && s.gameObject.GetInstanceID() == go.GetInstanceID(); }
        catch { return false; }
    }

    private static GameObject SafeGameObject(Selectable s)
    {
        try { return s?.gameObject; }
        catch { return null; }
    }
}
