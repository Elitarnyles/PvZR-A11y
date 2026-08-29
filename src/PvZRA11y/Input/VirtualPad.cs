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
            Core.Log.Msg($"[pad] pressed {button} on the virtual controller");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[pad] could not press {button}: {ex.Message}");
            Remove();
            return false;
        }
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

    /// <summary>Takes the device away again, once the game has had time to react.</summary>
    public static void Tick()
    {
        if (_removeIn < 0) return;
        if (--_removeIn > 0) return;

        _removeIn = -1;
        Remove();
    }

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
