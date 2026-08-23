using HarmonyLib;
using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.UI;

namespace PvZRA11y.Patches;

/// <summary>
/// Reads out the text the game paints over the lawn.
///
/// These are the lines a sighted player takes in without noticing they are reading:
/// "a huge wave of zombies is approaching", "final wave", the advice that appears when
/// something is wrong, and everything Crazy Dave says. None of it is a UI control, so
/// nothing else in this mod would ever see it — and the wave warnings in particular are
/// the difference between preparing for a wave and being surprised by one.
/// </summary>
internal static class MessagePatch
{
    /// <summary>In-game banners and advice.</summary>
    [HarmonyPatch(typeof(MessageWidget))]
    internal static class Widget
    {
        // Bound by position, not by name: parameter names come from the interop generator
        // and a guess that does not match makes Harmony refuse the patch outright.
        [HarmonyPatch(nameof(MessageWidget.SetLabel))]
        [HarmonyPostfix]
        private static void SetLabel_Postfix(string __0)
        {
            Announce(__0, "game message");
        }
    }

    // SpeechBubble.set_Text is deliberately not hooked. Patching it threw inside the
    // interop layer every time the game set the text:
    //   ArgumentOutOfRangeException at Il2CppStringToManaged, in set_Text
    // The string never survived the trip from native code, so the callback died and
    // nothing was ever spoken. Dialogue is read from the panel's own text instead — see
    // PanelScope.BodyTextOf — which needs no patch and covers every window with text in
    // it rather than only speech bubbles.

    /// <summary>Frames to wait for the game to fill in a message it has only named so far.</summary>
    private const int PanelReadDelayFrames = 8;

    private static int _pendingFrames;

    /// <summary>
    /// Picks up a message whose text was not ready when the game announced it. Called once
    /// per frame from Core.OnUpdate.
    /// </summary>
    public static void Tick()
    {
        if (_pendingFrames <= 0) return;
        if (--_pendingFrames > 0) return;

        string text = UI.PanelScope.BodyTextOf("messageWidget", ignoreSuppression: true);
        if (string.IsNullOrWhiteSpace(text)) return;

        Core.Log.Msg($"[message] resolved: {text}");
        Speech.Say(text, interrupt: false, context: "game message");
    }

    /// <summary>
    /// Speaks a line of game text, if there is anything in it worth speaking.
    ///
    /// Queued rather than interrupting: these arrive alongside other events and cutting
    /// those off would trade one missed thing for another. The speech layer's repeat guard
    /// handles the game setting the same text several times over, which it does.
    /// </summary>
    private static void Announce(string text, string context)
    {
        if (!Settings.SayGameMessages.Value) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        // The game marks these up for its own renderer; strip that back to words.
        string clean = UiText.Collapse(text);
        if (string.IsNullOrWhiteSpace(clean)) return;

        // Advice arrives here as an unresolved key - "[ADVICE_CLICK_SHOVEL]" - because the
        // lookup happens further down the line. The finished sentence appears in the panel a
        // moment later, so rather than drop the message we come back for it.
        if (clean.Length > 1 && clean[0] == '[' && clean[^1] == ']')
        {
            string resolved = A11y.GameText.ResolveOrKeep(clean);
            if (resolved != clean)
            {
                Core.Log.Msg($"[message] {clean} -> {resolved}");
                Speech.Say(UiText.Collapse(resolved), interrupt: false, context: context);
                return;
            }

            Core.Log.Msg($"[message] {clean} is unresolved; will read the panel instead");
            _pendingFrames = PanelReadDelayFrames;
            return;
        }

        Core.Log.Msg($"[message] {context}: {clean}");
        Speech.Say(clean, interrupt: false, context: context);
    }
}
