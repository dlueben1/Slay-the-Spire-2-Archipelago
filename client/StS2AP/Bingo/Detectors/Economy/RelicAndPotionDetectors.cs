using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>Relics held and potion slots filled.</summary>
internal static class RelicAndPotionDetectors
{
    // E19, M14, H08 "Have N relics": game relics in the relic bar, including game relics received
    // as AP items (user ruling). Relics this mod defines are left out: Relic Coupons, Boon relics
    // and Sight bonus relics are added on their own at run start (the same rule as H20).
    private static readonly (string Key, int Relics)[] RelicCountSquares =
    {
        ("E19", 6),
        ("M14", 12),
        ("H08", 25),
    };

    // I-E01 "Have relics of 3 different rarities (not Starter)" and I-M01 "Have relics of 4
    // different rarities": the rarities of those same game relics, never None.
    private const string ThreeRaritiesSquare = "I-E01";
    private const string FourRaritiesSquare = "I-M01";

    // E17 "Fill every potion slot": no empty potion slot, after each potion gained.
    private const string FullPotionSlotsSquare = "E17";

    public static void CheckRelics(Player player)
    {
        List<RelicModel> relics = player.Relics
            .Where(relic => !IsDefinedByThisMod(relic))
            .ToList();
        foreach ((string key, int count) in RelicCountSquares)
            EconomyDetector.CompleteIf(key, relics.Count >= count, player);

        HashSet<RelicRarity> rarities = relics
            .Select(relic => relic.Rarity)
            .Where(rarity => rarity != RelicRarity.None)
            .ToHashSet();
        EconomyDetector.CompleteIf(FourRaritiesSquare, rarities.Count >= 4, player);
        rarities.Remove(RelicRarity.Starter);
        EconomyDetector.CompleteIf(ThreeRaritiesSquare, rarities.Count >= 3, player);
    }

    private static bool IsDefinedByThisMod(RelicModel relic) =>
        relic.GetType().Assembly == typeof(RelicAndPotionDetectors).Assembly;

    public static void CheckPotionSlots(Player player)
    {
        EconomyDetector.CompleteIf(
            FullPotionSlotsSquare,
            player.MaxPotionCount > 0 && player.PotionSlots.All(potion => potion != null),
            player);
    }

    /// <summary>
    /// Obtain adds the relic before its first await, so the relic is in the bar by the time the
    /// method hands back its task and the postfix runs. Covers rewards, shops, events and AP relics.
    /// </summary>
    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), typeof(RelicModel), typeof(Player), typeof(int))]
    private static class AfterRelicObtained
    {
        [HarmonyPostfix]
        private static void Postfix(Player player) =>
            EconomyDetector.Run(nameof(AfterRelicObtained), player, static player =>
            {
                if (LocalContext.IsMe(player))
                    CheckRelics(player);
            });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionProcured))]
    private static class AfterPotionProcured
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState) =>
            EconomyDetector.Run(nameof(AfterPotionProcured), runState, static runState =>
            {
                if (EconomyDetector.LocalPlayer(runState) is { } me)
                    CheckPotionSlots(me);
            });
    }
}
