using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Card Sight bonus's extra reward card. AP card rewards enter the same generation scope in
/// <c>ApMirroredRewardDispatcher</c>; the Act 1 upgrade chance is in
/// <c>Patches_APCardRewardUpgradeOdds</c>.
/// </summary>
public static class Patches_CardSightRewards
{
    /// <summary>
    /// The game's own card rewards generate their cards in Populate, which a reroll calls again.
    /// A reward built from a fixed list of cards, as AP restores its own, already holds the extra
    /// card, so a reroll of it keeps its size.
    /// </summary>
    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    public static class MarkNativeRewardGeneration
    {
        [HarmonyPrefix]
        public static void Prefix(CardReward __instance, out IDisposable __state) =>
            __state = CardSight.EnterRewardGeneration(addsExtraCard: !__instance._cardsWereManuallySet);

        [HarmonyFinalizer]
        public static void Finalizer(IDisposable? __state) => __state?.Dispose();
    }

    [HarmonyPatch(
        typeof(CardFactory),
        nameof(CardFactory.CreateForReward),
        [typeof(Player), typeof(int), typeof(CardCreationOptions)]
    )]
    public static class AddExtraRewardCard
    {
        [HarmonyPrefix]
        public static void Prefix(ref int cardCount) =>
            cardCount += CardSight.TakeExtraRewardCard();
    }
}
