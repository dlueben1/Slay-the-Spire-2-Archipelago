using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Extensions;
using static StS2AP.Data.ItemTable;
using StS2AP.Utils.RunGraph;

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
                var configuredValue = !MultiplayerSupport.UsesFrozenHostSettings
                    && localSettings.OverrideRelicRewardsAvailableAnytime
                    ? localSettings.RelicRewardsAvailableAnytime
                    : ArchipelagoClient.Settings?.RelicRewardsAvailableAnytime
                        ?? localSettings.RelicRewardsAvailableAnytime;

                return Math.Clamp(configuredValue, 0, ArchipelagoProgress._maxRelicRewards);
            }
        }

        /// <summary>
        /// Singleplayer: records one eligible natural relic source. Every attempt is counted, but
        /// only the first ten create an AP check and a bank that can be paired with a Relic
        /// receipt. Multiplayer decides natural relic sources in RelicReceiptMultiplayer.
        /// </summary>
        public static bool RecordEligibleReward(Player player, out int rewardNumber)
        {
            rewardNumber = ++ArchipelagoClient.Progress.RelicRewardsAttempted;
            if (rewardNumber > ArchipelagoProgress._maxRelicRewards)
                return false;

            ArchipelagoClient.Progress.BankedRelicRewards++;
            RelicCoupons.RefreshCounter(player);
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
                    !progress.Items.IsUsed(receipt.Index)
                    && !progress.RelicChoiceAssignments.ContainsKey(receipt.Index)
                    && !RelicReceiptMultiplayer.IsReserved(player, receipt.Index)
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
        /// Returns the signed Relic Coupons counter. Negative values mean more eligible natural
        /// relic rewards are needed; positive values mean more AP Relic receipts are needed.
        /// </summary>
        public static int GetCouponBalance(Player? player)
        {
            var progress = ArchipelagoClient.Progress;
            var earnedRewards = Math.Min(
                ArchipelagoProgress._maxRelicRewards,
                progress.RelicRewardsAttempted
            );
            var relicReceipts = player == null ? 0 : CountReceivedRelics(player);

            return earnedRewards
                + progress.RelicRewardsAvailableAnytimeForRun
                - relicReceipts;
        }

        /// <summary>
        /// Singleplayer: pairs the newly recorded bank with the oldest waiting gated receipt. The
        /// AP item is consumed immediately because the native reward screen now owns the relic
        /// grant. Returns true if it succeeded consuming a receipt.
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

            progress.Items.MarkUsed(receipt.Index);
            progress.BankedRelicRewards--;
            ApRunGraphRecorder.RecordItemClaim(receipt.Index);
            RelicCoupons.Activate(player);
            LogUtility.Info(
                $"Paired Relic item w/ index {receipt.Index} with a natural relic reward; " +
                $"{progress.BankedRelicRewards} banked reward(s) remain"
            );
            return true;
        }

        /// <summary>
        /// Singleplayer: removes the bank created for the current natural reward when
        /// compatibility changes prevent suppressing the corresponding native relic.
        /// </summary>
        public static void DiscardLastBankedReward(Player player)
        {
            if (ArchipelagoClient.Progress.BankedRelicRewards > 0)
                ArchipelagoClient.Progress.BankedRelicRewards--;
            RelicCoupons.RefreshCounter(player);
        }

        /// <summary>
        /// Pairs older earned banks with waiting gated receipts for display in the AP reward menu.
        /// The assignment is persisted before the bank is spent, so reopening or loading cannot
        /// reroll the offered relic. The value remains a list to support multiple choices later.
        /// </summary>
        public static void ReconcileBankedRewards(Player player)
        {
            // Multiplayer pairs banks in the queued relic assignment action as the menu opens
            // (ApQueuedRelicClaims), after a chest can have taken a receipt.
            if (MultiplayerSupport.IsRealMultiplayerRun)
                return;
            var progress = ArchipelagoClient.Progress;
            bool changed = false;
            while (progress.BankedRelicRewards > 0)
            {
                var receipt = FindWaitingReceiptForNaturalReward(player);
                if (receipt == null)
                    break;

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
                    if (changed)
                        ApRunData.PublishLocalProgress(player);
                    return;
                }

                progress.BankedRelicRewards--;
                changed = true;
                RelicCoupons.Activate(player);
                LogUtility.Info(
                    $"Assigned banked relic reward to AP item w/ index {receipt.Index}; " +
                    $"{progress.BankedRelicRewards} banked reward(s) remain"
                );
            }
            if (changed)
                ApRunData.PublishLocalProgress(player);
        }

        /// <summary>
        /// Returns whether this Relic receipt belongs in the AP reward menu. Receipts are available
        /// there when they are among the run's first X or have a persisted banked assignment.
        /// Multiplayer also offers unassigned bank pairs, from the action-owned receipt ledger;
        /// the menu's queued relic assignment action pairs them as it opens.
        /// </summary>
        public static bool IsAvailableInRewardMenu(IndexedItemInfo receipt, Player player)
        {
            var progress = ArchipelagoClient.Progress;
            if (progress.Items.IsUsed(receipt.Index)
                || receipt.Item.GetCharacterItemType() != APItem.Relic
                || receipt.Item.GetAPCharacterNumber() != player.GetAPCharacterNumber())
            {
                return false;
            }

            if (MultiplayerSupport.IsRealMultiplayerRun)
                return RelicReceiptMultiplayer.GetMenuCandidates(player).Contains(receipt.Index);

            return IsAnytimeReceipt(receipt, player)
                || progress.RelicChoiceAssignments.ContainsKey(receipt.Index);
        }

        private static IndexedItemInfo? FindWaitingReceiptForNaturalReward(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            return GetRelicReceipts(player)
                .Skip(GetAvailableAnytimeForRun())
                .FirstOrDefault(receipt =>
                    !progress.Items.IsUsed(receipt.Index)
                    && !progress.RelicChoiceAssignments.ContainsKey(receipt.Index)
                    && !RelicReceiptMultiplayer.IsReserved(player, receipt.Index)
                );
        }

        private static bool IsAnytimeReceipt(IndexedItemInfo receipt, Player player) =>
            IsAnytimeReceiptIndex(player, receipt.Index);

        /// <summary>
        /// Whether this Relic receipt is among the run's first X, which need no banked natural
        /// relic reward.
        /// </summary>
        internal static bool IsAnytimeReceiptIndex(Player player, int receiptIndex)
        {
            return GetRelicReceipts(player)
                .Take(GetAvailableAnytimeForRun())
                .Any(candidate => candidate.Index == receiptIndex);
        }

        private sealed record RelicReceiptCache(
            ApReceivedItemLedger Ledger,
            long Revision,
            long? CharacterOffset,
            IReadOnlyList<IndexedItemInfo> Receipts);

        // Menu and badge checks look up the receipts once per Relic receipt, so keep the
        // filtered list until the ledger changes instead of rescanning all AP history each time.
        private static RelicReceiptCache? _relicReceipts;

        private static IReadOnlyList<IndexedItemInfo> GetRelicReceipts(Player player)
        {
            var ledger = ArchipelagoClient.Progress.Items;
            var characterOffset = player.GetAPCharacterNumber();
            if (_relicReceipts is { } cached
                && ReferenceEquals(cached.Ledger, ledger)
                && cached.Revision == ledger.Revision
                && cached.CharacterOffset == characterOffset)
            {
                return cached.Receipts;
            }

            // The ledger already lists receipts in AP item index order.
            IndexedItemInfo[] receipts = ledger.Received
                .Where(receipt =>
                    receipt.Item.GetAPCharacterNumber() == characterOffset
                    && receipt.Item.GetCharacterItemType() == APItem.Relic
                )
                .ToArray();
            _relicReceipts = new RelicReceiptCache(ledger, ledger.Revision, characterOffset, receipts);
            return receipts;
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
