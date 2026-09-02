using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using PvZRA11y.UI;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Last Stand, where the level does not start until you say so.
///
/// Every other level begins the moment you leave the plant chooser. This one gives you a budget
/// of sun and an empty lawn and waits: you spend the lot laying out a defence, and only then
/// press the button that sends the wave. Between stages it waits again, with whatever sun the
/// last stage left you.
///
/// The mod had no way to press that button. It is not on the lawn, so walking the grid never
/// reaches it, and it is not a control the focus machinery is allowed to walk to while the lawn
/// has the keyboard - the arrows and Enter belong to planting. So the level could be planned
/// and never begun.
///
/// The original mod put this on the key that otherwise freezes the game, and that is where it
/// goes here: on this level, while the button is showing, there is nothing running to freeze.
/// </summary>
public static class LastStand
{
    /// <summary>Where the button and its label live in the game's data model.</summary>
    private const string ButtonKey = "gameplay.lastStandStart";
    private const string ShowingKey = "gameplay.showLastStandButton";
    private const string LabelKey = "gameplay.lastStandButtonLabel";

    /// <summary>True on a Last Stand level.</summary>
    public static bool IsActive
    {
        get
        {
            try { return Lawn.AppRef?.GameMode == GameMode.ChallengeLastStand; }
            catch { return false; }
        }
    }

    /// <summary>
    /// True while the game is waiting to be told to begin.
    ///
    /// Asked of the same flag the button's own visibility is bound to, so the mod offers the
    /// action exactly when the game offers the button and not a moment either side.
    /// </summary>
    public static bool CanStart
    {
        get
        {
            if (!IsActive) return false;

            string showing = ModelText.FromRoot(ShowingKey);
            return showing != null && showing.Equals("True", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>What the button says: starting the first wave, or carrying on to the next.</summary>
    public static string Label()
    {
        string raw = ModelText.FromRoot(LabelKey);
        return string.IsNullOrWhiteSpace(raw) ? Strings.T("stand.start") : ModelText.Resolve(raw);
    }

    /// <summary>
    /// Sends the wave.
    ///
    /// Through the button's own model rather than a click at a screen position, which is how
    /// the original mod had to do it and what breaks the moment anything moves. The mod
    /// already presses buttons this way everywhere else.
    /// </summary>
    public static bool Start()
    {
        if (!CanStart) return false;

        Il2CppTekly.DataModels.Models.ButtonModel button = null;

        try
        {
            button = ModelText.ModelAt(ButtonKey)?
                .TryCast<Il2CppTekly.DataModels.Models.ButtonModel>();
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[stand] could not reach the start button: {ex.Message}");
        }

        if (button == null)
        {
            Speech.SayVerbatim(Strings.T("stand.no_button"), "last stand");
            return true;
        }

        string said = Label();

        try
        {
            Core.Log.Msg($"[stand] pressing \"{said}\"");
            button.Activate(0);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[stand] the button refused: {ex.Message}");
            Speech.SayVerbatim(Strings.T("stand.no_button"), "last stand");
            return true;
        }

        Speech.SayVerbatim(said, "last stand");
        return true;
    }

    /// <summary>How the level stands, for the key that reports progress.</summary>
    public static string Describe()
    {
        if (!IsActive) return null;

        int sun = Lawn.SunCount();

        if (CanStart)
        {
            // The budget is the whole of the planning phase, so it is said first and said as a
            // budget rather than as a running total.
            return sun < 0
                ? Strings.T("stand.planning_unknown", Label())
                : Strings.T("stand.planning", sun, Label());
        }

        string progress = Lawn.LevelProgress();
        return string.IsNullOrEmpty(progress) ? Strings.T("stand.onslaught") : progress;
    }

    private static bool _offered;

    /// <summary>
    /// Says when the game starts waiting for you, and stops saying it once it has.
    ///
    /// Worth announcing rather than leaving to be discovered: nothing else on the level marks
    /// the moment, and a player who does not know the game is waiting will sit through a
    /// silence wondering why no zombies are coming.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive) { _offered = false; return; }

        bool waiting = CanStart;

        if (!waiting) { _offered = false; return; }
        if (_offered) return;

        _offered = true;

        int sun = Lawn.SunCount();
        Core.Log.Msg($"[stand] waiting to be started, {sun} sun in hand");

        Speech.Say(sun < 0
                       ? Strings.T("stand.ready_unknown", Label())
                       : Strings.T("stand.ready", sun, Label()),
                   interrupt: false, context: "last stand", allowRepeat: true);
    }

    /// <summary>Last Stand, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- last stand ---");
        sb.AppendLine($"  last stand level : {IsActive}");

        if (!IsActive) { sb.AppendLine(); return; }

        sb.AppendLine($"  waiting to start : {CanStart}");
        sb.AppendLine($"  button says      : {Label()}");
        sb.AppendLine($"  showing flag     : {ModelText.FromRoot(ShowingKey) ?? "<none>"}");
        sb.AppendLine($"  button model     : {(ModelText.ModelAt(ButtonKey) == null ? "not there" : "there")}");
        sb.AppendLine($"  sun              : {Lawn.SunCount()}");
        sb.AppendLine();
    }
}
