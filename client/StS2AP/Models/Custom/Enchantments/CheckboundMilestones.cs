namespace StS2AP.Models.Enchantments;

/// <summary>
/// Checkbound's milestones: +1 at each share of a character's checks sent, up to +6. Each gap is
/// a little wider than the last, so the early points come quickly and the last lands late in the
/// seed (user decision, 2026-11-14).
/// </summary>
public static class CheckboundMilestones
{
    public static readonly IReadOnlyList<int> Percents = [5, 12, 22, 36, 55, 80];

    /// <summary>The bonus for <paramref name="sent"/> of <paramref name="total"/> checks, from 0 to 6.</summary>
    public static int Bonus(int sent, int total) =>
        total <= 0 ? 0 : Percents.Count(milestone => 100L * sent >= (long)milestone * total);
}
