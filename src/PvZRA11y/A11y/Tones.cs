using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PvZRA11y.A11y;

/// <summary>
/// Generated tones, used to convey position faster than words can.
///
/// Speech is precise but slow. On the lawn a player needs to know where the cursor is
/// several times a second, and "row three, column five" spoken every time is unusable.
/// A tone carries the same two numbers instantly: how far left or right it sounds is the
/// column, how high it is pitched is the row. After a few minutes it stops being a sound
/// and starts being a position, which is the whole idea — it is the approach the original
/// PvZ accessibility mod established, and it is why that mod is playable at all.
///
/// Clips are synthesised rather than shipped as files, so the mod stays a single DLL, and
/// they are built with the stereo balance baked in rather than relying on the AudioSource
/// pan, so several can overlap without fighting over one setting. That matters for the
/// zombie sonar, where a whole row is played as one spread of sound.
/// </summary>
public static class Tones
{
    private const int SampleRate = 44100;

    /// <summary>Fade applied at each end of a tone. Without it every beep starts with a click.</summary>
    private const int FadeSamples = 220; // about 5 ms

    private static GameObject _host;
    private static AudioSource _source;
    private static readonly Dictionary<long, AudioClip> Cache = new();

    public static bool Ready => _source != null;

    /// <summary>
    /// Creates the audio host. Called once the game has a scene, since it needs a
    /// GameObject that survives level loads.
    /// </summary>
    public static void Initialize()
    {
        if (_source != null) return;

        try
        {
            _host = new GameObject("PvZRA11y.Audio");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;

            _source = _host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;   // pure 2D; the stereo balance is baked into the clip
            _source.bypassEffects = true;
            _source.bypassListenerEffects = true;

            Core.Log.Msg("Audio cues ready.");
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not set up audio cues: {ex.Message}");
            _source = null;
        }
    }

    public static void Shutdown()
    {
        try
        {
            Cache.Clear();
            if (_host != null) UnityEngine.Object.Destroy(_host);
        }
        catch { /* shutting down anyway */ }
        _host = null;
        _source = null;
    }

    /// <summary>
    /// Plays a tone.
    /// </summary>
    /// <param name="frequency">Pitch in hertz. Higher reads as further up the lawn.</param>
    /// <param name="pan">0 is hard left, 1 is hard right.</param>
    /// <param name="durationMs">Length. Keep short; these fire on every cursor move.</param>
    /// <param name="volume">0 to 1, applied on top of the user's cue volume.</param>
    public static void Play(float frequency, float pan, int durationMs, float volume = 1f)
    {
        if (_source == null) return;
        if (volume <= 0f) return;

        try
        {
            AudioClip clip = GetClip(frequency, pan, durationMs);
            if (clip != null) _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not play a tone: {ex.Message}");
        }
    }

    /// <summary>
    /// A short two-note figure for "you cannot go that way". Distinct from a position tone
    /// so it never gets mistaken for one.
    /// </summary>
    public static void PlayEdge(float volume = 1f)
    {
        Play(220f, 0.5f, 60, volume);
    }

    /// <summary>
    /// A rising two-note figure, for the one thing that has to cut through everything else.
    ///
    /// Deliberately unlike any other cue here: two notes rather than one, rising rather than
    /// flat, and higher than the whole range the lawn positions use. It has to be
    /// recognisable in the half second before a pole-vaulter clears your front line.
    /// </summary>
    public static void PlayAlert(float pan, float volume = 1f)
    {
        Play(1150f, pan, 70, volume);
        PlayDelayed(1500f, pan, 90, volume, 80);
    }

    private readonly record struct Scheduled(long DueAt, float Frequency, float Pan, int DurationMs, float Volume);

    private static readonly List<Scheduled> Pending = new();

    /// <summary>
    /// Plays a tone after a delay.
    ///
    /// This is what turns a list of zombies into a picture of the lawn. Sounding them all
    /// at once gives a chord that says how many there are and nothing about where; spread
    /// over a second, with the delay set by how far along the row each one is, the ear
    /// reads the spacing directly. Near ones arrive first, and a gap in the sequence is a
    /// gap in the row.
    /// </summary>
    public static void PlayDelayed(float frequency, float pan, int durationMs, float volume, int delayMs)
    {
        if (_source == null || volume <= 0f) return;

        if (delayMs <= 0)
        {
            Play(frequency, pan, durationMs, volume);
            return;
        }

        Pending.Add(new Scheduled(Environment.TickCount64 + delayMs, frequency, pan, durationMs, volume));
    }

    /// <summary>Releases tones whose time has come. Called once per frame.</summary>
    public static void Pump()
    {
        if (Pending.Count == 0) return;

        long now = Environment.TickCount64;
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            Scheduled item = Pending[i];
            if (item.DueAt > now) continue;

            Pending.RemoveAt(i);
            Play(item.Frequency, item.Pan, item.DurationMs, item.Volume);
        }
    }

    /// <summary>Drops anything still waiting, so a new scan does not collide with the last.</summary>
    public static void ClearPending() => Pending.Clear();

    /// <summary>
    /// Clips are reused: a lawn has at most a few dozen distinct row and column pairings,
    /// so after the first pass over the board nothing new is ever synthesised.
    /// </summary>
    private static AudioClip GetClip(float frequency, float pan, int durationMs)
    {
        pan = Mathf.Clamp01(pan);

        // Round the key so near-identical requests share a clip.
        long key = ((long)Mathf.RoundToInt(frequency) << 24)
                 | ((long)Mathf.RoundToInt(pan * 100f) << 12)
                 | (uint)Mathf.Clamp(durationMs, 1, 4000);

        if (Cache.TryGetValue(key, out AudioClip cached) && cached != null) return cached;

        AudioClip clip = Synthesise(frequency, pan, durationMs);
        if (clip != null) Cache[key] = clip;
        return clip;
    }

    private static AudioClip Synthesise(float frequency, float pan, int durationMs)
    {
        int frames = Math.Max(1, SampleRate * durationMs / 1000);
        var data = new Il2CppStructArray<float>(frames * 2);

        // Equal-power panning keeps the loudness steady as the cue crosses the middle,
        // which a plain linear fade does not.
        float left = Mathf.Cos(pan * Mathf.PI * 0.5f);
        float right = Mathf.Sin(pan * Mathf.PI * 0.5f);

        float step = 2f * Mathf.PI * frequency / SampleRate;

        for (int i = 0; i < frames; i++)
        {
            float sample = Mathf.Sin(step * i);

            // Taper both ends so the tone does not begin or end on a discontinuity.
            if (i < FadeSamples) sample *= i / (float)FadeSamples;
            int fromEnd = frames - 1 - i;
            if (fromEnd < FadeSamples) sample *= fromEnd / (float)FadeSamples;

            data[i * 2] = sample * left;
            data[i * 2 + 1] = sample * right;
        }

        AudioClip clip = AudioClip.Create($"a11y_{frequency:F0}_{pan:F2}", frames, 2, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
