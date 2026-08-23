using HarmonyLib;
using PvZRA11y.Gameplay;

namespace PvZRA11y.Patches;

/// <summary>
/// Catches which note the game is about to show.
///
/// The note on the award screen is a picture, so there is nothing on screen to read and
/// nothing to poll for. This one method is handed the note's number as the screen is built,
/// which makes it the only place the answer exists before the picture appears.
/// </summary>
[HarmonyPatch(typeof(Il2Cpp.ZombieNoteBinder))]
internal static class ZombieNotePatch
{
    [HarmonyPatch(nameof(Il2Cpp.ZombieNoteBinder.BindNoteNumber))]
    [HarmonyPostfix]
    private static void BindNoteNumber_Postfix(double noteNumber)
    {
        try { Notes.NoteBound((int)noteNumber); }
        catch (Exception ex) { Core.Log.Warning($"[note] could not record the note number: {ex.Message}"); }
    }
}
