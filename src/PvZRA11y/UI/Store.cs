using System.Globalization;
using Il2CppTekly.DataModels.Binders;
using Il2CppTekly.DataModels.Models;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Gameplay;
using PvZRA11y.Localization;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Crazy Dave's shop.
///
/// The screen itself needed nothing: it is built from ordinary controls, so walking it and
/// pressing things worked the day it was unlocked. What was missing was the half of each
/// item that matters. A tile read as "$20000, button, 1 of 10" — the price, and no idea
/// what it was the price of.
///
/// The name is where the almanac keeps its own: in the data model behind the tile, under
/// the same "*.name" key. So is the cost, and so is whether the thing is sold out or not
/// yet available, which is worth knowing before spending twenty thousand coins finding out.
/// </summary>
public static class Store
{
    public const string PanelId = "store";

    /// <summary>True while the shop is the screen in front.</summary>
    public static bool IsActive
        => PanelScope.FrontPanelId == PanelId || ScreenTracker.CurrentId == PanelId;

    #region Naming an item

    /// <summary>
    /// What to call a shop tile, or null when this control is not one.
    ///
    /// Called from UiText.GetLabel on every focus change, so it must be cheap and must never
    /// throw.
    /// </summary>
    public static string LabelFor(Selectable selectable)
    {
        if (selectable == null || !IsActive) return null;

        try { if (PanelScope.PanelIdOf(selectable) != PanelId) return null; }
        catch { return null; }

        BinderContainer container = ModelText.ContainerOn(selectable);
        if (container == null) return null;

        string rawName = ModelText.Value(container, "*.name");
        if (string.IsNullOrWhiteSpace(rawName)) return null;

        string name = ModelText.Resolve(rawName);
        if (string.IsNullOrEmpty(name)) return null;

        var parts = new List<string>(3) { name };

        string cost = ModelText.Value(container, "*.coinCost");
        if (!string.IsNullOrWhiteSpace(cost)) parts.Add(Money(cost));

        // Said last, because it is the thing that stops you rather than describes the item.
        string state = StateOf(container);
        if (!string.IsNullOrEmpty(state)) parts.Add(state);

        return string.Join(", ", parts);
    }

    private static string StateOf(BinderContainer container)
    {
        if (IsTrue(ModelText.Value(container, "*.isSoldOut"))) return Strings.T("store.sold_out");
        if (IsTrue(ModelText.Value(container, "*.isComingSoon"))) return Strings.T("store.coming_soon");
        if (IsTrue(ModelText.Value(container, "*.isUnavailable"))) return Strings.T("store.unavailable");
        return null;
    }

    private static bool IsTrue(string value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");

    #endregion

    #region The purse

    /// <summary>
    /// Says how many coins the player has.
    ///
    /// On the first question key while the shop is open. Everywhere else that key repeats
    /// the last thing said, which in a shop is far less useful than knowing whether you can
    /// afford what you are standing on.
    /// </summary>
    public static void AnnounceCoins()
    {
        int? coins = CoinCount();

        Speech.SayVerbatim(coins == null
            ? Strings.T("store.coins_unknown")
            : Strings.T("store.coins", Money(coins.Value)), "store coins");
    }

    /// <summary>
    /// The player's coins, or null when nothing will say.
    ///
    /// Three routes, tried in order and logged, because the shop is reached from the menu
    /// where the gameplay activity may not exist and this mod has been caught before
    /// assuming one path is the path.
    /// </summary>
    public static int? CoinCount()
    {
        // 1. The service that owns the number.
        try
        {
            var user = Lawn.UserServiceRef();
            if (user != null)
            {
                int coins = user.GetCoins();
                if (Settings.VerboseLogging.Value) Core.Log.Msg($"[store] coins from the user service: {coins}");
                return coins;
            }
        }
        catch (Exception ex)
        {
            if (Settings.VerboseLogging.Value) Core.Log.Msg($"[store] the user service would not say: {ex.Message}");
        }

        // 2. The data model the shop's own coin display is bound to.
        foreach (string key in new[] { "user.coins", "store.coins", "coins", "users.coins" })
        {
            string value = ModelText.FromRoot(key);
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (int.TryParse(Digits(value), NumberStyles.Any, CultureInfo.InvariantCulture, out int coins))
            {
                if (Settings.VerboseLogging.Value) Core.Log.Msg($"[store] coins from \"{key}\": {coins}");
                return coins;
            }
        }

        Core.Log.Warning("[store] nothing would say how many coins the player has");
        return null;
    }

    /// <summary>Strips everything that is not a digit, so "$20,000" becomes 20000.</summary>
    private static string Digits(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// A money figure, grouped so it is read as one number rather than a run of digits.
    ///
    /// The separator lives in the translation, not here, so it can be changed to whatever a
    /// given language and screen reader read best.
    /// </summary>
    private static string Money(string raw)
    {
        string digits = Digits(raw);
        return int.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out int value)
            ? Money(value)
            : raw.Trim();
    }

    private static string Money(int value)
    {
        string separator = Strings.T("store.thousands");
        string digits = value.ToString(CultureInfo.InvariantCulture);

        var sb = new System.Text.StringBuilder(digits.Length + 4);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(separator);
            sb.Append(digits[i]);
        }

        return sb.ToString();
    }

    #endregion
}
