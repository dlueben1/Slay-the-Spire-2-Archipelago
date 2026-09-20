using System.Collections.Concurrent;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Services;

/// <summary>
/// Coordinates items received from the Archipelago multiworld and applies their effects to the
/// local client's progress.
///
/// Network callbacks may enqueue receipts from a background thread, but processing must happen on
/// Godot's main thread because it reads and mutates <see cref="ArchipelagoProgress"/> and game
/// state. Outside a run, processing is deferred directly to the main thread. During a run, the
/// receipt queue is drained by the Harmony bridge in <c>Patches_ProcessReceivedItems</c>.
/// </summary>
internal static class ArchipelagoItemService
{
    #region State and Events

    /// <summary>
    /// Queue of received items to process
    /// </summary>
    private static ConcurrentQueue<IndexedItemInfo> ProcessQueue { get; } = new();

    /// <summary>
    /// Gets or sets the highest sequential Archipelago receipt index processed by this client.
    /// </summary>
    private static int LastProcessedIndex { get; set; }

    /// <summary>
    /// Prevents normal queue draining while the complete server receipt history is being replayed.
    /// Queue writes remain available so receipts arriving during replay are retained for later.
    /// </summary>
    private static bool IsReplayInProgress { get; set; }

    /// <summary>
    /// Raised on Godot's main thread after a character-unlock receipt has updated local progress.
    /// Subscribers can refresh character-dependent UI immediately.
    /// </summary>
    internal static event Action<CharacterConfig>? CharacterUnlocked;

    #endregion

    #region Queue Lifecycle

    /// <summary>
    /// Adds an Archipelago receipt to the thread-safe processing queue.
    /// </summary>
    /// <param name="receipt">The received item and its stable, server-assigned receipt index.</param>
    internal static void EnqueueReceivedItem(IndexedItemInfo receipt)
    {
        LogUtility.Info($"Enqueuing {receipt.Item.ItemName}");
        ProcessQueue.Enqueue(receipt);

        // NRun._Process does not execute outside of a run, so if we're not in one, we need to drain the queue explicitly
        if (!RunManager.Instance.IsInProgress)
        {
            LogUtility.Info($"Processing {receipt.Item.ItemName}, since not in game");

            Callable.From(ProcessQueuedItems).CallDeferred();
        }
    }

    /// <summary>
    /// Drains queued receipts on Godot's main thread in the order supplied by Archipelago.
    /// Processing pauses while receipt history is being replayed during save loading.
    /// </summary>
    internal static void ProcessQueuedItems()
    {
        if (IsReplayInProgress)
            return;

        while (ProcessQueue.TryDequeue(out IndexedItemInfo? receipt))
        {
            // Preserve the existing high-water behavior: encountering an old receipt ends this
            // drain, and any remaining receipts wait for the next scheduled drain or process tick.
            if (receipt.Index <= LastProcessedIndex)
                return;

            ProcessReceivedItem(receipt);
            LastProcessedIndex = receipt.Index;
        }
    }

    /// <summary>
    /// Clears pending receipts and resets the processed-index high-water mark for a new AP slot.
    /// </summary>
    internal static void ResetQueue()
    {
        ProcessQueue.Clear();
        LastProcessedIndex = 0;
    }

    /// <summary>
    /// Rebuilds receipt-derived progress by replaying the session's complete item history.
    /// </summary>
    /// <remarks>
    /// Archipelago exposes the history as a zero-based list, while its receipt indexes are
    /// one-based. Replay deliberately suppresses live-only relic reconciliation; the save-loading
    /// flow reconciles relic rewards once after the full history has been restored.
    /// </remarks>
    internal static void ReplayReceivedItems()
    {
        IsReplayInProgress = true;
        try
        {
            ResetQueue();
            for (int i = 0; i < ArchipelagoClient.Session.Items.AllItemsReceived.Count; i++)
            {
                ItemInfo item = ArchipelagoClient.Session.Items.AllItemsReceived[i];

                // MultiClient.Net receipt indexes are one-based even though this list is zero-based.
                ProcessReceivedItem(new IndexedItemInfo(item, i + 1), isLiveDelivery: false);
                LastProcessedIndex = i + 1;
            }
        }
        finally
        {
            IsReplayInProgress = false;
        }
    }

    #endregion

    #region Receipt Routing

    /// <summary>
    /// Routes one received item to its universal or character-specific behavior.
    /// </summary>
    /// <param name="receipt">The item and unique receipt index to process.</param>
    /// <param name="isLiveDelivery">
    /// <see langword="true"/> for a newly delivered receipt; <see langword="false"/> when rebuilding
    /// state from receipt history during save loading.
    /// </param>
    private static void ProcessReceivedItem(IndexedItemInfo receipt, bool isLiveDelivery = true)
    {
        ItemInfo item = receipt.Item;
        LogUtility.Success(
            $"Received: {item.ItemName} from {item.Player.Name} "
                + $"(ID: {item.ItemId} / LocID: {item.LocationId} / Index: {receipt.Index})"
        );

        /// Universal IDs occupy the reserved range below 10,000 and do not include a character
        /// offset. The gap prevents collisions as new character-specific ranges are introduced.
        if (item.ItemId < 10000)
        {
            ProcessUniversalItem(receipt);
            return;
        }

        ProcessCharacterSpecificItem(receipt, isLiveDelivery);
    }

    /// <summary>
    /// Routes a receipt whose item ID contains a character offset to its concrete handler.
    /// Keeping the complete mapping in one switch makes supported item behavior easy to audit.
    /// </summary>
    private static void ProcessCharacterSpecificItem(IndexedItemInfo receipt, bool isLiveDelivery)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        ItemInfo item = receipt.Item;

        switch (item.GetCharacterSpecificItemID())
        {
            case APItem.Unlock:
                ProcessCharacterUnlock(item);
                break;

            case APItem.ProgressiveSmith:
                IncrementThreshold(item, progress.ProgressiveSmiths, "Progressive Smiths");
                break;

            case APItem.ProgressiveRest:
                IncrementThreshold(item, progress.ProgressiveRests, "Progressive Rests");
                break;

            case APItem.ProgressiveAncient:
                IncrementThreshold(item, progress.ProgressiveAncients, "Progressive Ancients");
                if (ArchipelagoClient.Settings.AncientRelicLocation == AncientRelicLocation.Anytime)
                    progress.AllReceivedItems.Add(new IndexedItemInfo(item, receipt.Index));
                break;

            case APItem.ProgressiveStarterCard:
                IncrementThreshold(
                    item,
                    progress.ProgressiveStarterCards,
                    "Progressive Starter Cards"
                );
                ProgressiveStarterUtility.QueueReconcileCurrentPlayer();
                break;

            case APItem.ProgressiveStarterRelic:
                IncrementThreshold(
                    item,
                    progress.ProgressiveStarterRelics,
                    "Progressive Starter Relics"
                );
                ProgressiveStarterUtility.QueueReconcileCurrentPlayer();
                break;

            case APItem.Relic:
                ProcessRelic(receipt, isLiveDelivery);
                break;

            case APItem.OneGold:
            case APItem.FiveGold:
            case APItem.CombatGold:
            case APItem.EliteGold:
            case APItem.BossGold:
                ProcessGold(item);
                break;

            case APItem.ShopCardSlot:
            case APItem.NeutralShopCardSlot:
            case APItem.ShopRelicSlot:
            case APItem.ShopPotionSlot:
            case APItem.ProgressiveShopRemove:
                ProcessShopUpgrade(item);
                break;

            case APItem.SwarmingElites:
            case APItem.WearyTraveler:
            case APItem.Poverty:
            case APItem.TightBelt:
            case APItem.AscenderBane:
            case APItem.Inflation:
            case APItem.Scarcity:
            case APItem.ToughEnemies:
            case APItem.DeadlyEnemies:
            case APItem.DoubleBoss:
                ProcessAscensionModifier(receipt);
                break;

            default:
                StoreRewardPoolItem(receipt);
                break;
        }
    }

    #endregion

    #region Character-Specific Item Handlers

    /// <summary>
    /// Unlocks the configured character and notifies any open character-selection UI.
    /// </summary>
    private static void ProcessCharacterUnlock(ItemInfo item)
    {
        LogUtility.Info("Before GameUtility Unlock");
        GameUtility.UnlockCharacter(item);
        LogUtility.Info("After GameUtility Unlock");

        // Resolve the AP character offset back to its configured character before publishing the
        // event. Processing already occurs on Godot's main thread, so UI subscribers may react now.
        long offset = item.GetCharacterOffset();
        LogUtility.Info("After offset acquisition");
        CharacterConfig? config = ArchipelagoClient.Settings.Characters.Values.FirstOrDefault(
            character => character.CharOffset == offset
        );
        LogUtility.Info("After Settings check");

        if (config == null)
        {
            LogUtility.Warn($"Got Unlock for character not configured {item.ItemId}");
            return;
        }

        LogUtility.Info("after config null check");
        CharacterUnlocked?.Invoke(config);
    }

    /// <summary>
    /// Stores a relic receipt and performs live-only reconciliation when it belongs to the current
    /// player character.
    /// </summary>
    private static void ProcessRelic(IndexedItemInfo receipt, bool isLiveDelivery)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        ItemInfo item = receipt.Item;

        // Save loading replays the entire list and reconciles once after replay is complete.
        if (!isLiveDelivery)
        {
            progress.AllReceivedItems.Add(new IndexedItemInfo(item, receipt.Index));
            return;
        }

        // Retain every receipt because it may matter for another character or a later run.
        progress.AllReceivedItems.Add(new IndexedItemInfo(item, receipt.Index));

        var player = GameUtility.CurrentPlayer;
        long? characterOffset = player?.Character.GetCharacterOffset();
        if (
            player == null
            || !characterOffset.HasValue
            || item.GetCharacterOffset() != characterOffset.Value
        )
        {
            return;
        }

        RelicCoupons.RefreshCounter(player);

        // Late receipts belong in the AP menu. Reconcile all pairs so checkpoint loads do not
        // depend on callback order.
        RelicRewardUtility.ReconcileBankedRewards(player);
    }

    /// <summary>
    /// Adds a gold receipt's configured value to the receiving character's aggregate gold pool.
    /// </summary>
    private static void ProcessGold(ItemInfo item)
    {
        long characterOffset = item.GetCharacterOffset();
        APItem itemId = item.GetCharacterSpecificItemID();

        try
        {
            bool hasExistingValue = ArchipelagoClient.Progress.GoldReceived.TryGetValue(
                characterOffset,
                out int gold
            );
            if (!hasExistingValue)
                gold = 0;

            ArchipelagoClient.Progress.GoldReceived[characterOffset] =
                gold + ItemTable.GoldItemAmounts[itemId];
        }
        catch (KeyNotFoundException)
        {
            LogUtility.Error(
                $"GoldItemAmounts does not have a value for this item! "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
        catch
        {
            LogUtility.Error(
                $"Failed to process Gold when this item was received: "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
    }

    /// <summary>
    /// Increments the per-character tracker for a shop slot or progressive shop removal receipt.
    /// </summary>
    private static void ProcessShopUpgrade(ItemInfo item)
    {
        APItem itemId = item.GetCharacterSpecificItemID();
        long characterOffset = item.GetCharacterOffset();

        Dictionary<long, int> source = itemId switch
        {
            APItem.ShopCardSlot => ArchipelagoClient.Progress.ShopCardSlotsReceived,
            APItem.NeutralShopCardSlot => ArchipelagoClient.Progress.ShopNeutralSlotsReceived,
            APItem.ShopRelicSlot => ArchipelagoClient.Progress.ShopRelicSlotsReceived,
            APItem.ShopPotionSlot => ArchipelagoClient.Progress.ShopPotionSlotsReceived,
            _ => ArchipelagoClient.Progress.ShopRemovesReceived,
        };

        try
        {
            bool hasExistingValue = source.TryGetValue(characterOffset, out int amount);
            if (!hasExistingValue)
                amount = 0;

            source[characterOffset] = amount + 1;
            LogUtility.Success($"New Value for {itemId} is {source[characterOffset]}");
        }
        catch (KeyNotFoundException)
        {
            LogUtility.Error(
                $"Shop slot tracker does not have a value for this character! "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
        catch
        {
            LogUtility.Error(
                $"Failed to process Shop Slot item when this item was received: "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
    }

    /// <summary>
    /// Applies an ascension-modifier receipt, marks it consumed, and retains it in receipt history.
    /// </summary>
    private static void ProcessAscensionModifier(IndexedItemInfo receipt)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        progress.Ascensions.ProcessAscensionLevel(GameUtility.CurrentConfig, receipt.Item, false);
        progress.UsedItems.Add(receipt.Index);
        progress.AllReceivedItems.Add(receipt);
    }

    /// <summary>
    /// Stores an item whose concrete reward will be resolved later by the AP reward menu.
    /// </summary>
    private static void StoreRewardPoolItem(IndexedItemInfo receipt)
    {
        ArchipelagoClient.Progress.AllReceivedItems.Add(receipt);
    }

    #endregion

    #region Universal Item Handlers

    /// <summary>
    /// Routes a character-agnostic receipt whose item ID can be cast directly to <see cref="APItem"/>.
    /// Buffs are consumed at the next player turn; bonus items become persistent menu rewards.
    /// </summary>
    private static void ProcessUniversalItem(IndexedItemInfo receipt)
    {
        ItemInfo item = receipt.Item;
        var universalId = (APItem)item.ItemId;

        switch (universalId)
        {
            case APItem.FreeAttack:
            case APItem.FreePower:
            case APItem.FreeSkill:
            case APItem.Dexterity:
            case APItem.Strength:
            case APItem.Plating:
            case APItem.Friendship:
            case APItem.Thorns:
            case APItem.Buffer:
            case APItem.Vigor:
            case APItem.Artifact:
            case APItem.PostCombatCardUpgrade:
            case APItem.PostCombatCardRemoval:
            case APItem.AdditionalCardReward:
                BuffUtility.EnqueueBuff(universalId, receipt.Index);
                break;

            case APItem.BonusWaxRelic:
                ProcessBonusItem(receipt);
                break;

            default:
                LogUtility.Warn(
                    $"[ArchipelagoClient] Received unrecognized universal item ID "
                        + $"{item.ItemId} ({item.ItemName}) - not handled."
                );
                break;
        }
    }

    /// <summary>
    /// Records a character-agnostic bonus receipt so it can be claimed from the AP reward menu.
    /// The Nth receipt unlocks the Nth configured entry; excess copies are intentionally discarded.
    /// </summary>
    private static void ProcessBonusItem(IndexedItemInfo receipt)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        if (!progress.TryGetBonusOrdinal(receipt, out string category, out int ordinal))
        {
            LogUtility.Warn(
                $"Received bonus item {receipt.Item.ItemName} with no known category; ignoring"
            );
            return;
        }

        int configuredCount = ArchipelagoClient.Settings?.BonusItemsFor(category).Count ?? 0;
        if (ordinal >= configuredCount)
        {
            LogUtility.Warn(
                $"Ignoring extra {receipt.Item.ItemName}: this is copy {ordinal + 1} but only "
                    + $"{configuredCount} {category} bonus item(s) are configured for this slot"
            );
            return;
        }

        progress.AllReceivedItems.Add(receipt);
        LogUtility.Success($"Unlocked {category} bonus item {ordinal + 1} of {configuredCount}");
    }

    #endregion

    #region Shared Counter Helpers

    /// <summary>
    /// Increments a progressive threshold tracker for the character encoded in the receipt ID.
    /// </summary>
    private static void IncrementThreshold(ItemInfo item, Dictionary<long, int> source, string name)
    {
        long characterOffset = item.GetCharacterOffset();

        try
        {
            bool hasExistingValue = source.TryGetValue(characterOffset, out int amount);
            if (!hasExistingValue)
                amount = 0;

            source[characterOffset] = amount + 1;
            LogUtility.Success($"New Value for {name} is {source[characterOffset]}");
        }
        catch (KeyNotFoundException)
        {
            LogUtility.Error(
                $"{name} does not have a value for this character! "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
        catch
        {
            LogUtility.Error(
                $"Failed to process {name} when this item was received: "
                    + $"({item.ItemDisplayName} from {item.Player.Name})"
            );
        }
    }

    #endregion
}
