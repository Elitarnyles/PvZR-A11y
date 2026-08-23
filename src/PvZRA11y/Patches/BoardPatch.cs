using HarmonyLib;
using Il2CppReloaded.Gameplay;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Gameplay;
using PvZRA11y.Localization;

namespace PvZRA11y.Patches;

/// <summary>
/// Follows the state of a level: when a lawn appears, what it looks like, and what turns up on it.
///
/// The board object is caught here rather than searched for, because it is the anchor for
/// everything on the lawn — the grid, the sun, the cursor — and hunting for it every frame
/// would be both slower and less certain than being handed it.
/// </summary>
[HarmonyPatch(typeof(Board))]
internal static class BoardPatch
{
    [HarmonyPatch("InitLevel")]
    [HarmonyPostfix]
    private static void InitLevel_Postfix(Board __instance)
    {
        Lawn.NoteBoard(__instance);
        ClearArrivals();
        LawnInput.Reset();
        Sonar.Reset();
        SeedChooser.Reset();
    }

    [HarmonyPatch("StartLevel")]
    [HarmonyPostfix]
    private static void StartLevel_Postfix()
    {
        // Said as one piece so it is not cut in half by the first cursor move.
        int rows = Lawn.SafeRowCount();
        Speech.Say(Strings.T("lawn.level_start", rows), interrupt: false, context: "level start");
        LawnInput.AnnounceSquare(interrupt: false);

        // A level starting is the one moment when everything the mod depends on is live at
        // once: board, cursor, seed bank, panels. Checking it here means a single play
        // session answers questions that would otherwise take a round trip each.
        if (Settings.VerboseLogging.Value)
            Diagnostics.SelfTest.Run("level started");
    }

    /// <summary>
    /// A zombie entering a row is the one event a player cannot afford to miss, and it is
    /// the thing sighted players read off the screen constantly.
    /// </summary>
    [HarmonyPatch("AddZombieInRow")]
    [HarmonyPostfix]
    private static void AddZombieInRow_Postfix(ZombieType theZombieType, int theRow, int theFromWave)
    {
        // Negative wave numbers are the game populating the level preview, not real spawns.
        if (theFromWave < 0) return;
        if (!Settings.SayZombieArrivals.Value) return;

        // A pole-vaulter, a football zombie or a bobsled covers ground far faster than the
        // rest, so it gets a sound as well as a sentence. By the time the sentence finishes
        // one of these has already moved a column.
        if (IsFast(theZombieType))
        {
            float volume = Settings.FastZombieCueVolume.Value;
            if (volume > 0f)
            {
                int rows = Math.Max(1, Lawn.SafeRowCount());
                float pan = rows <= 1 ? 0.5f : Math.Clamp(theRow / (float)(rows - 1), 0f, 1f);
                Tones.PlayAlert(pan, volume);
            }
        }

        // Not spoken here. A wave arrives as a burst of calls in a single frame, and three
        // ordinary zombies entering one row produced three identical sentences — which the
        // speech layer then merged back into one, because it cannot tell a repeat of the
        // same event from three separate ones. Counting them here, where the difference is
        // known, gives one sentence that says how many.
        Pending.TryGetValue(theRow, out var row);
        row ??= new Dictionary<ZombieType, int>();
        row.TryGetValue(theZombieType, out int seen);
        row[theZombieType] = seen + 1;
        Pending[theRow] = row;
    }

    /// <summary>Zombies that turned up this frame, by row and then by kind.</summary>
    private static readonly Dictionary<int, Dictionary<ZombieType, int>> Pending = new();

    /// <summary>
    /// Says what arrived, one sentence per row. Called once a frame, after the game has
    /// finished adding whatever this frame brought.
    /// </summary>
    public static void FlushArrivals()
    {
        if (Pending.Count == 0) return;

        foreach (var entry in Pending)
        {
            var names = new List<string>(entry.Value.Count);
            foreach (var kind in entry.Value)
            {
                string name = ZombieName(kind.Key);
                names.Add(kind.Value > 1 ? Strings.T("lawn.zombie_several", kind.Value, name) : name);
            }

            // allowRepeat, because the counting above has already merged everything that
            // arrived together. Anything identical that follows is a further wave, and
            // hearing about it is the entire point.
            Speech.Say(Strings.T("lawn.zombie_in_row", string.Join(", ", names), entry.Key + 1),
                interrupt: false, context: "zombie spawned", allowRepeat: true);
        }

        Pending.Clear();
    }

    /// <summary>Drops anything counted but not yet said. For a level ending mid-wave.</summary>
    public static void ClearArrivals() => Pending.Clear();

    /// <summary>
    /// Losing has no flag of its own the way completing a level does, so it is caught here.
    /// Without it the lawn would keep the keyboard while the defeat screen is up.
    /// </summary>
    [HarmonyPatch("ZombiesWon")]
    [HarmonyPostfix]
    private static void ZombiesWon_Postfix()
    {
        Lawn.NoteLevelLost();
        Speech.Say(Strings.T("lawn.level_lost"), interrupt: true, context: "level lost");
    }

    [HarmonyPatch("AddPlant")]
    [HarmonyPostfix]
    private static void AddPlant_Postfix(int theGridX, int theGridY, SeedType theSeedType)
    {
        Speech.Say(Strings.T("lawn.planted", Lawn.PlantName(theSeedType), theGridY + 1, theGridX + 1),
            interrupt: true, context: "plant added");
    }

    /// <summary>
    /// The three that outrun everything else, taken from the original PvZ accessibility mod
    /// rather than guessed.
    /// </summary>
    private static bool IsFast(ZombieType type) => type
        is ZombieType.Polevaulter or ZombieType.Football or ZombieType.Bobsled;

    private static string ZombieName(ZombieType type)
    {
        string key = "zombie." + type;
        return Strings.Has(key) ? Strings.T(key) : UI.UiText.Prettify(type.ToString());
    }
}
