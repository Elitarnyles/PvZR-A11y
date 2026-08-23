using HarmonyLib;
using Il2CppTekly.Common.Presentables;
using Il2CppTekly.PanelViews;
using PvZRA11y.UI;

namespace PvZRA11y.Patches;

/// <summary>
/// Notices screens opening.
///
/// Every screen in the game is a Tekly PanelView and they all route through
/// PanelView.Show, so one patch on the base class covers the main menu, level select,
/// the options screens and the confirmation popups alike.
///
/// This deliberately does not announce anything. The game opens several panels in one
/// go and re-shows the same panel repeatedly, so announcing here produced a stream of
/// wrong screen names. Naming the screen is <see cref="ScreenTracker"/>'s job, and it
/// works it out from what actually has focus. All that is needed here is the nudge to
/// put focus somewhere on the new screen.
/// </summary>
[HarmonyPatch(typeof(PanelView))]
internal static class PanelViewPatch
{
    [HarmonyPatch(nameof(PanelView.Show))]
    [HarmonyPostfix]
    private static void Show_Postfix(PanelView __instance)
    {
        if (__instance == null) return;

        string id;
        try { id = __instance.Id; }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read panel id: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(id)) return;

        if (Config.Settings.VerboseLogging.Value)
            Core.Log.Msg($"[panel] shown: {id}");

        // Records which panel is in front, so a dialog opening over a screen takes
        // precedence over what it covers.
        PanelScope.NoteShown(id);
        ScreenTracker.NotePanelShown(id);

        // The new screen may arrive with nothing selected, which leaves the keyboard
        // with no target. Focus takes it from here once the panel has finished building.
        Focus.RequestInitialFocus();
    }

    /// <summary>
    /// Panels report every state change through here, which is the only way to learn that
    /// one has closed. Knowing that matters as much as knowing one opened: it is how the
    /// lawn gets its keys back after a pause dialog goes away.
    /// </summary>
    [HarmonyPatch(nameof(PanelView.OnStateChanged))]
    [HarmonyPostfix]
    private static void OnStateChanged_Postfix(PanelView __instance)
    {
        if (__instance == null) return;

        try
        {
            string id = __instance.Id;
            if (string.IsNullOrWhiteSpace(id)) return;

            PresentableState state = __instance.State;
            if (state is PresentableState.Shown or PresentableState.Showing)
                PanelScope.NoteShown(id);
            else
                PanelScope.NoteHidden(id);

            if (Config.Settings.VerboseLogging.Value)
                Core.Log.Msg($"[panel] {id} -> {state}");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not track a panel state change: {ex.Message}");
        }
    }
}
