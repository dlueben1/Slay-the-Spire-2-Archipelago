namespace StS2AP.Utils;

/// <summary>Shares cumulative buff conversion equally across the slot's configured characters.</summary>
internal static class UniversalBuffGold
{
    public const int ValuePerBuff = 5;

    /// <summary>
    /// Adds the difference between cumulative shares, retaining fractional gold for later
    /// receipts. Passing all receipts at once produces the same bank as adding them one by one.
    /// </summary>
    public static int AddToBank(
        IDictionary<long, int> goldBank,
        IEnumerable<long> characterOffsets,
        int previousBuffCount,
        int addedBuffCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(previousBuffCount);
        ArgumentOutOfRangeException.ThrowIfNegative(addedBuffCount);
        long[] offsets = characterOffsets.Distinct().ToArray();
        if (offsets.Length == 0)
            throw new ArgumentException("Buff gold requires at least one configured character.", nameof(characterOffsets));

        long previousTotal = (long)previousBuffCount * ValuePerBuff;
        long newTotal = previousTotal + (long)addedBuffCount * ValuePerBuff;
        int amount = checked((int)(newTotal / offsets.Length - previousTotal / offsets.Length));
        foreach (long offset in offsets)
        {
            goldBank.TryGetValue(offset, out int previous);
            goldBank[offset] = checked(previous + amount);
        }

        return amount;
    }
}
