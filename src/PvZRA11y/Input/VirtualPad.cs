using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PvZRA11y.Input;

/// <summary>
/// A gamepad that is not there, for the parts of the game that only listen to one.
///
/// The Zen Garden is the reason this exists. Its whole action map — move, back, use, shop,
/// next garden, previous and next tool, poke the snail — is bound to a gamepad and to nothing
/// else. There is not one keyboard binding in it, and two of those actions have no handler in
/// the code at all, so there is no method to call instead: the wiring lives in an interface
/// prefab and answers only to a controller. Without a controller the garden's shop cannot be
/// opened and the garden cannot be left, which in the tutorial means it cannot be finished.
///
/// So the mod adds a controller. Unity's input system will accept a device that no hardware
/// backs, and an event queued onto it is indistinguishable from a real press. This is not a
/// trick played on the game so much as the same door everyone else walks through.
///
/// The device is added for the press and taken away again a few frames later, because a game
/// that believes a controller is plugged in changes its mind about a great deal — which
/// button glyphs to draw, which control scheme is active, sometimes whether the keyboard is
/// listened to at all. Holding it open only as long as the press needs keeps that to a blink.
/// </summary>
public static class VirtualPad
{
    private static Gamepad _pad;
    private static int _removeIn = -1;

    /// <summary>How long the device stays after a press, so the game can finish reacting.</summary>
    private const int KeepForFrames = 12;

    /// <summary>True when a device is currently pretending to be plugged in.</summary>
    public static bool Present => _pad != null;

    /// <summary>
    /// Presses and releases one button.
    ///
    /// Both halves go through in the same call, each followed by an input update, so the game
    /// sees a complete press rather than a button held down for however long it takes the mod
    /// to come round again. A held button would repeat, and a repeat on "leave the garden" is
    /// not something to find out about afterwards.
    /// </summary>
    public static bool Press(GamepadButton button)
    {
        if (!Config.Settings.UseVirtualPad.Value)
        {
            Core.Log.Msg("[pad] the virtual controller is switched off in the settings");
            return false;
        }

        if (!Ensure()) return false;

        try
        {
            var down = new GamepadState().WithButton(button, true);
            var up = new GamepadState();

            InputSystem.QueueStateEvent(_pad, down, -1.0);
            InputSystem.Update();

            InputSystem.QueueStateEvent(_pad, up, -1.0);
            InputSystem.Update();

            _removeIn = KeepForFrames;
            Core.Log.Msg($"[pad] pressed {button} on the virtual controller;" +
                         $" the game now thinks the controls are {ControlType()}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[pad] could not press {button}: {ex.Message}");
            Remove();
            return false;
        }
    }

    /// <summary>
    /// Which controls the game believes are in use.
    ///
    /// Worth logging around every press. A game that has just seen a controller may rebuild a
    /// screen for one - different navigation, different prompts, sometimes controls that are
    /// no longer individually selectable - and that would look from outside like a screen
    /// that came up empty.
    /// </summary>
    public static string ControlType()
    {
        try
        {
            // The lawn's activity only exists during a level; these presses happen in menus
            // too, so the scene is asked as well before giving up.
            var app = Gameplay.Lawn.AppRef
                   ?? UnityEngine.Object.FindObjectOfType<
                          Il2CppReloaded.TreeStateActivities.GameplayActivity>();

            var service = app?.InputService;
            return service == null ? "unknown" : service.CurrentControlType.ToString();
        }
        catch { return "unknown"; }
    }

    private static bool Ensure()
    {
        try { if (_pad != null) return true; }
        catch { _pad = null; }

        // A real controller is better than a made-up one in every way, so if the player has
        // plugged one in, use theirs and never add a second.
        try
        {
            Gamepad already = Gamepad.current;
            if (already != null)
            {
                _pad = already;
                Core.Log.Msg("[pad] using the controller that is already connected");
                return true;
            }
        }
        catch { /* ask for a new one */ }

        try
        {
            _pad = InputSystem.AddDevice<Gamepad>("PvZRA11y virtual pad");
            Core.Log.Msg($"[pad] added a virtual controller: {(_pad == null ? "failed" : _pad.name)}");
            return _pad != null;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[pad] could not add a virtual controller: {ex.Message}");
            _pad = null;
            return false;
        }
    }

    /// <summary>
    /// Hands the game back to the keyboard and mouse.
    ///
    /// Pressing a controller button does not only press a button: the game decides a
    /// controller is in use and rebuilds the screen for one. In the shop that means the item
    /// tiles stop being controls of their own and become the innards of two grid containers
    /// that steer themselves - so the shop opens correctly and then reads as empty, because
    /// there is genuinely nothing left on it to walk to.
    ///
    /// The scheme follows whichever device spoke last, so the mod says something with the
    /// mouse. One pixel out and back is the smallest thing that counts as having been used,
    /// and it lands on a screen where the pointer means nothing anyway.
    /// </summary>
    public static bool HandBackToKeyboard()
    {
        Keyboard keyboard;
        try { keyboard = Keyboard.current; }
        catch { return false; }
        if (keyboard == null) return false;

        try
        {
            // A key, not a mouse move. Moving the pointer was the obvious idea and it does
            // nothing: the input system filters pointer movement out of the decision about
            // which controls are in use, because a pointer drifts on its own and would
            // otherwise be taking the controls back from a player holding a gamepad.
            //
            // OEM5 is chosen because nothing in this game binds it - checked against the
            // whole action asset - so it actuates the keyboard without doing anything else.
            var down = new KeyboardState();
            down.Press(Key.OEM5);

            InputSystem.QueueStateEvent(keyboard, down, -1.0);
            InputSystem.Update();

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(), -1.0);
            InputSystem.Update();

            Core.Log.Msg($"[pad] handed the controls back; the game now thinks they are {ControlType()}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[pad] could not hand the controls back: {ex.Message}");
            return false;
        }
    }

    /// <summary>Takes the device away again, once the game has had time to react.</summary>
    public static void Tick()
    {
        if (_handBackIn > 0 && --_handBackIn == 0) HandBackToKeyboard();

        if (_removeIn < 0) return;
        if (--_removeIn > 0) return;

        _removeIn = -1;
        Remove();
    }

    private static int _handBackIn;

    /// <summary>
    /// Presses a button and then gives the controls straight back.
    ///
    /// For the presses that open a screen. The button has to arrive as a controller or the
    /// game will not act on it at all, but the screen it opens has to be built for a keyboard
    /// or there will be nothing on it to read.
    /// </summary>
    public static bool PressThenHandBack(GamepadButton button)
    {
        if (!Press(button)) return false;

        // After the press has landed and the screen has had time to come up. Handing the
        // controls back in the same frame would arrive before the screen was built and the
        // game would build it for a controller anyway.
        _handBackIn = HandBackAfterFrames;
        return true;
    }

    /// <summary>Long enough for the screen the press opened to exist.</summary>
    private const int HandBackAfterFrames = 6;

    private static void Remove()
    {
        if (_pad == null) return;

        // Never unplug a controller the player actually owns.
        try
        {
            if (Gamepad.current != null && _pad.deviceId == Gamepad.current.deviceId
                && !_pad.name.Contains("virtual"))
            {
                _pad = null;
                return;
            }
        }
        catch { /* fall through and remove it */ }

        try
        {
            InputSystem.RemoveDevice(_pad);
            Core.Log.Msg("[pad] removed the virtual controller");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[pad] could not remove the virtual controller: {ex.Message}");
        }

        _pad = null;
    }
}
