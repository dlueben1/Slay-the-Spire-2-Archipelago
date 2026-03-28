using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using System.Reflection;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches needed to support replacing Card/Relic/etc. Rewards with Archipelago Locations,
    /// and then sending those locations to other players when claimed.
    /// </summary>
    public static class Patches_InjectAPRewards
    {
        /// <summary>
        /// Patches RewardsSet.GenerateRewardsFor to replace or inject Archipelago Location rewards.
        /// </summary>
        [HarmonyPatch(typeof(RewardsSet), "GenerateRewardsFor")]
        public class GenerateRewardsForPatch
        {
            /// <summary>
            /// Reflection needed to nab `Options` off of a `CardReward`
            /// </summary>
            private static readonly PropertyInfo? s_optionsProp = typeof(CardReward).GetProperty("Options", BindingFlags.Instance | BindingFlags.NonPublic);

            /// <summary>
            /// Reflection needed to read `_wasGoldStolenBack` off of a `GoldReward`
            /// </summary>
            private static readonly FieldInfo? s_wasGoldStolenBackField = typeof(GoldReward).GetField("_wasGoldStolenBack", BindingFlags.Instance | BindingFlags.NonPublic);

            /// <summary>
            /// Inject Archipelago Rewards into the Loot Screen.
            /// I'm fairly certain I can write this with less nesting, but I'm scared to use `return` wrong on a HarmonyPatch lol
            /// </summary>
            [HarmonyPostfix]
            static void Postfix(ref List<Reward> __result, Player player, AbstractRoom room)
            {
                // We only want to inject for post-combat rewards
                if (room is CombatRoom)
                {
                    // Prepare the Character name from it's Title
                    var name = player.APName();

                    // Determine if a Relic Reward is being placed
                    var relicReward = __result.FirstOrDefault(r => r is RelicReward);
                    if (relicReward != null)
                    {
                        // Have we already given out enough relic rewards?
                        ArchipelagoClient.Progress.RelicRewardsAttempted++;
                        if (ArchipelagoClient.Progress.RelicRewardsAttempted <= ArchipelagoProgress._maxRelicRewards)
                        {
                            // Replace this reward with an AP Location reward
                            __result.Remove(relicReward);
                            __result.Add(new ArchipelagoReward($"{name} Relic {ArchipelagoClient.Progress.RelicRewardsAttempted}"));
                        }
                    }

                    // Determine if a Card Reward is being placed
                    var cardReward = __result.FirstOrDefault(r => r is CardReward);
                    if (cardReward != null)
                    {
                        // Is this a rare card reward?
                        var cardOpts = s_optionsProp.GetValue(cardReward) as CardCreationOptions;
                        bool isRare = cardOpts.RarityOdds == CardRarityOddsType.BossEncounter;

                        // If it's rare, then we always want to replace it (only happens twice, Act 1 & 2 Boss)
                        if (isRare)
                        {
                            // Replace this reward with an AP Location reward
                            ArchipelagoClient.Progress.RareCardRewardsAttempted++;
                            __result.Remove(cardReward);
                            __result.Add(new ArchipelagoReward($"{name} Rare Card Reward {ArchipelagoClient.Progress.RareCardRewardsAttempted}"));
                        }
                        // Otherwise, we have more checks to do
                        else
                        {
                            // Have we already given out enough card rewards (or are we skipping this one because we are doing every-other-card?
                            ArchipelagoClient.Progress.CardRewardsAttempted++;
                            var shouldSkipCardReward = ArchipelagoClient.Settings.ShouldShuffleAllCards
                                ? false
                                : (ArchipelagoClient.Progress.CardRewardsAttempted % 2 == 0);
                            if (ArchipelagoClient.Progress.CardRewardsAttempted <= ArchipelagoProgress._maxCardRewards && !shouldSkipCardReward)
                            {
                                // Replace this reward with an AP Location reward
                                var rewardNumber = ArchipelagoClient.Settings.ShouldShuffleAllCards
                                    ? ArchipelagoClient.Progress.CardRewardsAttempted
                                    : (ArchipelagoClient.Progress.CardRewardsAttempted + 1) / 2;
                                __result.Remove(cardReward);
                                __result.Add(new ArchipelagoReward($"{name} Card Reward {rewardNumber}"));
                            }
                        }
                    }

                    // If we're in GoldSanity, we want to replace the Gold Reward with an AP Location reward (so long as it's not returned gold)
                    var goldReward = __result.FirstOrDefault(r => r is GoldReward && s_wasGoldStolenBackField?.GetValue(r) is false);
                    if (goldReward != null && ArchipelagoClient.Settings.GoldSanity)
                    {
                        // Is this a boss gold reward? (It's a different location/check)
                        if (room.RoomType == RoomType.Boss)
                        {
                            // Grab the act number
                            int actNumber = GameUtility.CurrentPlayer?.RunState?.CurrentActIndex + 1 ?? 0;

                            // Replace this reward with an AP Location reward
                            __result.Remove(goldReward);
                            __result.Add(new ArchipelagoReward($"{name} Boss Gold {actNumber}"));
                        }
                        // Otherwise, see if it's one of the first twenty gold rewards, and if so then replace it with an AP item
                        else
                        {
                            ArchipelagoClient.Progress.GoldRewardsAttempted++;
                            // Have we already given out enough gold rewards?
                            if (ArchipelagoClient.Progress.GoldRewardsAttempted <= ArchipelagoProgress._maxGoldRewards)
                            {
                                // Replace this reward with an AP Location reward
                                __result.Remove(goldReward);
                                __result.Add(new ArchipelagoReward($"{name} Combat Gold {ArchipelagoClient.Progress.GoldRewardsAttempted}"));
                            }
                        }
                    }
                }
            }
        }
    }
}
