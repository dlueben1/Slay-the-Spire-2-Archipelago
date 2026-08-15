
using Archipelago.MultiClient.Net.Models;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System.Collections.Concurrent;
using StS2AP.Extensions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Patches 
{

    ///<summary>
    /// This class facilitates Item Processing.
    /// In general, item processing should happen on the main godot thread, in order to avoid
    /// race conditions.  As such, any reading or writing to the ArchipelagoProgress global
    /// should happen on the main thread.
    /// </summary>
    public static class Patches_ItemProcessor {

        private static ConcurrentQueue<IndexedItemInfo> ProcessQueue { get; } = new();
        public static int LastIndexHandled { get; set; }
        // Sufficient assuming we're single threaded
        private static bool Paused { get; set; } = false;

        public static event Action<CharacterConfig>? CharacterUnlocked;
        
        /// <summary>
        /// Adds an item to the processing queue.  
        /// </summary>
        /// <param name="info"> The item to process</param>
        public static void AddToQueue(IndexedItemInfo info)
        {
            LogUtility.Info($"Enqueuing {info.Item.ItemName}");
            ProcessQueue.Enqueue(info);
            if(!RunManager.Instance.IsInProgress) 
            {
                LogUtility.Info($"Processing {info.Item.ItemName}, since not in game");
                // We're not in a run, so the NRun._Process function won't fire.
                // As such, send a task to the main thread to do stuff
                Callable.From(ProcessItemInQueue).CallDeferred();
            }
        }

        private static void ProcessItemInQueue()
        {
            // Don't want to block, but also don't want to be processing items while a reprocess is going on
            // When the run is finally started, this will get fired on every "tick", for some definition of tick
            if(Paused)
            {
                return;
            }

            while (ProcessQueue.TryDequeue(out var info))
            {
                // If we've already processed this, don't do it again.
                if(info.Index <= LastIndexHandled)
                {
                    return;
                }
                ProcessItem(info);
                LastIndexHandled = info.Index;
            }
        }

        public static void ClearQueue()
        {
            ProcessQueue.Clear();
            LastIndexHandled = 0;
        }
        
        /// <summary>
        /// Hooking into the _Process of NRun.  Unfortunately, I can't find a good class
        /// that is always available that also has a _Process function overridden, and 
        /// overriding Node._Process seems foolhardy.  So this will only fire in the middle
        /// of a run.
        /// </summary>
        [HarmonyPatch(typeof(NRun), nameof(NRun._Process))]
        public static class _ProcessPatch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                ProcessItemInQueue();
            }
        }

        /// <summary>
        /// Determines what to do with an Item that we've received from Archipelago.
        /// This function is controlled by a Spinlock, and can only process one item at a time.
        /// </summary>
        /// <param name="item">Received Item</param>
        /// <param name="index">The index of the item in the Archipelago Multiworld</param>
        private static void ProcessItem(IndexedItemInfo indexedInfo, bool liveDelivery = true)
        {
            var Progress = ArchipelagoClient.Progress;
            var Settings = ArchipelagoClient.Settings;
            var item = indexedInfo.Item;
            var index = indexedInfo.Index;
            // Log the item
            LogUtility.Success(
                $"Received: {item.ItemName} from {item.Player.Name} (ID: {item.ItemId} / LocID: {item.LocationId} / Index: {index})"
            );

            /// Universal items (IDs < 10000) are character-agnostic and handled separately.
            /// The 10k ID gap ensures universal IDs never collide with character-specific IDs,
            /// no matter how many characters we add in the future.
            if (item.ItemId < 10000)
            {
                HandleUniversalItem(item, index);
                return;
            }

            // Character-specific items (IDs >=ExitReadLock 10000): strip the character offset to get the base item type.
            switch (item.GetCharacterSpecificItemID())
            {
                // Character Unlocks
                case APItem.Unlock:
                {
                    LogUtility.Info("Before GameUtility Unlock");
                    GameUtility.UnlockCharacter(item);
                    LogUtility.Info("After GameUtility Unlock");

                    /// Fire the CharacterUnlocked event on the Godot main thread.
                    /// This allows the character select screen (if open) to immediately
                    /// refresh the appropriate button without waiting for OnSubmenuOpened.
                    var offset = item.GetCharacterOffset();
                    LogUtility.Info("After offset acquisition");
                    var config = ArchipelagoClient.Settings.Characters.Values.FirstOrDefault(
                        config => config.CharOffset == offset
                    );
                    LogUtility.Info("After Settings check");
                    if (config == null)
                    {
                        LogUtility.Warn($"Got Unlock for character not configured {item.ItemId}");
                        break;
                    }
                    LogUtility.Info("after config null check");
                    CharacterUnlocked?.Invoke(config);

                    break;
                }
                // Progressive threshold items
                case APItem.ProgressiveSmith:
                    HandleThreshholdItem(item, Progress.ProgressiveSmiths, "Progressive Smiths");
                    break;
                case APItem.ProgressiveRest:
                    HandleThreshholdItem(item, Progress.ProgressiveRests, "Progressive Rests");
                    break;
                case APItem.ProgressiveAncient:
                {
                    HandleThreshholdItem(item, Progress.ProgressiveAncients, "Progressive Ancients");

                    if (Settings.AncientRelicLocation == AncientRelicLocation.Anytime)
                    {
                        // NeowSanity's first progressive unlock still controls the normal Act 1 Neow reward.
                        // Every Act 2/3 unlock becomes a per-run, linked Ancient choice in the AP reward menu.
                        var characterOffset = item.GetCharacterOffset();
                        Progress.ProgressiveAncients.TryGetValue(characterOffset, out var unlockCount);
                        if (!Settings.NeowSanity || unlockCount > 1)
                            Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));
                    }

                    break;
                }
                case APItem.ProgressiveStarterCard:
                    HandleThreshholdItem(item, Progress.ProgressiveStarterCards, "Progressive Starter Cards");
                    ProgressiveStarterUtility.QueueReconcileCurrentPlayer();
                    break;
                case APItem.ProgressiveStarterRelic:
                    HandleThreshholdItem(item, Progress.ProgressiveStarterRelics, "Progressive Starter Relics");
                    ProgressiveStarterUtility.QueueReconcileCurrentPlayer();
                    break;
                case APItem.Relic:
                {
                    // Save loading replays the whole item list, then reconciles once at the end.
                    if (!liveDelivery)
                    {
                        Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));
                        return;
                    }

                    // Keep every receipt. Other characters and out-of-run deliveries may
                    // matter when their run starts or a checkpoint is loaded.
                    Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));

                    var player = GameUtility.CurrentPlayer;
                    var characterOffset = player?.Character.GetCharacterOffset();
                    if (player == null
                        || !characterOffset.HasValue
                        || item.GetCharacterOffset() != characterOffset.Value)
                    {
                        return;
                    }

                    // A receipt arriving after its Elite/chest reward belongs in the AP menu.
                    // Reconcile all pairs so checkpoint loads do not depend on callback order.
                    RelicRewardUtility.ReconcileBankedRewards(player);
                    if (ArchipelagoRewardUI.IsOpen)
                        ArchipelagoRewardUI.ShowRewards();
                    return;
                }
                // Gold is condensed into a single reward pool
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem.CombatGold:
                case APItem.EliteGold:
                case APItem.BossGold:
                {
                    // Get the IDs for storing the item
                    var charOffset = item.GetCharacterOffset();
                    var itemId = item.GetCharacterSpecificItemID();

                    // Add the Gold to the amount we've received
                    try
                    {
                        var haveKey = Progress.GoldReceived.TryGetValue(charOffset, out int gold);
                        if (!haveKey)
                            gold = 0;
                        Progress.GoldReceived[charOffset] =
                            gold + ItemTable.GoldItemAmounts[itemId];
                    }
                    catch (KeyNotFoundException e)
                    {
                        LogUtility.Error(
                            $"GoldItemAmounts does not have a value for this item! ({item.ItemDisplayName} from {item.Player.Name})"
                        );
                    }
                    catch
                    {
                        LogUtility.Error(
                            $"Failed to process Gold when this item was received: ({item.ItemDisplayName} from {item.Player.Name})"
                        );
                    }

                    break;
                }
                // Shop slot unlocks (cards/neutral/relic/potion) and Progressive Shop Remove.
                case APItem.ShopCardSlot:
                case APItem.NeutralShopCardSlot:
                case APItem.ShopRelicSlot:
                case APItem.ShopPotionSlot:
                case APItem.ProgressiveShopRemove:
                    {
                        // Get the IDs for storing the item
                        var itemId = item.GetCharacterSpecificItemID();
                        var playerId = item.GetCharacterOffset();

                        // Route to the matching per-category tracker
                        var source = itemId switch
                        {
                            APItem.ShopCardSlot => Progress.ShopCardSlotsReceived,
                            APItem.NeutralShopCardSlot => Progress.ShopNeutralSlotsReceived,
                            APItem.ShopRelicSlot => Progress.ShopRelicSlotsReceived,
                            APItem.ShopPotionSlot => Progress.ShopPotionSlotsReceived,
                            _ => Progress.ShopRemovesReceived,
                        };

                        // Increment the reward
                        try
                        {
                            var haveKey = source.TryGetValue(playerId, out int amount);
                            if (!haveKey) amount = 0;
                            source[playerId] = amount + 1;
                            LogUtility.Success($"New Value for {itemId} is {source[playerId]}");
                        }
                        catch (KeyNotFoundException e)
                        {
                            LogUtility.Error($"Shop slot tracker does not have a value for this character! ({item.ItemDisplayName} from {item.Player.Name})");
                        }
                        catch
                        {
                            LogUtility.Error($"Failed to process Shop Slot item when this item was received: ({item.ItemDisplayName} from {item.Player.Name})");
                        }

                        break;
                    }
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
                    Progress.Ascensions.ProcessAscensionLevel(
                        GameUtility.CurrentConfig,
                        item,
                        false
                    );
                    Progress.UsedItems.Add(index);
                    Progress.AllReceivedItems.Add(indexedInfo);
                    break;

                // Everything else ends up in the "reward pool"
                default:
                {
                        Progress.AllReceivedItems.Add(indexedInfo);
                    break;
                }
            }
        }

        /// <summary>
        /// Handles universal items that do not have a character offset baked in.
        ///
        /// Universal items have no character offset, so their ItemId is cast directly to APItem
        /// without any modulo operation. Currently all universal items are combat buffs applied
        /// via <see cref="BuffUtility"/> at the start of the player's next turn.
        /// </summary>
        private static void HandleUniversalItem(ItemInfo item, int index)
        {
            // Cast ItemId directly � no modulo needed since universal items have no character offset.
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
                    BuffUtility.EnqueueBuff(universalId, index);
                    break;
                default:
                    LogUtility.Warn(
                        $"[ArchipelagoClient] Received unrecognized universal item ID {item.ItemId} ({item.ItemName}) � not handled."
                    );
                    break;
            }
        }

        /// <summary>
        /// Helper for handling common threshold containers
        /// </summary>
        private static void HandleThreshholdItem(
            ItemInfo item,
            Dictionary<long, int> source,
            string name
        )
        {
            // Get the IDs for storing the item
            var itemId = item.GetCharacterSpecificItemID();
            var offset = item.GetCharacterOffset();

            // Increment the reward
            try
            {
                var haveKey = source.TryGetValue(offset, out int amount);
                if (!haveKey)
                    amount = 0;
                source[offset] = amount + 1;
                LogUtility.Success($"New Value for {name} is {source[offset]}");
            }
            catch (KeyNotFoundException e)
            {
                LogUtility.Error(
                    $"{name} does not have a value for this character! ({item.ItemDisplayName} from {item.Player.Name})"
                );
            }
            catch
            {
                LogUtility.Error(
                    $"Failed to process {name} when this item was received: ({item.ItemDisplayName} from {item.Player.Name})"
                );
            }
        }

        public static void ReprocessItems()
        {
            Paused = true;
            try
            {
                ClearQueue();
                for (
                    global::System.Int32 i = 0;
                    i < ArchipelagoClient.Session.Items.AllItemsReceived.Count;
                    i++
                )
                {
                    ItemInfo info = ArchipelagoClient.Session.Items.AllItemsReceived[i];

                    // i+1 because the index from multiclient .net is essentially 1 based, not 0
                    ProcessItem(new IndexedItemInfo(info, i + 1), false);
                    LastIndexHandled = i + 1;
                }
            }
            finally
            {
                Paused = false;
            }
        }

    }
}