using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Localization;

namespace PvZRA11y.Gameplay;

/// <summary>
/// Dr Zomboss, and the things he throws.
///
/// The last level is the one place in the game where the danger is not a zombie walking
/// towards you. It is a ball of fire or ice aimed at one row, a foot coming down on another,
/// and a machine dropping in somewhere else — all of it announced to a sighted player by an
/// animation and nothing else. The sonar never saw any of it, because none of it is a zombie
/// on a square.
///
/// The boss carries every one of those decisions in his own fields before the attack lands,
/// so the mod can say which row is about to be hit while there is still time to move a plant
/// or drop an ice-shroom.
/// </summary>
public static class Boss
{
    /// <summary>The row a ball is aimed at when no ball is in the air.</summary>
    private const int NoRow = -1;

    private static int _lastFireballRow = NoRow;
    private static ZombiePhase _lastPhase = (ZombiePhase)(-1);
    private static bool _sawBoss;

    /// <summary>The boss zombie, or null when there is not one on this level.</summary>
    public static Zombie Find()
    {
        Board board = Lawn.BoardRef;
        if (board == null) return null;

        try
        {
            var zombies = board.m_zombies;
            if (zombies == null) return null;

            int count = zombies.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    Zombie zombie = zombies[i];
                    if (zombie == null || zombie.mDead) continue;
                    if (zombie.mZombieType == ZombieType.Boss) return zombie;
                }
                catch { /* one bad entry must not cost the rest */ }
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[boss] could not look for the boss: {ex.Message}");
        }

        return null;
    }

    /// <summary>True on the level where Dr Zomboss is fought.</summary>
    public static bool IsFinalLevel
    {
        get
        {
            try { return Lawn.AppRef != null && Lawn.AppRef.IsFinalBossLevel(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Watches the boss and says what he is about to do.
    ///
    /// Announced on the change, not repeated: each of these is a decision the boss makes
    /// once and then carries out, so saying it again every frame would bury the next one.
    /// </summary>
    public static void Tick()
    {
        if (!Lawn.IsOnBoard) { Forget(); return; }

        Zombie boss = Find();
        if (boss == null) { Forget(); return; }

        if (!_sawBoss)
        {
            _sawBoss = true;
            Core.Log.Msg("[boss] Dr Zomboss is on the lawn");
            Speech.Say(Strings.T("boss.arrived"), interrupt: false, context: "boss", allowRepeat: true);
        }

        WatchBall(boss);
        WatchPhase(boss);
    }

    private static void Forget()
    {
        _sawBoss = false;
        _lastFireballRow = NoRow;
        _lastPhase = (ZombiePhase)(-1);
    }

    /// <summary>
    /// The ball of fire or ice, and the row it is aimed at.
    ///
    /// Both are chosen at the moment he spits, well before it lands, and they are the whole
    /// of what a player needs: which row to clear, and whether it will burn the plants there
    /// or freeze them. The row is picked at random each time, so there is no learning it.
    /// </summary>
    private static void WatchBall(Zombie boss)
    {
        int row;
        bool fire;

        try
        {
            row = boss.mFireballRow;
            fire = boss.mIsFireBall;
        }
        catch { return; }

        if (row == _lastFireballRow) return;
        _lastFireballRow = row;

        if (row < 0) return;   // the ball is gone, which needs no announcement

        Core.Log.Msg($"[boss] {(fire ? "fireball" : "iceball")} aimed at row {row + 1}");

        Speech.Say(Strings.T(fire ? "boss.fireball" : "boss.iceball", row + 1),
                   interrupt: true, context: "boss", allowRepeat: true);
    }

    /// <summary>
    /// What the boss is doing, when it is worth a word.
    ///
    /// Only the phases that mean something is about to happen to the lawn. His idling, his
    /// entrance and the several phases of being hit are not things a player acts on, and
    /// naming them all would drown the two that matter.
    /// </summary>
    private static void WatchPhase(Zombie boss)
    {
        ZombiePhase phase;
        try { phase = boss.mZombiePhase; }
        catch { return; }

        if (phase == _lastPhase) return;
        _lastPhase = phase;

        string line = null;

        switch (phase)
        {
            case ZombiePhase.BossStomping:
                // The row is chosen before the foot comes down, from the rows that still have
                // something in them to crush.
                int row;
                try { row = boss.mTargetRow; }
                catch { row = -1; }

                line = row >= 0 ? Strings.T("boss.stomp", row + 1) : Strings.T("boss.stomp_any");
                break;

            case ZombiePhase.BossBungeesEnter:
            case ZombiePhase.BossBungeesDrop:
                line = Strings.T("boss.bungees");
                break;

            case ZombiePhase.BossDropRV:
                line = Strings.T("boss.rv");
                break;

            case ZombiePhase.BossSpawning:
                line = Strings.T("boss.spawning");
                break;
        }

        Core.Log.Msg($"[boss] phase {phase}{(line == null ? " (not spoken)" : "")}");

        if (line != null)
            Speech.Say(line, interrupt: true, context: "boss", allowRepeat: true);
    }

    /// <summary>What the boss is up to right now, for the key that asks.</summary>
    public static string Describe()
    {
        Zombie boss = Find();
        if (boss == null) return null;

        var parts = new List<string>(3);

        try
        {
            int row = boss.mFireballRow;
            if (row >= 0)
                parts.Add(Strings.T(boss.mIsFireBall ? "boss.fireball" : "boss.iceball", row + 1));
        }
        catch { }

        try
        {
            if (boss.mZombiePhase == ZombiePhase.BossStomping)
            {
                int row = boss.mTargetRow;
                parts.Add(row >= 0 ? Strings.T("boss.stomp", row + 1) : Strings.T("boss.stomp_any"));
            }
        }
        catch { }

        // How much of him is left, using the same wording the sonar uses for a plant.
        try
        {
            int health = boss.mBodyHealth;
            int max = boss.mBodyMaxHealth;
            if (max > 0) parts.Add(Strings.T("boss.health", Math.Max(0, health * 100 / max)));
        }
        catch { }

        return parts.Count == 0 ? Strings.T("boss.quiet") : string.Join(", ", parts);
    }

    /// <summary>The boss's state, for the self-test.</summary>
    public static void Dump(System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- boss ---");
        sb.AppendLine($"  final level    : {IsFinalLevel}");

        Zombie boss = Find();
        sb.AppendLine($"  boss present   : {boss != null}");
        if (boss == null) { sb.AppendLine(); return; }

        void Line(string name, Func<string> read)
        {
            string value;
            try { value = read(); } catch (Exception ex) { value = "<threw: " + ex.Message + ">"; }
            sb.AppendLine($"  {name,-14} : {value}");
        }

        Line("phase", () => boss.mZombiePhase.ToString());
        Line("fireball row", () => boss.mFireballRow.ToString());
        Line("is fireball", () => boss.mIsFireBall.ToString());
        Line("target row", () => boss.mTargetRow.ToString());
        Line("boss mode", () => boss.mBossMode.ToString());
        Line("stomp counter", () => boss.mBossStompCounter.ToString());
        Line("head counter", () => boss.mBossHeadCounter.ToString());
        Line("bungee counter", () => boss.mBossBungeeCounter.ToString());
        Line("body health", () => $"{boss.mBodyHealth} of {boss.mBodyMaxHealth}");

        sb.AppendLine();
    }
}
