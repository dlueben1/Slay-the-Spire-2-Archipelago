using StS2AP.Data;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>Pending one-time buffs, ordered by AP receipt index across history replays.</summary>
internal sealed class BuffReceiptQueue
{
    private readonly SortedDictionary<int, (APItem BuffType, bool NotificationShown)> _pending = new();

    public int LastConsumedIndex { get; private set; } = -1;

    public BuffReceiptQueue(int lastConsumedIndex = -1) => RestoreConsumedIndex(lastConsumedIndex);

    public bool Enqueue(APItem buffType, int itemIndex, bool notificationShown)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemIndex);
        if (!ItemTable.IsUniversalCombatBuff((long)buffType))
            throw new ArgumentOutOfRangeException(nameof(buffType));
        if (itemIndex <= LastConsumedIndex)
            return false;
        if (_pending.TryGetValue(itemIndex, out var existing))
        {
            if (existing.BuffType != buffType)
                throw new InvalidDataException($"Conflicting buff receipt {itemIndex}.");
            return false;
        }
        _pending.Add(itemIndex, (buffType, notificationShown));
        return true;
    }

    public bool TryPeek(out (APItem BuffType, int ItemIndex, bool NotificationShown) entry)
    {
        entry = default;
        if (_pending.Count == 0)
            return false;
        var first = _pending.First();
        entry = (first.Value.BuffType, first.Key, first.Value.NotificationShown);
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
