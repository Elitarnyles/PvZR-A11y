using HarmonyLib;
using Il2CppReloaded.Gameplay;
using PvZRA11y.Gameplay;

namespace PvZRA11y.Patches;

/// <summary>
/// Catches hold of the plant chooser while it is open.
///
/// Everything this mod does on that screen goes through the screen object itself rather
/// than through its controls, so it has to be reachable. Nothing hands it to us and there
/// is no path to it from the board, so it is taken as it goes past.
///
/// The screen is read rather than its controls because the screen's own list is the one the
/// game reasons about: costs, cooldowns, whether a plant suits the level. The cards are the
/// same plants drawn, and the mod keeps its position in step with them.
/// </summary>
[HarmonyPatch(typeof(SeedChooserScreen))]
internal static class SeedChooserPatch
{
    [HarmonyPatch(nameof(SeedChooserScreen.UpdateSeedChooserScreen))]
    [HarmonyPostfix]
    private static void UpdateSeedChooserScreen_Postfix(SeedChooserScreen __instance)
    {
        SeedChooser.NoteScreen(__instance);
    }

    [HarmonyPatch(nameof(SeedChooserScreen.SetFromSeedbank))]
    [HarmonyPostfix]
    private static void SetFromSeedbank_Postfix(SeedChooserScreen __instance)
    {
        SeedChooser.NoteScreen(__instance);
    }
}
