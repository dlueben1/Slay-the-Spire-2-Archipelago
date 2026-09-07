using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Extensions;
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
                var configuredValue = !MultiplayerSupport.UsesFrozenHostSettings
                    && localSettings.OverrideRelicRewardsAvailableAnytime
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
        public static bool RecordEligibleReward(Player player, out int rewardNumber)
        {
            bool localOwner = MultiplayerLocationChecks.IsLocalProgressOwner(player);
            ApRunProgressState? replicated = null;
            if (MultiplayerSupport.IsRealMultiplayerRun
                && !localOwner
                && !MultiplayerLocationChecks.TryGetRemoteProgress(player, out replicated))
            {
                rewardNumber = int.MaxValue;
                return false;
            }

            rewardNumber = localOwner || !MultiplayerSupport.IsRealMultiplayerRun
                ? ++ArchipelagoClient.Progress.RelicRewardsAttempted
                : ++replicated!.RelicRewardsAttempted;

            if (rewardNumber > ArchipelagoProgress._maxRelicRewards)
            {
                MultiplayerLocationChecks.PublishLocalProgress(player);
                return false;
            }

            if (localOwner || !MultiplayerSupport.IsRealMultiplayerRun)
                ArchipelagoClient.Progress.BankedRelicRewards++;
            else
                replicated!.BankedRelicRewards++;
            if (localOwner)
                RelicCoupons.RefreshCounter(player);
            MultiplayerLocationChecks.PublishLocalProgress(player);
            return true;
        }

        /// <summary>
        /// Returns whether a gated Relic receipt is waiting for a natural relic source.
        /// </summary>
        public static bool HasWaitingReceiptForNaturalReward(Player player)
        {
            return FindWaitingReceiptIndexForNaturalReward(player).HasValue;
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
        /// Pairs the newly recorded bank with the oldest waiting gated receipt. The AP item is
        /// consumed immediately because the native reward screen now owns the relic grant.
        /// Returns true if it succeeded consuming a receipt.
        /// </summary>
        public static bool TryConsumeWaitingReceiptForNaturalReward(Player player, int? frozenReceiptIndex = null)
        {
            bool localOwner = MultiplayerLocationChecks.IsLocalProgressOwner(player);
            ApRunProgressState? replicated = null;
            if (MultiplayerSupport.IsRealMultiplayerRun
                && !localOwner
                && !MultiplayerLocationChecks.TryGetRemoteProgress(player, out replicated))
            {
                return false;
            }

            int? receiptIndex = frozenReceiptIndex ?? FindWaitingReceiptIndexForNaturalReward(player);
            if (!receiptIndex.HasValue)
                return false;

            bool used = localOwner || !MultiplayerSupport.IsRealMultiplayerRun
                ? ArchipelagoClient.Progress.Items.IsUsed(receiptIndex.Value)
                : replicated!.UsedItems.Contains(receiptIndex.Value);
            if (used)
                return frozenReceiptIndex.HasValue;

            int bankedRewards = localOwner || !MultiplayerSupport.IsRealMultiplayerRun
                ? ArchipelagoClient.Progress.BankedRelicRewards
                : replicated!.BankedRelicRewards;

            if (bankedRewards <= 0)
            {
                LogUtility.Error(
                    $"Cannot pair Relic item w/ index {receiptIndex.Value} for {player.APName()}: " +
                    "no banked relic reward exists"
                );
                return false;
            }

            if (localOwner || !MultiplayerSupport.IsRealMultiplayerRun)
            {
                ArchipelagoClient.Progress.Items.MarkUsed(receiptIndex.Value);
                ArchipelagoClient.Progress.BankedRelicRewards--;
            }
            else
            {
                replicated!.UsedItems.Add(receiptIndex.Value);
                replicated.BankedRelicRewards--;
            }
            if (localOwner)
                RelicCoupons.Activate(player);
            LogUtility.Info(
                $"Paired Relic item w/ index {receiptIndex.Value} with a natural relic reward; " +
                $"{bankedRewards - 1} banked reward(s) remain"
            );
            MultiplayerLocationChecks.PublishLocalProgress(player);
            return true;
        }

        /// <summary>
        /// Removes the bank created for the current natural reward when compatibility changes
        /// prevent suppressing the corresponding native relic.
        /// </summary>
        public static void DiscardLastBankedReward(Player player)
        {
            bool localOwner = MultiplayerLocationChecks.IsLocalProgressOwner(player);
            if (!MultiplayerSupport.IsRealMultiplayerRun || localOwner)
            {
                if (ArchipelagoClient.Progress.BankedRelicRewards > 0)
                    ArchipelagoClient.Progress.BankedRelicRewards--;
                if (localOwner)
                    RelicCoupons.RefreshCounter(player);
            }
            else if (MultiplayerLocationChecks.TryGetRemoteProgress(
                player,
                out ApRunProgressState progress)
                && progress.BankedRelicRewards > 0)
            {
                progress.BankedRelicRewards--;
            }
            MultiplayerLocationChecks.PublishLocalProgress(player);
        }

        /// <summary>
        /// Pairs older earned banks with waiting gated receipts for display in the AP reward menu.
        /// The assignment is persisted before the bank is spent, so reopening or loading cannot
        /// reroll the offered relic. The value remains a list to support multiple choices later.
        /// </summary>
        public static void ReconcileBankedRewards(Player player, IReadOnlySet<int>? approvedMenu = null)
        {
            // Multiplayer assigns only after the host has excluded receipts reserved by a chest.
            // Item callbacks still publish receipt history; opening the menu performs the pairing.
            if (MultiplayerSupport.IsRealMultiplayerRun && approvedMenu == null)
                return;
            var progress = ArchipelagoClient.Progress;
            bool changed = false;
            while (progress.BankedRelicRewards > 0)
            {
                var receipt = MultiplayerSupport.IsRealMultiplayerRun
                    ? GetRelicReceipts(player).Skip(GetAvailableAnytimeForRun()).FirstOrDefault(r =>
                        !progress.Items.IsUsed(r.Index)
                        && !progress.RelicChoiceAssignments.ContainsKey(r.Index)
                        && approvedMenu!.Contains(r.Index)
                        && RelicReceiptMultiplayer.CanUseMenu(player, r.Index))
                    : FindWaitingReceiptForNaturalReward(player);
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
        /// Multiplayer also advertises unassigned bank pairs; the host approves them on opening.
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
                return GetMenuReservationCandidates(player).Contains(receipt.Index)
                    && (!RelicReceiptMultiplayer.IsReserved(player, receipt.Index)
                        || RelicReceiptMultiplayer.CanUseMenu(player, receipt.Index));

            return IsAnytimeReceipt(receipt, player)
                || progress.RelicChoiceAssignments.ContainsKey(receipt.Index);
        }

        /// <summary>
        /// Completes a relic claimed through the AP reward menu and releases its persisted choice.
        /// The bank, when one was required, was already spent when this assignment was created.
        /// </summary>
        public static void CompleteMenuClaim(Player player, int itemIndex)
        {
            var progress = ArchipelagoClient.Progress;
            progress.Items.MarkUsed(itemIndex);

            progress.RelicChoiceAssignments.Remove(itemIndex);
            ApRunData.PublishLocalProgress(player);
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

        public static int? FindWaitingReceiptIndexForNaturalReward(Player player)
        {
            if (!MultiplayerSupport.IsRealMultiplayerRun
                || MultiplayerLocationChecks.IsLocalProgressOwner(player))
            {
                return FindWaitingReceiptForNaturalReward(player)?.Index;
            }

            if (!MultiplayerLocationChecks.TryGetRemoteProgress(
                player,
                out ApRunProgressState progress))
                return null;

            return MultiplayerLocationChecks.GetReplicatedRelicReceiptIndexes(player, progress)
                .Skip(Math.Clamp(
                    progress.RelicRewardsAvailableAnytimeForRun,
                    0,
                    ArchipelagoProgress._maxRelicRewards
                ))
                .Where(index =>
                    !progress.UsedItems.Contains(index)
                    && !progress.RelicChoiceAssignments.ContainsKey(index)
                    && !RelicReceiptMultiplayer.IsReserved(player, index))
                .Select(index => (int?)index)
                .FirstOrDefault();
        }

        public static IReadOnlyList<int> GetMenuReservationCandidates(Player player)
        {
            var progress = ArchipelagoClient.Progress;
            var receipts = GetRelicReceipts(player).ToList();
            var available = receipts.Take(GetAvailableAnytimeForRun())
                .Concat(receipts.Where(r => progress.RelicChoiceAssignments.ContainsKey(r.Index)));
            var waiting = receipts.Skip(GetAvailableAnytimeForRun()).Where(r =>
                !progress.Items.IsUsed(r.Index)
                && !progress.RelicChoiceAssignments.ContainsKey(r.Index)
                && (!RelicReceiptMultiplayer.IsReserved(player, r.Index)
                    || RelicReceiptMultiplayer.CanUseMenu(player, r.Index)))
                .Take(progress.BankedRelicRewards);
            return available.Concat(waiting).Where(r => !progress.Items.IsUsed(r.Index))
                .Select(r => r.Index).Distinct().ToList();
        }

        /// <summary>Opening order must not renumber the host's frozen chest check.</summary>
        public static void RecordFrozenChestReward(Player player, int rewardNumber)
        {
            bool local = MultiplayerLocationChecks.IsLocalProgressOwner(player);
            if (local)
            {
                var progress = ArchipelagoClient.Progress;
                progress.BankedRelicRewards += Math.Max(0, Math.Min(rewardNumber, 10)
                    - Math.Min(progress.RelicRewardsAttempted, 10));
                progress.RelicRewardsAttempted = Math.Max(progress.RelicRewardsAttempted, rewardNumber);
                RelicCoupons.RefreshCounter(player);
            }
            else if (MultiplayerLocationChecks.TryGetRemoteProgress(player, out var progress))
            {
                progress.BankedRelicRewards += Math.Max(0, Math.Min(rewardNumber, 10)
                    - Math.Min(progress.RelicRewardsAttempted, 10));
                progress.RelicRewardsAttempted = Math.Max(progress.RelicRewardsAttempted, rewardNumber);
            }
            MultiplayerLocationChecks.PublishLocalProgress(player);
        }

        private static bool IsAnytimeReceipt(IndexedItemInfo receipt, Player player)
        {
            return GetRelicReceipts(player)
                .Take(GetAvailableAnytimeForRun())
                .Any(candidate => candidate.Index == receipt.Index);
        }

        private static IEnumerable<IndexedItemInfo> GetRelicReceipts(Player player)
        {
            var characterOffset = player.GetAPCharacterNumber();
            return ArchipelagoClient.Progress.AllReceivedItems
                .Where(receipt =>
                    receipt.Item.GetAPCharacterNumber() == characterOffset
                    && receipt.Item.GetCharacterItemType() == APItem.Relic
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
