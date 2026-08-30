using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The Slot Machine mini-game.
///
/// An ordinary lawn with one thing bolted on: a fruit machine along the top. Twenty-five sun
/// pulls the handle, three reels spin, and what they land on falls onto the lawn as coins -
/// sun, diamonds, or seed packets you can pick up and plant. Two thousand sun wins the level.
/// You are still being attacked the whole time, and the sun you spend pulling is the sun you
/// would otherwise plant with, which is the whole of the game.
///
/// None of that reaches a player who cannot see it. The handle is a picture with no control on
/// it, the reels are pictures, and what they landed on is announced by a caption that appears
/// and fades. The mod pulls the handle through the game's own method, reads the three reels out
/// of the seed bank they are drawn from, and says what came of it.
///
/// The numbers here are the game's own, read out of its code rather than remembered from the
/// original: twenty-five to pull, two thousand to win.
/// </summary>
public static class SlotMachine
{
    /// <summary>What a pull costs.</summary>
    public const int PullCost = 25;

    /// <summary>The sun that wins the level.</summary>
    public const int Target = 2000;

    /// <summary>The three reels are the first three slots of the seed bank.</summary>
    private const int Reels = 3;

    /// <summary>True on a Slot Machine level.</summary>
    public static bool IsActive
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsSlotMachineLevel(); }
            catch { return false; }
        }
    }

    private static Challenge Challenge()
    {
        try { return Lawn.BoardRef?.mChallenge; }
        catch { return null; }
    }

    /// <summary>True while the reels are still turning.</summary>
    public static bool Rolling
    {
        get
        {
            try { return Challenge()?.mChallengeState == ChallengeState.SlotMachineRolling; }
            catch { return false; }
        }
    }

    /// <summary>What the three reels are showing, or null when they cannot be read.</summary>
    public static List<SeedType> Reading()
    {
        try
        {
            SeedBank bank = Lawn.BoardRef?.mSeedBank;
            var packets = bank?.mSeedPackets;
            if (packets == null) return null;

            var shown = new List<SeedType>(Reels);
            for (int i = 0; i < Reels && i < packets.Length; i++)
            {
                SeedPacket packet = packets[i];
                if (packet == null) return null;
                shown.Add(packet.mPacketType);
            }

            return shown.Count == Reels ? shown : null;
        }
        catch { return null; }
    }

    /// <summary>One reel's face, in words.</summary>
    private static string FaceName(SeedType face)
    {
        if (face == SeedType.SlotMachineSun) return Strings.T("slots.face_sun");
        if (face == SeedType.SlotMachineDiamond) return Strings.T("slots.face_diamond");
        return Lawn.PlantName(face);
    }

    /// <summary>How many times the handle has been pulled, or -1.</summary>
    private static int RollCount()
    {
        try { return Challenge()?.mSlotMachineRollCount ?? -1; }
        catch { return -1; }
    }

    /// <summary>
    /// Pulls the handle.
    ///
    /// Through the game's own method, which takes the sun and starts the reels itself. What it
    /// will not do is tell you whether that worked: it answers false for a cursor with
    /// something in it and for reels already turning, but when the sun is short it takes the
    /// early exit and answers TRUE anyway. Believing it announced a pull that never happened,
    /// with a sun total that had not changed - and a wrong answer in the voice of a right one
    /// is the failure this mod can least afford.
    ///
    /// So the answer is thrown away and the game is watched instead. The count of pulls is
    /// bumped on the success path and nowhere else, which makes it the one thing that says
    /// yes or no without room for argument.
    /// </summary>
    public static bool Pull()
    {
        if (!IsActive) return false;

        Challenge challenge = Challenge();
        if (challenge == null)
        {
            Speech.SayVerbatim(Strings.T("slots.no_machine"), "slots");
            return true;
        }

        if (Rolling)
        {
            Speech.SayVerbatim(Strings.T("slots.still_rolling"), "slots");
            return true;
        }

        // Named before trying, because the game refuses this silently and the refusal is one
        // the player can do something about. A won seed packet in hand stops every pull until
        // it is planted or put back, and "not enough sun" would be a lie about the cause.
        CursorType? holding = Lawn.CursorKind();
        if (holding.HasValue && holding.Value != CursorType.Normal)
        {
            Speech.SayVerbatim(Strings.T("slots.hands_full"), "slots");
            return true;
        }

        int before = RollCount();
        int sun = Lawn.SunCount();

        try { challenge.PullSlotMachineHandle(); }
        catch (Exception ex)
        {
            Core.Log.Warning($"[slots] the handle would not move: {ex.Message}");
            Speech.SayVerbatim(Strings.T("slots.no_machine"), "slots");
            return true;
        }

        int after = RollCount();
        bool pulled = before < 0 || after > before;

        if (!pulled)
        {
            Core.Log.Msg($"[slots] the handle did not move; {sun} sun, pulls still {after}");

            Speech.SayVerbatim(sun >= 0
                ? Strings.T("slots.not_enough_sun", PullCost, sun)
                : Strings.T("slots.will_not_pull"), "slots");

            return true;
        }

        Core.Log.Msg($"[slots] pull {after} with {sun} sun");
        Speech.SayVerbatim(Strings.T("slots.pulled", Math.Max(0, sun - PullCost)), "slots");

        _watching = true;
        return true;
    }

    /// <summary>How the level stands, for the key that reports progress.</summary>
    public static string Describe()
    {
        if (!IsActive) return null;

        int sun = Lawn.SunCount();
        if (sun < 0) return Strings.T("slots.no_machine");

        var parts = new List<string>(3) { Strings.T("slots.progress", sun, Target) };

        if (Rolling) parts.Add(Strings.T("slots.rolling"));
        else if (sun >= PullCost) parts.Add(Strings.T("slots.can_pull"));
        else parts.Add(Strings.T("slots.cannot_afford", PullCost));

        return string.Join(" ", parts);
    }

    /// <summary>What the reels are showing right now, said on request.</summary>
    public static bool AnnounceReels()
    {
        if (!IsActive) return false;

        if (Rolling)
        {
            Speech.SayVerbatim(Strings.T("slots.still_rolling"), "slots");
            return true;
        }

        List<SeedType> shown = Reading();
        if (shown == null)
        {
            Speech.SayVerbatim(Strings.T("slots.no_machine"), "slots");
            return true;
        }

        Speech.SayVerbatim(Describe(shown), "slots");
        return true;
    }

    /// <summary>The three faces and what they are worth together.</summary>
    private static string Describe(List<SeedType> shown)
    {
        var names = new List<string>(Reels);
        foreach (SeedType face in shown) names.Add(FaceName(face));

        string line = string.Join(", ", names);

        // Said in the same breath as the faces, because "Sun, Sun, Peashooter" only means
        // something to someone who already knows that two of a kind pays.
        bool all = shown[0] == shown[1] && shown[1] == shown[2];
        bool pair = !all && (shown[0] == shown[1] || shown[1] == shown[2] || shown[0] == shown[2]);

        if (!all && !pair) return Strings.T("slots.nothing", line);

        // Which symbol won, and what it pays. "Two of a kind" on its own leaves the player to
        // remember a payout table; and the diamonds need saying out loud, because they are the
        // one prize that looks like a win and does nothing for the level - they pay shop
        // money, and only sun counts towards the two thousand.
        SeedType won = all ? shown[0]
                     : shown[0] == shown[1] || shown[0] == shown[2] ? shown[0] : shown[1];

        string prize;
        if (won == SeedType.SlotMachineSun) prize = Strings.T(all ? "slots.won_sun_big" : "slots.won_sun");
        else if (won == SeedType.SlotMachineDiamond) prize = Strings.T(all ? "slots.won_diamond_big" : "slots.won_diamond");
        else prize = Strings.T(all ? "slots.won_plants" : "slots.won_plant", Lawn.PlantName(won));

        return Strings.T(all ? "slots.jackpot" : "slots.two_of_a_kind", line) + " " + prize;
    }

    #region watching the reels stop

    private static bool _watching;
    private static bool _wasRolling;

    /// <summary>
    /// Says what the reels landed on, the moment they stop.
    ///
    /// The game marks it with a caption and a sound, neither of which says what the faces
    /// were. Only after a pull the mod made itself: the reels are read every frame anyway,
    /// and announcing a stop nobody asked for would talk over the lawn.
    /// </summary>
    public static void Tick()
    {
        if (!Lawn.IsOnBoard || !IsActive) { _watching = false; _wasRolling = false; return; }

        bool rolling = Rolling;
        bool stopped = _wasRolling && !rolling;
        _wasRolling = rolling;

        if (!stopped || !_watching) return;
        _watching = false;

        List<SeedType> shown = Reading();
        if (shown == null) return;

        Core.Log.Msg($"[slots] the reels stopped on {string.Join(", ", shown)}");
        Speech.Say(Describe(shown), interrupt: true, context: "slots", allowRepeat: true);
    }

    #endregion

    /// <summary>The machine, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- slot machine ---");
        sb.AppendLine($"  slot machine level : {IsActive}");

        if (!IsActive) { sb.AppendLine(); return; }

        sb.AppendLine($"  rolling            : {Rolling}");
        sb.AppendLine($"  sun                : {Lawn.SunCount()} of {Target}");

        try { sb.AppendLine($"  pulls so far       : {Challenge()?.mSlotMachineRollCount}"); }
        catch { sb.AppendLine("  pulls so far       : <unreadable>"); }

        List<SeedType> shown = Reading();
        sb.AppendLine(shown == null
            ? "  reels              : <unreadable>"
            : $"  reels              : {string.Join(", ", shown)}");

        sb.AppendLine();
    }
}
