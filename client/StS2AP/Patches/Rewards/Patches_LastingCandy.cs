using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Relics;
#if !STS2_AT_LEAST_0_111_0
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
#endif

namespace StS2AP.Patches;

/// <summary>
/// Makes Lasting Candy count AP card rewards, so every second one gains the extra Power. An AP
/// reward has no cards until its picker first opens. That reveal runs the relic's option hook
/// once on every replica, which keeps multiplayer copies of the counter equal, and reopening a
/// revealed reward runs no hook. Native card rewards left in the run count as well.
/// </summary>
public static class Patches_LastingCandy
{
    private static void CountCardReward(LastingCandy candy)
    {
#if STS2_AT_LEAST_0_111_0
        // Matches the native BeforeCombatRewardOffered.
        if (candy.IsInTriggeringCombat)
            TaskHelper.RunSafely(candy.DoActivateVisuals());
        candy.CombatRewardsSeen++;
#else
        // Matches the native AfterCombatEnd.
        candy.CombatsSeen++;
        if (candy.IsInTriggeringCombat)
            TaskHelper.RunSafely(candy.DoActivateVisuals());
#endif
        candy.InvokeDisplayAmountChanged();
    }

    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.TryModifyCardRewardOptions))]
    private static class CountApCardRewards
    {
#if STS2_AT_LEAST_0_111_0
        // 0.111 decides from the count before the reward being generated is added to it.
        [HarmonyPostfix]
#else
        // Older versions decide from the count after the reward being generated is added to it.
        [HarmonyPrefix]
#endif
        private static void Count(LastingCandy __instance, Player player)
        {
            if (Patches_APCardRewardUpgradeOdds.IsGeneratingApReward && __instance.Owner == player)
                CountCardReward(__instance);
        }
    }

#if !STS2_AT_LEAST_0_111_0
    // 0.111 counts only combats whose reward set still has a native card reward. Older versions count
    // every combat, so a fight whose reward became an AP check would still move the counter.
    // Vanilla Guests have no AP card rewards and keep the native cadence.
    private static bool CountsCardRewards(Player? player) => player != null
        && ReferenceEquals(player.RunState, RunManager.Instance.DebugOnlyGetState())
        && MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.CardRewards)
        && ApPlayerContextResolver.TryGetRewardSettings(player, out _);

    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.AfterCombatEnd))]
    private static class SuppressCombatCount
    {
        [HarmonyPrefix]
        private static bool Prefix(LastingCandy __instance, ref Task __result)
        {
            if (!CountsCardRewards(__instance.Owner))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    private static class CountNativeCardRewards
    {
        [HarmonyPrefix]
        private static void Prefix(CardReward __instance)
        {
            // AP rewards always report IsPopulated, and a reroll is the same reward again.
            if (__instance.IsPopulated
                || __instance._hasBeenRerolled
                || __instance.Options.Source != CardCreationSource.Encounter
                || !CountsCardRewards(__instance.Player))
                return;

            foreach (LastingCandy candy in __instance.Player.Relics.OfType<LastingCandy>())
            {
                // Melted relics no longer receive the option hook that spends the count.
                if (!candy.IsMelted)
                    CountCardReward(candy);
            }
        }
    }
#endif
}
