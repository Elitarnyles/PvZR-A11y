using MelonLoader;
using UnityEngine.InputSystem;

namespace PvZRA11y.Config;

/// <summary>
/// Every user-facing option, stored through MelonPreferences so it lands in
/// UserData/MelonPreferences.cfg and survives updates.
///
/// Key bindings are held as strings naming a member of UnityEngine.InputSystem.Key
/// ("F1", "Tab", "Numpad0"). That keeps the config file readable and means an
/// unknown name degrades to "unbound" rather than throwing on startup.
/// </summary>
public static class Settings
{
    private const string Category = "PvZRA11y";

    // --- General -----------------------------------------------------------
    public static MelonPreferences_Entry<bool> Enabled;
    public static MelonPreferences_Entry<string> Language;
    public static MelonPreferences_Entry<bool> VerboseLogging;
    public static MelonPreferences_Entry<bool> AllowSapi;

    // --- Narration ---------------------------------------------------------
    public static MelonPreferences_Entry<bool> SpeakRoles;
    public static MelonPreferences_Entry<bool> SpeakOnHover;
    public static MelonPreferences_Entry<bool> SpeakScreenChanges;
    public static MelonPreferences_Entry<bool> SpeakPositionInList;

    // --- On the lawn -------------------------------------------------------
    public static MelonPreferences_Entry<float> PositionCueVolume;
    public static MelonPreferences_Entry<bool> SayTilePosition;
    public static MelonPreferences_Entry<bool> SayZombieArrivals;
    public static MelonPreferences_Entry<bool> SayGameMessages;
    public static MelonPreferences_Entry<float> SonarVolume;
    public static MelonPreferences_Entry<float> FastZombieCueVolume;
    public static MelonPreferences_Entry<bool> SayTripwire;
    public static MelonPreferences_Entry<int> TripwireColumn;
    public static MelonPreferences_Entry<bool> SayEmptyHands;
    public static MelonPreferences_Entry<bool> AutoCollectSun;
    public static MelonPreferences_Entry<bool> AutoCollectItems;
    public static MelonPreferences_Entry<bool> SayCoinPickups;
    public static MelonPreferences_Entry<bool> SayAlmanacHitPoints;

    // --- Navigation --------------------------------------------------------
    public static MelonPreferences_Entry<bool> AutoFocusFirstControl;
    public static MelonPreferences_Entry<bool> UseHitTest;

    // --- Keys --------------------------------------------------------------
    public static MelonPreferences_Entry<string> KeyActivate;
    public static MelonPreferences_Entry<string> KeyInfo1;
    public static MelonPreferences_Entry<string> KeyInfo2;
    public static MelonPreferences_Entry<string> KeyInfo3;
    public static MelonPreferences_Entry<string> KeyInfo4;
    public static MelonPreferences_Entry<string> KeyStartLevel;
    public static MelonPreferences_Entry<string> KeyFreeze;
    public static MelonPreferences_Entry<string> KeyShovel;
    public static MelonPreferences_Entry<string> KeyDumpUi;
    public static MelonPreferences_Entry<string> KeySelfTest;
    public static MelonPreferences_Entry<string> KeyFocusNext;
    public static MelonPreferences_Entry<string> KeyCycleLeft;
    public static MelonPreferences_Entry<string> KeyCycleRight;
    public static MelonPreferences_Entry<string> KeySilence;

    public static void Load()
    {
        var cat = MelonPreferences.CreateCategory(Category, "PvZ Replanted Accessibility");

        Enabled = cat.CreateEntry("Enabled", true,
            description: "Master switch. When off, the mod stays loaded but says nothing.");
        Language = cat.CreateEntry("Language", "en",
            description: "Language code. Loads UserData/PvZRA11y/lang/<code>.txt when present, otherwise built-in English.");
        VerboseLogging = cat.CreateEntry("VerboseLogging", false,
            description: "Log every announcement and its origin. Useful when reporting a bug, noisy otherwise.");
        AllowSapi = cat.CreateEntry("AllowSapi", false,
            description: "Fall back to the Windows SAPI voice when no screen reader is running. Can stutter the game.");

        SpeakRoles = cat.CreateEntry("SpeakRoles", true,
            description: "Append the control type, so you hear 'Play, button' instead of just 'Play'.");
        SpeakOnHover = cat.CreateEntry("SpeakOnHover", false,
            description: "Also announce controls the mouse pointer passes over.");
        SpeakScreenChanges = cat.CreateEntry("SpeakScreenChanges", true,
            description: "Announce the name of each screen as it opens.");
        SpeakPositionInList = cat.CreateEntry("SpeakPositionInList", true,
            description: "Append '3 of 7' when moving through a list of controls.");

        PositionCueVolume = cat.CreateEntry("PositionCueVolume", 0.5f,
            description: "Volume of the tone that plays as the lawn cursor moves: left to right is the column, pitch is the row. 0 turns it off.");
        SayTilePosition = cat.CreateEntry("SayTilePosition", false,
            description: "Also speak 'row 3, column 5' on every move.");
        SayZombieArrivals = cat.CreateEntry("SayZombieArrivals", true,
            description: "Announce each zombie as it walks onto the lawn, with its row.");

        SayGameMessages = cat.CreateEntry("SayGameMessages", true,
            description: "Read the text the game paints over the lawn: wave warnings, advice, and everything Crazy Dave says.");
        SonarVolume = cat.CreateEntry("SonarVolume", 0.8f,
            description: "Volume of the sonar tones. Each zombie sounds once: left to right is how far it has walked, pitch is its row, and near ones sound first. 0 leaves only the spoken list.");
        FastZombieCueVolume = cat.CreateEntry("FastZombieCueVolume", 1.0f,
            description: "Volume of the alert for a fast zombie arriving - a pole-vaulter, a football zombie or a bobsled. These reach your plants far sooner than the rest, so they get a sound of their own. 0 turns it off.");
        SayTripwire = cat.CreateEntry("SayTripwire", true,
            description: "Warn when a zombie gets past the warning line, once per row until that row is cleared.");
        TripwireColumn = cat.CreateEntry("TripwireColumn", 3,
            description: "Column the warning line sits on, counted from the left starting at 1. Lower means later warnings.");

        SayEmptyHands = cat.CreateEntry("SayEmptyHands", false,
            description: "Say 'nothing in hand' after a plant leaves your cursor. Off by default: it happens after every planting.");
        AutoCollectSun = cat.CreateEntry("AutoCollectSun", true,
            description: "Gather sun for you as it falls, using the game's own vacuum. Removes the need to find and click each one.");
        AutoCollectItems = cat.CreateEntry("AutoCollectItems", true,
            description: "Also gather coins and the prizes a level drops when won - new seed packets, trophies, presents. These are easy to miss entirely.");
        SayAlmanacHitPoints = cat.CreateEntry("SayAlmanacHitPoints", false,
            description: "Read the hit-point figure hidden inside a zombie's toughness line in the almanac, as in \"Toughness: high, 1370 hit points\". Off by default, matching the original PvZ accessibility mod, which drops it.");

        SayCoinPickups = cat.CreateEntry("SayCoinPickups", false,
            description: "Announce coins as well as prizes. Off by default: coins are frequent and prizes are what you actually need to hear about.");

        AutoFocusFirstControl = cat.CreateEntry("AutoFocusFirstControl", true,
            description: "When a screen opens with nothing focused, put focus on its first control. Without this the keyboard has nothing to act on.");
        UseHitTest = cat.CreateEntry("UseHitTest", false,
            description: "Also hide controls that something else is drawn on top of. Off by default: on this game it rejects everything, and screen-bounds filtering already does the job.");

        KeyActivate = cat.CreateEntry("KeyActivate", "Enter",
            description: "Press the focused control. Only acts when the game left the control unpressed, so it does not double-fire.");
        // Four keys, each asking one kind of question, answered differently depending on
        // where you are. Taken from the original PvZ accessibility mod so that anyone who
        // knows that one already knows this.
        KeyInfo1 = cat.CreateEntry("KeyInfo1", "F1",
            description: "On the lawn: scan your row for zombies. Press twice quickly for which rows have any. In the plant chooser: which zombies this level sends. Elsewhere: repeat the last announcement.");
        KeyInfo2 = cat.CreateEntry("KeyInfo2", "F2",
            description: "On the lawn: full report on the square under the cursor. In the plant chooser: what kind of level this is. Elsewhere: say the current screen and what has focus.");
        KeyInfo3 = cat.CreateEntry("KeyInfo3", "F3",
            description: "On the lawn: say how much sun you have. In the plant chooser, and elsewhere: read out the whole screen.");
        KeyInfo4 = cat.CreateEntry("KeyInfo4", "F4",
            description: "On the lawn: say how far through the level you are. In the plant chooser: the plant you are on, and how many slots are left. Elsewhere: read out the whole screen.");

        KeyStartLevel = cat.CreateEntry("KeyStartLevel", "F6",
            description: "In the plant chooser: start the level. On the lawn: read the whole seed bank without changing what is in your hand. Not Escape, which this game already uses to pause.");
        KeyFreeze = cat.CreateEntry("KeyFreeze", "F5",
            description: "Stop and restart the clock. While frozen you can still walk the lawn, read squares and plant - the zombies simply wait.");
        KeyShovel = cat.CreateEntry("KeyShovel", "Backspace",
            description: "Dig up the plant on the current square. Backspace to match the original PvZ accessibility mod.");
        KeySelfTest = cat.CreateEntry("KeySelfTest", "F11",
            description: "Ask the game about everything and check the answers, writing the result to the log. The fastest way to report that something is wrong.");
        // F12 is deliberately left alone. It belongs to a screen-reader add-on that copies
        // the last spoken line, which is how bug reports about wording get made at all.
        KeyDumpUi = cat.CreateEntry("KeyDumpUi", "F10",
            description: "Write a full description of the current screen to the MelonLoader log. Use this when something is unlabelled.");
        KeyFocusNext = cat.CreateEntry("KeyFocusNext", "Tab",
            description: "Move to the next control. Hold Shift to move back. A fallback for screens the arrow keys do not reach.");
        KeyCycleLeft = cat.CreateEntry("KeyCycleLeft", "Minus",
            description: "Scroll a carousel one step back - the level list today, plant slots later.");
        KeyCycleRight = cat.CreateEntry("KeyCycleRight", "Equals",
            description: "Scroll a carousel one step forward.");
        KeySilence = cat.CreateEntry("KeySilence", "LeftCtrl",
            description: "Stop the current announcement, the same way Ctrl works everywhere else.");

        MelonPreferences.Save();
    }

    /// <summary>
    /// Resolves a configured key name to an InputSystem key. Returns
    /// <see cref="Key.None"/> for blank or unrecognised names.
    /// </summary>
    public static Key ParseKey(MelonPreferences_Entry<string> entry)
    {
        string name = entry?.Value;
        if (string.IsNullOrWhiteSpace(name)) return Key.None;
        if (Enum.TryParse(name.Trim(), ignoreCase: true, out Key key)) return key;

        Core.Log.Warning($"Unknown key name \"{name}\" for {entry.Identifier}. Treating it as unbound.");
        return Key.None;
    }
}
