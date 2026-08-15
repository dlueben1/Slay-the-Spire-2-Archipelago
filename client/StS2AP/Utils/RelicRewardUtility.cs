using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Extensions;
using StS2AP.Models;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils
{
    /// <summary>
    /// Coordinates AP Relic receipts with the first ten eligible Elite, treasure, and Black Star
    /// rewards in a run. The first configured receipts are available immediately; later receipts
    /// must be paired with an earned natural relic reward.
    /// </summary>
    public static class RelicRewardUtility
    {
        /// <summary>
        /// The anytime value that should be captured when a new run starts. A local override wins
        /// over slot data, and the result is kept within the number of Relic checks in a run.
        /// </summary>
        public static int EffectiveAvailableAnytime
        {
            get
            {
                var localSettings = ArchipelagoClient.LocalSettings.Value;
                var configuredValue = localSettings.OverrideRelicRewardsAvailableAnytime
                    ? localSettings.RelicRewardsAvailableAnytime
                    : ArchipelagoClient.Settings?.RelicRewardsAvailableAnytime
                        ?? localSettings.RelicRewardsAvailableAnytime;

                return Math.Clamp(configuredValue, 0, ArchipelagoProgress._maxRelicRewards);
            }
        }

        /// <summary>
        /// Records one eligible natural relic source. Every attempt is counted, but only the first
        /// ten create an AP check and a bank that can be paired with a Relic receipt.
        /// </summary>
        public static bool RecordEligibleReward(out int rewardNumber)
        {
            var progress = ArchipelagoClient.Progress;
            progress.RelicRewardsAttempted++;
            rewardNumber = progress.RelicRewardsAttempted;

            if (rewardNumber > ArchipelagoProgress._maxRelicRewards)
                return false;

            progress.BankedRelicRewards++;
            RelicCoupons.RefreshCounter();
            return true;
        }

        /// <summary>
        /// Returns whether a gated Relic receipt is waiting for a natural relic source.
        /// </summary>
        public static bool HasWaitingReceiptForNaturalReward(Player player)
        {
            return FindWaitingReceiptForNaturalReward(player) != null;
        }

        /// <summary>
        /// Returns the number of gated Relic receipts still waiting for a natural relic source.
        /// Used by the opt-in relic debug overlay.
        /// </summary>
        public static int CountWaitingReceiptsForNaturalReward(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            return GetRelicReceipts(player)
                .Skip(GetAvailableAnytimeForRun())
                .Count(receipt =>
                    !progress.UsedItems.Contains(receipt.Index)
                    && !progress.RelicChoiceAssignments.ContainsKey(receipt.Index)
                );
        }

        /// <summary>
        /// Returns every AP Relic receipt received for the current character, including anytime,
        /// waiting, assigned, and already-consumed receipts.
        /// </summary>
        public static int CountReceivedRelics(Player player)
        {
            return GetRelicReceipts(player).Count();
        }

        /// <summary>
        /// Pairs the newly recorded bank with the oldest waiting gated receipt. The AP item is
        /// consumed immediately because the native reward screen now owns the relic grant.
        /// Returns true if it succeeded consuming a receipt.
        /// </summary>
        public static bool TryConsumeWaitingReceiptForNaturalReward(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            var receipt = FindWaitingReceiptForNaturalReward(player);
            if (receipt == null)
                return false;

            if (progress.BankedRelicRewards <= 0)
            {
                LogUtility.Error(
                    $"Cannot pair Relic item w/ index {receipt.Index} for {player.APName()}: " +
                    "no banked relic reward exists"
                );
                return false;
            }

            progress.UsedItems.Add(receipt.Index);
            progress.BankedRelicRewards--;
            RelicCoupons.Activate(player);
            LogUtility.Info(
                $"Paired Relic item w/ index {receipt.Index} with a natural relic reward; " +
                $"{progress.BankedRelicRewards} banked reward(s) remain"
            );
            return true;
        }

        /// <summary>
        /// Pairs older earned banks with waiting gated receipts for display in the AP reward menu.
        /// The assignment is persisted before the bank is spent, so reopening or loading cannot
        /// reroll the offered relic. The value remains a list to support multiple choices later.
        /// </summary>
        public static void ReconcileBankedRewards(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            while (progress.BankedRelicRewards > 0)
            {
                var receipt = FindWaitingReceiptForNaturalReward(player);
                if (receipt == null)
                    return;

                var choices = progress.GetOrAssignRelicChoices(
                    receipt.Index,
                    player,
                    choiceCount: 1
                );
                if (choices.Count != 1)
                {
                    LogUtility.Error(
                        $"Could not pair banked relic reward with AP item w/ index {receipt.Index} " +
                        $"for {player.APName()}; " +
                        "leaving both available for retry"
                    );
                    return;
                }

                progress.BankedRelicRewards--;
                RelicCoupons.Activate(player);
                LogUtility.Info(
                    $"Assigned banked relic reward to AP item w/ index {receipt.Index}; " +
                    $"{progress.BankedRelicRewards} banked reward(s) remain"
                );
            }
        }

        /// <summary>
        /// Returns whether this Relic receipt belongs in the AP reward menu. Receipts are available
        /// there when they are among the run's first X or have a persisted banked assignment.
        /// </summary>
        public static bool IsAvailableInRewardMenu(IndexedItemInfo receipt, Player player)
        {
            var progress = ArchipelagoClient.Progress;
            if (progress.UsedItems.Contains(receipt.Index)
                || receipt.Item.GetCharacterSpecificItemID() != APItem.Relic
                || receipt.Item.GetCharacterOffset() != player.Character.GetCharacterOffset())
            {
                return false;
            }

            return IsAnytimeReceipt(receipt, player)
                || progress.RelicChoiceAssignments.ContainsKey(receipt.Index);
        }

        /// <summary>
        /// Completes a relic claimed through the AP reward menu and releases its persisted choice.
        /// The bank, when one was required, was already spent when this assignment was created.
        /// </summary>
        public static void CompleteMenuClaim(int itemIndex)
        {
            var progress = ArchipelagoClient.Progress;
            if (!progress.UsedItems.Contains(itemIndex))
                progress.UsedItems.Add(itemIndex);

            progress.RelicChoiceAssignments.Remove(itemIndex);
        }

        private static IndexedItemInfo? FindWaitingReceiptForNaturalReward(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            return GetRelicReceipts(player)
                .Skip(GetAvailableAnytimeForRun())
                .FirstOrDefault(receipt =>
                    !progress.UsedItems.Contains(receipt.Index)
                    && !progress.RelicChoiceAssignments.ContainsKey(receipt.Index)
                );
        }

        private static bool IsAnytimeReceipt(IndexedItemInfo receipt, Player player)
        {
            return GetRelicReceipts(player)
                .Take(GetAvailableAnytimeForRun())
                .Any(candidate => candidate.Index == receipt.Index);
        }

        private static IEnumerable<IndexedItemInfo> GetRelicReceipts(Player player)
        {
            var characterOffset = player.Character.GetCharacterOffset();
            return ArchipelagoClient.Progress.AllReceivedItems
                .Where(receipt =>
                    receipt.Item.GetCharacterOffset() == characterOffset
                    && receipt.Item.GetCharacterSpecificItemID() == APItem.Relic
                )
                .OrderBy(receipt => receipt.Index);
        }

        private static int GetAvailableAnytimeForRun()
        {
            return Math.Clamp(
                ArchipelagoClient.Progress.RelicRewardsAvailableAnytimeForRun,
                0,
                ArchipelagoProgress._maxRelicRewards
            );
        }
    }
}
