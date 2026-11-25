using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>Max HP squares.</summary>
internal static class MaxHpDetectors
{
    // M23 "Have Max HP below half your starting Max HP": below half the character's own starting
    // Max HP (before Frail Start or anything else changes it).
    private const string BelowHalfMaxHpSquare = "M23";

    // M24 "Gain 15 Max HP in one run": the run record's MaxHpGained summed over the run, which
    // counts every gain (AP Max HP items included) and ignores losses.
    private const string GainFifteenMaxHpSquare = "M24";
    private const int GainedMaxHp = 15;

    public static void CheckMaxHp(Player player)
    {
        EconomyDetector.CompleteIf(
            BelowHalfMaxHpSquare,
            player.Creature.MaxHp * 2 < player.Character.StartingHp,
            player);
        if (BingoSquares.IsOpen(GainFifteenMaxHpSquare))
        {
            int gained = EconomyDetector.RunRecord(player).Sum(stats => stats.MaxHpGained);
            EconomyDetector.CompleteIf(GainFifteenMaxHpSquare, gained >= GainedMaxHp, player);
        }
    }

    private static void CheckPlayerCreature(Creature creature)
    {
        if (creature.Player is { } player && LocalContext.IsMe(player))
            CheckMaxHp(player);
    }

    /// <summary>
    /// GainMaxHp raises Max HP and writes the run record before its first real await (the heal),
    /// so the postfix sees both.
    /// </summary>
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp))]
    private static class AfterMaxHpGained
    {
        [HarmonyPostfix]
        private static void Postfix(Creature creature) =>
            EconomyDetector.Run(nameof(AfterMaxHpGained), creature, CheckPlayerCreature);
    }

    /// <summary>
    /// LoseMaxHp lowers Max HP without awaiting unless it must first damage the player down to the
    /// new maximum; that case is picked up at the next checkpoint instead.
    /// </summary>
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp))]
    private static class AfterMaxHpLost
    {
        [HarmonyPostfix]
        private static void Postfix(Creature creature) =>
            EconomyDetector.Run(nameof(AfterMaxHpLost), creature, CheckPlayerCreature);
    }
}
