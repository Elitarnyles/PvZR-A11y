using System.Runtime.InteropServices;

namespace PvZRA11y.A11y;

/// <summary>
/// Thin managed wrapper over Tolk.dll, the screen-reader abstraction layer that
/// talks to NVDA, JAWS, Window-Eyes and SAPI.
///
/// Tolk.dll ships in the game's UserLibs folder, which MelonLoader adds to the
/// native library search path, so a plain DllImport resolves it. Every entry
/// point is wrapped so that a missing or mismatched DLL degrades to silence
/// instead of taking the game down.
///
/// P/Invoke signatures follow the reference wrapper in game-a11y/PvZ-Replanted-A11y (MIT).
/// </summary>
internal static class Tolk
{
    private const string Dll = "Tolk.dll";

    private static bool _probed;
    private static bool _available;

    /// <summary>Error text from the load attempt, or null when the DLL loaded cleanly.</summary>
    internal static string LoadError { get; private set; }

    #region Native entry points

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Load();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Unload();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_IsLoaded();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySapi);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_PreferSAPI([MarshalAs(UnmanagedType.I1)] bool preferSapi);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Tolk_DetectScreenReader();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_HasSpeech();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string text,
                                           [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_Speak([MarshalAs(UnmanagedType.LPWStr)] string text,
                                          [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_Silence();

    #endregion

    /// <summary>
    /// True once Tolk.dll has been located and loaded. The first call performs the
    /// probe; every later call is a field read.
    /// </summary>
    internal static bool Available
    {
        get
        {
            if (_probed) return _available;
            _probed = true;
            try
            {
                Tolk_Load();
                _available = true;
                LoadError = null;
            }
            catch (DllNotFoundException ex)
            {
                _available = false;
                LoadError = "Tolk.dll not found in UserLibs: " + ex.Message;
            }
            catch (BadImageFormatException ex)
            {
                _available = false;
                LoadError = "Tolk.dll has the wrong architecture (need 64-bit): " + ex.Message;
            }
            catch (Exception ex)
            {
                _available = false;
                LoadError = "Tolk.dll failed to load: " + ex.Message;
            }
            return _available;
        }
    }

    private static T Guard<T>(Func<T> call, T fallback)
    {
        if (!Available) return fallback;
        try { return call(); }
        catch (Exception ex)
        {
            Core.Log?.Warning($"Tolk call failed: {ex.Message}");
            return fallback;
        }
    }

    private static void Guard(Action call)
    {
        if (!Available) return;
        try { call(); }
        catch (Exception ex) { Core.Log?.Warning($"Tolk call failed: {ex.Message}"); }
    }

    internal static bool IsLoaded() => Guard(() => Tolk_IsLoaded(), false);

    internal static void Unload() => Guard(() => Tolk_Unload());

    internal static void TrySapi(bool value) => Guard(() => Tolk_TrySAPI(value));

    internal static void PreferSapi(bool value) => Guard(() => Tolk_PreferSAPI(value));

    internal static bool HasSpeech() => Guard(() => Tolk_HasSpeech(), false);

    /// <summary>Name of the running screen reader, or empty when none was detected.</summary>
    internal static string DetectScreenReader() => Guard(() =>
    {
        IntPtr p = Tolk_DetectScreenReader();
        return p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(p) ?? string.Empty;
    }, string.Empty);

    /// <summary>Sends text to both speech and braille output.</summary>
    internal static bool Output(string text, bool interrupt)
        => Guard(() => Tolk_Output(text ?? string.Empty, interrupt), false);

    internal static bool Speak(string text, bool interrupt)
        => Guard(() => Tolk_Speak(text ?? string.Empty, interrupt), false);

    internal static bool Silence() => Guard(() => Tolk_Silence(), false);
}
