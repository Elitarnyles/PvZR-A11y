using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Zombiquarium: the one where you keep the zombies alive instead of killing them.
///
/// A tank of snorkel zombies. They give off sun, you spend that sun on more of them, and a
/// thousand sun buys the trophy that wins it. The catch is that they starve, and a brain costs
/// five sun - so every brain is sun not spent on a zombie, and every zombie is another mouth.
/// Let them all die and the level is lost.
///
/// Everything else about this mini-game already goes through machinery the mod has: buying from
/// the seed bank, collecting the sun, hearing the zombies. Feeding did not, because feeding is
/// not an action on anything - it is a click on open water, and open water is not a control, not
/// a plant, and not a square the mod had any reason to let you act on.
///
/// The numbers are the game's own: five sun a brain, three brains in the water at once, a
/// thousand to win.
/// </summary>
public static class Zombiquarium
{
    /// <summary>What one brain costs.</summary>
    public const int BrainCost = 5;

    /// <summary>How many brains the water will hold at once.</summary>
    public const int MaxBrains = 3;

    /// <summary>The sun that buys the trophy.</summary>
    public const int Target = 1000;

    /// <summary>The water, in board pixels. Outside it a click does nothing at all.</summary>
    private const int TankLeft = 80;
    private const int TankRight = 720;
    private const int TankTop = 90;
    private const int TankBottom = 430;

    /// <summary>True on the Zombiquarium level.</summary>
    public static bool IsActive
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsZombiquariumLevel(); }
            catch { return false; }
        }
    }

    /// <summary>True when the tank is also what the keyboard belongs to.</summary>
    public static bool Playable => IsActive && Lawn.HasInput;

    private static Challenge Challenge()
    {
        try { return Lawn.BoardRef?.mChallenge; }
        catch { return null; }
    }

    /// <summary>How many brains are floating, or -1 when the board will not say.</summary>
    public static int BrainsInWater()
    {
        Board board = Lawn.BoardRef;
        if (board == null) return -1;

        int count = 0;

        // Brains here are not on squares - they are dropped wherever the click landed - so
        // they are counted by walking the board's own list rather than by asking a square.
        try
        {
            var items = board.m_gridItems;
            if (items == null) return -1;

            int total = items.Count;
            for (int i = 0; i < total; i++)
            {
                GridItem item = items[i];
                if (item == null) continue;

                try
                {
                    if (item.mDead) continue;
                    if (item.mGridItemType == GridItemType.Brain) count++;
                }
                catch { /* one bad entry must not cost the count */ }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[tank] could not count the brains: {ex.Message}");
            return -1;
        }

        return count;
    }

    /// <summary>How many zombies are still swimming.</summary>
    public static int Swimmers()
    {
        Board board = Lawn.BoardRef;
        if (board == null) return -1;

        try
        {
            var zombies = board.m_zombies;
            if (zombies == null) return -1;

            int count = 0;
            int total = zombies.Count;

            for (int i = 0; i < total; i++)
            {
                try
                {
                    Zombie zombie = zombies[i];
                    if (zombie != null && !zombie.mDead) count++;
                }
                catch { }
            }

            return count;
        }
        catch { return -1; }
    }

    /// <summary>One swimmer: where it is, and how to say where that is.</summary>
    public readonly record struct Swimmer(int Index, float X, float Y);

    /// <summary>The zombies in the tank, left to right.</summary>
    public static List<Swimmer> Zombies()
    {
        var found = new List<Swimmer>();

        Board board = Lawn.BoardRef;
        if (board == null) return found;

        try
        {
            var zombies = board.m_zombies;
            if (zombies == null) return found;

            int total = zombies.Count;
            for (int i = 0; i < total; i++)
            {
                try
                {
                    Zombie zombie = zombies[i];
                    if (zombie == null || zombie.mDead) continue;

                    found.Add(new Swimmer(found.Count, zombie.mPosX, zombie.mPosY));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[tank] could not find the zombies: {ex.Message}");
        }

        found.Sort((a, b) => a.X.CompareTo(b.X));
        return found;
    }

    /// <summary>Where something is in the tank, in words rather than in pixels.</summary>
    public static string Where(float x, float y)
    {
        string across = x < TankLeft + (TankRight - TankLeft) / 3f ? Strings.T("tank.left")
                      : x > TankRight - (TankRight - TankLeft) / 3f ? Strings.T("tank.right")
                      : Strings.T("tank.centre");

        string down = y < TankTop + (TankBottom - TankTop) / 3f ? Strings.T("tank.high")
                    : y > TankBottom - (TankBottom - TankTop) / 3f ? Strings.T("tank.low")
                    : Strings.T("tank.middle");

        return Strings.T("tank.where", across, down);
    }

    private static int _chosen;

    /// <summary>
    /// Steps through the zombies, saying where each one is swimming.
    ///
    /// The zombies are what a brain is aimed at, so they are what there is to choose between.
    /// There is no cursor to do it with: the game refuses to move its grid cursor on this
    /// level at all - the check is right there in its own code, by game mode, alongside the
    /// Zen Garden - because a tank of open water has no squares to walk. Every arrow press
    /// therefore answered "edge of the lawn", which is exactly what the player heard.
    /// </summary>
    public static bool Cycle(int step)
    {
        if (!Playable) return false;

        List<Swimmer> swimmers = Zombies();
        if (swimmers.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("tank.empty"), "tank");
            return true;
        }

        _chosen = ((_chosen + step) % swimmers.Count + swimmers.Count) % swimmers.Count;

        Swimmer chosen = swimmers[_chosen];
        Speech.SayVerbatim(Strings.T("tank.zombie_at", _chosen + 1, swimmers.Count,
                                     Where(chosen.X, chosen.Y)), "tank");
        return true;
    }

    /// <summary>
    /// Drops a brain by the zombie that was chosen.
    ///
    /// Aimed at a zombie because that is what the food is for, and because there is nothing
    /// else on this level to aim at - no squares, no cursor. They swim to the nearest brain,
    /// so dropping it where one of them is now is as close to feeding that one as the game
    /// allows.
    ///
    /// The game's own click handler does the dropping, which means its rules apply as they
    /// would to a mouse: the price, the limit of three, and the edges of the water. It says
    /// nothing about any of them, so the mod checks first and names the one that stopped it.
    /// </summary>
    public static bool Feed()
    {
        if (!Playable) return false;

        Challenge challenge = Challenge();
        Board board = Lawn.BoardRef;

        if (challenge == null || board == null)
        {
            Speech.SayVerbatim(Strings.T("tank.no_tank"), "tank");
            return true;
        }

        int before = BrainsInWater();
        if (before >= MaxBrains)
        {
            Speech.SayVerbatim(Strings.T("tank.water_full", MaxBrains), "tank");
            return true;
        }

        int sun = Lawn.SunCount();
        if (sun >= 0 && sun < BrainCost)
        {
            Speech.SayVerbatim(Strings.T("tank.too_poor", BrainCost, sun), "tank");
            return true;
        }

        List<Swimmer> swimmers = Zombies();
        if (swimmers.Count == 0)
        {
            Speech.SayVerbatim(Strings.T("tank.empty"), "tank");
            return true;
        }

        if (_chosen < 0 || _chosen >= swimmers.Count) _chosen = 0;
        Swimmer target = swimmers[_chosen];

        // Into the water even when the zombie is nosing the glass: the click handler ignores
        // anything outside the tank, and a brain refused for being an inch too far left would
        // look to the player exactly like one refused for being too poor.
        int px = Clamp((int)target.X, TankLeft + 5, TankRight - 5);
        int py = Clamp((int)target.Y, TankTop + 5, TankBottom - 5);

        try { challenge.ZombiquariumMouseDown(px, py); }
        catch (Exception ex)
        {
            Core.Log.Warning($"[tank] the brain would not drop: {ex.Message}");
            Speech.SayVerbatim(Strings.T("tank.no_tank"), "tank");
            return true;
        }

        // Asked of the water rather than assumed from the call. The click handler returns
        // nothing at all, so a brain that was never dropped looks exactly like one that was.
        int after = BrainsInWater();
        if (after <= before)
        {
            Core.Log.Msg($"[tank] no brain appeared at {px},{py}; {before} in the water, {sun} sun");
            Speech.SayVerbatim(Strings.T("tank.no_brain"), "tank");
            return true;
        }

        Core.Log.Msg($"[tank] dropped a brain at {px},{py}; now {after} in the water");
        Speech.SayVerbatim(Strings.T("tank.fed", Where(px, py), after, MaxBrains), "tank");
        return true;
    }

    private static int Clamp(int value, int low, int high) =>
        value < low ? low : value > high ? high : value;

    /// <summary>How the tank stands, for the key that reports progress.</summary>
    public static string Describe()
    {
        if (!IsActive) return null;

        var parts = new List<string>(3);

        int sun = Lawn.SunCount();
        parts.Add(sun < 0 ? Strings.T("tank.no_tank") : Strings.T("tank.progress", sun, Target));

        int swimming = Swimmers();
        if (swimming >= 0)
            parts.Add(Strings.T(swimming == 1 ? "tank.one_zombie" : "tank.zombies", swimming));

        int brains = BrainsInWater();
        if (brains >= 0)
            parts.Add(Strings.T(brains == 0 ? "tank.no_food" : "tank.food", brains, MaxBrains));

        return string.Join(" ", parts);
    }

    /// <summary>The tank, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- zombiquarium ---");
        sb.AppendLine($"  tank level : {IsActive}");

        if (!IsActive) { sb.AppendLine(); return; }

        sb.AppendLine($"  sun        : {Lawn.SunCount()} of {Target}");
        sb.AppendLine($"  zombies    : {Swimmers()}");
        sb.AppendLine($"  brains     : {BrainsInWater()} of {MaxBrains}");
        foreach (Swimmer swimmer in Zombies())
            sb.AppendLine($"      zombie {swimmer.Index + 1}: {swimmer.X:F0},{swimmer.Y:F0}" +
                          $" — {Where(swimmer.X, swimmer.Y)}");
        sb.AppendLine();
    }
}
