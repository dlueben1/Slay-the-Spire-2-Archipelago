using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace StS2AP.Patches;

/// <summary>
/// Gives regular AP Card Reward items the upgrade odds of their assigned act while
/// leaving the base game's RNG roll and reward-modification hooks in control.
/// </summary>
public static class Patches_APCardRewardUpgradeOdds
{
    [ThreadStatic]
    private static int? s_rewardActIndex;

    /// <summary>
    /// Populates one new AP reward under a temporary act override. CardReward.Populate is
    /// synchronous, so the override is always cleared before control returns to the UI.
    /// </summary>
    public static void PopulateForAct(CardReward reward, int actIndex)
    {
        var previousActIndex = s_rewardActIndex;
        try
        {
            s_rewardActIndex = actIndex;
            reward.Populate();
        }
        finally
        {
            s_rewardActIndex = previousActIndex;
        }
    }

    /// <summary>
    /// CardFactory calculates the current act's base odds before calling this public hook.
    /// Replace those incoming odds with the AP reward's assigned act, then let the original
    /// hook apply every normal relic and model modifier.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardRewardUpgradeOdds))]
    private static class OverrideAssignedActUpgradeOdds
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card, ref decimal originalOdds)
        {
            if (!s_rewardActIndex.HasValue || card.Rarity == CardRarity.Rare)
                return;

            var scaling = ArchipelagoClient.Progress.Ascensions.HasLevel(AscensionLevel.Scarcity)
                ? 0.125m
                : 0.25m;
            originalOdds = s_rewardActIndex.Value * scaling;
        }
    }
}
