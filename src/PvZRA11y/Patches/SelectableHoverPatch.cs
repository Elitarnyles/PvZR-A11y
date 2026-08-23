using HarmonyLib;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PvZRA11y.Patches;

/// <summary>
/// Announces controls the mouse pointer passes over.
///
/// Keyboard focus is handled by polling in <see cref="Focus"/>, which catches every
/// focus change regardless of who caused it. Hover is different: it never touches
/// EventSystem selection, so polling would miss it entirely and it needs its own hook.
///
/// Off by default. It is useful for a sighted person testing alongside, and for anyone
/// who navigates with a mouse, but for keyboard play it just adds chatter.
/// </summary>
[HarmonyPatch(typeof(Selectable))]
internal static class SelectableHoverPatch
{
    [HarmonyPatch(nameof(Selectable.OnPointerEnter))]
    [HarmonyPostfix]
    private static void OnPointerEnter_Postfix(Selectable __instance, PointerEventData eventData)
    {
        if (!Settings.SpeakOnHover.Value) return;
        if (__instance == null) return;

        try
        {
            if (!UiText.IsVisible(__instance)) return;

            string text = UiText.Describe(__instance);
            if (string.IsNullOrEmpty(text)) return;

            Speech.Say(text, interrupt: true, context: "pointer hover");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Hover announcement failed: {ex.Message}");
        }
    }
}
