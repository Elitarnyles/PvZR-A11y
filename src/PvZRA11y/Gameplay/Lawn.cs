using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using Il2CppReloaded.TreeStateActivities;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using PvZRA11y.Localization;
using PvZRA11y.UI;
using UnityEngine;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The lawn: moving around it, and saying what is there.
///
/// The cursor is the game's own <see cref="GamepadCursor"/> rather than one of ours. That
/// is the single most useful thing about modding this version instead of the original: the
/// game already has a cursor that snaps to grid squares and knows about pool tiles, roof
/// slopes and the odd placements that break naive coordinate maths. Driving it means
/// planting goes through the game's normal code path, so anything it checks — cost, cooldown,
/// whether a lily pad is needed — is checked for us, and nothing has to be kept in sync.
///
/// Grid coordinates here are the game's: x is the column, 0 at the left, y is the row,
/// 0 at the top. Everything spoken is converted to 1-based, because "row 0" helps nobody.
/// </summary>
public static class Lawn
{
    private static Board _board;

    /// <summary>
    /// The activity that owns the board. It outlives individual boards, which is the whole
    /// reason it is kept: it is the only thing that can be asked whether the board we hold
    /// is still the board the game is playing.
    /// </summary>
    private static GameplayActivity _app;

    /// <summary>Single-player, so player 0 throughout.</summary>
    private const int Player = 0;

    /// <summary>PvZ lawns are always nine columns wide.</summary>
    public const int Columns = 9;

    /// <summary>Pitch range for the position cue. Low notes are the far rows, high are the near ones.</summary>
    private const float LowestTone = 420f;
    private const float HighestTone = 900f;

    public static bool IsOnBoard => _board != null;

    /// <summary>The live board, for the other lawn helpers. Null off the lawn.</summary>
    internal static Board BoardRef => _board;

    /// <summary>
    /// Panels that take the keyboard away from the lawn.
    ///
    /// Deliberately a list of what blocks rather than a list of what allows. The in-game
    /// display is not one panel but several shown together — the HUD, the fast-forward
    /// button, transient messages — and an allow-list silently locked the player out of
    /// moving the moment any of them happened to be on top. Naming the dialogs instead
    /// means an unfamiliar panel costs nothing: at worst Enter tries to plant while a
    /// window is open, which is a nuisance, where the other way round is a dead keyboard.
    /// </summary>
    /// Only ids seen in a real session go in here. A guessed name would either do nothing
    /// or, worse, match something that is not a dialog at all.
    private static readonly HashSet<string> BlockingPanels = new(StringComparer.Ordinal)
    {
        "gameOptions",
        "serializedrestart",
        "options",
        "awardScreen",
        "speechBubble",
        // The board is already built behind the plant chooser, so without this the arrow
        // keys would walk the lawn while the player is trying to pick a deck.
        "seedChooser",

        // The game's generic yes/no message box. Modal, and the lawn is usually still
        // there underneath it.
        "dialog",
        "dialogZengarden",
    };

    private static bool _lastHadInput = true;

    /// <summary>Set when the zombies win, which the board does not otherwise flag.</summary>
    private static bool _levelLost;

    /// <summary>
    /// True while the player has deliberately stopped the clock.
    ///
    /// This is the one case where a paused board must keep the keyboard. Everything else
    /// that pauses the game does so to put a window in front of you; freezing does the
    /// opposite — it stops the zombies precisely so you can take your time walking the
    /// lawn, reading squares and planting without being eaten while you think.
    /// </summary>
    public static bool Frozen { get; private set; }

    /// <summary>
    /// Stops or restarts the game clock, using the game's own pause so that everything
    /// halts together rather than only the parts we thought to stop.
    /// </summary>
    public static bool ToggleFreeze()
    {
        if (_board == null) return false;

        bool wanted = !Frozen;

        // Stopping the clock rather than asking the game to pause. Both of the game's own
        // pauses were tried first and neither stopped anything: Board.Pause only marks the
        // board, and GameplayActivity.GamePause returned without complaint while the
        // zombies kept walking.
        //
        // The level advances off an accumulator fed by delta time, so a time scale of zero
        // starves it. Our own per-frame work is unaffected — Unity keeps calling Update at
        // any time scale — which is exactly the split this needs: the lawn holds still while
        // you keep moving over it.
        try
        {
            Time.timeScale = wanted ? 0f : 1f;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not change the game clock: {ex.Message}");
            return false;
        }

        Frozen = wanted;
        Core.Log.Msg($"[lawn] clock {(Frozen ? "stopped" : "running")}");
        return true;
    }

    /// <summary>
    /// Puts the clock back. Called when a level starts, so a game frozen at the moment the
    /// player quit never comes back frozen with no obvious way out.
    /// </summary>
    private static void ClearFreeze()
    {
        if (Frozen)
        {
            try { Time.timeScale = 1f; }
            catch (Exception ex) { Core.Log.Warning($"Could not restore the game clock: {ex.Message}"); }
        }
        Frozen = false;
    }

    public static void NoteLevelLost()
    {
        if (_levelLost) return;
        _levelLost = true;
        Core.Log.Msg("[lawn] level lost");
    }

    /// <summary>
    /// Whether the lawn should be receiving the arrow keys and Enter right now.
    ///
    /// A board existing is not the same as a board being in charge. Pause the game and the
    /// board is still there, still fully readable — but the keys belong to the dialog on
    /// top of it, and Enter must press its buttons rather than try to plant something.
    ///
    /// Reading the lawn stays available either way; it is only acting on it that defers.
    /// </summary>
    public static bool HasInput
    {
        get
        {
            if (_board == null) return false;

            bool allowed = !_levelLost;

            // The level being over is the general answer, and it is the game's own: once
            // it is finished the lawn is a backdrop and every key belongs to whatever is
            // being shown on top of it. Matching panel names one at a time was a losing
            // game — there is always another screen that was not on the list.
            if (allowed)
            {
                try { if (_board.mLevelComplete) allowed = false; }
                catch { /* if the game will not say, assume play continues */ }
            }

            // A board paused by us is still ours to walk around; a board paused by the game
            // has a window on top of it and the keys belong there.
            if (allowed && !Frozen)
            {
                try { if (_board.mPaused) allowed = false; }
                catch { /* same */ }
            }

            if (allowed)
            {
                string front = UI.PanelScope.FrontPanelId;
                if (front != null && BlockingPanels.Contains(front)) allowed = false;
            }

            if (allowed != _lastHadInput)
            {
                _lastHadInput = allowed;
                Core.Log.Msg($"[lawn] keyboard {(allowed ? "returned to the lawn" : "handed to the interface")}" +
                             $" (front panel: {UI.PanelScope.FrontPanelId ?? "none"})");
            }

            return allowed;
        }
    }

    public static void NoteBoard(Board board)
    {
        _board = board;
        _levelLost = false;
        _lastHadInput = true;
        ClearFreeze();

        try { _app = board?.mApp; } catch { _app = null; }

        Core.Log.Msg($"[lawn] board ready, {SafeRowCount()} rows");
    }

    /// <summary>
    /// Keeps the board pointer honest, once a frame.
    ///
    /// The pointer used to be cleared only when a scene with some other name loaded. A level
    /// restart reloads a scene called "Gameplay", and finishing a level loads no scene at
    /// all, so in both cases the mod went on holding a board the game had already torn down.
    /// Everything then read off it threw, three times a second, and the sonar reported the
    /// resulting emptiness as "All clear" — a confident answer about a lawn that no longer
    /// existed.
    ///
    /// Rather than guess at the moment of death, the owner is asked which board it is playing
    /// now. That answers restart, completion and defeat with one question, and it heals
    /// itself: if the game has a board, the mod finds it, so a wrong guess here can never
    /// leave the player without a keyboard for a whole level.
    /// </summary>
    public static void TickBoardLifetime()
    {
        if (_app == null) return;

        Board current;
        try { current = _app.Board; }
        catch (Exception ex)
        {
            // The activity itself has gone. Nothing is playable through it any more.
            Core.Log.Warning($"[lawn] could not ask for the current board: {ex.Message}");
            _app = null;
            if (_board != null) NoteBoardGone();
            return;
        }

        if (SameBoard(current, _board))
        {
            _mismatchFrames = 0;
            return;
        }

        // Not acted on the first frame it is seen. InitLevel hands us the board through a
        // Harmony postfix, and nothing proves the activity has finished pointing at it by
        // then — so a single frame of disagreement is far more likely to be the game still
        // wiring a level up than a board that has died. Acting on it immediately would throw
        // away a live lawn and take the arrow keys with it for the rest of the level, which
        // is a worse failure than the one this whole method exists to fix.
        if (++_mismatchFrames < MismatchFramesBeforeActing) return;
        _mismatchFrames = 0;

        if (current == null)
        {
            NoteBoardGone();
            return;
        }

        // A board we were never handed — a restart that reused the activity. Take it.
        Core.Log.Msg("[lawn] the game swapped the board underneath us; picking up the new one");
        NoteBoard(current);
    }

    /// <summary>Consecutive frames the activity has disagreed with the board we hold.</summary>
    private static int _mismatchFrames;

    /// <summary>
    /// How long that disagreement must last before it is believed. Short enough that a dead
    /// board is dropped within a tenth of a second, long enough to sit out the frame or two
    /// in which a level is being assembled.
    /// </summary>
    private const int MismatchFramesBeforeActing = 5;

    /// <summary>
    /// Compares two boards by their native pointer.
    ///
    /// The managed wrappers are separate objects even when they stand for the same native
    /// board, and Board is not a UnityEngine.Object, so neither reference equality nor
    /// Unity's destroyed-object test says anything useful here.
    /// </summary>
    private static bool SameBoard(Board a, Board b)
    {
        if (a == null || b == null) return a == null && b == null;
        try { return a.Pointer == b.Pointer; }
        catch { return false; }
    }

    public static void NoteBoardGone()
    {
        if (_board == null) return;

        _board = null;
        ClearFreeze();
        Core.Log.Msg("[lawn] board gone");
    }

    #region Cursor

    /// <summary>The game's grid cursor for player one, or null when there is no board.</summary>
    private static GamepadCursor Cursor()
    {
        if (_board == null) return null;
        try
        {
            var cursors = _board.GamepadCursors;
            if (cursors == null || cursors.Length == 0) return null;
            return cursors[Player];
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not reach the grid cursor: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Makes sure the grid cursor is switched on. The game turns it off whenever it decides
    /// the player is on mouse and keyboard, so this is re-asserted rather than done once.
    /// </summary>
    private static GamepadCursor ActiveCursor()
    {
        GamepadCursor cursor = Cursor();
        if (cursor == null) return null;

        try { if (!cursor.Enabled) cursor.Enabled = true; }
        catch (Exception ex) { Core.Log.Warning($"Could not enable the grid cursor: {ex.Message}"); }

        return cursor;
    }

    public static bool TryGetPosition(out int x, out int y)
    {
        x = y = 0;
        GamepadCursor cursor = Cursor();
        if (cursor == null) return false;
        try
        {
            x = cursor.m_gridX;
            y = cursor.m_gridY;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Moves the cursor one square. Returns false when it did not move, which means the
    /// edge of the lawn — worth a distinct sound rather than silence.
    /// </summary>
    /// <summary>
    /// Panels that are a character talking to you and nothing else.
    ///
    /// They carry no button. In the game you click anywhere to move the conversation on,
    /// which leaves a keyboard player with nothing to press and a bubble that never ends.
    /// </summary>
    private static readonly HashSet<string> DialoguePanels = new(StringComparer.Ordinal)
    {
        "speechBubble",
    };

    /// <summary>
    /// When the end of a message was last announced, so a second press can close it.
    ///
    /// Zero for "not waiting", never long.MinValue: the window test subtracts this from
    /// Environment.TickCount64, and subtracting long.MinValue overflows to a negative
    /// number, which made the guard false on the very first press. The two-step close this
    /// was written for therefore never once happened — "waiting for a second press" appears
    /// in no log on this machine, while "closed on a second press" appears three times.
    /// </summary>
    private static long _endOfMessageAt;

    /// <summary>How long that second press stays available.</summary>
    private const long EndOfMessageWindowMs = 4000;

    /// <summary>
    /// Ends a conversation the way the game does, not the way that was convenient.
    ///
    /// The mod used to call CrazyDaveLeave, which simply sends the character away. That is a
    /// passthrough to the service that owns his bubble, his animation and his sound, and
    /// nothing else — so when Crazy Dave offered to sell an extra seed slot, the offer went
    /// away with him and the player never saw the question. He reported it as "I cannot get
    /// to the buttons", and he was right that there are buttons: the game has a yes/no
    /// message box on the panel id "dialog", with the price given in the line just before it.
    ///
    /// The decision to raise that box lives a layer up, on the cut scene, in the method that
    /// also owns CanGetPacketUpgrade. Calling it lets the game decide what the end of a
    /// conversation means — dismiss it, or put a question up — which is the whole point.
    /// </summary>
    private static bool FinishConversation(CrazyDaveState state)
    {
        CutScene scene = null;
        try { scene = _board?.mCutScene; } catch { /* fall through to the old route */ }

        if (scene != null)
        {
            try
            {
                scene.AdvanceCrazyDaveDialog(true);
                Core.Log.Msg($"[dialogue] handed the end of the conversation to the cut scene (was {state})");
                return true;
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"[dialogue] the cut scene refused: {ex.Message}");
            }
        }

        // Last resort. This is the call that used to throw offers away, so it is only made
        // when the proper route is not there at all, and it says so in the log.
        if (_app == null) return false;

        try
        {
            _app.CrazyDaveLeave();
            Core.Log.Msg($"[dialogue] no cut scene; sent him away instead (was {state})");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[dialogue] could not end the conversation: {ex.Message}");
            return false;
        }
    }

    /// <summary>The kind of dialog on screen, for the log, without risking a throw.</summary>
    private static string SafeDialogType(Dialog dialog)
    {
        try { return dialog.mDialogType.ToString(); }
        catch { return "unknown"; }
    }

    /// <summary>What Crazy Dave is doing, for the log. Never throws.</summary>
    public static string DaveState()
    {
        try { return _app == null ? "no activity" : _app.CrazyDaveState.ToString(); }
        catch { return "unreadable"; }
    }

    /// <summary>True while a character is talking and the conversation is what Enter means.</summary>
    public static bool DialogueInFront => DialoguePanels.Contains(PanelScope.FrontPanelId ?? "");

    /// <summary>
    /// Moves a character's dialogue on, the way clicking does.
    ///
    /// Returns false when there was nothing to advance. The game's own method reports
    /// whether more text followed, which is also how the last line is recognised.
    /// </summary>
    public static bool AdvanceDialogue(out bool moreToCome)
    {
        moreToCome = false;
        if (_app == null) return false;

        try
        {
            moreToCome = _app.AdvanceCrazyDaveText();
            if (moreToCome)
            {
                _endOfMessageAt = 0;
                Core.Log.Msg("[dialogue] advanced to the next line");
                return true;
            }

            // On the last line the game's own advance does nothing and reports that nothing
            // followed. What happens next is not ours to decide, and guessing cost a real
            // purchase: Crazy Dave's offer of a seventh seed slot arrives as an ordinary
            // speech bubble, and sending him away at the end of it threw the offer out
            // before its buttons ever appeared. The log recorded it as
            // "last line; sent him on his way (was Idling)" — indistinguishable from any
            // other conversation ending.
            CrazyDaveState state = _app.CrazyDaveState;
            Dialog dialog = null;
            try { dialog = Dialog.CurrentDialog; } catch { /* older builds may not have it */ }

            if (dialog != null)
            {
                Core.Log.Msg($"[dialogue] last line, and a dialog is waiting ({SafeDialogType(dialog)}); left alone");
                return false;
            }

            if (state == CrazyDaveState.HandingTalking || state == CrazyDaveState.HandingIdling)
            {
                Core.Log.Msg($"[dialogue] last line, but Dave is handing something over ({state}); left alone");
                return false;
            }

            // Two presses to end a conversation, not one. The first says so, which is also
            // how you learn there is nothing more; the second closes it. Anything the game
            // wants to put on screen — a question, a price, a pair of buttons — has the gap
            // between them to appear in, and if it does the first branch above catches it.
            long now = Environment.TickCount64;
            if (_endOfMessageAt == 0 || now - _endOfMessageAt > EndOfMessageWindowMs)
            {
                _endOfMessageAt = now;
                Core.Log.Msg($"[dialogue] last line (state {state}); waiting for a second press before closing");
                Speech.Say(Strings.T("dialogue.end"), interrupt: false, context: "dialogue");
                return true;
            }

            _endOfMessageAt = 0;
            return FinishConversation(state);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"[dialogue] could not advance: {ex.Message}");
            return false;
        }
    }

    private static Il2Cpp.TimeCycler _cycler;

    /// <summary>
    /// How fast the game is running, as a multiple of normal — or null when it cannot be
    /// read.
    ///
    /// Taken from the fast-forward control on the heads-up display rather than from the
    /// settings service. The settings service holds the speed chosen in the options menu and
    /// does not move when the in-game control is used: measured on a real level, it read
    /// Normal throughout while the game plainly sped up. The control carries an array of
    /// speeds and an index into it, so the number it reports is the game's own, not a guess.
    ///
    /// Read, never patched. An earlier attempt to learn this by patching the activity's
    /// ToggleFastMo hung the game at startup with nothing in the log at all.
    /// </summary>
    public static float? SpeedMultiplier()
    {
        try
        {
            if (_cycler == null)
                _cycler = UnityEngine.Object.FindObjectOfType<Il2Cpp.TimeCycler>();

            if (_cycler == null) return null;

            var cycles = _cycler.m_cycles;
            if (cycles == null || cycles.Length == 0) return null;

            int index = _cycler.m_cycleIndex;
            if (index < 0 || index >= cycles.Length) return null;

            var cycle = cycles[index];
            return cycle?.TimeScale;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the game speed: {ex.Message}");
            _cycler = null;
            return null;
        }
    }

    /// <summary>What the fast-forward control shows on screen, for the log.</summary>
    public static string SpeedLabel()
    {
        try { return _cycler?.m_textMeshProUGUI?.text; }
        catch { return null; }
    }

    /// <summary>The speed chosen in the options menu. Not what the in-game control changes.</summary>
    public static GameplaySpeed? SpeedSetting()
    {
        try
        {
            ISettingsService settings = _app?.SettingsService;
            return settings == null ? null : settings.GameplaySpeed;
        }
        catch { return null; }
    }

    /// <summary>What came of trying to move the cursor.</summary>
    public enum MoveOutcome
    {
        /// <summary>The cursor is on a different square than it was.</summary>
        Moved,

        /// <summary>The cursor is against the side of the lawn and cannot go further.</summary>
        Edge,

        /// <summary>The cursor could not be read or driven at all.</summary>
        CursorLost,
    }

    /// <summary>
    /// Steps the game's grid cursor one square.
    ///
    /// Three outcomes rather than true and false, because the old boolean collapsed four
    /// separate failures into one. A cursor that could not be found and a cursor pressed
    /// against the left wall produced the same single note, so a player whose cursor had
    /// broken heard the entire lawn as one continuous edge and had no way to tell.
    /// </summary>
    public static MoveOutcome Move(int dx, int dy)
    {
        GamepadCursor cursor = ActiveCursor();
        if (cursor == null) return MoveOutcome.CursorLost;

        if (!TryGetPosition(out int fromX, out int fromY)) return MoveOutcome.CursorLost;

        try { cursor.UpdateGridPositionFromDelta(dx, dy); }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not move the grid cursor: {ex.Message}");
            return MoveOutcome.CursorLost;
        }

        if (!TryGetPosition(out int toX, out int toY)) return MoveOutcome.CursorLost;
        return (toX != fromX || toY != fromY) ? MoveOutcome.Moved : MoveOutcome.Edge;
    }

    #endregion

    #region Position cue

    /// <summary>
    /// Plays the position of a square: stereo balance is the column, pitch is the row.
    /// </summary>
    public static void PlayPositionCue(int x, int y)
    {
        float volume = Settings.PositionCueVolume.Value;
        if (volume <= 0f) return;

        float pan = Columns <= 1 ? 0.5f : Mathf.Clamp01(x / (float)(Columns - 1));

        Tones.Play(ToneForRow(y), pan, 90, volume);
    }

    /// <summary>
    /// The pitch that stands for a row. Shared with the sonar on purpose: a zombie in your
    /// row sounds at the same pitch as the cursor, so the two readings line up instead of
    /// being two separate things to learn.
    /// </summary>
    public static float ToneForRow(int y)
    {
        int rows = Math.Max(1, SafeRowCount());
        // Row 0 is the far side of the lawn, so it gets the lowest pitch.
        float up = rows <= 1 ? 0.5f : 1f - (Mathf.Clamp(y, 0, rows - 1) / (float)(rows - 1));
        return LowestTone + (HighestTone - LowestTone) * up;
    }

    public static void PlayEdgeCue()
    {
        float volume = Settings.PositionCueVolume.Value;
        if (volume > 0f) Tones.PlayEdge(volume);
    }

    #endregion

    #region Describing a square

    /// <summary>
    /// Everything worth knowing about the square under the cursor: what is planted there,
    /// what the ground is, whether the plant in hand could go there, and whether the row
    /// still has its mower.
    /// </summary>
    /// <summary>
    /// Set by anything in a square description that could not be read.
    ///
    /// Cleared at the top of every DescribeSquare, so it never leaks from one square to the
    /// next. It exists because a failed read looks exactly like an empty square: the plant
    /// comes back null, the ground comes back null, the zombie list comes back null, and
    /// "empty" is the honest answer to all three only when nothing threw.
    /// </summary>
    private static bool _readFailed;

    public static string DescribeSquare(int x, int y)
    {
        _readFailed = false;
        if (_board == null) return null;

        var parts = new List<string>(4);

        string occupant = OccupantOf(x, y);

        // Zombies standing here are part of what is on the square, not a separate question.
        string standing = Sonar.DescribeZombiesAt(y, x);
        if (Sonar.LastCollectFailed || Sonar.LastSkipped > 0) _readFailed = true;

        if (!string.IsNullOrEmpty(occupant))
        {
            parts.Add(occupant);
        }
        else
        {
            // Water and roof stay whatever else is here, because they decide what can be
            // planted. "Empty" is different: it only ever meant "nothing planted", and with
            // a zombie on the square it is both wrong-sounding and useless — the square is
            // plainly not empty. So it is said only when there is genuinely nothing to say.
            string ground = GroundOf(x, y);

            if (!string.IsNullOrEmpty(ground)) parts.Add(ground);
            else if (_readFailed) parts.Add(Strings.T("lawn.unreadable"));
            else if (string.IsNullOrEmpty(standing)) parts.Add(Strings.T("lawn.empty"));
        }

        if (!string.IsNullOrEmpty(standing)) parts.Add(standing);

        string planting = PlantingStateOf(x, y);
        if (!string.IsNullOrEmpty(planting)) parts.Add(planting);

        if (Settings.SayTilePosition.Value)
            parts.Add(Strings.T("lawn.position", y + 1, x + 1));

        return string.Join(", ", parts);
    }

    /// <summary>The plant or obstacle occupying a square, or null when it is bare.</summary>
    private static string OccupantOf(int x, int y)
    {
        try
        {
            Plant plant = _board.GetTopPlantAt(x, y, PlantPriority.Any);
            if (plant != null)
            {
                string name = PlantName(plant.mSeedType);
                string hurt = HealthPhrase(plant);
                return hurt == null ? name : $"{name}, {hurt}";
            }

            if (_board.GetGridItemAt(GridItemType.Gravestone, x, y) != null) return Strings.T("lawn.gravestone");
            if (_board.GetGridItemAt(GridItemType.Crater, x, y) != null) return Strings.T("lawn.crater");
            if (_board.GetGridItemAt(GridItemType.Ladder, x, y) != null) return Strings.T("lawn.ladder");
            if (_board.IsIceAt(x, y)) return Strings.T("lawn.ice");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read square {x},{y}: {ex.Message}");
            _readFailed = true;
        }
        return null;
    }

    /// <summary>
    /// How damaged a plant is, or null while it is untouched.
    ///
    /// Only spoken once something has bitten it, because on a full lawn the healthy plants
    /// are the ones you do not need to hear about — and a percentage on every square would
    /// bury the one square that matters. Given as a percentage rather than raw points: a
    /// wall-nut has four thousand and a peashooter three hundred, so the numbers mean
    /// nothing next to each other.
    /// </summary>
    public static string HealthPhrase(Plant plant)
    {
        if (plant == null) return null;
        try
        {
            int max = plant.mPlantMaxHealth;
            int now = plant.mPlantHealth;
            if (max <= 0 || now >= max) return null;

            int percent = Mathf.Clamp(Mathf.RoundToInt(now / (float)max * 100f), 0, 100);
            return Strings.T("lawn.health", percent);
        }
        catch { return null; }
    }

    /// <summary>The plant on a square with its condition, for the detailed report.</summary>
    public static string PlantConditionAt(int x, int y)
    {
        if (_board == null) return null;
        try
        {
            Plant plant = _board.GetTopPlantAt(x, y, PlantPriority.Any);
            if (plant == null) return null;

            int max = plant.mPlantMaxHealth;
            int now = plant.mPlantHealth;
            if (max <= 0) return null;

            int percent = Mathf.Clamp(Mathf.RoundToInt(now / (float)max * 100f), 0, 100);
            return Strings.T("lawn.health", percent);
        }
        catch { return null; }
    }

    /// <summary>
    /// What the ground is, when that is worth a word — or null for ordinary lawn.
    ///
    /// Null rather than "empty" so the caller decides. Plain lawn is the absence of news,
    /// and whether the absence of news is worth saying depends on what else is on the
    /// square.
    /// </summary>
    private static string GroundOf(int x, int y)
    {
        try
        {
            if (_board.IsPoolSquare(x, y)) return Strings.T("lawn.water");
            if (_board.StageHasRoof()) return Strings.T("lawn.roof");
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the ground at {x},{y}: {ex.Message}");
            _readFailed = true;
        }
        return null;
    }

    /// <summary>
    /// Why the plant currently in hand can or cannot go here. Silent when nothing is held,
    /// since the reason would be meaningless.
    /// </summary>
    private static string PlantingStateOf(int x, int y)
    {
        SeedType held = HeldSeed();
        if (held == SeedType.None) return null;

        try
        {
            PlantingReason reason = _board.CanPlantAt(x, y, held);
            string key = "planting." + reason;
            return Strings.Has(key) ? Strings.T(key) : Strings.T("planting.blocked");
        }
        catch { return null; }
    }

    /// <summary>The seed the player is currently holding, or None.</summary>
    public static SeedType HeldSeed()
    {
        if (_board == null) return SeedType.None;
        try
        {
            CursorObject cursor = _board.CursorObjects[Player];
            if (cursor == null) return SeedType.None;
            return cursor.CursorType == CursorType.PlantFromBank ? cursor.Type : SeedType.None;
        }
        catch { return SeedType.None; }
    }

    /// <summary>
    /// A plant's spoken name. Enum names are mostly readable already, so only the ones that
    /// are not get an entry; the rest are split into words on the fly and stay translatable.
    /// </summary>
    public static string PlantName(SeedType seed)
    {
        string key = "plant." + seed;
        return Strings.Has(key) ? Strings.T(key) : UiText.Prettify(seed.ToString());
    }

    #endregion

    #region Acting on a square

    /// <summary>
    /// Plants whatever is in hand on the square under the cursor, by replaying the click
    /// the game would receive from a mouse. Going through MouseDown rather than AddPlant
    /// means sun is spent, cooldowns start and every placement rule is enforced — none of
    /// which we would want to reimplement.
    /// </summary>
    public static bool PlantAtCursor()
    {
        if (_board == null) return false;
        if (!TryGetPosition(out int x, out int y)) return false;

        if (!TryPixelForSquare(x, y, out int px, out int py))
        {
            Core.Log.Warning($"[lawn] no pixel position maps back to square {x},{y}; not planting");
            return false;
        }

        try
        {
            Core.Log.Msg($"[lawn] click at pixel {px},{py} for square {x},{y}");
            _board.MouseDown(px, py, 1, Player);
            _board.MouseUp(px, py, 1, Player);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not plant at {x},{y}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds a pixel inside a given square, and proves it by converting back.
    ///
    /// GridToPixel may return a corner or a centre depending on the level type, so rather
    /// than assume, each candidate is round-tripped through PixelToGrid and only accepted
    /// if it lands on the square we meant. That turns a guess into a check.
    /// </summary>
    private static bool TryPixelForSquare(int x, int y, out int px, out int py)
    {
        px = py = 0;
        try
        {
            int baseX = _board.GridToPixelX(x, y);
            int baseY = _board.GridToPixelY(x, y);

            // Corner first, then half a cell in, covering both conventions.
            int[] offsetsX = { 0, 40, -40 };
            int[] offsetsY = { 0, 50, -50 };

            foreach (int dx in offsetsX)
            foreach (int dy in offsetsY)
            {
                int tryX = baseX + dx;
                int tryY = baseY + dy;

                if (_board.PixelToGridX(tryX, tryY) != x) continue;
                if (_board.PixelToGridY(tryX, tryY) != y) continue;

                px = tryX;
                py = tryY;
                return true;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not map square {x},{y} to a pixel: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Digs up whatever is planted on the square under the cursor.
    ///
    /// Routed through the board's tool click rather than removing the plant directly, so
    /// the game keeps control of what a shovel is allowed to do — a Cob Cannon takes two
    /// squares, a pumpkin comes off before the plant inside it, and none of that has to be
    /// known here.
    /// </summary>
    /// <summary>What the cursor is holding right now, for the log and for putting it back.</summary>
    public static CursorType? CursorKind()
    {
        try { return _board?.CursorObjects[Player]?.CursorType; }
        catch { return null; }
    }

    /// <summary>
    /// Puts the shovel down after digging.
    ///
    /// The mod reaches the shovel by handing the board a click that already carries the
    /// tool, which is not the route the game itself takes — it normally picks the shovel up
    /// from its button first. Whatever the game does at the end of that longer route, it
    /// does not happen here: the cursor was left holding the shovel afterwards, and while a
    /// tool is in hand the game refuses to select a plant. So the number keys went dead and
    /// stayed dead, with nothing to say why.
    ///
    /// Reported by the player, who dug while holding a plant and then found the digits
    /// stopped responding entirely.
    /// </summary>
    public static void PutToolDown()
    {
        if (_board == null) return;

        CursorType? before = CursorKind();
        if (before != CursorType.Shovel && before != CursorType.Hammer) return;

        try { _board.ClearCursor(true, Player); }
        catch (Exception ex) { Core.Log.Warning($"[lawn] could not clear the cursor: {ex.Message}"); }

        // Belt and braces. If the game's own clear did not take, the cursor type is set back
        // by hand — a stuck tool costs the player the number keys for the rest of the level.
        if (CursorKind() != CursorType.Normal)
        {
            try { _board.CursorObjects[Player].CursorType = CursorType.Normal; }
            catch (Exception ex) { Core.Log.Warning($"[lawn] could not reset the cursor: {ex.Message}"); }
        }

        Core.Log.Msg($"[lawn] tool put down: {before} -> {CursorKind()}");
    }

    public static bool ShovelAtCursor(out string removed)
    {
        removed = null;
        if (_board == null) return false;
        if (!TryGetPosition(out int x, out int y)) return false;

        try
        {
            Plant plant = _board.GetTopPlantAt(x, y, PlantPriority.Any);
            if (plant == null) return false;
            removed = PlantName(plant.mSeedType);
        }
        catch { /* name is a courtesy; carry on without it */ }

        if (!TryPixelForSquare(x, y, out int px, out int py))
        {
            Core.Log.Warning($"[lawn] no pixel position maps back to square {x},{y}; not digging");
            return false;
        }

        try
        {
            Core.Log.Msg($"[lawn] shovel at pixel {px},{py} for square {x},{y}");
            _board.MouseDownWithTool(px, py, 1, CursorType.Shovel, Player);
            _board.MouseUp(px, py, 1, Player);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not dig up the plant at {x},{y}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sweeps up everything lying on the lawn, using the game's own vacuum — the same one
    /// the Gold Magnet uses, so each item animates and scores exactly as if it were clicked.
    ///
    /// Two passes, because the vacuum has two modes and neither covers everything. The
    /// global pass takes sun wherever it fell; the nearby pass, given a radius larger than
    /// the lawn, takes coins and the prizes a level drops when it is won — new seed packets,
    /// trophies, presents. Those are the things a sighted player spots and clicks, and the
    /// ones there is no other way to find.
    ///
    /// Going through the vacuum rather than collecting each item by hand also sidesteps a
    /// problem with no clean answer: nothing on a coin says whether it has already been
    /// collected, so a hand-rolled sweep would keep grabbing items that are mid-flight.
    /// </summary>
    public static bool VacuumPickups(bool includeSun, bool includeItems)
    {
        if (_board == null) return false;

        bool any = false;

        if (includeSun)
        {
            try
            {
                _board.VacuumCoins(BoardCentreX, BoardCentreY, SweepRadius, Player, CoinVacuumStyle.GlobalSunOnly);
                any = true;
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"Could not collect sun automatically: {ex.Message}");
            }
        }

        if (!includeItems) return any;

        try
        {
            _board.VacuumCoins(BoardCentreX, BoardCentreY, SweepRadius, Player, CoinVacuumStyle.Nearby);
            any = true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not collect items automatically: {ex.Message}");
        }

        return any;
    }

    /// <summary>Middle of the game's 800 by 600 board space, and a radius that covers all of it.</summary>
    private const int BoardCentreX = 400;
    private const int BoardCentreY = 300;
    private const int SweepRadius = 10000;

    #endregion

    #region Status

    /// <summary>
    /// How far through the level you are.
    ///
    /// The wave numbers the board keeps are not the ones the game shows. It counts every
    /// spawn — twenty of them in a normal level — while the meter on screen counts flags,
    /// which are groups of ten. Reading the internal counter out loud gave "wave 11 of 20"
    /// for a level the player could see was halfway through its second of two flags.
    ///
    /// The arithmetic below is the original PvZ accessibility mod's, so the wording matches
    /// what that mod says: a percentage first, because that is the part that tells you how
    /// much is left, then the flag count.
    ///
    /// Asking twice swaps the order, so whichever half you care about can be heard first
    /// without waiting through the other.
    /// </summary>
    public static string LevelProgress()
    {
        if (_board == null) return Strings.T("lawn.no_board");

        try
        {
            int total = _board.mNumWaves;
            int current = _board.mCurrentWave;

            if (total <= 0) return Strings.T("lawn.no_waves");

            // Flags are groups of ten waves, except in short levels where the whole level
            // is one flag.
            int wavesPerFlag = total < 10 ? total : 10;
            int flags = wavesPerFlag > 0 ? total / wavesPerFlag : 0;
            int flagsDone = wavesPerFlag > 0 ? current / wavesPerFlag : 0;

            if (current >= total) return Strings.T("lawn.final_wave");

            string waves = flags >= 1 ? Strings.T("lawn.wave", flagsDone, flags) : null;
            string percent = Strings.T("lawn.percent", Mathf.RoundToInt(current / (float)total * 100f));

            if (string.IsNullOrEmpty(waves)) return percent;

            _progressAlternate = !_progressAlternate;
            return _progressAlternate ? percent + ", " + waves : waves + ", " + percent;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the level progress: {ex.Message}");
            return Strings.T("lawn.no_waves");
        }
    }

    private static bool _progressAlternate;

    /// <summary>Current sun, or -1 when it cannot be read.</summary>
    public static int SunCount()
    {
        if (_board == null) return -1;
        try { return _board.mSunMoney[Player].Amount; }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the sun count: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// The horizontal extent of the lawn in the game's own pixel units, measured by asking
    /// the board where its first and last columns sit.
    ///
    /// Measured rather than assumed. The obvious guess was that the board matches the
    /// classic 800-unit playfield; zombies turn out to walk in from beyond x=1400, so every
    /// number derived from that guess — which column something is in, how far right it
    /// sounds — came out wrong. Asking the board costs one call and cannot drift when the
    /// game changes.
    /// </summary>
    public static bool TryLawnBounds(out float left, out float right)
    {
        left = 0f;
        right = 1f;
        if (_board == null) return false;

        try
        {
            int first = _board.GridToPixelX(0, 0);
            int last = _board.GridToPixelX(Columns - 1, 0);
            if (last <= first) return false;

            // GridToPixelX gives a column's own edge, so the lawn runs one cell past the last.
            float cell = (last - first) / (float)(Columns - 1);
            left = first;
            right = last + cell;
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not measure the lawn: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The row a point on the lawn belongs to.
    ///
    /// Found by asking the board where each row sits and taking the nearest, rather than by
    /// handing the position to PixelToGridY. That conversion expects a point inside a square,
    /// and a zombie's position is the top of its sprite — about a third of a row higher. The
    /// conversion rounds down, so that offset was enough to drop a zombie into the row above:
    /// three zombies a hundred units apart, plainly in three different rows, came back as
    /// rows one, one and two. Scanning a row then found something that was not in it, which
    /// is the worst way for this to be wrong — you plant against a threat that is elsewhere.
    ///
    /// Nearest-match needs no correction factor and stays right on roof levels, where the
    /// rows are not evenly spaced.
    /// </summary>
    public static int RowAt(float x, float y)
    {
        if (_board == null) return 0;

        try
        {
            int rows = SafeRowCount();
            int best = 0;
            float bestDistance = float.MaxValue;

            for (int row = 0; row < rows; row++)
            {
                float distance = Math.Abs(_board.GridToPixelY(0, row) - y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = row;
            }

            return best;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not work out the row at y={y}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>The column a point on the lawn belongs to, or -1 when it is off to the right.</summary>
    public static int ColumnAt(float x, float y)
    {
        if (_board == null) return -1;
        try
        {
            if (TryLawnBounds(out float left, out float right) && x >= right) return -1;
            return Mathf.Clamp(_board.PixelToGridXKeepOnBoard((int)x, (int)y), 0, Columns - 1);
        }
        catch { return -1; }
    }

    /// <summary>
    /// The level's own data: its name, its area, and which zombies it will send. Available
    /// before the level starts, which is what makes the plant chooser readable.
    /// </summary>
    public static Il2CppReloaded.Data.LevelEntryData LevelData()
    {
        if (_board == null) return null;
        try { return _board.mLevelEntryData; }
        catch { return null; }
    }

    /// <summary>Rows on this lawn — five normally, six with a pool.</summary>
    public static int SafeRowCount()
    {
        if (_board == null) return 5;
        try
        {
            int rows = _board.GetNumRows();
            return rows > 0 ? rows : 5;
        }
        catch { return 5; }
    }

    /// <summary>Whether the row still has its lawn mower, the last line of defence.</summary>
    public static bool RowHasMower(int y)
    {
        if (_board == null) return false;
        try { return _board.FindLawnMowerInRow(y) != null; }
        catch { return false; }
    }

    #endregion
}
