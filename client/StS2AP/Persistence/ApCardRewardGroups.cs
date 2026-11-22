namespace StS2AP.Persistence;

/// <summary>One card reward within a floor's card choices: where it starts and how many cards it offered.</summary>
public readonly record struct ApCardRewardGroup(int Start, int Count);

/// <summary>
/// Splits a floor's card choices back into the card rewards they came from. The game keeps one
/// flat list per floor: a taken reward adds its picked card, then the cards left behind; a skipped
/// or rerolled reward adds all of its cards as not picked. Nothing marks where one reward ends,
/// so the split looks for one reward size that explains the whole list.
/// </summary>
public static class ApCardRewardGroups
{
    // Three cards is the usual offer; relics add or take away one.
    private static readonly int[] OfferSizes = { 3, 4, 2, 5 };

    public static IReadOnlyList<ApCardRewardGroup> Split(IReadOnlyList<bool> picked)
    {
        if (picked.Count == 0)
            return Array.Empty<ApCardRewardGroup>();
        foreach (int size in OfferSizes)
        {
            if (FitsSize(picked, size))
            {
                var groups = new List<ApCardRewardGroup>();
                for (int start = 0; start < picked.Count; start += size)
                    groups.Add(new ApCardRewardGroup(start, size));
                return groups;
            }
        }
        return SplitAtPicks(picked);
    }

    /// <summary>
    /// A shop's card choices: rewards taken there, then its unbought stock, which the game adds
    /// as not picked when the shop closes. Each taken card starts a reward of up to three cards
    /// that ends at the next taken card; every other card is stock. A reward skipped in a shop
    /// cannot be told apart from stock.
    /// </summary>
    public static IReadOnlyList<ApCardRewardGroup> SplitShop(IReadOnlyList<bool> picked)
    {
        var groups = new List<ApCardRewardGroup>();
        for (int start = 0; start < picked.Count; start++)
        {
            if (!picked[start])
                continue;
            int end = start + 1;
            while (end < picked.Count && end - start < OfferSizes[0] && !picked[end])
                end++;
            groups.Add(new ApCardRewardGroup(start, end - start));
            start = end - 1;
        }
        return groups;
    }

    /// <summary>
    /// How many AP card rewards were claimed on each floor, by floor index. Claims a checkpoint
    /// reload undid never reached the surviving run's history.
    /// </summary>
    public static Dictionary<int, int> ApRewardsByFloor(ApRunGraphLog log, int floorCount)
    {
        var byFloor = new Dictionary<int, int>();
        if (floorCount == 0)
            return byFloor;
        foreach (var e in log.Events)
        {
            if (e.Kind != ApRunGraphEventKind.ItemClaimed || e.Abandoned || !IsCardRewardItem(e.ItemName))
                continue;
            int index = Math.Clamp(e.Floor - 1, 0, floorCount - 1);
            byFloor[index] = byFloor.GetValueOrDefault(index) + 1;
        }
        return byFloor;
    }

    /// <summary>
    /// "Card Reward" and "Rare Card Reward", with or without a character's name in front.
    /// "Additional Card Reward" is a buff that adds a reward to a later fight, not a claimed reward.
    /// </summary>
    public static bool IsCardRewardItem(string itemName) =>
        itemName.EndsWith("Card Reward", StringComparison.Ordinal)
        && !itemName.Equals("Additional Card Reward", StringComparison.Ordinal);

    /// <summary>Every reward has this many cards, and any taken card comes before the ones left behind.</summary>
    private static bool FitsSize(IReadOnlyList<bool> picked, int size)
    {
        if (picked.Count % size != 0)
            return false;
        for (int start = 0; start < picked.Count; start += size)
        {
            for (int i = start + 1; i < start + size; i++)
            {
                if (picked[i] && !picked[i - 1])
                    return false;
            }
        }
        return true;
    }

    /// <summary>When no single size fits, each taken card starts a new reward with the cards after it.</summary>
    private static List<ApCardRewardGroup> SplitAtPicks(IReadOnlyList<bool> picked)
    {
        var groups = new List<ApCardRewardGroup>();
        int start = 0;
        for (int i = 1; i < picked.Count; i++)
        {
            if (picked[i] && !picked[i - 1])
            {
                groups.Add(new ApCardRewardGroup(start, i - start));
                start = i;
            }
        }
        groups.Add(new ApCardRewardGroup(start, picked.Count - start));
        return groups;
    }
}
