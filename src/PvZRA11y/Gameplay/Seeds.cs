using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Input;
using PvZRA11y.Localization;
using UnityEngine.InputSystem;

namespace PvZRA11y.Gameplay;

/// <summary>
/// The seed bank: which plant is in hand, and how to change it.
///
/// The number keys already pick a slot — that is the game's own binding and it works
/// without us. What was missing is any way to hear the result, and any way to step
/// through the slots without knowing in advance what is in them, which is precisely the
/// knowledge a blind player does not have.
///
/// Cycling goes through the game's own gamepad seed control rather than poking the cursor
/// directly, so a slot that is still recharging or unaffordable is skipped or refused by
/// the same rules that apply to everyone else.
/// </summary>
public static class Seeds
{
    private const int Player = 0;

    private static Board Board => Lawn.BoardRef;

    /// <summary>The seed bank for player one, or null off the lawn.</summary>
    private static SeedBank Bank()
    {
        try { return Board?.SeedBanks[Player]; }
        catch { return null; }
    }

    private static SeedBankGamepadControl Control()
    {
        try { return Board?.m_seedBankControls[Player]; }
        catch { return null; }
    }

    /// <summary>How many slots the bank has.</summary>
    public static int SlotCount()
    {
        try { return Bank()?.NumPackets ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// Index of the slot currently chosen, or -1 when none is.
    ///
    /// Two places have to be consulted. The cursor knows which slot it is holding, which
    /// covers picking a plant up; but a slot can also be marked as chosen without the cursor
    /// carrying it yet, and that is the state the number keys leave behind. Watching only the
    /// cursor meant the number keys selected silently while the cycle keys announced
    /// perfectly — the same action reported or not depending on how you asked for it.
    /// </summary>
    public static int SelectedIndex()
    {
        if (Board == null) return -1;

        try
        {
            CursorObject cursor = Board.CursorObjects[Player];
            if (cursor != null && cursor.CursorType == CursorType.PlantFromBank)
                return cursor.SeedBankIndex;
        }
        catch { /* fall through to the packets */ }

        return MarkedIndex();
    }

    /// <summary>The slot the game has marked as chosen, whether or not the cursor holds it.</summary>
    private static int MarkedIndex()
    {
        try
        {
            var packets = Bank()?.SeedPackets;
            if (packets == null) return -1;

            for (int i = 0; i < packets.Length; i++)
            {
                SeedPacket packet = packets[i];
                if (packet != null && packet.IsGamepadSelected) return i;
            }
        }
        catch { /* nothing marked */ }

        return -1;
    }

    public static SeedPacket PacketAt(int index)
    {
        if (index < 0) return null;
        try
        {
            var packets = Bank()?.SeedPackets;
            if (packets == null || index >= packets.Length) return null;
            return packets[index];
        }
        catch { return null; }
    }

    /// <summary>
    /// Steps to the next or previous slot using the game's own control, the same path the
    /// shoulder buttons take on a gamepad.
    /// </summary>
    /// <summary>Puts one slot in hand, by index. Returns false when it cannot be done.</summary>
    public static bool Select(int index)
    {
        SeedBankGamepadControl control = Control();
        if (control == null || index < 0 || index >= SlotCount()) return false;

        try
        {
            control._setSelected(index);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not select seed slot {index}: {ex.Message}");
            return false;
        }
    }

    public static bool Cycle(int delta)
    {
        SeedBankGamepadControl control = Control();
        if (control == null) return false;

        int slots = SlotCount();
        if (slots <= 0) return false;

        // The obvious route was the game's own _selectNext, which is what a gamepad's
        // shoulder buttons call. It throws: the handler reads something out of the input
        // callback it is handed, and a synthesised empty one has nothing in it. Choosing the
        // slot ourselves and setting it directly avoids the callback entirely.
        int current = SelectedIndex();
        int from = current < 0 ? (delta > 0 ? -1 : slots) : current;

        for (int step = 1; step <= slots; step++)
        {
            int index = ((from + delta * step) % slots + slots) % slots;
            if (!SlotHasPlant(index)) continue;

            try
            {
                control._setSelected(index);
                return true;
            }
            catch (Exception ex)
            {
                Core.Log.Warning($"Could not select seed slot {index}: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// What to say about a slot: the plant, its cost, and anything stopping you using it.
    /// </summary>
    /// <summary>
    /// The whole seed bank, in order, without touching what is in your hand.
    ///
    /// Every other route to the bank goes through the game's own selection, which means
    /// finding out what you have costs you what you were holding — and on a bank of eight
    /// you end up somewhere random with sun about to be spent on it. There was no way to
    /// ask "what have I got" without changing the answer.
    ///
    /// Slots that cannot be afforded are included on purpose. The game's own navigation
    /// skips them, so until you could pay for a Wall-nut you had no way to learn you had
    /// one — which is exactly backwards, because that is when you need to plan for it.
    /// </summary>
    public static string DescribeBank()
    {
        int slots = SlotCount();
        if (slots <= 0) return Strings.T("seeds.no_bank");

        int held = SelectedIndex();
        var parts = new List<string>(slots);

        for (int i = 0; i < slots; i++)
        {
            string one = Describe(i);
            if (string.IsNullOrEmpty(one)) continue;

            // The one in your hand is named as such, so a readout you asked for mid-level
            // still tells you where you are standing in it.
            parts.Add(i == held ? Strings.T("seeds.holding", one, i + 1) : one);
        }

        return parts.Count == 0 ? Strings.T("seeds.no_bank") : string.Join(", ", parts);
    }

    public static string Describe(int index)
    {
        SeedPacket packet = PacketAt(index);
        if (packet == null) return null;

        try
        {
            SeedType type = packet.PacketType;
            if (type == SeedType.None) return Strings.T("seeds.empty_slot", index + 1);

            // Wording and ordering follow the original PvZ accessibility mod:
            //     Scaredy-shroom, Ready, 25
            //     Sun-shroom, Refreshing, 25
            //     Cherry Bomb, 55 of 150 sun
            // The state comes before the cost because it is what decides whether you can
            // use the plant at all, and when you cannot afford it the cost is replaced by
            // how much of it you have — a number you can act on rather than one you cannot.
            string name = Lawn.PlantName(type);
            int cost = packet.GetCost();
            int sun = Lawn.SunCount();

            if (IsRecharging(packet))
                return $"{name}, {Strings.T("seeds.refreshing")}, {cost}";

            if (cost > 0 && sun >= 0 && sun < cost)
                return $"{name}, {Strings.T("seeds.of_sun", sun, cost)}";

            return $"{name}, {Strings.T("seeds.ready")}, {cost}";
        }
        catch (Exception ex)
        {
            Core.Log.Warning($"Could not describe seed slot {index}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Describes the plant in hand, or says that there is none.</summary>
    public static string DescribeHeld()
    {
        int index = SelectedIndex();
        if (index < 0) return Strings.T("seeds.nothing_held");

        string description = Describe(index);
        return string.IsNullOrEmpty(description)
            ? Strings.T("seeds.nothing_held")
            : Strings.T("seeds.holding", description, index + 1);
    }

    /// <summary>Whether a slot holds anything at all, so cycling skips the empty ones.</summary>
    private static bool SlotHasPlant(int index)
    {
        SeedPacket packet = PacketAt(index);
        if (packet == null) return false;
        try { return packet.PacketType != SeedType.None; }
        catch { return false; }
    }

    /// <summary>True while the packet is still refilling rather than merely unaffordable.</summary>
    private static bool IsRecharging(SeedPacket packet)
    {
        try { return packet.mRefreshCounter > 0; }
        catch { return false; }
    }
}
