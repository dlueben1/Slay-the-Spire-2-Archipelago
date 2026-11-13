namespace StS2AP.Utils;

/// <summary>
/// Decides which shops of a run are Blitz merchants. The game saves before a shop is entered and
/// rebuilds the shop on Continue, so the answer has to come out the same every time for the same
/// shop: it depends only on the run's seed and where the shop is.
/// </summary>
internal static class BlitzMerchantRoll
{
    /// <summary>
    /// Whether the shop at this place is a Blitz merchant, for a chance in percent.
    /// </summary>
    public static bool IsBlitzMerchant(ulong runSeed, int actIndex, int column, int row, int chancePercent)
    {
        if (chancePercent <= 0)
            return false;
        if (chancePercent >= 100)
            return true;

        ulong value = runSeed;
        value = Mix(value ^ (0x9E3779B97F4A7C15UL * (ulong)(actIndex + 1)));
        value = Mix(value ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(column + 1)));
        value = Mix(value ^ (0x165667B19E3779F9UL * (ulong)(row + 1)));
        return (int)(value % 100) < chancePercent;
    }

    // The SplitMix64 finalizer: nearby seeds and coordinates give unrelated results.
    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }
}
