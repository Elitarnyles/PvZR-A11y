using PvZRA11y.Config;

namespace PvZRA11y.A11y;

/// <summary>
/// The mod's single speech channel.
///
/// Callers hand text to <see cref="Say"/> from wherever they happen to be — a Harmony
/// postfix, the focus watcher, a hotkey — and it lands in a queue. <see cref="Pump"/>
/// drains that queue once per frame from Core.OnUpdate, so no game-thread callback
/// ever blocks on the screen reader's IPC.
///
/// The queue also gives us free de-duplication, which matters more than it sounds:
/// the game raises OnSelect and OnPointerEnter for the same widget in the same frame,
/// and without a guard NVDA says everything twice.
/// </summary>
public static class Speech
{
    private readonly record struct Utterance(string Text, bool Interrupt, string Context);

    private static readonly object Gate = new();
    private static readonly Queue<Utterance> Pending = new();
    private static readonly List<string> History = new();

    private const int HistoryLimit = 50;

    private static string _lastText = string.Empty;
    private static long _lastTextAt = long.MinValue;

    /// <summary>Repeated text inside this window is dropped. Milliseconds.</summary>
    private const long DedupWindowMs = 300;

    /// <summary>Name reported by Tolk, or null when nothing was detected.</summary>
    public static string DetectedReader { get; private set; }

    /// <summary>The most recent thing actually sent to the screen reader.</summary>
    public static string LastAnnouncement { get; private set; } = string.Empty;

    public static bool Ready { get; private set; }

    public static void Initialize()
    {
        if (!Tolk.Available)
        {
            Core.Log.Error($"Speech unavailable. {Tolk.LoadError}");
            Core.Log.Error("Expected Tolk.dll in the game's UserLibs folder.");
            Ready = false;
            return;
        }

        // SAPI polls synchronously and stutters the game; NVDA and JAWS do not.
        Tolk.TrySapi(Settings.AllowSapi.Value);

        string reader = Tolk.DetectScreenReader();
        DetectedReader = string.IsNullOrEmpty(reader) ? null : reader;
        Ready = true;

        Core.Log.Msg($"Tolk loaded. Screen reader: {DetectedReader ?? "none detected"}");
        if (DetectedReader == null && !Settings.AllowSapi.Value)
            Core.Log.Warning("No screen reader running and SAPI is disabled, so nothing will be spoken.");
    }

    public static void Shutdown()
    {
        lock (Gate) Pending.Clear();
        Tolk.Unload();
        Ready = false;
    }

    /// <summary>
    /// Queues text for the screen reader.
    /// </summary>
    /// <param name="text">What to say. Null and whitespace are ignored.</param>
    /// <param name="interrupt">
    /// True cuts off whatever is being spoken — right for focus changes and anything
    /// the player triggered. False lets the current utterance finish, which is what
    /// you want for background events so they do not stomp on each other.
    /// </param>
    /// <param name="context">Free-form origin label written to the log in verbose mode.</param>
    public static void Say(string text, bool interrupt = true, string context = null, bool allowRepeat = false)
    {
        if (!Settings.Enabled.Value) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Nothing drains the queue when there is no screen reader to drain it into, so
        // without this every line spoken all session accumulates in memory, silently.
        if (!Ready) return;

        text = text.Trim();

        lock (Gate)
        {
            long now = Environment.TickCount64;

            // allowRepeat is for events where saying the same words twice means it really
            // happened twice — a second zombie entering the row you were just told about.
            // The speech layer cannot tell those from one event announced twice, so the
            // caller, which can, says so.
            if (!allowRepeat && text == _lastText && now - _lastTextAt < DedupWindowMs)
            {
                if (Settings.VerboseLogging.Value)
                    Core.Log.Msg($"[speech] deduped \"{text}\"  ({context})");
                return;
            }

            _lastText = text;
            _lastTextAt = now;

            if (interrupt) DropQueued(context);
            Pending.Enqueue(new Utterance(text, interrupt, context));
        }
    }

    /// <summary>Speaks without touching the de-duplication window. For "repeat that" hotkeys.</summary>
    public static void SayVerbatim(string text, string context = null)
    {
        if (!Settings.Enabled.Value || string.IsNullOrWhiteSpace(text)) return;
        if (!Ready) return;

        lock (Gate)
        {
            DropQueued(context);
            Pending.Enqueue(new Utterance(text.Trim(), true, context));
        }
    }

    /// <summary>
    /// Throws away what is queued from the same source, and only from the same source.
    ///
    /// Interrupting used to empty the whole queue. That is right for the thing being
    /// replaced — walking the cursor across four squares should say the fourth, not all
    /// four — but it also deleted lines that had nothing to do with it and that nobody had
    /// heard yet. A wave warning is queued by the game's own code and waits a frame; one tap
    /// of an arrow key in that frame erased it, and "a huge wave of zombies is approaching"
    /// is exactly the sentence there is no other way to get.
    ///
    /// Cutting off speech that is already being read aloud is a separate matter and stays
    /// with the screen reader, which is told to interrupt when the line goes out.
    /// </summary>
    private static void DropQueued(string context)
    {
        if (Pending.Count == 0) return;

        int kept = 0;
        for (int i = Pending.Count; i > 0; i--)
        {
            Utterance u = Pending.Dequeue();
            if (u.Context == context) continue;

            Pending.Enqueue(u);
            kept++;
        }

        if (kept > 0 && Settings.VerboseLogging.Value)
            Core.Log.Msg($"[speech] interrupt from \"{context}\" kept {kept} queued from elsewhere");
    }

    /// <summary>Drains the queue. Call once per frame from the main thread.</summary>
    public static void Pump()
    {
        if (!Ready) return;

        while (true)
        {
            Utterance u;
            lock (Gate)
            {
                if (Pending.Count == 0) return;
                u = Pending.Dequeue();
            }

            if (Settings.VerboseLogging.Value)
                Core.Log.Msg($"[speech] {(u.Interrupt ? "!" : "+")} \"{u.Text}\"   ({u.Context})");

            // Output drives braille displays as well as speech.
            Tolk.Output(u.Text, u.Interrupt);

            LastAnnouncement = u.Text;
            lock (Gate)
            {
                History.Add(u.Text);
                if (History.Count > HistoryLimit) History.RemoveAt(0);
            }
        }
    }

    public static void Silence()
    {
        lock (Gate) Pending.Clear();
        Tolk.Silence();
    }

    /// <summary>Most recent announcements, oldest first.</summary>
    public static IReadOnlyList<string> RecentHistory()
    {
        lock (Gate) return History.ToArray();
    }
}
