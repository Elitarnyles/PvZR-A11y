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

    /// <summary>
    /// Drops a brain where the cursor is standing.
    ///
    /// At the cursor rather than in the middle, because where the food goes is a decision: the
    /// zombies swim to the nearest brain, so three brains dropped in one place feed whoever is
    /// closest three times over while the far side of the tank starves.
    ///
    /// The game's own click handler does the work, which means it also applies its own rules -
    /// the price, the limit of three, and the edges of the water. It says nothing about any of
    /// them, so the mod checks first and says which one stopped it.
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

        if (!TryTankPixel(out int px, out int py))
        {
            Speech.SayVerbatim(Strings.T("tank.not_water"), "tank");
            return true;
        }

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
            Core.Log.Msg($"[tank] no brain appeared; {before} in the water, {sun} sun");
            Speech.SayVerbatim(Strings.T("tank.no_brain"), "tank");
            return true;
        }

        Core.Log.Msg($"[tank] dropped a brain at {px},{py}; now {after} in the water");
        Speech.SayVerbatim(Strings.T("tank.fed", after, MaxBrains), "tank");
        return true;
    }

    /// <summary>
    /// A pixel in the water under the cursor, or false when the cursor is not over water.
    ///
    /// The tank is a rectangle in board pixels, not a set of squares, and it does not line up
    /// with the lawn's grid at the edges. Rather than pretend it does, the cursor's square is
    /// turned into a pixel and that pixel is tested against the water.
    /// </summary>
    private static bool TryTankPixel(out int px, out int py)
    {
        px = py = 0;

        if (!Lawn.TryGetPosition(out int x, out int y)) return false;
        if (!Lawn.TryPixelInSquare(x, y, out int cx, out int cy)) return false;

        if (cx < TankLeft || cx > TankRight || cy < TankTop || cy > TankBottom) return false;

        px = cx;
        py = cy;
        return true;
    }

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
        sb.AppendLine($"  cursor over water: {TryTankPixel(out int px, out int py)} ({px},{py})");
        sb.AppendLine();
    }
}
