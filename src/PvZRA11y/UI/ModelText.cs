using Il2CppTekly.DataModels.Binders;
using Il2CppTekly.DataModels.Models;
using PvZRA11y.A11y;
using PvZRA11y.Config;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Reading the game's own data model, for the screens whose controls carry no text.
///
/// Several screens in this game are built the same way: a grid of identical clones, each one
/// a button with a picture and nothing else, and what it stands for held only in the data
/// model behind it. The almanac was the first. The shop is the second, and its tiles read
/// out as bare prices with no idea what they were the price of.
///
/// Each such control carries a binder that knows its own absolute key, so the entry can be
/// asked directly. That avoids counting siblings, which would be wrong anyway wherever a
/// grid shows a filtered subset of what the data holds.
/// </summary>
public static class ModelText
{
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    /// <summary>The binder on a control, or on the nearest parent that has one.</summary>
    public static BinderContainer ContainerOn(Selectable selectable)
    {
        if (selectable == null) return null;

        try
        {
            BinderContainer direct = selectable.GetComponent<BinderContainer>();
            if (direct != null) return direct;
        }
        catch { /* no such component here */ }

        try { return selectable.GetComponentInParent<BinderContainer>(); }
        catch { return null; }
    }

    /// <summary>
    /// One value out of a control's own model, such as "*.name". Null when it is not there.
    ///
    /// Asking a binder for a field it does not have throws rather than answering no, and it
    /// arrives wrapped, so it cannot be caught by type. Reported once per key and message,
    /// because the frame around a grid — its Back and Close buttons — sits under the same
    /// binder and has no entry behind it, which otherwise filled the log with the same
    /// stack trace several times per walk across a page.
    /// </summary>
    public static string Value(BinderContainer container, string relativeKey)
    {
        if (container == null) return null;

        try
        {
            IModel model = null;
            return container.TryGet(relativeKey, out model) ? ValueOf(model) : null;
        }
        catch (Exception ex)
        {
            string once = relativeKey + ": " + ex.Message;
            if (Settings.VerboseLogging.Value && Reported.Add(once))
                Core.Log.Msg($"[model] \"{relativeKey}\" is not on every control here ({ex.Message})");

            return null;
        }
    }

    /// <summary>
    /// The model at an absolute key, or null.
    ///
    /// A dotted key has to be parsed first. The lookup that takes a plain string is a single
    /// level deep - it compares the whole string against each direct child's name - so
    /// "achievements.total" finds nothing even though both halves of it exist and the game
    /// reads that very path itself. A parsed key walks the path a segment at a time, stepping
    /// into each object model as it goes, which is what the game does.
    ///
    /// This was worth an evening. The achievements screen read as empty, and the shop's
    /// second route to the coin count had been quietly failing for as long as it had existed
    /// - unnoticed only because a third route answered.
    /// </summary>
    public static IModel ModelAt(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        try
        {
            RootModel root = RootModel.Instance;
            if (root == null) return null;

            IModel model = null;

            if (key.IndexOf('.') >= 0)
            {
                ModelKey parsed = ModelKey.Parse(key);
                if (parsed != null && root.TryGetModel(parsed, 0, out model) && model != null)
                    return model;
            }

            return root.TryGetModel(key, out model) ? model : null;
        }
        catch (Exception ex)
        {
            string once = "root " + key + ": " + ex.Message;
            if (Settings.VerboseLogging.Value && Reported.Add(once))
                Core.Log.Msg($"[model] \"{key}\" could not be read ({ex.Message})");

            return null;
        }
    }

    /// <summary>One value from the root of the data model, by absolute key.</summary>
    public static string FromRoot(string key) => ValueOf(ModelAt(key));

    private static string ValueOf(IModel model)
    {
        if (model == null) return null;

        // The interface covers strings, numbers and booleans alike; the concrete casts below
        // are the fallback for when that cast does not survive interop.
        try
        {
            IValueModel value = model.TryCast<IValueModel>();
            if (value != null) return value.ToDisplayString();
        }
        catch { }

        try
        {
            StringValueModel text = model.TryCast<StringValueModel>();
            if (text != null) return text.OnToDisplayString();
        }
        catch { }

        try
        {
            NumberValueModel number = model.TryCast<NumberValueModel>();
            if (number != null) return number.OnToDisplayString();
        }
        catch { }

        // Booleans were the gap. Every flag on a challenge tile - locked, beaten, whether to
        // show a streak - is a BoolValueModel, and none of the three casts above catches one,
        // so a screen full of locked entries read back as a screen with nothing locked on it.
        // A missing answer and a false one look identical to a caller that only has a string.
        try
        {
            BoolValueModel flag = model.TryCast<BoolValueModel>();
            if (flag != null) return flag.OnToDisplayString();
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Turns a string-table identifier into words, leaving plain text alone.
    ///
    /// Both spellings are tried: the table stores its identifiers with a leading dollar and
    /// the translator is normally handed them without one.
    /// </summary>
    public static string Resolve(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string key = raw.Trim();
        string text = GameText.Resolve(key) ?? GameText.Resolve("$" + key);

        if (!string.IsNullOrWhiteSpace(text)) return UiText.Collapse(text);

        // A key that would not translate still reads better split into words than spoken as
        // an identifier, and the log then names exactly which key to go and look at.
        return UiText.Prettify(key.StartsWith("$", StringComparison.Ordinal) ? key[1..] : key);
    }
}
