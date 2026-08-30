using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;
using UnityEngine;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Hearing where the zombies are.
///
/// This is the difference between operating the game and playing it. Planting is useless
/// without knowing what is coming down which row and how close it is — the thing a sighted
/// player reads off the screen continuously, without effort, and the one piece of
/// information the game offers no other way to get.
///
/// A scan answers it in about a second. Every zombie becomes a tone: how far right it
/// sounds is how far along the row it has walked, its pitch is which row it is in, and when
/// it sounds is how close it is — near ones first. A row under attack and a row that is
/// clear are told apart instantly, before a single word is spoken. The names follow for the
/// detail the tones cannot carry.
///
/// The design is lifted from the original PvZ accessibility mod, which got it right.
/// </summary>
public static class Sonar
{
    private const int Player = 0;

    /// <summary>Milliseconds of spread across the full width of the lawn.</summary>
    private const float SpreadMs = 1000f;

    /// <summary>
    /// How far across the lawn a position is, from 0 at the house to 1 at the far edge.
    /// Drives both the stereo balance and the delay, so a zombie sounds where it is and
    /// when it is due.
    /// </summary>
    private static float AcrossLawn(float posX)
    {
        if (!Lawn.TryLawnBounds(out float left, out float right)) return 0.5f;
        if (right <= left) return 0.5f;
        return Mathf.Clamp01((posX - left) / (right - left));
    }

    private const int ToneLengthMs = 70;

    /// <summary>Rows currently holding a zombie past the warning line, so it is announced once.</summary>
    private static readonly HashSet<int> AlertedRows = new();

    private static int _tripwireTicks;

    /// <summary>Frames between tripwire checks. Four times a second is plenty for walking zombies.</summary>
    private const int TripwireInterval = 15;

    /// <summary>
    /// Scans the row the cursor is in.
    ///
    /// The wording copies the original PvZ accessibility mod exactly, because a player who
    /// knows that mod should not have to learn a second dialect:
    ///
    ///     1. I: Normal,
    ///
    /// Count first, so you know the size of the problem before any detail. Columns as
    /// letters, because "I" is one syllable where "column nine" is three words and at five
    /// zombies that difference decides whether you finish listening in time. The row is
    /// never named — you walked to it, you know which one it is.
    /// </summary>
    public static void ScanCurrentRow()
    {
        if (!Lawn.IsOnBoard)
        {
            Speech.SayVerbatim(Strings.T("lawn.no_board"), "sonar");
            return;
        }

        if (!Lawn.TryGetPosition(out _, out int row)) return;

        List<ZombieInfo> found = Collect(row);
        if (found == null)
        {
            // Not "Not on a lawn": IsOnBoard was checked a few lines up and said yes. The
            // board is there and could not be read, and telling the player they are not on a
            // lawn contradicts the one thing they cannot check any other way.
            Speech.SayVerbatim(Strings.T("sonar.unreadable"), "sonar");
            return;
        }

        Tones.ClearPending();
        PlayTones(found);

        Speech.SayVerbatim(Compose(found, Boss.BallInRow(row)), "sonar");
        ReportSkipped("sonar");
    }

    /// <summary>
    /// Names the rows that have anything in them, and nothing else.
    ///
    /// This is the question that matters when deciding where to plant, and the per-row scan
    /// answers a different one. Reading out every zombie with its column and name is precise
    /// and useless for the purpose: the rows under threat have to be reassembled in your head
    /// from a list ordered by distance.
    /// </summary>
    public static void ScanRowsWithZombies()
    {
        if (!Lawn.IsOnBoard)
        {
            Speech.SayVerbatim(Strings.T("lawn.no_board"), "sonar");
            return;
        }

        List<ZombieInfo> all = Collect(null);
        if (all == null)
        {
            Speech.SayVerbatim(Strings.T("sonar.unreadable"), "sonar rows");
            return;
        }

        Tones.ClearPending();
        PlayTones(all);

        var rows = new SortedSet<int>();
        foreach (ZombieInfo info in all) rows.Add(info.Row + 1);

        // A ball crossing an otherwise empty row still makes that row a row you have to know
        // about, so it counts here exactly as a zombie would.
        int ballRow = Boss.BallRow(out _);
        if (ballRow >= 0) rows.Add(ballRow + 1);

        if (rows.Count == 0)
        {
            // An empty result after losing zombies is not a clear lawn. This branch returns
            // before ReportSkipped further down would ever run, so the note has to be made
            // here — and instead of "all clear", never beside it.
            if (LastSkipped > 0) { ReportSkipped("sonar rows"); return; }

            Speech.SayVerbatim(Strings.T("sonar.all_clear"), "sonar rows");
            return;
        }

        Speech.SayVerbatim(Strings.T("sonar.rows", string.Join(", ", rows)), "sonar rows");
        ReportSkipped("sonar rows");
    }

    /// <summary>
    /// Says so when the last read came back short.
    ///
    /// Spoken, not merely logged. A list that quietly lost an entry is the one failure this
    /// mod cannot afford, because it is indistinguishable from a correct answer — the player
    /// acts on it exactly as confidently either way.
    /// </summary>
    private static void ReportSkipped(string context)
    {
        if (LastSkipped <= 0) return;
        Speech.SayVerbatim(Strings.T("sonar.incomplete", LastSkipped), context);
    }

    /// <summary>
    /// What is standing on one square, or null when nothing is.
    ///
    /// This is what lets the cursor find zombies as well as plants — the same cursor you
    /// plant with, so an instant such as a cherry bomb can be aimed by walking onto the
    /// thing you want to hit rather than by working out coordinates from a row scan.
    /// </summary>
    public static string DescribeZombiesAt(int row, int column)
    {
        List<ZombieInfo> found = Collect(row);
        if (found == null) return null;

        List<ZombieInfo> here = new();
        foreach (ZombieInfo info in found)
            if (info.Column == column) here.Add(info);

        if (here.Count == 0) return null;
        if (here.Count == 1) return ShortName(here[0]);

        var names = new List<string>(here.Count);
        foreach (ZombieInfo info in here) names.Add(ShortName(info));
        return string.Join(", ", names);
    }

    /// <summary>
    /// Where a zombie standing on one square actually is, in board pixels.
    ///
    /// A square is a large thing and a zombie is not centred in it — in the mallet mini-game
    /// they climb out of graves and stand wherever they stand. Swinging at the middle of the
    /// square is aiming at the floor. This gives the thing itself to aim at.
    /// </summary>
    public static bool TryZombieAt(int row, int column, out float x, out float y, out string name)
    {
        x = 0f;
        y = 0f;
        name = null;

        List<ZombieInfo> found = Collect(row);
        if (found == null) return false;

        bool any = false;
        float bestX = 0f;

        foreach (ZombieInfo info in found)
        {
            if (info.Column != column) continue;

            // The one furthest along is the one about to do damage, so it is the one worth
            // hitting when two share a square.
            if (any && info.PosX >= bestX) continue;

            bestX = info.PosX;
            x = info.PosX;
            y = info.PosY;
            name = ShortName(info);
            any = true;
        }

        return any;
    }

    /// <summary>
    /// The zombie closest to a square, described as a place to walk to, or null when the
    /// board holds none.
    ///
    /// For the mallet mini-game, where the targets appear and duck away again: a swing that
    /// hits nothing is only useful if it also says where something is.
    /// </summary>
    public static string NearestZombieFrom(int row, int column)
    {
        List<ZombieInfo> all = Collect(null);
        if (all == null || all.Count == 0) return null;

        ZombieInfo best = default;
        int bestDistance = int.MaxValue;
        bool any = false;

        foreach (ZombieInfo info in all)
        {
            if (info.Column < 0) continue;

            // Rows count for more than columns: walking up and down is the slower move and
            // the one worth being told about.
            int distance = Math.Abs(info.Row - row) * 3 + Math.Abs(info.Column - column);
            if (any && distance >= bestDistance) continue;

            best = info;
            bestDistance = distance;
            any = true;
        }

        if (!any) return null;

        return Strings.T("sonar.nearest", ShortName(best), best.Row + 1, best.Column + 1);
    }

    /// <summary>Builds the terse line: count, then each zombie, column letter only when it changes.</summary>
    private static string Compose(List<ZombieInfo> found) => Compose(found, null);

    /// <summary>
    /// The row read aloud, with anything else crossing it.
    ///
    /// Dr Zomboss's fireball and iceball are not zombies and are not projectiles either, so
    /// the sonar walked straight past them - on the one level where what is coming at you is
    /// mostly not a zombie. They are named first, before the count, because a ball crossing
    /// your row outranks anything walking down it.
    /// </summary>
    private static string Compose(List<ZombieInfo> found, string crossing)
    {
        if (found.Count == 0)
            return crossing == null ? Strings.T("sonar.none") : crossing;

        if (crossing != null) return crossing + ". " + Compose(found, null);

        // Nearest the house first: that is the order they matter in.
        found.Sort((a, b) => a.PosX.CompareTo(b.PosX));

        var sb = new System.Text.StringBuilder();
        sb.Append(found.Count).Append(". ");

        int previousColumn = int.MinValue;
        foreach (ZombieInfo info in found)
        {
            if (info.Column != previousColumn)
            {
                previousColumn = info.Column;
                sb.Append(ColumnLetter(info.Column)).Append(": ");
            }

            sb.Append(ShortName(info)).Append(", ");
        }

        return sb.ToString();
    }

    /// <summary>Columns are spoken as A to I, left to right. Anything not yet on the lawn is "off".</summary>
    private static string ColumnLetter(int column)
    {
        if (column < 0 || column >= Lawn.Columns) return Strings.T("sonar.off_lawn");
        return ((char)('A' + column)).ToString();
    }

    /// <summary>
    /// The name as it belongs in a list: "Cone-head" rather than "Cone-head zombie". In a
    /// row of five the repeated word is a third of the sentence and carries nothing.
    /// </summary>
    private static string ShortName(ZombieInfo info)
    {
        string name = ZombieName(info.Type);

        const string suffix = " zombie";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^suffix.Length];

        // Said before the other modifiers so it attaches to the name itself. A Bucket-head
        // whose bucket has been shot off was, until now, still announced "Bucket-head" —
        // so shots went on being spent on armour that was no longer there.
        if (info.Armour == Armour.Gone) name = Strings.T("sonar.armour_gone", name);
        else if (info.Armour == Armour.Damaged) name = Strings.T("sonar.armour_damaged", name);
        else if (info.Armour == Armour.Dinted) name = Strings.T("sonar.armour_dinted", name);

        if (info.Hypnotised) return Strings.T("sonar.hypnotised", name);
        if (info.Frozen) return Strings.T("sonar.frozen", name);
        if (info.Headless) return Strings.T("sonar.headless", name);
        return name;
    }

    private static void PlayTones(List<ZombieInfo> found)
    {
        float volume = Settings.SonarVolume.Value;
        if (volume <= 0f) return;

        foreach (ZombieInfo info in found)
        {
            float across = AcrossLawn(info.PosX);
            Tones.PlayDelayed(Lawn.ToneForRow(info.Row), across, ToneLengthMs, volume, (int)(across * SpreadMs));
        }
    }

    /// <summary>
    /// Watches for a zombie getting dangerously far up a row, and says so once per row.
    ///
    /// Without this the player has to keep scanning to notice a breakthrough. The warning
    /// clears itself when the row is pushed back, so a row that is repeatedly overrun keeps
    /// warning rather than falling silent after the first time.
    /// </summary>
    public static void TickTripwire()
    {
        if (!Settings.SayTripwire.Value) return;
        if (!Lawn.HasInput) return;
        if (++_tripwireTicks < TripwireInterval) return;
        _tripwireTicks = 0;

        int line = Settings.TripwireColumn.Value;
        List<ZombieInfo> all = Collect(null);

        // A failed read must not clear the rows already flagged: doing so would let the
        // tripwire fire again for zombies that never left, and worse, an unreadable board
        // would silently look like a board with nothing across the line.
        if (all == null) return;

        var breached = new HashSet<int>();
        foreach (ZombieInfo info in all)
            if (info.Column >= 0 && info.Column + 1 <= line) breached.Add(info.Row);   // setting counts from 1

        foreach (int row in breached)
        {
            if (AlertedRows.Contains(row)) continue;
            AlertedRows.Add(row);
            Speech.Say(Strings.T("sonar.tripwire", row + 1), interrupt: false, context: "tripwire");
        }

        AlertedRows.RemoveWhere(row => !breached.Contains(row));
    }

    public static void Reset()
    {
        AlertedRows.Clear();
        _tripwireTicks = 0;
        Tones.ClearPending();
    }

    /// <summary>
    /// Whether a zombie has actually joined the level, as opposed to waiting in the wings.
    ///
    /// The game's own answer is used rather than a distance threshold, because it knows
    /// about the cases a threshold would get wrong — a bungee dropping in, a digger
    /// surfacing. If it refuses to answer we count the zombie: a spurious one in the list
    /// is a nuisance, a missed one is a lost level.
    /// </summary>
    private static bool InPlay(Zombie zombie)
    {
        try { return zombie.IsOnBoard(); }
        catch { return true; }
    }

    /// <summary>What is left of a zombie's helmet or shield. Later values are worse off.</summary>
    private enum Armour { None, Intact, Dinted, Damaged, Gone }

    private readonly record struct ZombieInfo(
        ZombieType Type, int Row, int Column, float PosX, float PosY,
        bool Hypnotised, bool Frozen, bool Headless, Armour Armour);

    /// <summary>
    /// How battered a piece of armour is.
    ///
    /// Judged by health against maximum rather than by the helmet type, because only the
    /// maximum separates "never had one" from "had one and it has been shot off". The
    /// thresholds and the words are the original PvZ accessibility mod's, so they mean the
    /// same thing they have always meant to someone coming from it.
    /// </summary>
    private static Armour Rate(int health, int max)
    {
        if (max <= 0) return Armour.None;
        if (health <= 0) return Armour.Gone;
        if (health < max / 3f) return Armour.Damaged;
        if (health < max / 1.5f) return Armour.Dinted;
        return Armour.Intact;
    }

    /// <summary>A zombie wearing two things reports the one in worse shape. None never wins.</summary>
    private static Armour Worse(Armour a, Armour b)
        => a == Armour.None ? b : b == Armour.None ? a : (a > b ? a : b);

    /// <summary>
    /// Gathers the zombies actually standing on the lawn.
    ///
    /// Rows are worked out by asking the board to convert the zombie's position into a grid
    /// square, rather than read off the zombie — it has no field for its current row, and
    /// the board's conversion already accounts for pool lanes and roof slopes.
    /// </summary>
    private static List<ZombieInfo> Collect(int? onlyRow)
    {
        var result = new List<ZombieInfo>();
        LastSkipped = 0;
        LastCollectFailed = false;

        Board board = Lawn.BoardRef;
        if (board == null) { LastCollectFailed = true; return null; }

        try
        {
            var zombies = board.m_zombies;
            if (zombies == null) { LastCollectFailed = true; return null; }

            bool verbose = Settings.VerboseLogging.Value;

            // Hoisted, because the game's pooled array can be handed back to while we read it.
            int count = zombies.Count;

            for (int i = 0; i < count; i++)
            {
                try
                {
                Zombie zombie = zombies[i];
                if (zombie == null) continue;
                if (zombie.mDead || zombie.IsDeadOrDying()) continue;

                // The game builds each wave in advance and parks it far off to the right,
                // well beyond the lawn. Those must not be counted — they put a zombie in
                // every row of a level that had only just started. A zombie that has spawned
                // and is walking in is a different matter entirely: it is reported, and
                // labelled "Off-Board" until it reaches the first column.
                if (!InPlay(zombie)) continue;

                float posX = zombie.mPosX;
                float posY = zombie.mPosY;

                // A balloon zombie is drawn above the lawn, and its position says so, which
                // put it a whole row too high in every scan. Taking the altitude back off
                // gives the row it is actually over — which is the row you have to defend.
                float altitude = 0f;
                try { altitude = zombie.mAltitude; } catch { }
                if (altitude > 0f) posY += altitude;

                int row = Lawn.RowAt(posX, posY);
                int column = Lawn.ColumnAt(posX, posY);

                // A zombie eating a plant stands with its body past the square it is biting,
                // so measuring by its own position reported it one column behind the plant —
                // "the thing eating your Wall-nut is one square further along" is exactly
                // backwards. While it is eating, the game's own target column is the answer.
                try
                {
                    if (zombie.mIsEating)
                    {
                        int target = zombie.mTargetCol;
                        if (target >= 0 && target < Lawn.Columns) column = target;
                    }
                }
                catch { /* keep the measured column */ }

                if (verbose)
                {
                    // Altitude and the eating flag are printed because the two corrections
                    // above depend on them, and because the sign of an altitude is the kind
                    // of thing that is fifty-fifty until something measures it.
                    string eating = "?";
                    string targetCol = "?";
                    try { eating = zombie.mIsEating.ToString(); } catch { }
                    try { targetCol = zombie.mTargetCol.ToString(); } catch { }

                    Core.Log.Msg($"[sonar] {zombie.mZombieType} at x={posX:F0} y={zombie.mPosY:F0}" +
                                 $" altitude={altitude:F0} -> row {row + 1}," +
                                 $" column {(column < 0 ? "off board" : (column + 1).ToString())}" +
                                 $" (eating={eating}, targetCol={targetCol}), inPlay={InPlay(zombie)}");
                }

                if (onlyRow.HasValue && row != onlyRow.Value) continue;

                result.Add(new ZombieInfo(
                    zombie.mZombieType,
                    row,
                    column,
                    posX,
                    posY,
                    zombie.mMindControlled,
                    zombie.mIceTrapCounter > 0,
                    !zombie.mHasHead,
                    Worse(Rate(zombie.mHelmHealth, zombie.mHelmMaxHealth),
                          Rate(zombie.mShieldHealth, zombie.mShieldMaxHealth))));
                }
                catch (Exception ex)
                {
                    // One zombie collected out of the pool between the wrapper and the read
                    // must not cost the rest of the list. Counted rather than swallowed:
                    // a short answer nobody mentions is the failure this mod can least
                    // afford, because it sounds exactly like a correct one.
                    LastSkipped++;
                    if (LastSkipped == 1 || Settings.VerboseLogging.Value)
                        Core.Log.Warning($"Skipped zombie {i}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // The board itself could not be read. Null, not an empty list: an empty list is
            // indistinguishable from a genuinely clear lawn, and answering "All clear" for a
            // board that failed is a confident lie about the one thing the player asked.
            Core.Log.Warning($"Could not read the zombies: {ex.Message}");
            LastCollectFailed = true;
            return null;
        }

        return result;
    }

    /// <summary>Zombies dropped by the last Collect. Non-zero means the answer was short.</summary>
    internal static int LastSkipped;

    /// <summary>
    /// True when the last Collect could not read the board at all.
    ///
    /// Separate from LastSkipped, which counts zombies lost one at a time. Both mean the
    /// answer is not to be trusted, and a caller that has no other way to know must be able
    /// to ask — otherwise an unreadable board comes out as an empty one, which sounds like a
    /// perfectly good answer.
    /// </summary>
    internal static bool LastCollectFailed;


    public static string ZombieName(ZombieType type)
    {
        string key = "zombie." + type;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(type.ToString());
    }

}
