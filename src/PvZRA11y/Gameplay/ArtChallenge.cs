using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The three picture puzzles: Seeing Stars, Art Challenge Wall-nut and Art Challenge Sunflower.
///
/// One mini-game wearing three coats. The lawn is marked out with a faint pattern - a star, a
/// wall-nut, a sunflower - and the level is won by planting the right plant on every marked
/// square while the zombies come. Which plant is never in doubt: each puzzle uses exactly one.
/// Where is the entire puzzle, and where is the part drawn on the ground in a colour, which is
/// to say the part that does not exist for a player who cannot see it.
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

    /// <summary>Which plant the pattern wants on a square, or None where it wants nothing.</summary>
    public static SeedType Wanted(int x, int y)
    {
        try { return Challenge()?.GetArtChallengeSeed(x, y) ?? SeedType.None; }
        catch { return SeedType.None; }
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

    /// <summary>Every square the pattern asks for, in reading order.</summary>
    public static List<Square> Pattern()
    {
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
        if (pattern.Count == 0) return null;

        int done = 0;
        foreach (Square square in pattern) if (square.Done) done++;

        return done >= pattern.Count
            ? Strings.T("art.complete", pattern.Count)
            : Strings.T("art.progress", done, pattern.Count);
    }

    /// <summary>
    /// Reads out the squares still to plant, a row at a time.
    ///
    /// By row, because that is how the lawn is walked. A flat list of twenty positions in
    /// reading order is the same information and a great deal harder to act on: you go to a
    /// row, and then you want to know which columns in it are still waiting.
    /// </summary>
    public static bool AnnounceRemaining()
    {
        if (!IsActive) return false;

        List<Square> pattern = Pattern();
        if (pattern.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("art.no_pattern"), "art");
            return true;
        }

        var byRow = new SortedDictionary<int, List<int>>();
        SeedType plant = SeedType.None;

        foreach (Square square in pattern)
        {
            if (square.Done) continue;

            plant = square.Wanted;

            if (!byRow.TryGetValue(square.Y, out List<int> columns))
                byRow[square.Y] = columns = new List<int>();

            columns.Add(square.X + 1);
        }

        if (byRow.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("art.complete", pattern.Count), "art");
            return true;
        }

        var lines = new List<string>(byRow.Count + 1)
        {
            Strings.T("art.still_to_plant", CountLeft(pattern), Lawn.PlantName(plant)),
        };

        foreach (KeyValuePair<int, List<int>> row in byRow)
            lines.Add(Strings.T("art.row_needs", row.Key + 1, string.Join(", ", row.Value)));

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
