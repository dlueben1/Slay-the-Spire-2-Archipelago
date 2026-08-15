using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using System.Reflection;
using static MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches needed to support replacing Card/Relic/etc. Rewards with Archipelago Locations,
    /// and then sending those locations to other players when claimed.
    /// </summary>
    public static class Patches_InjectAPRewards
    {

        /// <summary>
        /// Adds the numbered AP check for one native Elite or Black Star relic. A waiting receipt
        /// keeps that exact reward; otherwise the reward is removed and its earned bank remains.
        /// </summary>
        private static void ProcessNativeRelicReward(
            List<Reward> rewards,
            RelicReward relicReward,
            Player player
        )
        {
            if (!RelicRewardUtility.RecordEligibleReward(out var rewardNumber))
                return;

            rewards.Add(new ArchipelagoReward($"{player.APName()} Relic {rewardNumber}"));

            // The native reward already exists. A receipt decides whether it survives beside the check.
            if (!RelicRewardUtility.TryConsumeWaitingReceiptForNaturalReward(player))
                rewards.Remove(relicReward);
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
                    var potionReward = __result.FirstOrDefault(r => r is PotionReward);
                    if (potionReward != null && ArchipelagoClient.Settings.PotionSanity)
                    {
                        ArchipelagoClient.Progress.PotionRewardsAttempted++;
                        // Have we already given out enough potion rewards?
                        if (ArchipelagoClient.Progress.PotionRewardsAttempted <= ArchipelagoProgress._maxPotionRewards)
                        {
                            // Replace this reward with an AP Location reward
                            __result.Remove(potionReward);
                            __result.Add(new ArchipelagoReward($"{name} Potion Drop {ArchipelagoClient.Progress.PotionRewardsAttempted}"));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the room's base Elite relic after tutorial and extra-room rewards are assembled.
        /// Treasure uses the same point because its native relic picker already exists by then;
        /// its AP check is sent automatically so the chest cinematic is not interrupted by a
        /// separate rewards screen.
        /// </summary>
        [HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom))]
        public static class ProcessRoomRelicReward
        {
            [HarmonyPostfix]
            public static void Postfix(RewardsSet __instance, AbstractRoom room)
            {
                var player = __instance.Player;
                if (player != GameUtility.CurrentPlayer)
                    return;

                if (room.RoomType == RoomType.Elite)
                {
                    // The base reward is first. Hook-added relics such as Black Star are handled
                    // at their own append point so each one gets a separate attempt.
                    var relicReward = __instance.Rewards.OfType<RelicReward>().FirstOrDefault();
                    if (relicReward != null)
                        ProcessNativeRelicReward(__instance.Rewards, relicReward, player);
                    return;
                }

                if (room.RoomType != RoomType.Treasure)
                {
                    return;
                }

                // An empty chest is not an eligible relic source. Check this before
                // recording the attempt so Silver Crucible neither sends a location
                // nor consumes a reward number or creates a bank.
                if (!Hook.ShouldGenerateTreasure(player.RunState, player))
                {
                    LogUtility.Info("Skipping AP Relic check for an empty treasure chest");
                    return;
                }

                if (!RelicRewardUtility.RecordEligibleReward(out var rewardNumber))
                    return;

                // Opening the chest is the interaction that earns this check. Sending it here
                // avoids inserting an AP rewards screen between the chest-open animation and the
                // native relic picker. SendCheck is idempotent for an already-checked location.
                // The alternative was the chest opening 2 times or having to manually generate a relic
                // I opted to use the native game default way. My rationale was that floor checks automatically send
                // things out so what's 3 more.
                GameUtility.SendCheck($"{player.APName()} Relic {rewardNumber}");

                var relicPicker = RunManager.Instance.TreasureRoomRelicSynchronizer;
                var nativeRelicExists = relicPicker.CurrentRelics?.Count > 0;
                if (nativeRelicExists
                    && !RelicRewardUtility.TryConsumeWaitingReceiptForNaturalReward(player))
                {
                    // The AP check was still sent; only the native chest relic becomes a bank.
                    // BeginRelicPicking currently exposes its backing List as IReadOnlyList. Clear
                    // it here so the native empty-chest flow owns the later completion event.
                    if (relicPicker.CurrentRelics is List<RelicModel> relics)
                    {
                        relics.Clear();
                    }
                    else
                    {
                        // Fail open if the game changes this collection type. Do not leave a bank
                        // behind as well as the native relic, which would duplicate the reward.
                        ArchipelagoClient.Progress.BankedRelicRewards--;
                        RelicCoupons.RefreshCounter(player);
                        LogUtility.Error(
                            """
                            Could not suppress the native treasure relic; preserving vanilla
                             without a Relic bank. Please notify the devs. 
                            """
                        );
                    }
                }
                else if (!nativeRelicExists)
                {
                    // Receipts delivered after the picker was generated should not suddenly appear
                    // in the chest. They spend the new bank through the AP menu instead.
                    // Note the logic is only sound because of the GateTreasureRelicPicker prefix
                    RelicRewardUtility.ReconcileBankedRewards(player);
                }
            }
        }

        /// <summary>
        /// Decides chest ownership before the native picker pulls from its relic bag. If no receipt
        /// is waiting, leave an empty picker for the AP check and bank recorded when the chest opens.
        /// </summary>
        [HarmonyPatch(
            typeof(TreasureRoomRelicSynchronizer),
            nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking)
        )]
        public static class GateTreasureRelicPicker
        {
            [HarmonyPrefix]
            public static bool Prefix(
                ref List<RelicModel> ____currentRelics,
                ref PlayerVote ____predictedVote
            )
            {
                if (____currentRelics != null)
                {
                    throw new InvalidOperationException(
                        "Attempted to start new relic picking session while one was already occurring!"
                    );
                }

                var player = GameUtility.CurrentPlayer;
                if (player == null
                    || ArchipelagoClient.Progress.RelicRewardsAttempted
                        >= ArchipelagoProgress._maxRelicRewards
                    || RelicRewardUtility.HasWaitingReceiptForNaturalReward(player))
                {
                    return true;
                }

                ____currentRelics = new List<RelicModel>();
                ____predictedVote = new PlayerVote
                {
                    voteReceived = true,
                    index = 0,
                };
                return false;
            }
        }

        /// <summary>
        /// Black Star appends its own native reward during reward hooks. Process only that new
        /// reward so it stays independent from the base Elite relic and receives its own AP check.
        /// </summary>
        [HarmonyPatch(typeof(BlackStar), nameof(BlackStar.TryModifyRewards))]
        public static class ProcessBlackStarRelicReward
        {
            [HarmonyPrefix]
            public static void Prefix(List<Reward> rewards, out int __state)
            {
                __state = rewards.Count;
            }

            [HarmonyPostfix]
            public static void Postfix(
                Player player,
                List<Reward> rewards,
                AbstractRoom? room,
                bool __result,
                int __state
            )
            {
                if (!__result
                    || player != GameUtility.CurrentPlayer
                    || room?.RoomType != RoomType.Elite)
                {
                    return;
                }

                var relicReward = rewards.Skip(__state).OfType<RelicReward>().FirstOrDefault();
                if (relicReward != null)
                    ProcessNativeRelicReward(rewards, relicReward, player);
            }
        }

        /// <summary>
        /// When an AP Location reward has already been claimed, make it semi-transparent in the rewards screen to indicate that it's been claimed.
        /// </summary>
        [HarmonyPatch(typeof(NRewardsScreen), "ShowScreen")]
        public static class ClaimedAPRewardsAreSemiTransparentPatch
        {
            private const float _claimedAlpha = 0.5f;
            private const float _normalAlpha = 1f;

            // Postfix runs after the screen creates and adds the NRewardButton controls.
            static void Postfix(NRewardsScreen __result)
            {
                // Grab the private _rewardsContainer field (where NRewardButton instances are added).
                FieldInfo? containerField = typeof(NRewardsScreen).GetField("_rewardsContainer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (containerField == null)
                {
                    return;
                }

                Control? rewardsContainer = containerField.GetValue(__result) as Control;
                if (rewardsContainer == null)
                {
                    return;
                }

                // Iterate created reward buttons and set their opacity based on reward type.
                foreach (NRewardButton btn in rewardsContainer.GetChildren().OfType<NRewardButton>())
                {
                    Reward? reward = btn.Reward;
                    if (reward == null)
                    {
                        continue;
                    }

                    // Make Claimed Archipelago Rewards semi-transparent
                    float targetAlpha = (reward is ArchipelagoReward && ((ArchipelagoReward)reward).IsChecked) ? _claimedAlpha : _normalAlpha;

                    // Immediate change:
                    btn.Modulate = new Color(1f, 1f, 1f, targetAlpha);
                }
            }
        }

    }
}
