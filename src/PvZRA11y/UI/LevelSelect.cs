using Il2CppTMPro;
using Il2CppUI.Scripts;
using PvZRA11y.A11y;
using PvZRA11y.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace PvZRA11y.UI;

/// <summary>
/// Makes the level carousel usable without a mouse.
///
/// The screen is a horizontal carousel: a strip of level tiles that scrolls, showing only
/// a handful at a time. Everything outside that window is off screen, so the visibility
/// filter correctly hides it — which means walking between controls can only ever reach
/// the two or three levels currently on display. Scrolling has to be a deliberate action,
/// hence the dedicated cycle keys.
///
/// Choosing a level is two steps in this game, not one. Pressing a tile selects it and
/// reveals a play button on it; pressing that button starts the level. Rather than make
/// the player discover this, Enter does whichever step is outstanding: select the level,
/// or start it if it is already selected.
/// </summary>
public static class LevelSelect
{
    private static LevelSelectScreen _screen;

    /// <summary>
    /// Which level the carousel has centred, taken from the game telling us rather than
    /// read back off the carousel.
    ///
    /// The carousel's own m_curItem is not an index at all: it is the strip's scroll
    /// offset, stored as the negative of the position — level 3 reads as -3 — and it lags
    /// behind while the strip is still sliding. Reading it produced a confidently spoken
    /// but wrong level name every time, which is the worst kind of wrong. The index handed
    /// to SetSelectedLevelIndex is authoritative and arrives immediately.
    /// </summary>
    private static int _currentIndex = -1;

    public static void NoteSelectedIndex(int index)
    {
        _currentIndex = index;
    }

    /// <summary>True while the level carousel is the screen in front of the player.</summary>
    public static bool IsActive { get; private set; }

    public static void NoteScreen(LevelSelectScreen screen)
    {
        if (screen != null) _screen = screen;
    }

    public static void NoteEntered(LevelSelectScreen screen)
    {
        NoteScreen(screen);
        IsActive = true;
        _currentIndex = -1;
        Core.Log.Msg("[levels] entered level select");
    }

    /// <summary>Switching world hands us a different strip, so the remembered position is void.</summary>
    public static void NoteCarouselChanged()
    {
        _currentIndex = -1;
    }

    public static void NoteLeft()
    {
        IsActive = false;
        Core.Log.Msg("[levels] left level select");
    }

    /// <summary>Scrolls the carousel one level along. Announcement follows from the game's own callback.</summary>
    public static bool Cycle(int delta)
    {
        if (!IsActive || _screen == null) return false;

        try
        {
            if (delta > 0) _screen.SelectNextLevel();
            else _screen.SelectPrevLevel();
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not scroll the level carousel: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handles Enter on a level tile.
    ///
    /// The carousel decides which level is chosen, not the tile you happen to be standing
    /// on, so this works in two steps: bring the tile to the middle if it is not already
    /// there, and start it if it is. That matches what a sighted player does — scroll the
    /// strip until the level is centred, then press play.
    ///
    /// Returns false for anything that is not a level tile, so normal activation takes over.
    /// </summary>
    public static bool TryActivate(Selectable selectable)
    {
        LevelListItem item = ItemOn(selectable);
        if (item == null) return false;

        string label = LabelOf(item);

        try
        {
            LevelSelectCarousel carousel = CarouselOf(item);
            int index = IndexOf(carousel, item);
            int current = CurrentIndex(carousel);

            Core.Log.Msg($"[levels] Enter on \"{label}\": tile index {index}, carousel at {current}");

            if (carousel != null && index >= 0 && index != current)
            {
                Core.Log.Msg($"[levels] bringing \"{label}\" to the middle");
                carousel.SelectLevel(index, false);
                return true;
            }

            return TryStart(item, carousel, label);
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not act on level \"{label}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts the level that is currently centred.
    ///
    /// The tile's own play button is preferred, since that is what a mouse click hits. It
    /// is not always live, so the carousel's play action is the fallback — the same one a
    /// gamepad triggers.
    /// </summary>
    private static bool TryStart(LevelListItem item, LevelSelectCarousel carousel, string label)
    {
        try
        {
            Button play = item?.playButton;
            bool playLive = play != null && play.gameObject != null && play.gameObject.activeInHierarchy;
            Core.Log.Msg($"[levels] starting \"{label}\", play button {(playLive ? "live" : "not live")}");

            Speech.Say(Strings.T("msg.starting_level", label), interrupt: true, context: "level start");

            if (playLive)
            {
                play.onClick.Invoke();
                return true;
            }

            if (carousel != null)
            {
                carousel._onPlayActionPerformed(default);
                return true;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not start \"{label}\": {ex.Message}");
        }
        return false;
    }

    private static LevelSelectCarousel CarouselOf(LevelListItem item)
    {
        try { return item?.m_levelSelect; }
        catch { return null; }
    }

    /// <summary>
    /// The centred position. Prefers what the game reported; falls back to undoing the
    /// carousel's negative offset if we have not been told yet.
    /// </summary>
    private static int CurrentIndex(LevelSelectCarousel carousel)
    {
        if (_currentIndex >= 0) return _currentIndex;
        try { return carousel == null ? -1 : Math.Abs(carousel.m_curItem); }
        catch { return -1; }
    }

    /// <summary>
    /// Position of a tile within its carousel. The carousel holds transforms, and the tile
    /// may be one of them or sit inside one, so both are checked.
    /// </summary>
    private static int IndexOf(LevelSelectCarousel carousel, LevelListItem item)
    {
        if (carousel == null || item == null) return -1;
        try
        {
            var items = carousel.m_items;
            if (items == null) return -1;

            Transform target = item.transform;
            int targetId = target.GetInstanceID();
            int parentId = target.parent == null ? 0 : target.parent.GetInstanceID();

            for (int i = 0; i < items.Length; i++)
            {
                Transform candidate = items[i];
                if (candidate == null) continue;
                int id = candidate.GetInstanceID();
                if (id == targetId || (parentId != 0 && id == parentId)) return i;
            }
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not locate a level tile in its carousel: {ex.Message}");
        }
        return -1;
    }

    /// <summary>Announces the level the carousel has just settled on.</summary>
    public static void AnnounceSelected()
    {
        string label = CurrentLabel();
        if (string.IsNullOrEmpty(label)) return;
        Speech.Say(label, interrupt: true, context: "level selected");
    }

    /// <summary>
    /// The name on the tile the carousel has centred.
    ///
    /// Logs what it read as well as what it found, because getting this wrong is silent:
    /// the wrong level name is spoken perfectly confidently and there is no other way to
    /// notice. The numbers in the log are what tell us whether m_curItem indexes what we
    /// think it does.
    /// </summary>
    public static string CurrentLabel()
    {
        try
        {
            LevelSelectCarouselGroup group = _screen?.m_selectedCarouselGroup;
            LevelSelectCarousel carousel = group?.levelCarousel;
            if (carousel == null)
            {
                Core.Log.Msg("[levels] no carousel is selected");
                return null;
            }

            var items = carousel.m_items;
            int index = CurrentIndex(carousel);
            int count = items?.Length ?? 0;

            if (items == null || index < 0 || index >= count)
            {
                Core.Log.Msg($"[levels] index {index} is outside the carousel's {count} items");
                return null;
            }

            Transform item = items[index];
            string label = item == null ? null : TextIn(item);

            Core.Log.Msg($"[levels] carousel at {index} of {count}, reads \"{label ?? "<no text>"}\"" +
                         $" (object \"{item?.name ?? "<null>"}\")");

            return label;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not read the selected level: {ex.Message}");
            return null;
        }
    }

    /// <summary>The name on a tile, falling back to its object name.</summary>
    public static string LabelOf(LevelListItem item)
    {
        if (item == null) return string.Empty;
        try
        {
            string text = TextIn(item.transform);
            return string.IsNullOrEmpty(text) ? UiText.Prettify(item.gameObject.name) : text;
        }
        catch { return string.Empty; }
    }

    private static string TextIn(Transform root)
    {
        try
        {
            var tmp = root.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return UiText.Collapse(tmp.text);
        }
        catch { /* no text on this tile */ }
        return null;
    }

    private static LevelListItem ItemOn(Selectable selectable)
    {
        if (selectable == null) return null;
        try { return selectable.gameObject.GetComponent<LevelListItem>(); }
        catch { return null; }
    }
}
