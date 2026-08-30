using System.Text;
using Il2CppReloaded.Gameplay;
using Il2CppTekly.PanelViews;
using PvZRA11y.A11y;
using PvZRA11y.Gameplay;
using PvZRA11y.UI;

namespace PvZRA11y.Diagnostics;

/// <summary>
/// Asks the game everything, in one go, and checks the answers.
///
/// This exists because of a pattern in how this mod's bugs happened. Nearly every one came
/// from assuming something about the game rather than measuring it: that the board was 800
/// units wide when it is over 1400, that a method called GetPosYBasedOnRow would give a
/// row, that ShowPlayButton meant a level had been chosen. Each wrong guess cost a full
/// round trip — build, hand it over, play, read the log, fix.
///
/// The expensive part of that loop is not writing the code. It is that a person has to
/// launch the game and play it before anything can be learned. So this gathers, in a single
/// launch, what would otherwise be discovered one question at a time.
///
/// The important part is not the numbers it prints but the checks it makes. A dump of
/// coordinates has to be read carefully and can be misread; a line saying a conversion
/// fails on six squares out of forty-five cannot be.
/// </summary>
public static class SelfTest
{
    private static readonly List<string> Failures = new();

    /// <summary>Runs the whole battery and writes it to the MelonLoader log.</summary>
    public static void Run(string reason)
    {
        Failures.Clear();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("################ PvZRA11y self-test ################");
        sb.AppendLine($"reason: {reason}");
        sb.AppendLine("note  : this is one moment in time, not a running state.");
        sb.AppendLine();

        Section(sb, "speech", CheckSpeech);
        Section(sb, "audio", CheckAudio);
        LawnSection(sb, "board", CheckBoard);
        LawnSection(sb, "geometry", CheckGeometry);
        LawnSection(sb, "cursor", CheckCursor);
        LawnSection(sb, "seed bank", CheckSeedBank);
        LawnSection(sb, "zombies", CheckZombies);
        Section(sb, "plant chooser", Gameplay.SeedChooser.Dump);
        Section(sb, "zen garden", Gameplay.Garden.Dump);
        Section(sb, "challenge pages", UI.Challenges.Dump);
        Section(sb, "boss", Gameplay.Boss.Dump);
        Section(sb, "brains", Gameplay.Brains.Dump);
        Section(sb, "achievements", UI.Achievements.Dump);
        Section(sb, "notes", Gameplay.Notes.Check);
        Section(sb, "panels", CheckPanels);
        Section(sb, "pause", CheckPause);

        sb.AppendLine("---- verdict ----");
        if (Failures.Count == 0)
        {
            sb.AppendLine("  everything checked out");
        }
        else
        {
            sb.AppendLine($"  {Failures.Count} problem(s):");
            foreach (string failure in Failures) sb.AppendLine($"    FAIL  {failure}");
        }
        sb.AppendLine("###################################################");

        Core.Log.Msg(sb.ToString());
    }

    /// <summary>
    /// Runs one section. A section that throws is reported and the rest still run — a
    /// diagnostic that stops at the first problem tells you about one thing per launch,
    /// which is the situation this is meant to end.
    /// </summary>
    private static void Section(StringBuilder sb, string title, Action<StringBuilder> body)
    {
        sb.AppendLine($"---- {title} ----");
        try { body(sb); }
        catch (Exception ex)
        {
            sb.AppendLine($"  section threw: {ex.Message}");
            Fail($"{title} section threw: {ex.Message}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// A section that only means anything on a lawn.
    ///
    /// Run in a menu, these used to report the board, the cursor and the seed bank as
    /// failures — three alarming lines about things that are simply not there yet. This is
    /// the tool someone reaches for when they are already stuck, and sending them hunting a
    /// problem that does not exist is worse than saying nothing.
    /// </summary>
    private static void LawnSection(StringBuilder sb, string title, Action<StringBuilder> body)
    {
        if (!Lawn.IsOnBoard)
        {
            sb.AppendLine($"---- {title} ----");
            sb.AppendLine("  not on a lawn, so there is nothing here to check");
            sb.AppendLine();
            return;
        }

        Section(sb, title, body);
    }

    private static void Fail(string what) => Failures.Add(what);

    private static void Check(StringBuilder sb, bool ok, string label, string detail)
    {
        sb.AppendLine($"  [{(ok ? "ok" : "!!")}] {label}: {detail}");
        if (!ok) Fail($"{label} - {detail}");
    }

    private static void CheckSpeech(StringBuilder sb)
    {
        Check(sb, Speech.Ready, "screen reader bridge",
            Speech.Ready ? $"ready, reader = {Speech.DetectedReader ?? "none detected"}" : "not initialised");
    }

    private static void CheckAudio(StringBuilder sb)
    {
        Check(sb, Tones.Ready, "tone generator", Tones.Ready ? "ready" : "no audio host");
    }

    private static void CheckBoard(StringBuilder sb)
    {
        Board board = Lawn.BoardRef;
        Check(sb, board != null, "board", board == null ? "absent" : "present");
        if (board == null) return;

        sb.AppendLine($"  rows            : {board.GetNumRows()}");
        sb.AppendLine($"  night           : {board.StageIsNight()}");
        sb.AppendLine($"  pool            : {board.StageHasPool()}");
        sb.AppendLine($"  roof            : {board.StageHasRoof()}");
        sb.AppendLine($"  fog             : {board.StageHasFog()}");
        sb.AppendLine($"  gravestones     : {board.StageHasGraveStones()}");
        sb.AppendLine($"  vase breaker    : {Gameplay.Lawn.IsVaseBreakerLevel}");
        sb.AppendLine($"  dave talking    : {Gameplay.Lawn.ChallengeDaveTalking()},"
                      + $" message {Gameplay.Lawn.DaveMessageIndex()}");
        sb.AppendLine($"  whack a zombie  : {Gameplay.Lawn.IsWhackAZombieLevel}");

        // What is lying on the lawn, because a plant out of a vase is a thing with no sound
        // of its own and a limited life. If this list is empty on a Vase Breaker level right
        // after a plant vase was opened, the coin model behind the pickup keys is wrong.
        var lying = Gameplay.Lawn.Pickups();
        sb.AppendLine($"  lying on lawn   : {lying.Count}");
        foreach (Gameplay.Lawn.Pickup p in lying)
            sb.AppendLine($"      [{p.Kind}] {p.Label} at row {p.Row + 1}, column {p.Column + 1}"
                          + $" (pixel {p.X:0},{p.Y:0})");
        sb.AppendLine($"  sun             : {Lawn.SunCount()}");

        int rows = Lawn.SafeRowCount();
        Check(sb, rows is >= 5 and <= 6, "row count", $"{rows}, expected 5 or 6");
    }

    /// <summary>
    /// The check that matters most: does a grid square convert to a pixel and back to the
    /// same square? Planting and digging both depend on it, and a failure here is silent —
    /// the plant simply lands somewhere else, or nowhere at all.
    /// </summary>
    private static void CheckGeometry(StringBuilder sb)
    {
        Board board = Lawn.BoardRef;
        if (board == null) { sb.AppendLine("  no board"); return; }

        bool measured = Lawn.TryLawnBounds(out float left, out float right);
        Check(sb, measured && right > left, "lawn bounds",
            measured ? $"x from {left:F0} to {right:F0}, width {right - left:F0}" : "could not measure");

        int rows = Lawn.SafeRowCount();
        int roundTripped = 0;
        int total = 0;
        var offsets = new Dictionary<string, int>();
        var broken = new List<string>();

        int[] dxs = { 0, 40, -40 };
        int[] dys = { 0, 50, -50 };

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < Lawn.Columns; x++)
            {
                total++;
                int baseX = board.GridToPixelX(x, y);
                int baseY = board.GridToPixelY(x, y);

                string worked = null;
                foreach (int dx in dxs)
                {
                    foreach (int dy in dys)
                    {
                        if (board.PixelToGridX(baseX + dx, baseY + dy) != x) continue;
                        if (board.PixelToGridY(baseX + dx, baseY + dy) != y) continue;
                        worked = $"{dx},{dy}";
                        break;
                    }
                    if (worked != null) break;
                }

                if (worked == null)
                {
                    broken.Add($"r{y + 1}c{x + 1} at pixel {baseX},{baseY}");
                    continue;
                }

                roundTripped++;
                offsets[worked] = offsets.TryGetValue(worked, out int n) ? n + 1 : 1;
            }
        }

        Check(sb, roundTripped == total, "grid to pixel and back", $"{roundTripped} of {total} squares");

        foreach (KeyValuePair<string, int> pair in offsets)
            sb.AppendLine($"      offset {pair.Key,-8} works for {pair.Value} squares");

        int listed = 0;
        foreach (string bad in broken)
        {
            if (listed++ >= 8) break;
            sb.AppendLine($"      no offset works: {bad}");
        }

        // The same question the sonar asks of every zombie, asked where the answer is known.
        int rowsCorrect = 0;
        int colsCorrect = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < Lawn.Columns; x++)
            {
                int px = board.GridToPixelX(x, y);
                int py = board.GridToPixelY(x, y);
                if (Lawn.RowAt(px, py) == y) rowsCorrect++;
                if (Lawn.ColumnAt(px, py) == x) colsCorrect++;
            }
        }

        // Worth stating what this does and does not prove. It feeds the board's own
        // coordinates back in, so it only shows the conversion inverts itself. It passed at
        // 45 of 45 while every zombie was being reported a row too high, because a zombie's
        // position is not one of these coordinates — it is the top of a sprite two squares
        // tall. The check against live zombies further down is the one that catches that.
        Check(sb, rowsCorrect == total, "row from a board coordinate", $"{rowsCorrect} of {total}");
        Check(sb, colsCorrect == total, "column from a board coordinate", $"{colsCorrect} of {total}");

        sb.AppendLine("  row heights     :");
        for (int y = 0; y < rows; y++)
            sb.AppendLine($"      row {y + 1} at y={board.GridToPixelY(0, y)}");
    }

    private static void CheckCursor(StringBuilder sb)
    {
        bool has = Lawn.TryGetPosition(out int x, out int y);
        Check(sb, has, "grid cursor", has ? $"at row {y + 1}, column {x + 1}" : "unreachable");
    }

    private static void CheckSeedBank(StringBuilder sb)
    {
        int slots = Seeds.SlotCount();
        Check(sb, slots > 0, "seed bank", $"{slots} slots");

        for (int i = 0; i < slots; i++)
            sb.AppendLine($"      slot {i + 1}: {Seeds.Describe(i) ?? "<unreadable>"}");

        sb.AppendLine($"  slot in hand    : {Seeds.SelectedIndex()}");
    }

    private static void CheckZombies(StringBuilder sb)
    {
        Board board = Lawn.BoardRef;
        if (board == null) { sb.AppendLine("  no board"); return; }

        var zombies = board.m_zombies;
        int count = zombies == null ? 0 : zombies.Count;
        sb.AppendLine($"  entries in list : {count}");
        sb.AppendLine("  note            : the game builds each wave in advance and parks it");
        sb.AppendLine("                    off the right-hand side, so a level that has only");
        sb.AppendLine("                    just started shows a full list and none in play.");
        sb.AppendLine("                    Only the last line below describes the lawn.");

        int inPlay = 0;
        int ambiguous = 0;
        for (int i = 0; i < count; i++)
        {
            Zombie z = zombies[i];
            if (z == null) continue;

            bool onBoard;
            try { onBoard = z.IsOnBoard(); } catch { onBoard = true; }
            bool dying = z.mDead || z.IsDeadOrDying();
            if (onBoard && !dying) inPlay++;

            int row = Lawn.RowAt(z.mPosX, z.mPosY);
            sb.AppendLine($"      {z.mZombieType,-16} x={z.mPosX,7:F0} y={z.mPosY,6:F0}" +
                          $" -> row {row + 1}, column {Lawn.ColumnAt(z.mPosX, z.mPosY) + 1}" +
                          $"  onBoard={onBoard} dying={dying}{RowMargin(board, z, row)}");

            if (onBoard && !dying && IsRowAmbiguous(board, z, row)) ambiguous++;
        }

        sb.AppendLine($"  actually in play: {inPlay}");

        // The check that matters, and the one that was missing: a zombie should sit clearly
        // closer to its own row than to the next. When it does not, the row it is reported
        // in is a coin toss — and a scan that names the wrong row sends the player planting
        // against a threat that is somewhere else.
        Check(sb, ambiguous == 0, "zombies clearly inside their row",
            ambiguous == 0 ? "all of them" : $"{ambiguous} sitting between two rows");
    }

    /// <summary>
    /// Where a row sits at a given horizontal position, the way the game itself works it out.
    ///
    /// Measuring at column zero is only right on a flat lawn. A roof slopes, so the same row
    /// is up to a hundred pixels higher at the house end than at the far end, and a check
    /// that ignores that reports every zombie on the sloped half as a row out when it is not,
    /// or agrees with a reading that is.
    /// </summary>
    private static float RowHeight(Board board, float x, int row)
    {
        try { return board.GetPosYBasedOnRow(x, row); }
        catch { return board.GridToPixelY(0, row); }
    }

    /// <summary>How far a zombie is from its row, and from the next nearest, for the dump.</summary>
    private static string RowMargin(Board board, Zombie zombie, int row)
    {
        try
        {
            float own = Math.Abs(RowHeight(board, zombie.mPosX, row) - zombie.mPosY);
            float next = NextNearestDistance(board, zombie, row);
            return $"  offBy={own:F0} vs {next:F0}";
        }
        catch { return string.Empty; }
    }

    private static bool IsRowAmbiguous(Board board, Zombie zombie, int row)
    {
        try
        {
            float own = Math.Abs(RowHeight(board, zombie.mPosX, row) - zombie.mPosY);
            float next = NextNearestDistance(board, zombie, row);
            // Comfortably nearer its own row means less than half the gap to the next one.
            return next <= 0f || own > next * 0.5f;
        }
        catch { return false; }
    }

    private static float NextNearestDistance(Board board, Zombie zombie, int row)
    {
        int rows = Lawn.SafeRowCount();
        float best = float.MaxValue;
        for (int other = 0; other < rows; other++)
        {
            if (other == row) continue;
            float distance = Math.Abs(RowHeight(board, zombie.mPosX, other) - zombie.mPosY);
            if (distance < best) best = distance;
        }
        return best == float.MaxValue ? 0f : best;
    }

    private static void CheckPanels(StringBuilder sb)
    {
        sb.AppendLine($"  front panel     : {PanelScope.FrontPanelId ?? "none"}");

        foreach (PanelView panel in PanelScope.ShownPanels())
            sb.AppendLine($"      shown: {PanelScope.SafeId(panel) ?? "<no id>"}");

        sb.AppendLine($"  reachable ctrls : {Focus.CollectVisible().Count}");
    }

    private static void CheckPause(StringBuilder sb)
    {
        Board board = Lawn.BoardRef;
        if (board == null) { sb.AppendLine("  no board"); return; }

        sb.AppendLine($"  board paused    : {board.mPaused}");
        sb.AppendLine($"  level complete  : {board.mLevelComplete}");
        sb.AppendLine($"  frozen by us    : {Lawn.Frozen}");
        sb.AppendLine($"  lawn has input  : {Lawn.HasInput}");

        bool activity = false;
        try { activity = board.mApp != null; } catch { /* reported by the check below */ }

        // Freezing goes through the activity. Without it the clock cannot be stopped at all.
        Check(sb, activity, "gameplay activity", activity ? "reachable" : "not reachable");
    }
}
