using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The three picture puzzles: Seeing Stars, Art Challenge Wall-nut and Art Challenge Sunflower.
///
/// One mini-game wearing three coats. The lawn is marked out with a faint pattern - a star, a
/// wall-nut, a sunflower - and the level is won by planting the right plant on every marked
/// square while the zombies come. The pattern is drawn on the ground in a colour, which is to
/// say it does not exist at all for a player who cannot see it.
///
/// Two of the three want one plant throughout. The sunflower wants three - star fruit for the
/// petals, wall-nut for the face, umbrella leaf for the stem - and the game says so itself,
/// warning anyone who starts that level "without starfruit, umbrella leaf, and wall-nut
/// seeds??". So which plant is a real question on one of these levels, and every square has to
/// carry its own answer rather than one name being given for the board.
///
/// The pattern is not guessed at and not written down here. The game will say, square by
/// square, which plant its template wants there, so all three puzzles - and any fourth one an
/// update adds - are read from the game's own answer. The original mod had to hard-code the
/// star, and could not do the other two at all.
/// </summary>
public static class ArtChallenge
{
    /// <summary>True on any of the three.</summary>
    public static bool IsActive
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsArtChallenge(); }
            catch { return false; }
        }
    }

    private static Challenge Challenge()
    {
        try { return Lawn.BoardRef?.mChallenge; }
        catch { return null; }
    }

    /// <summary>Set when the last read of the pattern threw, so silence is not mistaken for none.</summary>
    private static bool _readFailed;

    /// <summary>Which plant the pattern wants on a square, or None where it wants nothing.</summary>
    public static SeedType Wanted(int x, int y)
    {
        Challenge challenge = Challenge();
        if (challenge == null) { _readFailed = true; return SeedType.None; }

        // A square outside the picture and a square the mod could not read come back as the
        // same value, and they mean opposite things: one is "nothing to do here" and the other
        // is "I do not know". The lawn's own square reader learned this lesson already.
        try { return challenge.GetArtChallengeSeed(x, y); }
        catch (Exception ex)
        {
            _readFailed = true;
            Core.Log.Warning($"[art] could not read the pattern at {x},{y}: {ex.Message}");
            return SeedType.None;
        }
    }

    /// <summary>Whether the right plant is already standing on a square.</summary>
    private static bool Satisfied(int x, int y, SeedType wanted)
    {
        try
        {
            Plant plant = Lawn.BoardRef?.GetTopPlantAt(x, y, PlantPriority.OnlyNormalPosition);
            return plant != null && plant.mSeedType == wanted;
        }
        catch { return false; }
    }

    /// <summary>One square of the pattern, and whether it is done.</summary>
    public readonly record struct Square(int X, int Y, SeedType Wanted, bool Done);

    /// <summary>
    /// True once the level's award has dropped.
    ///
    /// The game stops checking the picture at that moment, and so does the mod. A zombie
    /// eating one of the plants afterwards does not un-win the level, and reporting a square
    /// as missing would send the player back to replant during a wave for an award already
    /// banked.
    /// </summary>
    public static bool AlreadyWon
    {
        get
        {
            try { return Lawn.BoardRef?.HasLevelAwardDropped() ?? false; }
            catch { return false; }
        }
    }

    /// <summary>Every square the pattern asks for, in reading order.</summary>
    public static List<Square> Pattern()
    {
        _readFailed = false;

        var found = new List<Square>();
        if (Lawn.BoardRef == null) return found;

        int rows = Lawn.SafeRowCount();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < Lawn.Columns; x++)
            {
                SeedType wanted = Wanted(x, y);
                if (wanted == SeedType.None) continue;

                found.Add(new Square(x, y, wanted, Satisfied(x, y, wanted)));
            }
        }

        return found;
    }

    /// <summary>
    /// What this square wants, for the running commentary as the cursor walks the lawn.
    ///
    /// Null where the pattern wants nothing, which is most of the lawn - a square with no part
    /// in the picture should sound like an ordinary square, because that is what it is.
    /// </summary>
    public static string HintFor(int x, int y)
    {
        if (!IsActive) return null;

        SeedType wanted = Wanted(x, y);
        if (wanted == SeedType.None) return null;

        return Strings.T(Satisfied(x, y, wanted) ? "art.square_done" : "art.square_wants",
                         Lawn.PlantName(wanted));
    }

    /// <summary>How the picture stands, for the key that reports progress.</summary>
    public static string Describe()
    {
        if (!IsActive) return null;

        List<Square> pattern = Pattern();
        if (pattern.Count == 0) return _readFailed ? Strings.T("art.unreadable") : null;

        if (AlreadyWon) return Strings.T("art.won");

        int done = 0;
        foreach (Square square in pattern) if (square.Done) done++;

        return done >= pattern.Count
            ? Strings.T("art.complete", pattern.Count)
            : Strings.T("art.progress", done, pattern.Count);
    }

    /// <summary>
    /// Reads out the squares still to plant, grouped by plant and then by row.
    ///
    /// By plant first, because on the sunflower the answer to "what goes here" is three
    /// different things and a single name given for the whole board would send the player to
    /// plant star fruit on eleven squares that want something else. By row within that,
    /// because that is how a lawn is walked: you go to a row, and then you want the columns.
    /// </summary>
    public static bool AnnounceRemaining()
    {
        if (!IsActive) return false;

        List<Square> pattern = Pattern();

        if (pattern.Count == 0)
        {
            Speech.SayVerbatim(Strings.T(_readFailed ? "art.unreadable" : "art.no_pattern"), "art");
            return true;
        }

        if (AlreadyWon)
        {
            Speech.SayVerbatim(Strings.T("art.won"), "art");
            return true;
        }

        // Plants in the order they first appear in the picture, so the reading is stable from
        // one press to the next.
        var order = new List<SeedType>();
        var byPlant = new Dictionary<SeedType, SortedDictionary<int, List<int>>>();

        foreach (Square square in pattern)
        {
            if (square.Done) continue;

            if (!byPlant.TryGetValue(square.Wanted, out SortedDictionary<int, List<int>> rows))
            {
                byPlant[square.Wanted] = rows = new SortedDictionary<int, List<int>>();
                order.Add(square.Wanted);
            }

            if (!rows.TryGetValue(square.Y, out List<int> columns))
                rows[square.Y] = columns = new List<int>();

            columns.Add(square.X + 1);
        }

        if (order.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("art.complete", pattern.Count), "art");
            return true;
        }

        var lines = new List<string>();

        foreach (SeedType plant in order)
        {
            SortedDictionary<int, List<int>> rows = byPlant[plant];

            int count = 0;
            foreach (KeyValuePair<int, List<int>> row in rows) count += row.Value.Count;

            lines.Add(Strings.T(count == 1 ? "art.one_to_plant" : "art.still_to_plant",
                                count, Lawn.PlantName(plant)));

            foreach (KeyValuePair<int, List<int>> row in rows)
                lines.Add(Strings.T(row.Value.Count == 1 ? "art.row_needs_one" : "art.row_needs",
                                    row.Key + 1, string.Join(", ", row.Value)));
        }

        Speech.SayVerbatim(string.Join(". ", lines), "art");
        return true;
    }

    private static int CountLeft(List<Square> pattern)
    {
        int left = 0;
        foreach (Square square in pattern) if (!square.Done) left++;
        return left;
    }

    /// <summary>The pattern, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- art challenge ---");
        sb.AppendLine($"  art challenge : {IsActive}");

        if (!IsActive) { sb.AppendLine(); return; }

        List<Square> pattern = Pattern();
        sb.AppendLine($"  squares       : {pattern.Count}, {CountLeft(pattern)} still to plant");

        foreach (Square square in pattern)
            sb.AppendLine($"      row {square.Y + 1}, column {square.X + 1}:" +
                          $" {Lawn.PlantName(square.Wanted)} — {(square.Done ? "planted" : "missing")}");

        sb.AppendLine();
    }
}
