using HarmonyLib;
using Il2CppUI.Scripts;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using PvZRA11y.UI;

namespace PvZRA11y.Patches;

/// <summary>
/// Follows the level carousel.
///
/// The point of hooking the game's own selection callbacks rather than only announcing
/// what we ourselves did is that it works whatever moved the carousel — our cycle keys,
/// a mouse drag, the game restoring the last played level on entry. The player hears the
/// same thing either way, and we never have to guess which inputs the game handles.
/// </summary>
internal static class LevelSelectPatch
{
    [HarmonyPatch(typeof(LevelSelectScreen))]
    internal static class Screen
    {
        [HarmonyPatch(nameof(LevelSelectScreen.Start))]
        [HarmonyPostfix]
        private static void Start_Postfix(LevelSelectScreen __instance) => LevelSelect.NoteScreen(__instance);

        [HarmonyPatch(nameof(LevelSelectScreen.OnEnterLevelSelect))]
        [HarmonyPostfix]
        private static void OnEnter_Postfix(LevelSelectScreen __instance) => LevelSelect.NoteEntered(__instance);

        [HarmonyPatch(nameof(LevelSelectScreen.OnLeaveLevelSelect))]
        [HarmonyPostfix]
        private static void OnLeave_Postfix() => LevelSelect.NoteLeft();

        /// <summary>
        /// Fires whenever the carousel settles on a level. A mouse click raises it twice,
        /// which the speech layer's repeat guard absorbs.
        /// </summary>
        [HarmonyPatch(nameof(LevelSelectScreen.SetSelectedLevelIndex), typeof(int))]
        [HarmonyPostfix]
        private static void SetSelectedLevelIndex_Postfix(int level)
        {
            LevelSelect.NoteSelectedIndex(level);
            LevelSelect.AnnounceSelected();
        }

        [HarmonyPatch(nameof(LevelSelectScreen.SelectLevel), typeof(int), typeof(bool))]
        [HarmonyPostfix]
        private static void SelectLevel_Postfix(int level) => LevelSelect.NoteSelectedIndex(level);

        /// <summary>Changing world swaps the carousel underneath us.</summary>
        [HarmonyPatch(nameof(LevelSelectScreen.UpdateSelectedCarousel))]
        [HarmonyPostfix]
        private static void UpdateSelectedCarousel_Postfix() => LevelSelect.NoteCarouselChanged();
    }

    // LevelListItem.ShowPlayButton is deliberately not hooked. It looked like the game
    // saying "this level is chosen", but it fires for every tile as the carousel is built:
    // one press of Adventure produced seven announcements in a row. The carousel's own
    // selection callback above is the signal that actually means something.
}
