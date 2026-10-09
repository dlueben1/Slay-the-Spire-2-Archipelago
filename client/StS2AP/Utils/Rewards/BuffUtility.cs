using MegaCrit.Sts2.Core.Entities.Players;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>Coordinates independent buff and trap delivery with the same connection lifecycle.</summary>
public static class BuffUtility
{
    private static readonly CombatEffectDelivery Buffs = new(trap: false);
    private static readonly CombatEffectDelivery Traps = new(trap: true);
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;
        Buffs.Initialize();
        Traps.Initialize();
        _initialized = true;
    }

    public static async Task ProcessQueuedBuffsAsync(Player player)
    {
        // A received Artifact buff can protect against the independently queued trap.
        await Buffs.ProcessQueuedAsync(player);
        await Traps.ProcessQueuedAsync(player);
    }

    public static async Task LoadFromStorageAsync()
    {
        await Task.WhenAll(Buffs.LoadFromStorageAsync(), Traps.LoadFromStorageAsync());
    }

    public static void EnqueueBuff(APItem item, int index) => Buffs.Enqueue(item, index);
    public static void EnqueueTrap(APItem item, int index) => Traps.Enqueue(item, index);

    internal static void ProcessMultiplayerBuffs()
    {
        Buffs.ProcessMultiplayer();
        Traps.ProcessMultiplayer();
    }

    public static void ClearQueue()
    {
        Buffs.ClearQueue();
        Traps.ClearQueue();
    }

    internal static void ResetSlotState()
    {
        Buffs.ResetSlotState();
        Traps.ResetSlotState();
    }
}
