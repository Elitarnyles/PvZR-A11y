using Il2CppTekly.Localizations;
using Il2CppTMPro;
using UnityEngine;

namespace PvZRA11y.A11y;

/// <summary>
/// Turns the game's string-table keys into readable text.
///
/// Several things the game hands out are keys rather than sentences — "REPEATER_TOOLTIP",
/// "[ADVICE_CLICK_SHOVEL]". Spoken aloud they are gibberish, and for a plant's tooltip there
/// is nowhere else to get the words: that line never appears on screen unless a mouse hovers
/// the card.
///
/// The obvious route was the translator itself. It cannot be touched: the type is generic
/// over its own singleton base, and merely naming it from managed code throws
///
///     GenericArguments[0], 'Localizer', on 'Singleton`2[TImpl,TInterface]'
///     violates the constraint of type parameter 'TImpl'
///
/// So the translation is done the way the game's own interface does it, through a
/// TextLocalizer component — an ordinary MonoBehaviour with no such problem. One is kept on
/// a hidden object, handed a key, and asked what it says.
/// </summary>
public static class GameText
{
    private static GameObject _host;
    private static TextMeshProUGUI _text;
    private static TextLocalizer _localizer;
    private static bool _failed;

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a key, or returns null when it cannot be resolved.
    ///
    /// Null rather than the key itself, so a caller can leave the text out entirely instead
    /// of reading an identifier aloud as though it meant something.
    /// </summary>
    public static string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            if (Config.Settings.VerboseLogging.Value) Core.Log.Msg("[text] asked to resolve nothing");
            return null;
        }

        string id = key.Trim();
        if (id.Length > 1 && id[0] == '[' && id[^1] == ']') id = id[1..^1];

        if (Cache.TryGetValue(id, out string cached)) return cached;

        string result = Translate(id);
        Cache[id] = result;

        if (Config.Settings.VerboseLogging.Value)
            Core.Log.Msg($"[text] \"{id}\" -> {(result == null ? "<no translation>" : "\"" + result + "\"")}");

        return result;
    }

    /// <summary>Resolves a value that may already be plain text, leaving it alone if so.</summary>
    public static string ResolveOrKeep(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return Resolve(value) ?? value;
    }

    private static string Translate(string id)
    {
        if (_failed) return null;
        if (!EnsureHost()) return null;

        try
        {
            _text.text = string.Empty;
            _localizer.Id = id;
            _localizer.LocalizeText();

            string result = _text.text;
            if (string.IsNullOrWhiteSpace(result)) return null;

            // A miss usually leaves the key in place; that is not a translation.
            return result.Trim() == id ? null : result;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not translate \"{id}\": {ex.Message}");
            return null;
        }
    }

    private static bool EnsureHost()
    {
        if (_localizer != null) return true;

        try
        {
            _host = new GameObject("PvZRA11y.Text");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;

            // Left active on purpose. A disabled object never runs OnEnable, so the
            // component never wires itself up and answers nothing. Nothing is drawn anyway:
            // the object sits under no canvas.
            _text = _host.AddComponent<TextMeshProUGUI>();
            _localizer = _host.AddComponent<TextLocalizer>();
            _localizer.Text = _text;

            Core.Log.Msg("Game text translation ready.");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Could not set up game text translation: {ex}");
            _failed = true;
            return false;
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
        _text = null;
        _localizer = null;
    }
}
