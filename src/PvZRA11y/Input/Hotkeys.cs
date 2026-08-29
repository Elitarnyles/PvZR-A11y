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

        // Before the lawn, and ahead of everything else that reads a board: the Zen Garden
        // runs on an ordinary board, so every "am I on the lawn" test answers yes there while
        // the player is plainly not playing a level. The garden's own keys have to get first
        // refusal or the lawn handlers would answer for them.
        if (Garden.IsActive && Lawn.HasInput && HandleGarden(kb)) return;

        // On the lawn the arrow keys walk the grid. In menus, and while a dialog covers the
        // lawn, they belong to the game.
        if (Lawn.HasInput && HandleLawnMovement(kb)) return;

        if (HandleInfoKeys(kb)) return;

        // Checked against IsOnBoard rather than HasInput: freezing is what makes the board
        // paused, so requiring an unpaused board to unfreeze would lock it frozen.
        if (Lawn.IsOnBoard && Pressed(kb, _freeze)) { LawnInput.ToggleFreeze(); return; }

        // Only on the lawn: Backspace means "go back" in menus, and the game owns it there.
        if (Lawn.HasInput && Pressed(kb, _shovel)) { LawnInput.Shovel(); return; }

        // Except in the shop, where the game switches its own way out off and leaves nothing
        // to walk to. Backspace already means "back" everywhere a player expects it.
        if (Store.IsActive && Pressed(kb, _shovel) && Store.Leave()) return;

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

            // Crazy Dave also talks inside the shop panel, where there is no speech bubble
            // to notice. But he talks about every item you land on, so "Dave is talking" is
            // the shop's resting state and not a reason to take the key away from buying:
            // treating it as one left the shop with no way to purchase anything at all.
            // Enter moves him on only when there is nothing to press, which is exactly the
            // scene before the taco mini-game, where the game has switched its buttons off.
            if (Store.IsActive && Store.DaveTalking() && !Focus.CanActivateCurrent()
                && Store.AdvanceDave()) return;
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
    private static long _lastGardenSurveyAt = long.MinValue;

    /// <summary>
    /// Everything the keys mean inside the Zen Garden.
    ///
    /// The tool keys follow the original PvZ accessibility mod, at the player's request: the
    /// cycle keys and the digits step through what you own, and the activate key uses it on
    /// the pot you are standing on. The four question keys keep the meanings they have
    /// everywhere else in this mod - survey, detail, money, and the one odd thing on this
    /// screen - so nothing new has to be learned to walk in.
    /// </summary>
    private static bool HandleGarden(Keyboard kb)
    {
        // The shop opens over the garden without changing the game mode, so the garden layer
        // has to stand aside or it would answer for the shop as well.
        if (Garden.InStore() || Store.IsActive) return false;

        if (Pressed(kb, Key.UpArrow)) return GardenInput.Move(0, -1);
        if (Pressed(kb, Key.DownArrow)) return GardenInput.Move(0, 1);
        if (Pressed(kb, Key.LeftArrow)) return GardenInput.Move(-1, 0);
        if (Pressed(kb, Key.RightArrow)) return GardenInput.Move(1, 0);

        if (Pressed(kb, _cycleLeft)) return GardenInput.CycleTool(-1);
        if (Pressed(kb, _cycleRight)) return GardenInput.CycleTool(1);

        for (int digit = 0; digit < 10; digit++)
        {
            Key key = digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1);
            if (!Pressed(kb, key)) continue;

            // The game numbers a row of things one to ten with zero at the end, and the seed
            // bank already works that way, so the tools do too.
            return GardenInput.PickTool(digit == 0 ? 9 : digit - 1);
        }

        if (Pressed(kb, _activate)) return GardenInput.Use();

        // Tab opens the shop and Backspace leaves the garden, which is where the original PvZ
        // accessibility mod puts them. Tab is otherwise the game's fast-forward, and there is
        // nothing to fast-forward here; Backspace already means "back out of this" everywhere
        // else in the mod. From inside the shop, Backspace comes back here through the shop's
        // own way out, which the mod already binds.
        // Both of these press a button on the garden's own top bar, and the mod cannot reach
        // that bar yet: it is built in the interface layer, the board reports no position for
        // any of its buttons, and the only control the mod can see there is the template they
        // were made from. Calling the method behind the shop button directly is worse than
        // useless - it tears the garden down before it fails - so these keys say so plainly
        // until the bar itself can be reached.
        if (Pressed(kb, _focusNext))
        {
            if (Garden.OpenStore()) return true;
            Speech.Say(Strings.T("garden.no_shop"), context: "garden");
            return true;
        }

        if (Pressed(kb, _shovel))
        {
            if (Garden.Leave()) return true;
            Speech.Say(Strings.T("garden.cannot_leave"), context: "garden");
            return true;
        }

        if (Pressed(kb, _info1))
        {
            // Twice quickly asks the wider question, exactly as it does on the lawn: the first
            // press is "what needs me", the second is "what is here at all".
            long now = Environment.TickCount64;
            bool second = now - _lastGardenSurveyAt < DoubleTapMs;
            _lastGardenSurveyAt = now;
            return GardenInput.AnnounceSurvey(second);
        }

        if (Pressed(kb, _info2)) return GardenInput.AnnounceSlot();

        // The garden's currency is coins, not sun, and F3 is already the money question in
        // the shop.
        if (Pressed(kb, _info3)) { Store.AnnounceCoins(); return true; }

        if (Pressed(kb, _info4)) return GardenInput.AnnounceStinky();

        // The same key that reads the seed bank on the lawn reads the whole garden here. Both
        // answer "what have I got", without touching what is in your hand.
        if (Pressed(kb, _startLevel)) return GardenInput.AnnounceGarden();

        return false;
    }

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
