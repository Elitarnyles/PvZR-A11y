using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The brains of I, Zombie.
///
/// This is the one mode where the lawn is not the thing to defend. The plants are already
/// standing when the level opens, the player buys zombies instead of planting, and the whole
/// objective is a row of brains against the left edge — one per row, at column zero, behind
/// everything the defender has put in the way.
///
/// None of that reaches a player who cannot see it. The progress line the mod otherwise reads
/// out on this key says "Wave 0 of 4, 0% complete" and goes on saying it for the entire level,
/// because there are no waves here; the score that actually moves is the count of brains
/// eaten. And a brain that has been eaten is not removed from the board — it is left in place
/// with its state changed — so walking the leftmost column reported a brain in a row that had
/// already been cleared, which is the worst kind of wrong: confident, and about the only thing
/// that matters.
///
/// Read from the game's own grid items rather than from the score alone, because "three left"
/// and "three left, in rows one, four and five" are different amounts of help.
/// </summary>
public static class Brains
{
    /// <summary>Brains sit at column zero, and nowhere else.</summary>
    private const int BrainColumn = 0;

    private static int _lastEaten = -1;
    private static bool _announcedWin;

    /// <summary>True on an I, Zombie level.</summary>
    public static bool IsIZombieLevel
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsIZombieLevel(); }
            catch { return false; }
        }
    }

    /// <summary>One brain: which row it is in, and whether it has been eaten.</summary>
    public readonly record struct Brain(int Row, bool Eaten);

    /// <summary>
    /// Every brain the level started with, in row order.
    ///
    /// The eaten ones are kept rather than filtered out, so a caller can say how far through
    /// the level the player is without having to be told the starting count separately.
    /// </summary>
    public static List<Brain> All()
    {
        var found = new List<Brain>();

        Board board = Lawn.BoardRef;
        if (board == null) return found;

        int rows = Lawn.SafeRowCount();

        for (int row = 0; row < rows; row++)
        {
            GridItem brain;
            try { brain = board.GetGridItemAt(GridItemType.IZombieBrain, BrainColumn, row); }
            catch { continue; }

            if (brain == null) continue;

            bool eaten = false;
            try { eaten = brain.mGridItemState == GridItemState.BrainSquished; }
            catch { /* an unreadable state is safer called uneaten than called done */ }

            found.Add(new Brain(row, eaten));
        }

        return found;
    }

    /// <summary>True when the brain in this row is still there to be eaten.</summary>
    public static bool StandingIn(int row)
    {
        foreach (Brain brain in All())
            if (brain.Row == row) return !brain.Eaten;

        return false;
    }

    /// <summary>How many brains have been eaten, by the game's own count.</summary>
    private static int Score()
    {
        try
        {
            Challenge challenge = Lawn.BoardRef?.mChallenge;
            return challenge == null ? -1 : challenge.mChallengeScore;
        }
        catch { return -1; }
    }

    /// <summary>
    /// How the level stands: how many brains are left and which rows they are in.
    ///
    /// The rows are what the player acts on. Knowing three are left is worth little when the
    /// zombie being bought has to be dropped into one particular lane.
    /// </summary>
    public static string Describe()
    {
        List<Brain> all = All();
        if (all.Count == 0) return null;

        var standing = new List<int>();
        foreach (Brain brain in all)
            if (!brain.Eaten) standing.Add(brain.Row + 1);

        if (standing.Count == 0) return Strings.T("brains.none_left");

        return Strings.T("brains.left", standing.Count, all.Count, string.Join(", ", standing));
    }

    /// <summary>
    /// Says when a brain goes, and when the last one does.
    ///
    /// The game marks the moment with an animation and a sound, neither of which says which
    /// row it happened in — and in a mode where the player is steering several zombies down
    /// several lanes at once, which row was cleared is the whole of the news.
    /// </summary>
    public static void Tick()
    {
        if (!Lawn.IsOnBoard || !IsIZombieLevel) { Forget(); return; }

        List<Brain> all = All();
        if (all.Count == 0) return;

        int eaten = 0;
        foreach (Brain brain in all) if (brain.Eaten) eaten++;

        if (_lastEaten < 0) { _lastEaten = eaten; return; }   // first look: nothing has changed yet
        if (eaten == _lastEaten) return;

        // Going down would mean a level restarted under us. Follow it quietly rather than
        // announcing a brain that grew back.
        if (eaten < _lastEaten) { _lastEaten = eaten; _announcedWin = false; return; }

        _lastEaten = eaten;

        int left = all.Count - eaten;
        Core.Log.Msg($"[brains] {eaten} of {all.Count} eaten, {left} left");

        if (left == 0)
        {
            if (_announcedWin) return;
            _announcedWin = true;
            Speech.Say(Strings.T("brains.all_eaten"), interrupt: true, context: "brains", allowRepeat: true);
            return;
        }

        Speech.Say(Strings.T("brains.eaten", left), interrupt: false, context: "brains", allowRepeat: true);
    }

    private static void Forget()
    {
        _lastEaten = -1;
        _announcedWin = false;
    }

    /// <summary>The brains, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- brains ---");
        sb.AppendLine($"  I, Zombie level: {IsIZombieLevel}");

        if (!IsIZombieLevel) { sb.AppendLine(); return; }

        sb.AppendLine($"  challenge score: {Score()}");

        foreach (Brain brain in All())
            sb.AppendLine($"      row {brain.Row + 1}: {(brain.Eaten ? "eaten" : "still there")}");

        sb.AppendLine();
    }
}
