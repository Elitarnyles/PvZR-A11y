using System.Text;
using MelonLoader.Utils;

namespace PvZRA11y.Localization;

/// <summary>
/// Every piece of text the mod can speak.
///
/// English is compiled in, so the mod is fully functional with no data files at all
/// and a missing translation degrades to English rather than to a blank string.
/// A translation is a plain "key = value" text file dropped in
/// UserData/PvZRA11y/lang/, one line per key, '#' starts a comment.
///
/// On every start the complete English set is written to lang/en.default.txt so a
/// translator always has an up-to-date template to copy.
///
/// Two key families matter:
///   role.*, state.*, msg.*, screen.*   normal translatable phrases
///   ui.&lt;GameObject name&gt;              labels for controls the game leaves unlabelled
///
/// The ui.* entries are the ones that grow as we test. When a control announces
/// something unhelpful, press the "dump UI" key, read the object name out of the
/// log, and add a line here.
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        // -- Control roles, spoken after the label --------------------------
        ["role.button"] = "button",
        ["role.checkbox"] = "check box",
        ["role.slider"] = "slider",
        ["role.dropdown"] = "combo box",
        ["role.textfield"] = "edit",
        ["role.list"] = "list",
        ["role.grid"] = "grid",
        ["role.scrollbar"] = "scroll bar",
        ["role.selectable"] = "control",

        // -- Control states -------------------------------------------------
        ["state.checked"] = "checked",
        ["state.unchecked"] = "not checked",
        ["state.disabled"] = "unavailable",
        ["state.blank"] = "blank",
        ["char.space"] = "space",
        ["state.percent"] = "{0} percent",
        ["state.position"] = "{0} of {1}",

        // -- Mod messages ---------------------------------------------------
        ["msg.ready"] = "Accessibility mod version {0} ready.",
        ["msg.no_reader"] = "No screen reader detected.",
        ["msg.speech_on"] = "Speech on.",
        ["msg.speech_off"] = "Speech off.",
        ["msg.nothing_focused"] = "Nothing is focused.",
        ["msg.empty_screen"] = "No controls found on this screen.",
        ["msg.screen_is"] = "Screen: {0}.",
        ["msg.controls_count"] = "{0} controls.",
        ["msg.dump_written"] = "Screen contents written to the log.",
        ["msg.unlabelled"] = "unlabelled control",
        ["msg.not_available"] = "Not available from here.",
        ["msg.and_more"] = "and {0} more",
        ["sonar.armour_dinted"] = "{0}, dinted",
        ["sonar.armour_damaged"] = "{0}, damaged",
        ["sonar.armour_gone"] = "{0}, exposed",
        ["sonar.incomplete"] = "{0} zombies could not be read.",
        ["msg.deleted"] = "{0} deleted",
        ["msg.starting_level"] = "Starting {0}",
        ["msg.level_ready"] = "{0} chosen. Press Enter again to play.",
        ["msg.no_carousel"] = "Nothing to cycle through here.",

        // -- On the lawn -----------------------------------------------------
        ["lawn.level_start"] = "Level started. {0} rows.",
        ["lawn.position"] = "row {0}, column {1}",
        ["lawn.empty"] = "empty",
        ["lawn.water"] = "water",
        ["lawn.roof"] = "roof",
        ["lawn.ice"] = "ice",
        ["lawn.gravestone"] = "gravestone",
        ["lawn.crater"] = "crater",
        ["lawn.ladder"] = "ladder",
        ["lawn.health"] = "{0}% health",
        ["lawn.mower_present"] = "mower ready",
        ["lawn.mower_gone"] = "no mower",
        ["lawn.sun"] = "{0} sun",
        ["lawn.rows"] = "{0} rows",
        ["lawn.rows_without_mower"] = "no mower in rows {0}",
        ["lawn.wave"] = "Wave {0} of {1}",
        ["lawn.percent"] = "{0}% complete",
        ["lawn.final_wave"] = "Final wave.",
        ["lawn.no_waves"] = "No wave counter here.",
        ["lawn.no_board"] = "Not on a lawn.",
        ["lawn.edge_top"] = "Top row.",
        ["lawn.edge_bottom"] = "Bottom row.",
        ["lawn.edge_left"] = "Leftmost column, next to the house.",
        ["lawn.edge_right"] = "Rightmost column.",
        ["lawn.cursor_lost"] = "Lost the lawn cursor.",
        ["speed.normal"] = "Normal speed.",
        ["speed.faster"] = "Speed {0} times.",
        ["speed.slower"] = "Speed {0} times, slower than normal.",
        ["lawn.level_lost"] = "The zombies ate your brains.",
        ["lawn.frozen"] = "Frozen.",
        ["lawn.unfrozen"] = "Running.",
        ["lawn.nothing_to_dig"] = "Nothing to dig up here.",
        ["lawn.dug_up"] = "{0} dug up",
        ["lawn.dug_up_something"] = "Dug up",
        ["lawn.zombie_in_row"] = "{0}, row {1}",
        ["lawn.zombie_several"] = "{0} {1}s",
        ["lawn.planted"] = "{0} planted, row {1}, column {2}",

        // -- Choosing plants before a level ----------------------------------
        ["chooser.no_level"] = "No level information.",
        ["chooser.no_zombies"] = "No zombies listed.",
        ["chooser.need_more"] = "Please select {0} more plants to begin.",
        ["chooser.need_one"] = "Please select 1 more plant to begin.",
        ["chooser.ready"] = "Ready. Press {0} to begin.",
        ["chooser.no_plants"] = "No plants to choose from.",
        ["chooser.picked"] = "Picked. {0}",
        ["chooser.took"] = "Took {0}",
        ["chooser.returned"] = "Returned {0}",
        ["chooser.refused"] = "{0} is not available.",
        ["chooser.cannot_start"] = "Cannot tell how many plants are needed.",
        ["chooser.slot_empty"] = "Slot {0}, empty",
        ["chooser.slot_filled"] = "Slot {0}, {1}",
        ["area.Day"] = "Front yard, day",
        ["area.Night"] = "Front yard, night",
        ["area.Pool"] = "Pool",
        ["area.Fog"] = "Pool, fog",
        ["area.Roof"] = "Roof",
        ["area.Boss"] = "Roof, boss",
        ["area.ZenGarden"] = "Zen Garden",
        ["area.TreeOfWisdom"] = "Tree of Wisdom",

        // -- Zombie sonar ----------------------------------------------------
        // Wording follows the original PvZ accessibility mod: "1. I: Normal," - a count,
        // then column letters A to I, and no row number because you are standing in it.
        ["sonar.none"] = "No Zombies",
        ["sonar.off_lawn"] = "Off-Board",
        ["sonar.rows"] = "Zombies in rows {0}.",
        ["sonar.all_clear"] = "Lawn clear.",
        ["sonar.hypnotised"] = "hypnotised {0}",
        ["sonar.frozen"] = "frozen {0}",
        ["sonar.headless"] = "headless {0}",
        ["sonar.tripwire"] = "Zombie through in row {0}",

        // -- Things picked up on the lawn ------------------------------------
        ["pickup.collected"] = "Got {0}",
        ["pickup.Silver"] = "a silver coin",
        ["pickup.Gold"] = "a gold coin",
        ["pickup.Diamond"] = "a diamond",
        ["pickup.FinalSeedPacket"] = "a new seed packet",
        ["pickup.UsableSeedPacket"] = "a seed packet",
        ["pickup.PresentPlant"] = "a new plant",
        ["pickup.Trophy"] = "a trophy",
        ["pickup.RIPTrophy"] = "a trophy",
        ["pickup.Shovel"] = "the shovel",
        ["pickup.Almanac"] = "the almanac",
        ["pickup.CarKeys"] = "the car keys",
        ["pickup.WateringCan"] = "the watering can",
        ["pickup.Taco"] = "a taco",
        ["pickup.Note"] = "a note",
        ["pickup.Chocolate"] = "chocolate",
        ["pickup.AwardChocolate"] = "chocolate",
        ["pickup.AwardMoneyBag"] = "a bag of money",
        ["pickup.AwardPresent"] = "a present",
        ["pickup.AwardBagDiamond"] = "a bag of diamonds",
        ["pickup.AwardSilverSunflower"] = "a silver sunflower",
        ["pickup.AwardGoldSunflower"] = "a gold sunflower",
        ["pickup.PresentMinigames"] = "the mini-games",
        ["pickup.PresentPuzzleMode"] = "puzzle mode",
        ["pickup.PresentSurvivalMode"] = "survival mode",
        ["pickup.Brain"] = "a brain",

        // -- The seed bank ---------------------------------------------------
        ["seeds.ready"] = "Ready",
        ["seeds.refreshing"] = "Refreshing",
        ["seeds.of_sun"] = "{0} of {1} sun",
        ["seeds.empty_slot"] = "slot {0}, empty",
        ["seeds.nothing_held"] = "Nothing in hand.",
        ["seeds.holding"] = "Holding {0}, slot {1}",
        ["seeds.no_bank"] = "No seed bank here.",
        ["seeds.no_such_slot"] = "No slot {0}.",

        // Why a plant can or cannot go on the square under the cursor.
        ["planting.Ok"] = "can plant",
        ["planting.NotHere"] = "occupied",
        ["planting.OnlyOnGraves"] = "graves only",
        ["planting.OnlyInPool"] = "water only",
        ["planting.OnlyOnGround"] = "ground only",
        ["planting.NeedsPot"] = "needs a flower pot",
        ["planting.NotOnArt"] = "cannot plant on decoration",
        ["planting.NotPassedLine"] = "past the line",
        ["planting.NeedsUpgrade"] = "needs the plant it upgrades",
        ["planting.NotOnGrave"] = "not on graves",
        ["planting.NotOnCrater"] = "not on craters",
        ["planting.NotOnWater"] = "not on water",
        ["planting.NeedsGround"] = "needs ground",
        ["planting.NeedsSleeping"] = "would sleep here",
        ["planting.blocked"] = "cannot plant",

        // -- Screens ---------------------------------------------------------
        // Ids come from the game's PanelView.Id, confirmed against a running session.
        ["screen.splash1"] = "Title screen",
        ["screen.LegalScreen"] = "Legal notice",
        ["screen.LegalScreenPCP"] = "Privacy policy",
        ["screen.LegalScreenUA"] = "User agreement",
        ["screen.LegalScreenMustAccept"] = "You must accept to continue",
        ["screen.awardScreen"] = "Reward",
        ["screen.speechBubble"] = "Message",
        ["screen.readySetPlant"] = "Ready, set, plant",
        ["screen.loadingScrim"] = "Loading",
        ["screen.gameplay"] = "Lawn",
        ["screen.gameOptions"] = "Paused",
        ["screen.serializedrestart"] = "Continue or restart",
        ["screen.mainMenu"] = "Main menu",
        ["screen.users"] = "Choose player",
        ["screen.usersNewUser"] = "New player",
        ["screen.MainMenu"] = "Main menu",
        ["screen.LevelSelect"] = "Level select",
        ["screen.SeedChooser"] = "Choose your plants",
        ["screen.Options"] = "Options",
        ["screen.Pause"] = "Paused",
        ["screen.Confirm"] = "Confirm",
        ["screen.Almanac"] = "Almanac",
        ["screen.Store"] = "Store",
        ["screen.Achievements"] = "Achievements",

        // -- One-line plant descriptions -------------------------------------
        // Written here rather than taken from the game. Its own tooltip strings resolve to
        // nothing while the chooser is open — the text is loaded in banks and that one is
        // not among them — and the almanac holds a full encyclopaedia entry, several
        // sentences long, which is far too much when you are stepping through forty plants.
        ["plant.tip.Peashooter"] = "Shoots peas at zombies",
        ["plant.tip.Sunflower"] = "Gives you additional sun",
        ["plant.tip.Cherrybomb"] = "Blows up every zombie in a small area",
        ["plant.tip.Wallnut"] = "Blocks zombies with a hard shell",
        ["plant.tip.Potatomine"] = "Arms itself, then explodes underfoot",
        ["plant.tip.Snowpea"] = "Shoots frozen peas that slow zombies",
        ["plant.tip.Chomper"] = "Swallows a zombie whole, then chews for a while",
        ["plant.tip.Repeater"] = "Shoots two peas at a time",
        ["plant.tip.Puffshroom"] = "Free, short range, asleep in daylight",
        ["plant.tip.Sunshroom"] = "Gives small sun, then larger as it grows",
        ["plant.tip.Fumeshroom"] = "Breathes fumes that pass through screen doors",
        ["plant.tip.Gravebuster"] = "Eats a gravestone",
        ["plant.tip.Hypnoshroom"] = "Turns the zombie that eats it against the rest",
        ["plant.tip.Scaredyshroom"] = "Hides when zombies come close",
        ["plant.tip.Iceshroom"] = "Freezes every zombie on the lawn",
        ["plant.tip.Doomshroom"] = "Destroys everything near it and leaves a crater",
        ["plant.tip.Lilypad"] = "Lets other plants sit on water",
        ["plant.tip.Squash"] = "Crushes the first zombie that comes near",
        ["plant.tip.Threepeater"] = "Shoots into three lanes at once",
        ["plant.tip.Tanglekelp"] = "Drags one water zombie under",
        ["plant.tip.Jalapeno"] = "Burns every zombie in its lane",
        ["plant.tip.Spikeweed"] = "Hurts zombies walking over it and pops tyres",
        ["plant.tip.Torchwood"] = "Sets passing peas alight for double damage",
        ["plant.tip.Tallnut"] = "A taller wall that cannot be vaulted",
        ["plant.tip.Seashroom"] = "Free, short range, and floats on water",
        ["plant.tip.Plantern"] = "Clears the fog around it",
        ["plant.tip.Cactus"] = "Shoots spikes and can reach balloons",
        ["plant.tip.Blover"] = "Blows away fog and balloon zombies",
        ["plant.tip.Splitpea"] = "Shoots forwards and backwards",
        ["plant.tip.Starfruit"] = "Shoots in five directions",
        ["plant.tip.Pumpkinshell"] = "Shields the plant inside it",
        ["plant.tip.Magnetshroom"] = "Pulls the metal off zombies",
        ["plant.tip.Cabbagepult"] = "Lobs cabbages over walls",
        ["plant.tip.Flowerpot"] = "Lets you plant on the roof",
        ["plant.tip.Kernelpult"] = "Lobs corn, and sometimes butter that stuns",
        ["plant.tip.InstantCoffee"] = "Wakes a sleeping mushroom",
        ["plant.tip.Garlic"] = "Makes zombies change lane",
        ["plant.tip.Umbrella"] = "Shields nearby plants from bungees and catapults",
        ["plant.tip.Marigold"] = "Grows coins",
        ["plant.tip.Melonpult"] = "Lobs melons that splash onto nearby zombies",
        ["plant.tip.Gatlingpea"] = "Shoots four peas at a time",
        ["plant.tip.Twinsunflower"] = "Gives double sun",
        ["plant.tip.Gloomshroom"] = "Hits everything close around it",
        ["plant.tip.Cattail"] = "Attacks anywhere, and pops balloons",
        ["plant.tip.Wintermelon"] = "Lobs melons that slow what they hit",
        ["plant.tip.GoldMagnet"] = "Collects coins for you",
        ["plant.tip.Spikerock"] = "A tougher spikeweed that survives more tyres",
        ["plant.tip.Cobcannon"] = "Fires a corn missile wherever you aim",
        ["plant.tip.Imitater"] = "Becomes a second copy of another plant",

        // -- Plant and zombie names ------------------------------------------
        // Only the ones whose enum name does not already read correctly. Anything absent
        // is split into words automatically, so "InstantCoffee" needs no entry.
        ["plant.Wallnut"] = "Wall-nut",
        ["plant.Tallnut"] = "Tall-nut",
        ["plant.GiantWallnut"] = "Giant wall-nut",
        ["plant.ExplodeONut"] = "Explode-o-nut",
        ["plant.Snowpea"] = "Snow pea",
        ["plant.Splitpea"] = "Split pea",
        ["plant.Gatlingpea"] = "Gatling pea",
        ["plant.Threepeater"] = "Threepeater",
        ["plant.Leftpeater"] = "Leftpeater",
        ["plant.Cherrybomb"] = "Cherry bomb",
        ["plant.Potatomine"] = "Potato mine",
        ["plant.Puffshroom"] = "Puff-shroom",
        ["plant.Sunshroom"] = "Sun-shroom",
        ["plant.Fumeshroom"] = "Fume-shroom",
        ["plant.Hypnoshroom"] = "Hypno-shroom",
        ["plant.Scaredyshroom"] = "Scaredy-shroom",
        ["plant.Iceshroom"] = "Ice-shroom",
        ["plant.Doomshroom"] = "Doom-shroom",
        ["plant.Seashroom"] = "Sea-shroom",
        ["plant.Magnetshroom"] = "Magnet-shroom",
        ["plant.Gloomshroom"] = "Gloom-shroom",
        ["plant.Lilypad"] = "Lily pad",
        ["plant.Tanglekelp"] = "Tangle kelp",
        ["plant.Spikeweed"] = "Spikeweed",
        ["plant.Spikerock"] = "Spikerock",
        ["plant.Torchwood"] = "Torchwood",
        ["plant.Gravebuster"] = "Grave buster",
        ["plant.Pumpkinshell"] = "Pumpkin",
        ["plant.Cabbagepult"] = "Cabbage-pult",
        ["plant.Kernelpult"] = "Kernel-pult",
        ["plant.Melonpult"] = "Melon-pult",
        ["plant.Wintermelon"] = "Winter melon",
        ["plant.Flowerpot"] = "Flower pot",
        ["plant.Twinsunflower"] = "Twin sunflower",
        ["plant.Cobcannon"] = "Cob cannon",
        ["plant.Cattail"] = "Cattail",
        ["plant.Starfruit"] = "Starfruit",
        ["plant.Jalapeno"] = "Jalapeno",
        ["plant.Plantern"] = "Plantern",
        ["plant.None"] = "nothing",

        ["zombie.Normal"] = "Zombie",
        ["zombie.Flag"] = "Flag zombie",
        ["zombie.TrafficCone"] = "Cone-head zombie",
        ["zombie.Pail"] = "Bucket-head zombie",
        ["zombie.Polevaulter"] = "Pole-vaulting zombie",
        ["zombie.Door"] = "Screen door zombie",
        ["zombie.Newspaper"] = "Newspaper zombie",
        ["zombie.Football"] = "Football zombie",
        ["zombie.Dancer"] = "Dancing zombie",
        ["zombie.BackupDancer"] = "Backup dancer",
        ["zombie.DuckyTube"] = "Ducky tube zombie",
        ["zombie.Snorkel"] = "Snorkel zombie",
        ["zombie.Zamboni"] = "Zamboni",
        ["zombie.DolphinRider"] = "Dolphin rider",
        ["zombie.JackInTheBox"] = "Jack-in-the-box zombie",
        ["zombie.Balloon"] = "Balloon zombie",
        ["zombie.Digger"] = "Digger zombie",
        ["zombie.Pogo"] = "Pogo zombie",
        ["zombie.Bungee"] = "Bungee zombie",
        ["zombie.Ladder"] = "Ladder zombie",
        ["zombie.Catapult"] = "Catapult zombie",
        ["zombie.Gargantuar"] = "Gargantuar",
        ["zombie.RedeyeGargantuar"] = "Red-eye Gargantuar",
        ["zombie.Imp"] = "Imp",
        ["zombie.Boss"] = "Doctor Zomboss",
        ["zombie.Yeti"] = "Yeti zombie",

        // -- Labels for controls the game ships without readable text --------
        // Verified object names, translated from the reconnaissance in
        // game-a11y/PvZ-Replanted-A11y (MIT).
        ["ui.LargeHitArea"] = "Click to start",
        // The pluckable flowers from the original game's main menu. Harmless, but they
        // need names or they read out as raw object ids.
        ["ui.Flower01"] = "Decorative flower 1",
        ["ui.Flower02"] = "Decorative flower 2",
        ["ui.Flower03"] = "Decorative flower 3",
        ["ui.Scrollbar Vertical"] = "Scroll",
        ["ui.MusicP_Slider"] = "Music volume",
        ["ui.Sound FXP_Slider"] = "Sound effects volume",
        ["ui.Gamepad Speed XP_Slider"] = "Cursor speed",
        ["ui.Dropdown"] = "Resolution",
        ["ui.VibrationP_CheckBox (1)"] = "Vibration",
        ["ui.V-SyncP_CheckBox"] = "Vertical sync",
        ["ui.P_BackButton"] = "Back",
        ["ui.SeedBackground"] = "Plant card",
        ["ui.Shovel"] = "Shovel",
        ["ui.P_AccelerationButton"] = "Fast forward",
        ["ui.headcrab _CheckBox"] = "Headcrab",
        ["ui.retroZombie _CheckBox"] = "Retro zombie",
        ["ui.P_BacicButton_Yes"] = "Yes",
        ["ui.P_BacicButton_No"] = "No",
        ["ui.P_BacicButton_Quit"] = "Quit",
        ["ui.P_BacicButton_Cancel"] = "Cancel",
        ["ui.P_BacicButton_Continue"] = "Continue",
        ["ui.P_BacicButton_RestartLevel"] = "Restart level",
        ["ui.P_BacicButton_Leave"] = "Leave",
    };

    private static Dictionary<string, string> _active = Defaults;
    private static string _loadedCode = "en";

    public static string LoadedLanguage => _loadedCode;

    private static string LangDir =>
        Path.Combine(MelonEnvironment.UserDataDirectory, "PvZRA11y", "lang");

    public static void Load(string code)
    {
        WriteTemplate();

        if (string.IsNullOrWhiteSpace(code) || code.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            _active = Defaults;
            _loadedCode = "en";
            return;
        }

        string path = Path.Combine(LangDir, code.Trim() + ".txt");
        if (!File.Exists(path))
        {
            Core.Log.Warning($"No translation at {path}. Falling back to English.");
            _active = Defaults;
            _loadedCode = "en";
            return;
        }

        // Start from English so a partial translation still speaks in full.
        var merged = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);
        int count = 0;

        try
        {
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key.Length == 0 || value.Length == 0) continue;

                merged[key] = value;
                count++;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not read {path}: {ex.Message}. Falling back to English.");
            _active = Defaults;
            _loadedCode = "en";
            return;
        }

        _active = merged;
        _loadedCode = code.Trim();
        Core.Log.Msg($"Loaded {count} strings for language \"{_loadedCode}\".");
    }

    /// <summary>Looks up a key. Unknown keys come back as the key itself, which is loud enough to notice.</summary>
    public static string T(string key)
        => _active.TryGetValue(key, out string value) ? value : key;

    public static string T(string key, params object[] args)
    {
        string template = T(key);
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>True when a key exists, without falling back.</summary>
    public static bool Has(string key) => _active.ContainsKey(key);

    /// <summary>
    /// Looks up an override label for a GameObject that carries no readable text.
    /// </summary>
    public static bool TryUiLabel(string objectName, out string label)
    {
        label = null;
        if (string.IsNullOrEmpty(objectName)) return false;
        return _active.TryGetValue("ui." + objectName, out label);
    }

    /// <summary>
    /// Rewrites lang/en.default.txt with the compiled-in English set, so translators
    /// always have every current key in front of them.
    /// </summary>
    private static void WriteTemplate()
    {
        try
        {
            Directory.CreateDirectory(LangDir);
            var sb = new StringBuilder();
            sb.AppendLine("# PvZ Replanted Accessibility - English reference strings");
            sb.AppendLine("# Generated on every launch. Do not edit; copy to <code>.txt and translate that.");
            sb.AppendLine("# Format: key = value      Lines starting with # are ignored.");
            sb.AppendLine("# Keys under ui.* are labels for controls the game leaves unlabelled;");
            sb.AppendLine("# the part after ui. is the raw GameObject name and must not be translated.");
            sb.AppendLine();

            string section = null;
            foreach (var pair in Defaults)
            {
                string prefix = pair.Key.Split('.')[0];
                if (prefix != section)
                {
                    section = prefix;
                    sb.AppendLine();
                    sb.AppendLine($"# --- {section} ---");
                }
                sb.AppendLine($"{pair.Key} = {pair.Value}");
            }

            File.WriteAllText(Path.Combine(LangDir, "en.default.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not write the translation template: {ex.Message}");
        }
    }
}
