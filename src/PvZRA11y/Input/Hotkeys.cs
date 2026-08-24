using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Diagnostics;
using PvZRA11y.Gameplay;
using PvZRA11y.Localization;
using PvZRA11y.UI;
using UnityEngine.InputSystem;

namespace PvZRA11y.Input;

/// <summary>
/// The mod's own keyboard layer.
///
/// Four keys carry almost everything: F1 to F4, each asking one kind of question, with the
/// answer depending on where you are. That is the layout the original PvZ accessibility mod
/// uses, and following it is deliberate — someone who knows that mod should not have to
/// learn a second set, and four keys with meanings is less to remember than ten keys with
/// fixed jobs.
///
/// This replaced a layer that had grown to nineteen bindings, eight of them function keys
/// whose meaning could not be guessed from anything: F4 sun, F5 square, F6 layout, F7 row
/// scan, F8 whole lawn. None of it was designed. Each was the next free key at the moment
/// some feature was written, which is how you end up with a control scheme nobody can hold
/// in their head.
///
/// These keys sit alongside the game's rather than replacing them, so arrows and Enter keep
/// doing whatever the game already does with them in menus, and Tab stays with the game on
/// the lawn where it is the fast-forward control.
/// </summary>
public static class Hotkeys
{
    private static Key _activate;
    private static Key _info1;
    private static Key _info2;
    private static Key _info3;
    private static Key _info4;
    private static Key _startLevel;
    private static Key _freeze;
    private static Key _shovel;
    private static Key _selfTest;
    private static Key _dumpUi;
    private static Key _focusNext;
    private static Key _cycleLeft;
    private static Key _cycleRight;
    private static Key _silence;

    /// <summary>Two presses of the same key inside this window count as a double tap.</summary>
    private const long DoubleTapMs = 500;

    private static long _lastInfo1At = long.MinValue;

    /// <summary>Reads the configured keys. Call after Settings.Load and on preference reload.</summary>
    public static void Rebind()
    {
        _activate = Settings.ParseKey(Settings.KeyActivate);
        _info1 = Settings.ParseKey(Settings.KeyInfo1);
        _info2 = Settings.ParseKey(Settings.KeyInfo2);
        _info3 = Settings.ParseKey(Settings.KeyInfo3);
        _info4 = Settings.ParseKey(Settings.KeyInfo4);
        _startLevel = Settings.ParseKey(Settings.KeyStartLevel);
        _freeze = Settings.ParseKey(Settings.KeyFreeze);
        _shovel = Settings.ParseKey(Settings.KeyShovel);
        _selfTest = Settings.ParseKey(Settings.KeySelfTest);
        _dumpUi = Settings.ParseKey(Settings.KeyDumpUi);
        _focusNext = Settings.ParseKey(Settings.KeyFocusNext);
        _cycleLeft = Settings.ParseKey(Settings.KeyCycleLeft);
        _cycleRight = Settings.ParseKey(Settings.KeyCycleRight);
        _silence = Settings.ParseKey(Settings.KeySilence);
    }

    /// <summary>Called once per frame from Core.OnUpdate.</summary>
    public static void Update()
    {
        Keyboard kb;
        try { kb = Keyboard.current; }
        catch { return; }
        if (kb == null) return;

        // Toggling the whole mod has to work even while speech is off.
        if (Held(kb, Key.LeftCtrl) && Held(kb, Key.LeftAlt) && Pressed(kb, Key.A))
        {
            Settings.Enabled.Value = !Settings.Enabled.Value;
            MelonLoader.MelonPreferences.Save();

            if (Settings.Enabled.Value) Speech.SayVerbatim(Strings.T("msg.speech_on"), "toggle");
            else Speech.Silence();

            Core.Log.Msg($"Speech {(Settings.Enabled.Value ? "enabled" : "disabled")}.");
            return;
        }

        if (!Settings.Enabled.Value) return;

        // While a name is being typed, every key belongs to the text field.
        if (TextEntry.IsTyping && !Pressed(kb, _activate)) return;

        // The plant chooser needs the arrows too. Its cards are ordinary buttons, but the
        // game does nothing with the arrow keys there: every recorded session shows the
        // selection landing on one card as the screen opens and never moving again.
        if (SeedChooser.IsActive && HandleChooserMovement(kb)) return;

        // On the lawn the arrow keys walk the grid. In menus, and while a dialog covers the
        // lawn, they belong to the game.
        if (Lawn.HasInput && HandleLawnMovement(kb)) return;

        if (HandleInfoKeys(kb)) return;

        // Checked against IsOnBoard rather than HasInput: freezing is what makes the board
        // paused, so requiring an unpaused board to unfreeze would lock it frozen.
        if (Lawn.IsOnBoard && Pressed(kb, _freeze)) { LawnInput.ToggleFreeze(); return; }

        // Only on the lawn: Backspace means "go back" in menus, and the game owns it there.
        if (Lawn.HasInput && Pressed(kb, _shovel)) { LawnInput.Shovel(); return; }

        // The same key that starts a level from the chooser reads the deck once you are on
        // the lawn. Both answer "what have I got to work with", and on the lawn it is
        // otherwise doing nothing at all.
        if (Lawn.IsOnBoard && Pressed(kb, _startLevel)) { LawnInput.AnnounceBank(); return; }

        if (Pressed(kb, _silence)) { Speech.Silence(); return; }

        if (Pressed(kb, _selfTest))
        {
            SelfTest.Run("requested with a key");
            Speech.SayVerbatim(Strings.T("msg.dump_written"), "self-test");
            return;
        }

        if (Pressed(kb, _dumpUi))
        {
            Probe.DumpCurrentScreen();
            Speech.SayVerbatim(Strings.T("msg.dump_written"), "dump");
            return;
        }

        // Cycling means the seed bank on the lawn and the level carousel in the menus.
        if (Pressed(kb, _cycleLeft) || Pressed(kb, _cycleRight))
        {
            int delta = Pressed(kb, _cycleRight) ? 1 : -1;
            if (SeedChooser.CycleDeck(delta)) return;
            if (LawnInput.CycleSeed(delta)) return;
            if (!LevelSelect.Cycle(delta))
                Speech.Say(Strings.T("msg.no_carousel"), context: "cycle");
            return;
        }

        // Not while the lawn is in charge. Tab is the game's own fast-forward there, and
        // walking between heads-up-display buttons is pointless when the arrows drive the grid.
        if (!Lawn.HasInput && Pressed(kb, _focusNext))
        {
            bool back = Held(kb, Key.LeftShift) || Held(kb, Key.RightShift);
            Focus.Move(back ? -1 : 1);
            return;
        }

        // Last, so it never shadows anything above. A text field being edited gets first
        // refusal, so Enter finishes the entry instead of re-pressing the field.
        if (Pressed(kb, _activate))
        {
            if (TextEntry.HandleSubmit()) return;

            // A speech bubble has no control on it at all, so without this Enter falls
            // through to whatever was focused on the screen before and answers "Not
            // available from here" - leaving a conversation that cannot be got out of.
            if (Lawn.DialogueInFront && Gameplay.Dialogue.Advance()) return;
            if (LawnInput.Plant()) return;
            Focus.ActivateCurrent();
        }
    }

    /// <summary>
    /// The four question keys.
    ///
    /// Each asks one kind of thing, and what that means depends on where you are: on the
    /// lawn they answer about the game, in a menu about the screen. Nothing here needs a
    /// second key or a modifier.
    /// </summary>
    private static bool HandleInfoKeys(Keyboard kb)
    {
        // The plant chooser is checked first: a board exists behind it, so asking "am I on
        // the lawn" says yes while the player is plainly not playing yet.
        if (SeedChooser.IsActive)
        {
            if (Pressed(kb, _info1)) { SeedChooser.AnnounceZombieTypes(); return true; }
            if (Pressed(kb, _info2)) { SeedChooser.AnnounceLevelType(); return true; }
            if (Pressed(kb, _info3)) { Focus.ReadScreen(); return true; }
            if (Pressed(kb, _info4)) { SeedChooser.AnnounceCurrent(); return true; }
        }

        // In the shop the first question key says what is in the purse. Everywhere else it
        // repeats the last thing said, which matters far less than knowing whether you can
        // afford the thing you are standing on.
        if (Store.IsActive && Pressed(kb, _info1))
        {
            Store.AnnounceCoins();
            return true;
        }

        // Before the lawn, and for the same reason as the plant chooser: the almanac can
        // sit on top of a live board, so "am I on the lawn" answers yes while the player is
        // plainly reading an encyclopaedia.
        if (Almanac.IsOnGrid && Pressed(kb, _info4))
        {
            Almanac.AnnounceEntry();
            return true;
        }

        bool onLawn = Lawn.IsOnBoard;

        if (Pressed(kb, _info1))
        {
            if (!onLawn)
            {
                string last = Speech.LastAnnouncement;
                Speech.SayVerbatim(string.IsNullOrEmpty(last) ? Strings.T("msg.nothing_focused") : last, "repeat");
                return true;
            }

            // Pressed twice quickly: which rows have anything in them at all. That is the
            // question behind "where do I plant", and the per-row scan answers a different one.
            long now = Environment.TickCount64;
            bool second = now - _lastInfo1At < DoubleTapMs;
            _lastInfo1At = now;

            if (second) Sonar.ScanRowsWithZombies();
            else Sonar.ScanCurrentRow();
            return true;
        }

        if (Pressed(kb, _info2))
        {
            if (onLawn) LawnInput.AnnounceSquareDetail();
            else Focus.AnnounceCurrent();
            return true;
        }

        if (Pressed(kb, _info3))
        {
            if (onLawn) LawnInput.AnnounceSun();
            else Focus.ReadScreen();
            return true;
        }

        if (Pressed(kb, _info4))
        {
            if (onLawn) LawnInput.AnnounceProgress();
            else Focus.ReadScreen();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Arrow keys and Enter on the plant chooser. Left and right step one plant; up and down
    /// jump a row of eight, which is how the cards are laid out.
    /// </summary>
    private static bool HandleChooserMovement(Keyboard kb)
    {
        if (Pressed(kb, Key.LeftArrow)) return SeedChooser.Move(-1, 0);
        if (Pressed(kb, Key.RightArrow)) return SeedChooser.Move(1, 0);
        if (Pressed(kb, Key.UpArrow)) return SeedChooser.Move(0, -1);
        if (Pressed(kb, Key.DownArrow)) return SeedChooser.Move(0, 1);

        if (Pressed(kb, _activate)) return SeedChooser.Pick();
        if (Pressed(kb, _startLevel)) return SeedChooser.Start();
        return false;
    }

    /// <summary>
    /// Arrow keys on the lawn. Returns true once a direction was handled, so nothing further
    /// treats the same press as something else.
    /// </summary>
    private static bool HandleLawnMovement(Keyboard kb)
    {
        if (Pressed(kb, Key.UpArrow)) return LawnInput.Move(0, -1);
        if (Pressed(kb, Key.DownArrow)) return LawnInput.Move(0, 1);
        if (Pressed(kb, Key.LeftArrow)) return LawnInput.Move(-1, 0);
        if (Pressed(kb, Key.RightArrow)) return LawnInput.Move(1, 0);
        return false;
    }

    private static bool Pressed(Keyboard kb, Key key)
    {
        if (key == Key.None) return false;
        try
        {
            var control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }
        catch { return false; }
    }

    private static bool Held(Keyboard kb, Key key)
    {
        if (key == Key.None) return false;
        try
        {
            var control = kb[key];
            return control != null && control.isPressed;
        }
        catch { return false; }
    }
}
