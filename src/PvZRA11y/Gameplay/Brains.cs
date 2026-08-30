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
/// because there are no waves here; the score that actually moves is the count of brains eaten.
///
/// How many have gone and where the rest are come from two different places, and mixing them up
/// is the trap. A brain chewed to nothing is killed and freed from the grid on the same update,
/// so what is left on the board can say which rows still have one but can never say how many
/// there were. The count comes from the challenge score, which both ways of losing a brain go
/// through; the rows come from the board. A squished brain is the odd case that looks like both
/// — already scored, still sitting there for a few seconds — and is counted as gone.
///
/// Rows matter as much as the count, because "three left" is worth little when the zombie being
/// bought has to be dropped into one particular lane.
/// </summary>
public static class Brains
{
    /// <summary>Brains sit at column zero, and nowhere else.</summary>
    private const int BrainColumn = 0;

    /// <summary>True on an I, Zombie level.</summary>
    public static bool IsIZombieLevel
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsIZombieLevel(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// How many brains the level starts with.
    ///
    /// A constant in the shipped code rather than a count of anything: I, Zombie always runs
    /// on the five-row back yard and the win is scored against this number. It cannot be
    /// derived by counting what is on the board, because a brain that has been eaten is taken
    /// off it — see below.
    /// </summary>
    private const int Total = 5;

    /// <summary>
    /// True while the brain in this row is still there to be eaten.
    ///
    /// Three ways it can be gone, and asking about only one of them is how this went wrong
    /// the first time. A brain chewed to nothing is marked dead and freed from the grid by a
    /// sweep that runs on the following frame — and the lookup that finds it does not skip
    /// dead items, so between those two moments it is still handed over, looking exactly like
    /// a brain that is still there. A squished one is not even dead yet: it sits in place for
    /// a few seconds after being scored. Both are gone as far as the player is concerned.
    /// </summary>
    public static bool StandingIn(int row)
    {
        Board board = Lawn.BoardRef;
        if (board == null) return false;

        GridItem brain;
        try { brain = board.GetGridItemAt(GridItemType.IZombieBrain, BrainColumn, row); }
        catch { return false; }

        if (brain == null) return false;

        try { if (brain.mDead) return false; } catch { }

        try { return brain.mGridItemState != GridItemState.BrainSquished; }
        catch { return true; }
    }

    /// <summary>The rows that still have a brain, counting from one.</summary>
    public static List<int> RowsLeft()
    {
        var rows = new List<int>();

        int count = Lawn.SafeRowCount();
        for (int row = 0; row < count; row++)
            if (StandingIn(row)) rows.Add(row + 1);

        return rows;
    }

    /// <summary>
    /// How many brains have gone, by the game's own count.
    ///
    /// This is the number to trust, and counting the board is not. A brain chewed to nothing
    /// is killed and freed from the grid on the same update, so the items that remain say how
    /// many are left but can never say how many there were. Both ways of losing one — chewed
    /// and squished — score through the same place, so this covers both.
    /// </summary>
    private static int Eaten()
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
        List<int> rows = RowsLeft();

        int eaten = Eaten();
        int left = eaten < 0 ? rows.Count : Total - eaten;

        // The two ought to agree. When they do not, the score is believed and the difference
        // goes in the log rather than into a sentence nobody can act on.
        if (eaten >= 0 && left != rows.Count)
            Core.Log.Msg($"[brains] the score says {left} left, the board shows {rows.Count}");

        if (left <= 0 && rows.Count == 0) return Strings.T("brains.none_left");

        if (rows.Count == 0) return Strings.T("brains.left_unplaced", left, Total);

        return Strings.T("brains.left", left, Total, string.Join(", ", rows));
    }

    private static int _lastEaten = -1;

    /// <summary>
    /// Says when a brain goes, and when the last one does.
    ///
    /// The game marks the moment with an animation and a sound, neither of which says how far
    /// through the level that leaves you — and in a mode where the player is steering several
    /// zombies down several lanes at once, one brain going is the only progress there is.
    ///
    /// Watched on the score rather than on the board, because a brain that has been eaten is
    /// gone from the board and there is nothing left there to notice.
    /// </summary>
    public static void Tick()
    {
        if (!Lawn.IsOnBoard || !IsIZombieLevel) { Forget(); return; }

        int eaten = Eaten();
        if (eaten < 0) return;

        if (_lastEaten < 0) { _lastEaten = eaten; return; }   // first look: nothing has changed yet
        if (eaten == _lastEaten) return;

        // Going down means a level restarted under us. Follow it quietly rather than
        // announcing a brain that grew back.
        if (eaten < _lastEaten) { _lastEaten = eaten; return; }

        _lastEaten = eaten;

        int left = Math.Max(0, Total - eaten);
        Core.Log.Msg($"[brains] {eaten} of {Total} eaten, {left} left");

        Speech.Say(left == 0 ? Strings.T("brains.all_eaten") : Strings.T("brains.eaten", left),
                   interrupt: false, context: "brains", allowRepeat: true);
    }

    private static void Forget() => _lastEaten = -1;

    /// <summary>The brains, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- brains ---");
        sb.AppendLine($"  I, Zombie level: {IsIZombieLevel}");

        if (!IsIZombieLevel) { sb.AppendLine(); return; }

        sb.AppendLine($"  eaten by score : {Eaten()} of {Total}");

        int rows = Lawn.SafeRowCount();
        for (int row = 0; row < rows; row++)
            sb.AppendLine($"      row {row + 1}: {(StandingIn(row) ? "brain still there" : "no brain")}");

        sb.AppendLine();
    }
}
