namespace StS2AP.Models;

/// <summary>
/// Owns the selected receipt catalogue and this player's per-run consumption in one AP session.
/// Gold and other aggregate progression remain with their existing owners. Callers must replace
/// the owning progress on a slot change; an index is not a globally unique receipt identity.
/// Like ArchipelagoProgress, mutations belong on the game thread.
/// </summary>
public sealed class ApReceivedItemLedger
{
    // A consumed entry without its receipt is a restored checkpoint awaiting AP history.
    // It must survive history refreshes, including refreshes which filter out that item kind.
    private sealed record Entry(IndexedItemInfo? Receipt, bool Used);

    private Dictionary<int, Entry> _entries = new();
    private IReadOnlyList<IndexedItemInfo>? _received;
    private IReadOnlyList<int>? _used;

    public IReadOnlyList<IndexedItemInfo> Received => _received ??= Array.AsReadOnly(
        _entries.Values.Where(entry => entry.Receipt != null)
            .Select(entry => entry.Receipt!).OrderBy(receipt => receipt.Index).ToArray());

    public IReadOnlyList<int> UsedIndexes => _used ??= Array.AsReadOnly(
        _entries.Where(pair => pair.Value.Used).Select(pair => pair.Key).Order().ToArray());

    /// <summary>Changes even when a replacement catalogue has the same number of receipts.</summary>
    public long Revision { get; private set; }

    public bool IsUsed(int index) => _entries.TryGetValue(index, out var entry) && entry.Used;

    public void RegisterReceived(IndexedItemInfo receipt) => Register(receipt, used: false);

    /// <summary>Records an immediately applied item without exposing a separate list update.</summary>
    public void RegisterConsumed(IndexedItemInfo receipt) => Register(receipt, used: true);

    /// <summary>
    /// Records consumption at the existing grant boundary. Repeated acknowledgement is harmless;
    /// this is not admission to execute a grant, nor a retry/rollback mechanism.
    /// </summary>
    public void MarkUsed(int index)
    {
        if (!_entries.TryGetValue(index, out var entry))
            throw new InvalidOperationException($"Cannot consume unregistered AP receipt {index}.");
        if (entry.Used)
            return;

        _entries[index] = entry with { Used = true };
        Changed();
    }

    /// <summary>
    /// Restores durable consumption before the AP catalogue is available. Only the save/checkpoint
    /// boundary should construct this state; normal grants require a registered receipt.
    /// </summary>
    public static ApReceivedItemLedger FromUsedIndexes(IEnumerable<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        var ledger = new ApReceivedItemLedger();
        foreach (int index in indexes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ledger._entries[index] = new Entry(null, Used: true);
        }
        return ledger;
    }

    /// <summary>
    /// Replaces the transient catalogue within the same AP session without reopening consumed
    /// receipts. Validate the complete input before publishing it so a failed rebuild retains
    /// the previous catalogue and consumption. Saved indexes need not yet appear in the input.
    /// </summary>
    public void ReplaceReceivedItems(IEnumerable<IndexedItemInfo> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var replacement = _entries.Where(pair => pair.Value.Used)
            .ToDictionary(pair => pair.Key, _ => new Entry(null, Used: true));
        foreach (var receipt in receipts)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            if (_entries.TryGetValue(receipt.Index, out var previous))
                ValidateSameReceipt(previous.Receipt, receipt);
            if (replacement.TryGetValue(receipt.Index, out var existing))
                ValidateSameReceipt(existing.Receipt, receipt);
            replacement[receipt.Index] = new Entry(receipt, existing?.Used ?? false);
        }

        _entries = replacement;
        Changed();
    }

    /// <summary>Starts a fresh run using the current catalogue, discarding prior consumption.</summary>
    public void StartNewRun()
    {
        _entries = _entries.Where(pair => pair.Value.Receipt != null)
            .ToDictionary(pair => pair.Key, pair => new Entry(pair.Value.Receipt, Used: false));
        Changed();
    }

    private void Register(IndexedItemInfo receipt, bool used)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (_entries.TryGetValue(receipt.Index, out var existing))
        {
            ValidateSameReceipt(existing.Receipt, receipt);
            if (existing.Receipt != null && (existing.Used || !used))
                return;
            used |= existing.Used;
        }

        _entries[receipt.Index] = new Entry(receipt, used);
        Changed();
    }

    private static void ValidateSameReceipt(IndexedItemInfo? existing, IndexedItemInfo candidate)
    {
        if (existing == null)
            return;
        var left = existing.Item;
        var right = candidate.Item;
        if (left.ItemId != right.ItemId || left.LocationId != right.LocationId
            || left.Player.Team != right.Player.Team || left.Player.Slot != right.Player.Slot)
        {
            throw new InvalidOperationException(
                $"Conflicting AP receipt {candidate.Index}: "
                    + $"expected item {left.ItemId} at location {left.LocationId} "
                    + $"from {left.Player.Team}/{left.Player.Slot}, "
                    + $"received item {right.ItemId} at location {right.LocationId} "
                    + $"from {right.Player.Team}/{right.Player.Slot}.");
        }
    }

    private void Changed()
    {
        _received = null;
        _used = null;
        Revision++;
    }
}
