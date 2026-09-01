using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Beghouled and Beghouled Twist.
///
/// Not a lawn at all. Eight columns by five rows of plants, six kinds, and the game is to line
/// three of a kind up in a row or a column; seventy-five lines wins it. In Beghouled a move
/// swaps two neighbours and only a swap that scores is allowed. In Twist a move rotates a block
/// of four a quarter turn, and the block is named by its top-left corner.
///
/// The original mod answered this with a tone: walk onto a plant that could be part of a match
/// and it beeped. That says something is here and never says what to do about it, and this
/// player navigates by narration rather than by beeps, so it would have been half an answer
/// twice over.
///
/// What is offered instead is the whole move, in words, and a way to make it. The game will say
/// whether any given swap or turn scores, and it will say so on a copy of the board that it
/// puts back afterwards - so every legal move can be found and read out without touching the
/// game at all. Then one key plays the move that was just described.
/// </summary>
public static class Beghouled
{
    /// <summary>The puzzle occupies eight columns and five rows of the lawn.</summary>
    private const int Columns = 8;
    private const int Rows = 5;

    /// <summary>Lines to clear to win.</summary>
    public const int Target = 75;

    /// <summary>True on either of the two, whatever is on screen over it.</summary>
    public static bool IsActive => Lawn.IsMatchThreePuzzle;

    /// <summary>
    /// True when the puzzle is also the thing the keyboard belongs to.
    ///
    /// The game mode stays Beghouled after the level is won, and the award screen opens over
    /// the board rather than replacing it. Asking only "is this a Beghouled level" therefore
    /// went on answering yes with a trophy on screen, and the puzzle's keys went on eating
    /// Enter - so the award screen could be read and not pressed, and the level could not be
    /// left. The lawn already works out when the keyboard is not its own; this defers to it.
    /// </summary>
    public static bool Playable => IsActive && Lawn.HasInput;

    /// <summary>True on the one where a move is a quarter turn rather than a swap.</summary>
    public static bool IsTwist
    {
        get
        {
            try { return Lawn.AppRef?.GameMode == GameMode.ChallengeBeghouledTwist; }
            catch { return false; }
        }
    }

    private static Challenge Challenge()
    {
        try { return Lawn.BoardRef?.mChallenge; }
        catch { return null; }
    }

    /// <summary>
    /// True when the board is standing still and will take a move.
    ///
    /// Checked here because nothing else will. The methods that make a move do not look at the
    /// board's state at all - the game's own guard sits in the click handler that the mod does
    /// not go through - so a move sent while the plants are still falling would be applied to a
    /// board that no longer looks the way it was read.
    /// </summary>
    public static bool Ready
    {
        get
        {
            Challenge challenge = Challenge();
            if (challenge == null) return false;

            try
            {
                if (challenge.mChallengeState != ChallengeState.Normal) return false;
                return !(Lawn.BoardRef?.HasLevelAwardDropped() ?? true);
            }
            catch { return false; }
        }
    }

    /// <summary>A fresh snapshot of the board, or null.</summary>
    private static Challenge.BeghouledBoardState State()
    {
        Challenge challenge = Challenge();
        if (challenge == null) return null;

        try
        {
            var state = new Challenge.BeghouledBoardState();
            challenge.LoadBeghouledBoardState(state);
            return state;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[beghouled] could not read the board: {ex.Message}");
            return null;
        }
    }

    /// <summary>What is standing on a square, or None.</summary>
    public static SeedType PlantAt(int x, int y)
    {
        Challenge challenge = Challenge();
        Challenge.BeghouledBoardState state = State();
        if (challenge == null || state == null) return SeedType.None;

        try { return challenge.BeghouledGetPlantAt(x, y, state); }
        catch { return SeedType.None; }
    }

    #region the moves

    /// <summary>
    /// One move. A swap carries the direction it goes in; a turn carries only its corner.
    /// </summary>
    public readonly record struct Move(int X, int Y, int Dx, int Dy, bool Turn);

    /// <summary>
    /// Every move the board will accept.
    ///
    /// Asked of the game one candidate at a time. Both predicates swap the pieces, look, and
    /// put them back before answering, so this reads the board without disturbing it.
    ///
    /// The loops stop short by hand rather than trusting the game to reject what is off the
    /// edge. Its bounds test rejects anything ABOVE eight and above five, so column eight -
    /// which is on the lawn but not in the puzzle - sails through and would be offered as a
    /// swap into empty ground.
    /// </summary>
    public static List<Move> Moves()
    {
        var found = new List<Move>();

        Challenge challenge = Challenge();
        Challenge.BeghouledBoardState state = State();
        if (challenge == null || state == null) return found;

        try
        {
            if (IsTwist)
            {
                for (int y = 0; y + 1 < Rows; y++)
                    for (int x = 0; x + 1 < Columns; x++)
                        if (challenge.BeghouledTwistMoveCausesMatch(x, y, state))
                            found.Add(new Move(x, y, 0, 0, true));

                return found;
            }

            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (x + 1 < Columns && challenge.BeghouledIsValidMove(x, y, x + 1, y, state))
                        found.Add(new Move(x, y, 1, 0, false));

                    if (y + 1 < Rows && challenge.BeghouledIsValidMove(x, y, x, y + 1, state))
                        found.Add(new Move(x, y, 0, 1, false));
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[beghouled] could not work out the moves: {ex.Message}");
        }

        return found;
    }

    /// <summary>The moves that start on one square, for the running commentary.</summary>
    private static List<Move> MovesAt(int x, int y)
    {
        var here = new List<Move>();

        foreach (Move move in Moves())
        {
            if (move.Turn)
            {
                // A turn is named by its corner, but it moves all four, so a square is
                // involved if any of the four blocks touching it can turn.
                if (move.X >= x - 1 && move.X <= x && move.Y >= y - 1 && move.Y <= y) here.Add(move);
                continue;
            }

            // A swap belongs to both of its squares: standing on either one, the move is
            // available, and only saying so on the left-hand one would hide half of them.
            if (move.X == x && move.Y == y) here.Add(move);
            else if (move.X + move.Dx == x && move.Y + move.Dy == y)
                here.Add(new Move(x, y, -move.Dx, -move.Dy, false));
        }

        return here;
    }

    private static string DirectionName(int dx, int dy) =>
        Strings.T(dx > 0 ? "beghouled.right" : dx < 0 ? "beghouled.left"
                : dy > 0 ? "beghouled.down" : "beghouled.up");

    /// <summary>One move, in a sentence.</summary>
    public static string Describe(Move move)
    {
        if (move.Turn)
            return Strings.T("beghouled.turn_here", move.Y + 1, move.X + 1);

        string plant = Lawn.PlantName(PlantAt(move.X, move.Y));

        return Strings.T("beghouled.swap", plant, move.Y + 1, move.X + 1,
                         DirectionName(move.Dx, move.Dy));
    }

    /// <summary>What to add to the square announcement as the cursor walks the board.</summary>
    public static string HintFor(int x, int y)
    {
        if (!Playable || !Ready) return null;

        List<Move> here = MovesAt(x, y);
        if (here.Count == 0) return null;

        if (IsTwist) return Strings.T("beghouled.can_turn");

        var directions = new List<string>(here.Count);
        foreach (Move move in here) directions.Add(DirectionName(move.Dx, move.Dy));

        return Strings.T("beghouled.can_swap", string.Join(Strings.T("beghouled.or"), directions));
    }

    #endregion

    #region choosing and playing one

    private static int _chosen;

    /// <summary>Steps through the moves the board will take, saying each one.</summary>
    public static bool Cycle(int step)
    {
        if (!Playable) return false;

        if (!Ready)
        {
            Speech.SayVerbatim(Strings.T("beghouled.settling"), "beghouled");
            return true;
        }

        List<Move> moves = Moves();
        if (moves.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("beghouled.no_moves"), "beghouled");
            return true;
        }

        _chosen = ((_chosen + step) % moves.Count + moves.Count) % moves.Count;

        Speech.SayVerbatim(Describe(moves[_chosen]) + " " +
                           Strings.T("beghouled.position", _chosen + 1, moves.Count), "beghouled");
        return true;
    }

    /// <summary>Says how many moves there are, and the first few.</summary>
    public static bool AnnounceMoves()
    {
        if (!Playable) return false;

        if (!Ready)
        {
            Speech.SayVerbatim(Strings.T("beghouled.settling"), "beghouled");
            return true;
        }

        List<Move> moves = Moves();
        if (moves.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("beghouled.no_moves"), "beghouled");
            return true;
        }

        // The count first, then a handful. A busy board can offer twenty, and twenty
        // sentences is not something anyone listens to before deciding.
        var lines = new List<string>(4) { Strings.T("beghouled.count", moves.Count) };

        for (int i = 0; i < moves.Count && i < 3; i++) lines.Add(Describe(moves[i]));

        Speech.SayVerbatim(string.Join(". ", lines), "beghouled");
        return true;
    }

    /// <summary>
    /// Plays the move the player has stepped to.
    ///
    /// The board is read again first. Between choosing a move and playing it the plants may
    /// have fallen into new places - a match clearing somewhere else is enough - and a move
    /// that was legal a moment ago is then a swap of two things that are no longer there.
    /// </summary>
    public static bool Play()
    {
        if (!Playable) return false;

        if (!Ready)
        {
            Speech.SayVerbatim(Strings.T("beghouled.settling"), "beghouled");
            return true;
        }

        List<Move> moves = Moves();
        if (moves.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("beghouled.no_moves"), "beghouled");
            return true;
        }

        if (_chosen < 0 || _chosen >= moves.Count) _chosen = 0;

        Move move = moves[_chosen];
        string said = Describe(move);

        if (!Perform(move))
        {
            Speech.SayVerbatim(Strings.T("beghouled.would_not_play"), "beghouled");
            return true;
        }

        Core.Log.Msg($"[beghouled] played {said}");
        Speech.SayVerbatim(said, "beghouled");

        _chosen = 0;
        return true;
    }

    /// <summary>
    /// Whether the last thing sent to the board actually moved anything.
    ///
    /// A move that takes ends with the board falling, and falling is a state of its own. A move
    /// the game refused leaves the state exactly where it was. So the state having left Normal
    /// is the proof, and it is available in the same frame - no waiting, no guessing from the
    /// score, which does not move until the pieces land.
    ///
    /// Worth the trouble because the alternative is the failure this mod keeps having to hunt
    /// down: announcing a thing that did not happen, in the same words used when it does.
    /// </summary>
    private static bool Took()
    {
        try { return Challenge()?.mChallengeState != ChallengeState.Normal; }
        catch { return false; }
    }

    /// <summary>Plays a move by handing the game the click a mouse would have made.</summary>
    private static bool Perform(Move move)
    {
        Challenge challenge = Challenge();
        Board board = Lawn.BoardRef;
        if (challenge == null || board == null) return false;

        try
        {
            if (move.Turn) return Turn(challenge, move);

            if (!Lawn.TryPixelInSquare(move.X, move.Y, out int px, out int py))
            {
                Core.Log.Warning($"[beghouled] no pixel maps to square {move.X},{move.Y}");
                return false;
            }

            // A drag, the way the mouse does it: press on the plant, then move far enough in
            // one direction for the game to call it a drag. Anything over ten pixels counts,
            // and the axis with the larger movement is the one it takes.
            challenge.BeghouledDragStart(px, py);
            challenge.BeghouledDragUpdate(px + move.Dx * 40, py + move.Dy * 50);

            // The drag clears its own capture flag once it has decided, but only if it decided
            // at all - a delta the game thought too small leaves the board waiting for a mouse
            // that will never move again.
            if (!Took()) { try { challenge.BeghouledDragCancel(); } catch { } return false; }

            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[beghouled] the move would not go through: {ex.Message}");

            try { challenge.BeghouledDragCancel(); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Turns a block of four.
    ///
    /// The game takes a mouse position and works the block out from it, and it does not take
    /// the position of a square: it steps back half a square first, so the point that means a
    /// block is the corner where its four plants meet. Rather than reproduce that arithmetic,
    /// the game is asked which block a candidate point would turn, and the search stops when
    /// the answer is the block that was wanted.
    /// </summary>
    private static bool Turn(Challenge challenge, Move move)
    {
        Board board = Lawn.BoardRef;

        for (int corner = 0; corner < 4; corner++)
        {
            int gx = move.X + (corner & 1);
            int gy = move.Y + ((corner >> 1) & 1);

            int baseX, baseY;
            try
            {
                baseX = board.GridToPixelX(gx, gy);
                baseY = board.GridToPixelY(gx, gy);
            }
            catch { continue; }

            for (int dy = -20; dy <= 60; dy += 10)
            {
                for (int dx = -20; dx <= 60; dx += 10)
                {
                    int px = baseX + dx;
                    int py = baseY + dy;

                    int tx;
                    int ty;

                    bool onBlock;
                    try { onBlock = challenge.BeghouledTwistSquareFromMouse(px, py, out tx, out ty); }
                    catch { continue; }

                    if (!onBlock || tx != move.X || ty != move.Y) continue;

                    challenge.BeghouledTwistMouseDown(px, py);
                    return Took();
                }
            }
        }

        Core.Log.Warning($"[beghouled] no point turns the block at {move.X},{move.Y}");
        return false;
    }

    #endregion

    /// <summary>How the level stands, for the key that reports progress.</summary>
    public static string Describe()
    {
        if (!IsActive) return null;

        int score = -1;
        try { score = Challenge()?.mChallengeScore ?? -1; }
        catch { }

        string line = score < 0
            ? Strings.T("beghouled.no_score")
            : Strings.T("beghouled.progress", score, Target);

        if (!Ready) return line + " " + Strings.T("beghouled.settling");

        int moves = Moves().Count;
        return line + " " + (moves == 0
            ? Strings.T("beghouled.no_moves")
            : Strings.T("beghouled.count", moves));
    }

    /// <summary>The board, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- beghouled ---");
        sb.AppendLine($"  match-three   : {IsActive}{(IsTwist ? " (twist)" : "")}");

        if (!IsActive) { sb.AppendLine(); return; }

        sb.AppendLine($"  ready         : {Ready}");

        try { sb.AppendLine($"  lines cleared : {Challenge()?.mChallengeScore} of {Target}"); }
        catch { sb.AppendLine("  lines cleared : <unreadable>"); }

        for (int y = 0; y < Rows; y++)
        {
            var row = new List<string>(Columns);
            for (int x = 0; x < Columns; x++) row.Add(Lawn.PlantName(PlantAt(x, y)));
            sb.AppendLine($"      row {y + 1}: {string.Join(", ", row)}");
        }

        List<Move> moves = Moves();
        sb.AppendLine($"  moves         : {moves.Count}");
        foreach (Move move in moves) sb.AppendLine($"      {Describe(move)}");

        sb.AppendLine();
    }
}
