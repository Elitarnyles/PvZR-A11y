using MelonLoader;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Input;
using PvZRA11y.Localization;
using PvZRA11y.UI;

[assembly: MelonInfo(typeof(PvZRA11y.Core), "PvZ Replanted Accessibility",
    PvZRA11y.Core.Version, "Elitarny Les", PvZRA11y.Core.Homepage)]
[assembly: MelonGame("PopCap Games", "PvZ Replanted")]

namespace PvZRA11y;

/// <summary>
/// Mod entry point.
///
/// Everything is driven from two places: MelonLoader's per-frame callback, which pumps
/// speech, reads our hotkeys and watches UI focus, and a handful of Harmony patches for
/// events that polling cannot see.
///
/// Menus came first and the lawn second, deliberately: there is no point being able to read
/// a lawn you cannot reach a level to stand on. Both are done. The almanac, the shop and the
/// Zen Garden are not.
/// </summary>
public class Core : MelonMod
{
    /// <summary>
    /// The mod's version, in one place.
    ///
    /// Const rather than a field, so the MelonInfo attribute above can use it too — an
    /// attribute argument has to be a compile-time constant. Keep the Version property in
    /// PvZRA11y.csproj in step with it; that one is MSBuild's and cannot read this.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>Shown to every user in the MelonLoader log, so it has to be a real address.</summary>
    public const string Homepage = "https://github.com/Elitarnyles/PvZR-A11y";

    /// <summary>Log to PVZ Replanted\MelonLoader\Logs. Available from OnInitializeMelon onward.</summary>
    public static MelonLogger.Instance Log { get; private set; }

    public static HarmonyLib.Harmony Patcher { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Patcher = HarmonyInstance;

        Settings.Load();
        Hotkeys.Rebind();
        Strings.Load(Settings.Language.Value);
        Speech.Initialize();

        // MelonLoader applies [HarmonyPatch] types in this assembly automatically;
        // calling PatchAll here would double-patch every one of them.

        Log.Msg($"Language: {Strings.LoadedLanguage}");
        // Written out in full because it is the only place a user who hears nothing can
        // still find out what the keys are.
        Log.Msg("Keys, menus: Enter activate, Tab next control, F1 repeat last, F2 where am I, F3 read screen.");
        Log.Msg("Keys, lawn: arrows move, Enter plant, Backspace shovel, F5 freeze, F6 read seed bank, " +
                "minus and equals cycle seeds, F1 scan row (twice: rows with zombies), F2 square detail, " +
                "F3 sun, F4 progress.");
        Log.Msg("Keys, almanac: arrows move between entries, Enter opens one, F4 reads it in full.");
        Log.Msg("Keys, Whack a Zombie: arrows move, Backspace swings the mallet.");
        Log.Msg("Keys, final level: F4 says what Dr Zomboss is about to throw and at which row, "
              + "and how much of him is left. Fireballs, iceballs, stomps and drops are announced "
              + "as he decides them, which is before they land.");
        Log.Msg("Keys, Zen Garden: arrows move between pots, minus and equals and the digits "
              + "choose a tool, Enter uses it on the pot you are standing on or moves to the "
              + "next garden, F1 how many plants want something (twice: what is planted), "
              + "F2 the full report of this pot, F3 coins, F4 Stinky, F6 reads the whole garden.");
        Log.Msg("Keys, Vase Breaker: arrows move, Enter takes the plant lying on your square or breaks the vase standing on it, F6 lists what is lying on the lawn, minus and equals step through it, and the digits carry on past the seed bank into it.");
        Log.Msg("Keys, shop: arrows and Tab move, Enter buys or moves Crazy Dave on, Backspace leaves, F1 says how many coins you have.");
        Log.Msg("Keys, anywhere: LeftCtrl silence, Ctrl+Alt+A toggle speech, F10 dump screen to log, F11 self-test.");
    }

    public override void OnLateInitializeMelon()
    {
        Speech.Say(Strings.T("msg.ready", Version), interrupt: false, context: "startup");

        if (Speech.Ready && Speech.DetectedReader == null)
            Speech.Say(Strings.T("msg.no_reader"), interrupt: false, context: "startup");
    }

    /// <summary>
    /// The per-frame work, in order.
    ///
    /// The board is checked first, so nothing below spends the frame reading a lawn that has
    /// already been torn down. Then input, so a key press is acted on this frame. Then the
    /// screen, so a change is pending before focus is examined and can be folded into the
    /// same sentence. Then focus. Then everything queued goes out at once.
    /// </summary>
    private static readonly (string Name, Action Run)[] Steps =
    {
        ("board",        Gameplay.Lawn.TickBoardLifetime),
        ("hotkeys",      Hotkeys.Update),
        ("screen",       ScreenTracker.Poll),
        ("notes",        Gameplay.Notes.Tick),
        ("almanac",      UI.Almanac.Tick),
        ("shop",         UI.Store.Tick),
        ("challenges",   UI.Challenges.Tick),
        ("achievements", UI.Achievements.Tick),
        ("dialogue",     Gameplay.Dialogue.Tick),
        ("focus",        Focus.Update),
        ("readiness",    Focus.TickReadiness),
        ("text entry",   TextEntry.Update),
        ("messages",     Patches.MessagePatch.Tick),
        ("seed chooser", Gameplay.SeedChooser.Tick),
        ("arrivals",     Patches.BoardPatch.FlushArrivals),
        ("lawn",         Gameplay.LawnInput.Update),
        ("garden",       Gameplay.GardenInput.Tick),
        ("pad",          Input.VirtualPad.Tick),
        ("tripwire",     Gameplay.Sonar.TickTripwire),
        ("boss",         Gameplay.Boss.Tick),
        ("brains",       Gameplay.Brains.Tick),
        ("tones",        Tones.Pump),
        ("speech",       Speech.Pump),
    };

    private static readonly int[] StepFailures = new int[Steps.Length];

    /// <summary>Full reports for a failing step before it is left to fail quietly.</summary>
    private const int StepFailureLogLimit = 3;

    public override void OnUpdate()
    {
        // A throw in one step must not take the other ten with it. Speech is drained last,
        // so an unguarded fault anywhere earlier means nothing is spoken that frame — and a
        // fault that repeats every frame means a mod that is permanently silent while still
        // loaded, which from the player's side is indistinguishable from no mod at all.
        // MelonLoader swallows what comes out of OnUpdate and calls it again next frame, so
        // there is nothing to make the failure visible except this.
        for (int i = 0; i < Steps.Length; i++)
        {
            try
            {
                Steps[i].Run();
                StepFailures[i] = 0;
            }
            catch (Exception ex)
            {
                int failures = ++StepFailures[i];

                // Rate-limited on purpose: a step failing at sixty frames a second would
                // otherwise bury the log, and the log is the only channel that survives a
                // mod which has gone silent. Nothing is spoken here — if the failing step
                // is speech itself, a sentence would only go into a queue nobody drains.
                if (failures <= StepFailureLogLimit)
                    Log.Error($"[step] {Steps[i].Name} failed: {ex}");
                else if (failures == StepFailureLogLimit + 1)
                    Log.Error($"[step] {Steps[i].Name} keeps failing; staying quiet about it until it recovers.");
            }
        }
    }

    public override void OnPreferencesSaved()
    {
        Hotkeys.Rebind();
        Strings.Load(Settings.Language.Value);
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"Scene initialised: [{buildIndex}] {sceneName}");

        // Needs a live scene to attach its audio host to, so it cannot happen at startup.
        Tones.Initialize();

        // Leaving a level tears the board down without telling us, so assume it is gone
        // and let the next InitLevel hand us the new one.
        if (sceneName != "Gameplay") Gameplay.Lawn.NoteBoardGone();
    }

    public override void OnDeinitializeMelon()
    {
        GameText.Shutdown();
        Tones.Shutdown();
        Speech.Shutdown();
    }
}
