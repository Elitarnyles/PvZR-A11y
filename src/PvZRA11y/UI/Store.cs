using System.Globalization;
using Il2CppTekly.DataModels.Binders;
using Il2CppTekly.DataModels.Models;
using Il2CppTekly.PanelViews;
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

    #region Crazy Dave in the shop

    private static string _lastSaying;

    /// <summary>
    /// True while Crazy Dave is talking on the shop screen.
    ///
    /// His words appear inside the shop panel itself, not in the speech bubble the rest of
    /// the game uses, so nothing that watches for that bubble sees this at all. Before the
    /// taco mini-game it left the player on a screen whose three buttons the game had
    /// switched off, with no way to move the conversation on and nothing saying why.
    /// </summary>
    public static bool DaveTalking()
    {
        // From the shop's own model rather than by a key into the root. The key was a guess
        // taken from the prefabs and it answered nothing at all: the log showed the mod
        // deciding Dave was silent while he was plainly mid-sentence on screen.
        var model = Model();
        if (model != null)
        {
            try { return IsTrue(model.m_isDaveTalking?.OnToDisplayString()); }
            catch { /* fall through to the text on screen */ }
        }

        // Failing that, he is talking if there is something in the label that holds his
        // words. Less precise, and it cannot be wrong about whether words are on screen.
        return !string.IsNullOrWhiteSpace(SayingFromScreen());
    }

    /// <summary>What he is saying right now, or null.</summary>
    public static string DaveSaying()
    {
        var model = Model();
        if (model != null)
        {
            try
            {
                string text = model.m_daveSaying?.OnToDisplayString();
                if (!string.IsNullOrWhiteSpace(text)) return UiText.Collapse(text);
            }
            catch { }
        }

        return SayingFromScreen();
    }

    /// <summary>Dave's line as it is drawn, for when his model cannot be reached.</summary>
    private static string SayingFromScreen()
    {
        try
        {
            foreach (PanelView panel in PanelScope.ShownPanels())
            {
                if (PanelScope.SafeId(panel) != PanelId) continue;

                var texts = panel.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(false);
                if (texts == null) continue;

                for (int i = 0; i < texts.Length; i++)
                {
                    Il2CppTMPro.TMP_Text text = texts[i];
                    if (text == null) continue;

                    string name;
                    try { name = text.gameObject.name; } catch { continue; }
                    if (!name.Contains("Dave", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("Bubble", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("Saying", StringComparison.OrdinalIgnoreCase)) continue;

                    string raw;
                    try { raw = text.text; } catch { continue; }
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    return UiText.Collapse(raw);
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[store] could not read what Dave is saying: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Leaves the shop the way its own back button does.
    ///
    /// Needed because the visible button is switched off in the shop that opens before a
    /// mini-game: it is on screen, it is in the right place, and the game will not let it
    /// be pressed, so there was no way out at all. The button behind it is a model, and
    /// the model can be asked whether it is willing before it is used — this never presses
    /// something the game is holding shut.
    /// </summary>
    public static bool Leave()
    {
        var model = Model();
        if (model == null) return false;

        ButtonModel back;
        try { back = model.m_backButtonModel; }
        catch (Exception ex)
        {
            Core.Log.Warning($"[store] the back button could not be reached: {ex.Message}");
            return false;
        }

        if (back == null) return false;

        bool willing;
        try { willing = back.IsInteractable; }
        catch { willing = false; }

        if (!willing)
        {
            Core.Log.Msg("[store] the back button is not accepting presses");
            Speech.SayVerbatim(Strings.T("store.cannot_leave"), "store back");
            return true;
        }

        try
        {
            back.Activate(0);
            Core.Log.Msg("[store] left through the back button");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[store] could not leave: {ex.Message}");
            return false;
        }
    }

    /// <summary>Everything about the shop's conversation, for the dump.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- shop ---");

        var model = Model();
        sb.AppendLine($"  model reached  : {(model != null)}");

        if (model != null)
        {
            string talking, saying, index;
            try { talking = model.m_isDaveTalking?.OnToDisplayString() ?? "<null>"; }
            catch (Exception ex) { talking = "<threw: " + ex.Message + ">"; }
            try { saying = model.m_daveSaying?.OnToDisplayString() ?? "<null>"; }
            catch (Exception ex) { saying = "<threw: " + ex.Message + ">"; }
            try { index = model.m_talkingAboutIndex?.OnToDisplayString() ?? "<null>"; }
            catch (Exception ex) { index = "<threw: " + ex.Message + ">"; }

            sb.AppendLine($"  isDaveTalking  : {talking}");
            sb.AppendLine($"  daveSaying     : {saying}");
            sb.AppendLine($"  talkingAbout   : {index}");
        }

        foreach (string key in new[] { "store", "store.isDaveTalking", "store.daveSaying" })
            sb.AppendLine($"  root \"{key}\" : {ModelText.FromRoot(key) ?? "<null>"}");

        sb.AppendLine($"  from the screen: {SayingFromScreen() ?? "<null>"}");
        sb.AppendLine($"  talking?       : {DaveTalking()}");

        if (model != null)
        {
            string buttons, back, label;
            try { buttons = model.EnableButtons.ToString(); } catch (Exception ex) { buttons = "<threw: " + ex.Message + ">"; }
            try { back = model.m_backButtonModel == null ? "<null>" : model.m_backButtonModel.IsInteractable.ToString(); }
            catch (Exception ex) { back = "<threw: " + ex.Message + ">"; }
            try { label = model.m_backLabelModel?.OnToDisplayString() ?? "<null>"; }
            catch (Exception ex) { label = "<threw: " + ex.Message + ">"; }

            sb.AppendLine($"  EnableButtons  : {buttons}");
            sb.AppendLine($"  back accepts   : {back}");
            sb.AppendLine($"  back label     : {label}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Moves his dialogue on, the way clicking the shop does.
    ///
    /// Through the shop's own model rather than through the gameplay activity: the shop is
    /// opened from the menu and there is no board behind it, so the route that works during
    /// a level is not there.
    /// </summary>
    public static bool AdvanceDave()
    {
        Il2CppReloaded.DataModels.StoreModel model = Model();
        if (model == null) return false;

        try
        {
            model.AdvanceCrazyDaveDialog();
            Core.Log.Msg("[store] advanced Crazy Dave");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[store] could not advance Crazy Dave: {ex.Message}");
            return false;
        }
    }

    private static Il2CppReloaded.DataModels.StoreModel Model()
    {
        try
        {
            RootModel root = RootModel.Instance;
            if (root == null) return null;

            IModel model = null;
            if (!root.TryGetModel("store", out model) || model == null) return null;

            return model.TryCast<Il2CppReloaded.DataModels.StoreModel>();
        }
        catch (Exception ex)
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.Msg($"[store] the shop's model could not be reached: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads out each new line he says. Once per frame from Core.OnUpdate.</summary>
    public static void Tick()
    {
        if (!IsActive) { _lastSaying = null; return; }

        string saying = DaveSaying();
        if (string.IsNullOrWhiteSpace(saying)) return;
        if (saying == _lastSaying) return;

        _lastSaying = saying;
        Speech.Say(saying, interrupt: true, context: "store dave");
    }

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

        // 3. The number on screen. Last because it is the least direct, first in reliability:
        // it is by definition what a sighted player is looking at, and it does not care
        // which activity happens to be alive.
        int? shown = FromCoinLabel();
        if (shown != null) return shown;

        Core.Log.Warning("[store] nothing would say how many coins the player has");
        return null;
    }

    /// <summary>The game's own coin counter, read off the screen.</summary>
    private static int? FromCoinLabel()
    {
        try
        {
            var texts = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TMP_Text>();
            if (texts == null) return null;

            for (int i = 0; i < texts.Length; i++)
            {
                Il2CppTMPro.TMP_Text text = texts[i];
                if (text == null) continue;

                string name;
                try { name = text.gameObject.name; } catch { continue; }
                if (!name.Contains("CoinBank", StringComparison.OrdinalIgnoreCase)) continue;

                string raw;
                try { raw = text.text; } catch { continue; }
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string digits = Digits(raw);
                if (digits.Length == 0) continue;

                if (int.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out int coins))
                {
                    if (Settings.VerboseLogging.Value)
                        Core.Log.Msg($"[store] coins from the label \"{name}\": \"{raw}\" -> {coins}");
                    return coins;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[store] could not read the coin counter: {ex.Message}");
        }

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
