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
/// Reading the screen instead of its controls is not a preference. The chooser recycles
/// seven card objects to display forty-odd plants, so walking the controls reaches seven of
/// them and no amount of care with focus would find the rest.
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
