using HarmonyLib;
using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;

namespace PvZRA11y.Patches;

/// <summary>
/// Says what was just picked up.
///
/// Hooking the game's own collection rather than announcing what the auto-collector did
/// means it reports the truth in every case: items gathered automatically, sun clicked by
/// hand, a Gold Magnet doing its work. There is one place where things get collected, so
/// there is one place to listen.
///
/// Sun is deliberately silent. It arrives constantly, and the running total is a key press
/// away; narrating each piece would drown out everything that matters. Prizes are the
/// opposite — a seed packet dropped at the end of a level is easy to miss entirely and is
/// exactly the sort of thing a sighted player reads off the screen without thinking.
/// </summary>
[HarmonyPatch(typeof(Coin))]
internal static class CoinPatch
{
    [HarmonyPatch(nameof(Coin.Collect))]
    [HarmonyPostfix]
    private static void Collect_Postfix(Coin __instance)
    {
        if (__instance == null) return;

        try
        {
            CoinType type = __instance.mType;

            if (IsSun(type)) return;

            if (IsMoney(type))
            {
                if (!Settings.SayCoinPickups.Value) return;
                Speech.Say(Strings.T("pickup.collected", NameOf(type)), interrupt: false, context: "coin collected");
                return;
            }

            // A prize. Always worth saying.
            Speech.Say(Strings.T("pickup.collected", NameOf(type)), interrupt: false, context: "item collected");
            Core.Log.Msg($"[pickup] collected {type}");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not report a pickup: {ex.Message}");
        }
    }

    private static bool IsSun(CoinType type) => type
        is CoinType.Sun or CoinType.SmallSun or CoinType.LargeSun or CoinType.DoubleSun;

    private static bool IsMoney(CoinType type) => type
        is CoinType.Silver or CoinType.Gold or CoinType.Diamond;

    private static string NameOf(CoinType type)
    {
        string key = "pickup." + type;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(type.ToString());
    }
}
