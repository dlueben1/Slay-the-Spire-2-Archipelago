namespace StS2AP.Utils;

/// <summary>
/// Counts the Combat, Elite and Boss Gold items inside one condensed AP gold claim. The AP reward
/// menu pays all banked gold as one native reward, so without this the game's "after gaining
/// gold" effects, such as Dragon Fruit, would fire once for many items. One and Five Gold filler
/// are left out on purpose: they only add to whichever gain pays them out.
/// </summary>
internal static class GoldGainCount
{
    /// <summary>
    /// Records where a Combat, Elite or Boss Gold item ends in its character's raw AP gold bank.
    /// Call it after adding the item's gold, in receipt order, from both the live and the history
    /// path, so the positions match the redeemed-gold cursor.
    /// </summary>
    public static void RecordItemEnd(
        IDictionary<long, List<int>> itemEnds,
        long characterOffset,
        int bankTotalAfterItem)
    {
        if (!itemEnds.TryGetValue(characterOffset, out List<int>? ends))
            itemEnds[characterOffset] = ends = new List<int>();
        ends.Add(bankTotalAfterItem);
    }

    /// <summary>Counts the items whose gold ends inside the claimed raw range (before, after].</summary>
    public static int CountWithin(IReadOnlyList<int>? itemEnds, int rawBefore, int rawAfter)
    {
        if (itemEnds == null)
            return 0;
        return itemEnds.Count(end => end > rawBefore && end <= rawAfter);
    }
}
