using StS2AP.Data;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>Pending one-time combat effects, ordered by AP receipt index across history replays.</summary>
internal sealed class CombatEffectReceiptQueue
{
    private readonly SortedDictionary<int, (APItem EffectType, bool NotificationShown)> _pending = new();

    public int LastConsumedIndex { get; private set; } = -1;

    private readonly bool _traps;

    public CombatEffectReceiptQueue(int lastConsumedIndex = -1, bool traps = false)
    {
        _traps = traps;
        RestoreConsumedIndex(lastConsumedIndex);
    }

    public bool Enqueue(APItem effectType, int itemIndex, bool notificationShown)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemIndex);
        if (!(_traps ? ItemTable.IsUniversalTrap((long)effectType) : ItemTable.IsUniversalCombatBuff((long)effectType)))
            throw new ArgumentOutOfRangeException(nameof(effectType));
        if (itemIndex <= LastConsumedIndex)
            return false;
        if (_pending.TryGetValue(itemIndex, out var existing))
        {
            if (existing.EffectType != effectType)
                throw new InvalidDataException($"Conflicting combat effect receipt {itemIndex}.");
            return false;
        }
        _pending.Add(itemIndex, (effectType, notificationShown));
        return true;
    }

    public bool TryPeek(out (APItem EffectType, int ItemIndex, bool NotificationShown) entry)
    {
        entry = default;
        if (_pending.Count == 0)
            return false;
        var first = _pending.First();
        entry = (first.Value.EffectType, first.Key, first.Value.NotificationShown);
        return true;
    }

    public void Discard(int itemIndex) => _pending.Remove(itemIndex);

    public void RestoreConsumedIndex(int itemIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemIndex, -1);
        LastConsumedIndex = Math.Max(LastConsumedIndex, itemIndex);
        while (TryPeek(out var entry) && entry.ItemIndex <= LastConsumedIndex)
            _pending.Remove(entry.ItemIndex);
    }
}
