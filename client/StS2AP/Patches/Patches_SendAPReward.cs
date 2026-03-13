using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches needed to support replacing Card/Relic/etc. Rewards with Archipelago Locations,
    /// and then sending those locations to other players when claimed.
    /// </summary>
    public static class Patches_SendAPReward
    {
        /// <summary>
        /// Disables the tutorial rewards - when the game is first played, the rewards you get are fixed rather than dynamic.
        /// </summary>
        [HarmonyPatch(typeof(RewardsSet), "TryGenerateTutorialRewards")]
        public class TurnOffTutorialRewardsDuringArchipelagoPatch
        {
            /// <summary>
            /// Skip the original function and set the result to false
            /// </summary>
            static bool Prefix(ref bool __result, Player player, AbstractRoom room)
            {
                __result = false;
                return false;
            }
        }

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
            /// Inject Archipelago Rewards into the Loot Screen.
            /// I'm fairly certain I can write this with less nesting, but I'm scared to use `return` wrong on a HarmonyPatch lol
            /// </summary>
            [HarmonyPostfix]
            static void Postfix(ref List<Reward> __result, Player player, AbstractRoom room)
            {
                // We only want to inject for post-combat rewards
                if(room is CombatRoom)
                {
                    // Prepare the Character name from it's Title (Note: There is possibly a better way to get the raw name without the "The " prefix)
                    var name = player.Character.Title.GetFormattedText().Split().Last();

                    // Determine if a Relic Reward is being placed
                    var relicReward = __result.FirstOrDefault(r => r is RelicReward);
                    if (relicReward != null)
                    {
                        // Have we already given out enough relic rewards?
                        ArchipelagoClient.Progress.RelicRewardsAttempted++;
                        if(ArchipelagoClient.Progress.RelicRewardsAttempted <= ArchipelagoProgress._maxRelicRewards)
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
                        if(isRare)
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
                                __result.Remove(cardReward);
                                __result.Add(new ArchipelagoReward($"{name} Card Reward {ArchipelagoClient.Progress.CardRewardsAttempted}"));
                            }
                        }
                    }
                }
            }
        }

        ///// <summary>
        ///// Patches the Reward Screen to replace one or more of the rewards with an Archipelago Location reward.
        ///// </summary>
        //[HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.SetRewards))]
        //public class NRewardsScreenSetRewardsPatch
        //{
        //    public static void Prefix(NRewardsScreen __instance, ref IEnumerable<Reward> rewards)
        //    {
        //        // Is the user getting a card reward, and if so, should it be replaced?
        //        var cardReward = rewards.FirstOrDefault(r => r is CardReward);
        //        if(cardReward != null && ++ArchipelagoClient.Progress.CardRewardsAttempted < ArchipelagoProgress._maxCardRewards)
        //        {
        //            // Increment the number of Card Rewards we've seen
        //            ArchipelagoClient.Progress.CardRewardsAttempted++;

        //            // 
        //            bool isSkipped = (ArchipelagoClient.Progress.CardRewardsAttempted % 2 == 0 && !ArchipelagoClient.Settings.ShouldShuffleAllCards);
        //            if((ArchipelagoClient.Progress.CardRewardsAttempted % 2 == 0 && !ArchipelagoClient.Settings.ShouldShuffleAllCards) && )
        //        }

        //        // Should we replace a Relic?
        //        var relicReward = rewards.FirstOrDefault(r => r is RelicReward);
        //        if(relicReward != null)
        //        {

        //        }

        //        // Should we replace a Rare Card Reward?
        //        var rareReward = rewards.FirstOrDefault(r => r is SpecialCardReward);
        //        if(rareReward != null)
        //        {

        //        }

        //        //TODO: Make decision on WHY and WHAT to load up with
        //        List<Reward> patchedRewards = new List<Reward>();
        //        patchedRewards.AddRange(rewards);
        //        patchedRewards.Add(new ArchipelagoReward("Ironclad Rare Card Reward 1"));
        //        rewards = patchedRewards;
        //    }
        //}

    }
}
