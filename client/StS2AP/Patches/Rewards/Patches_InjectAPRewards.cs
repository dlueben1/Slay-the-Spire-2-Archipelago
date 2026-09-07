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
using StS2AP.Utils;
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
            if (!RelicRewardUtility.RecordEligibleReward(player, out var rewardNumber))
                return;

            rewards.Add(new ArchipelagoReward(
                player,
                $"{player.APName()} Relic {rewardNumber}"
            ));

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
            /// Inject Archipelago Rewards into the Loot Screen.
            /// I'm fairly certain I can write this with less nesting, but I'm scared to use `return` wrong on a HarmonyPatch lol
            /// </summary>
            [HarmonyPostfix]
            static void Postfix(ref List<Reward> __result, Player player, AbstractRoom room)
            {
                // AP_MP: AP reward specs must reach all peers before reward-set creation.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.CombatRewardLocations
                ))
                    return;

                if (!MultiplayerLocationChecks.TryGetCheckSettings(
                        player,
                        out ArchipelagoSettings settings))
                {
                    return;
                }

                // We only want to inject for post-combat rewards
                if (room is CombatRoom)
                {
                    // Prepare the Character name from it's Title
                    var name = player.APName();

                    // Determine if a Card Reward is being placed
                    CardReward? cardReward = __result.OfType<CardReward>().FirstOrDefault();
                    if (cardReward != null)
                    {
                        // Is this a rare card reward?
                        CardCreationOptions cardOpts = cardReward.Options;
                        bool isRare = cardOpts.RarityOdds == CardRarityOddsType.BossEncounter;

                        // If it's rare, then we always want to replace it (only happens twice, Act 1 & 2 Boss)
                        if (isRare)
                        {
                            // Replace this reward with an AP Location reward
                            int rewardNumber =
                                MultiplayerLocationChecks.IncrementRareCardRewards(player);
                            __result.Remove(cardReward);
                            __result.Add(new ArchipelagoReward(
                                player,
                                $"{name} Rare Card Reward {rewardNumber}"
                            ));
                        }
                        // Otherwise, we have more checks to do
                        else
                        {
                            // Have we already given out enough card rewards (or are we skipping this one because we are doing every-other-card?
                            int attempt = MultiplayerLocationChecks.IncrementCardRewards(player);
                            var shouldSkipCardReward = settings.ShouldShuffleAllCards
                                ? false
                                : (attempt % 2 == 0);
                            if (attempt <= ArchipelagoProgress._maxCardRewards && !shouldSkipCardReward)
                            {
                                // Replace this reward with an AP Location reward
                                var rewardNumber = settings.ShouldShuffleAllCards
                                    ? attempt
                                    : (attempt + 1) / 2;
                                __result.Remove(cardReward);
                                __result.Add(new ArchipelagoReward(
                                    player,
                                    $"{name} Card Reward {rewardNumber}"
                                ));
                            }
                        }
                    }

                    // If we're in GoldSanity, we want to replace the Gold Reward with an AP Location reward (so long as it's not returned gold)
                    var goldReward = __result.OfType<GoldReward>().FirstOrDefault(reward => !reward._wasGoldStolenBack);
                    if (goldReward != null && settings.GoldSanity)
                    {
                        // Is this a boss gold reward? (It's a different location/check)
                        if (room.RoomType == RoomType.Boss)
                        {
                            // Grab the act number
                            int actNumber = player.RunState.CurrentActIndex + 1;

                            // Replace this reward with an AP Location reward
                            __result.Remove(goldReward);
                            __result.Add(new ArchipelagoReward(
                                player,
                                $"{name} Boss Gold {actNumber}"
                            ));
                        }
                        // Otherwise, see if it's one of the first twenty gold rewards, and if so then replace it with an AP item
                        else
                        {
                            int rewardNumber =
                                MultiplayerLocationChecks.IncrementGoldRewards(player);
                            // Have we already given out enough gold rewards?
                            if (rewardNumber <= ArchipelagoProgress._maxGoldRewards)
                            {
                                // Replace this reward with an AP Location reward
                                __result.Remove(goldReward);
                                __result.Add(new ArchipelagoReward(
                                    player,
                                    $"{name} Combat Gold {rewardNumber}"
                                ));
                            }
                        }
                    }
                    var potionReward = __result.FirstOrDefault(r => r is PotionReward);
                    if (potionReward != null && settings.PotionSanity)
                    {
                        int rewardNumber =
                            MultiplayerLocationChecks.IncrementPotionRewards(player);
                        // Have we already given out enough potion rewards?
                        if (rewardNumber <= ArchipelagoProgress._maxPotionRewards)
                        {
                            // Replace this reward with an AP Location reward
                            __result.Remove(potionReward);
                            __result.Add(new ArchipelagoReward(
                                player,
                                $"{name} Potion Drop {rewardNumber}"
                            ));
                        }
                    }
                    MultiplayerLocationChecks.PublishLocalProgress(player);
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
                // AP_MP: Reward replacement needs matching owner and reward-set IDs.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.CombatRewardLocations
                ))
                    return;

                var player = __instance.Player;
                if (!MultiplayerLocationChecks.TryGetCheckSettings(player, out _))
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

                if (MultiplayerSupport.IsRealMultiplayerRun)
                {
                    var run = (RunState)player.RunState;
                    var candidate = RelicReceiptMultiplayer.GetChest(run).Candidates
                        .Single(c => c.PlayerNetId == player.NetId);
                    if (!candidate.GeneratesRelic
                        || !RelicReceiptMultiplayer.MarkChestOpened(run, player.NetId))
                        return;
                    RelicRewardUtility.RecordFrozenChestReward(player, candidate.RewardNumber);
                    if (!candidate.ApGated) return;
                    MultiplayerLocationChecks.QueueCheck(player, $"{player.APName()} Relic {candidate.RewardNumber}");
                    if (candidate.ReceiptIndex is int receiptIndex)
                    {
                        if (!RelicRewardUtility.TryConsumeWaitingReceiptForNaturalReward(player, receiptIndex))
                        {
                            MultiplayerSupport.InvalidateRunClaims("Frozen chest receipt could not be consumed.");
                            throw new InvalidOperationException($"Chest receipt {player.NetId}:{receiptIndex} could not be consumed.");
                        }
                        RelicReceiptMultiplayer.ConsumeChest(player, receiptIndex);
                    }
                    LogUtility.Info($"Treasure AP receipt settled: room={RelicReceiptMultiplayer.RoomKey(run)}, "
                        + $"player={player.NetId}, reward={candidate.RewardNumber}, receipt={candidate.ReceiptIndex}.");
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

                if (!RelicRewardUtility.RecordEligibleReward(player, out var rewardNumber))
                    return;

                // Opening the chest is the interaction that earns this check. Sending it here
                // avoids inserting an AP rewards screen between the chest-open animation and the
                // native relic picker. SendCheck is idempotent for an already-checked location.
                // The alternative was the chest opening 2 times or having to manually generate a relic
                // I opted to use the native game default way. My rationale was that floor checks automatically send
                // things out so what's 3 more.
                MultiplayerLocationChecks.QueueCheck(
                    player,
                    $"{player.APName()} Relic {rewardNumber}"
                );

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
                        RelicRewardUtility.DiscardLastBankedReward(player);
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
        /// Singleplayer gates its native picker directly. Multiplayer rolls the native candidates
        /// on every peer, then applies the host's frozen receipt mask before the chest can open.
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
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.CombatRewardLocations
                ))
                    return true;

                // Multiplayer's picker is shared. Let the base game build its deterministic
                // player-ordered candidates, then remove only the candidates whose AP source
                // was excluded by the host's entry-time decision in the postfix below.
                if (MultiplayerSupport.IsRealMultiplayerRun)
                {
                    RelicReceiptMultiplayer.GetChest(RunManager.Instance.DebugOnlyGetState()
                        ?? throw new InvalidOperationException("No run exists for the native chest picker."));
                    return true;
                }

                if (____currentRelics != null)
                {
                    throw new InvalidOperationException(
                        "Attempted to start new relic picking session while one was already occurring!"
                    );
                }

                var player = GameUtility.CurrentPlayer;
                if (player == null
                    || !MultiplayerLocationChecks.TryGetCheckSettings(player, out _)
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

            [HarmonyPostfix]
            public static void Postfix(ref List<RelicModel> ____currentRelics)
            {
                if (!MultiplayerSupport.IsRealMultiplayerRun
                    || !MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.CombatRewardLocations)
                    || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
                {
                    return;
                }

                var decision = RelicReceiptMultiplayer.GetChest(runState);
                var generated = decision.Candidates.Where(c => c.GeneratesRelic).ToList();
                if (generated.Count != (____currentRelics?.Count ?? 0)
                    || !decision.Candidates.Select(c => c.PlayerNetId).SequenceEqual(runState.Players.Select(p => p.NetId))
                    || decision.Candidates.Where((c, i) =>
                        c.GeneratesRelic != Hook.ShouldGenerateTreasure(runState, runState.Players[i])).Any())
                {
                    MultiplayerSupport.InvalidateRunClaims("Native treasure candidates differ from the host decision.");
                    throw new InvalidOperationException("Native treasure candidates differ from the host decision.");
                }
                // Every replica rolled every native candidate first, preserving RNG and bag order.
                // Filter only by the immutable host mask, never by this peer's live AP history.
                RelicReceiptMultiplayer.AgreeNativeCandidates(runState,
                    ____currentRelics?.Select(relic => relic.Id.ToString()).ToList() ?? []);
                if (____currentRelics != null)
                    for (int i = generated.Count - 1; i >= 0; i--)
                        if (!generated[i].Keep) ____currentRelics.RemoveAt(i);

                LogUtility.Info(
                    $"Treasure AP picker: act={runState.CurrentActIndex + 1}, coord={runState.CurrentMapCoord}, "
                        + $"candidates=[{string.Join(",", ____currentRelics?.Select(relic => relic.Id.ToString()) ?? [])}]."
                );
                if (____currentRelics?.Count > 0
                    && ____currentRelics.Count < runState.Players.Count)
                {
                    LogUtility.Info(
                        $"Opening shared scarcity chest: {____currentRelics.Count} relic candidate(s) "
                            + $"for {runState.Players.Count} players"
                    );
                }

                // Do not complete an empty picker here. The native empty-chest animation calls
                // CompleteWithNoRelics after the chest opens; completing it during room entry
                // emits RelicsAwarded twice and completes the UI's TaskCompletionSource twice.
                RelicReceiptMultiplayer.MarkPickerReady(runState);
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
                // AP_MP: Location completion must be attributed to the local AP owner only.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.CombatRewardLocations
                ))
                    return;

                if (!__result
                    || !MultiplayerLocationChecks.TryGetCheckSettings(player, out _)
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
                // AP_MP: Reward-screen presentation follows the replicated reward-set gate.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.CombatRewardLocations
                ))
                    return;

                Control? rewardsContainer = __result._rewardsContainer;
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
