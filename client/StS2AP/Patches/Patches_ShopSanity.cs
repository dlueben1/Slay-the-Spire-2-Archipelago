using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Models;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using static StS2AP.Data.CharTable;

namespace StS2AP.Patches
{
    public static class Patches_ShopSanity
    {
        /// <summary>Total card slots per shop visit</summary>
        private const int CardSlotMax = 5;

        /// <summary>Total colorless/neutral card slots per shop visit</summary>
        private const int NeutralSlotMax = 2;

        /// <summary>Total relic slots per shop visit</summary>
        private const int RelicSlotMax = 3;

        /// <summary>Total potion slots per shop visit</summary>
        private const int PotionSlotMax = 3;

        private static int SlotCeilingForAct(int act) => act switch
        {
            1 => 5,
            2 => 10,
            _ => 16,
        };

        /// <summary>Reflected accessors</summary>
        private static readonly PropertyInfo? CardCreationResultProp =
            AccessTools.Property(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CreationResult));

        private static readonly PropertyInfo? RelicModelProp =
            AccessTools.Property(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.Model));

        private static readonly PropertyInfo? PotionModelProp =
            AccessTools.Property(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.Model));

        private static readonly FieldInfo? CostField =
            AccessTools.Field(typeof(MerchantEntry), "_cost");

        private static readonly FieldInfo? PlayerField =
            AccessTools.Field(typeof(MerchantEntry), "_player");

        private static readonly FieldInfo? CharacterCardEntriesField =
            AccessTools.Field(typeof(MerchantInventory), "_characterCardEntries");

        private static readonly FieldInfo? ColorlessCardEntriesField =
            AccessTools.Field(typeof(MerchantInventory), "_colorlessCardEntries");

        private static readonly FieldInfo? RelicEntriesField =
            AccessTools.Field(typeof(MerchantInventory), "_relicEntries");

        private static readonly FieldInfo? PotionEntriesField =
            AccessTools.Field(typeof(MerchantInventory), "_potionEntries");

        private static readonly PropertyInfo? CardRemovalEntryProp =
            AccessTools.Property(typeof(MerchantInventory), nameof(MerchantInventory.CardRemovalEntry));

        private static readonly MethodInfo? MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        private static readonly FieldInfo? PurchaseCompletedField =
            AccessTools.Field(typeof(MerchantEntry), nameof(MerchantEntry.PurchaseCompleted));

        private static readonly FieldInfo? PurchaseFailedField =
            AccessTools.Field(typeof(MerchantEntry), nameof(MerchantEntry.PurchaseFailed));

        private static readonly FieldInfo? EntryUpdatedField =
            AccessTools.Field(typeof(MerchantEntry), nameof(MerchantEntry.EntryUpdated));

        private static readonly ConditionalWeakTable<MerchantInventory, MerchantInventory> ApInventories = new();

        #region Unclaimed-Location Queue
        /// <summary>
        /// Per-visit queue of not-yet-checked "Shop Slot N" location IDs for one
        /// player/act, shared across all four merchant categories as they populate.
        /// </summary>
        private sealed class ShopVisitContext
        {
            private readonly Queue<long> _missing = new();
            public ShopVisitContext(Player player, int act)
            {
                int ceiling = Math.Min(
                    SlotCeilingForAct(act),
                    ArchipelagoClient.Settings.TotalShopLocations);
                for (int slot = 1; slot <= ceiling; slot++)
                {
                    string checkName = $"{player.APName()} Shop Slot {slot}";

                    // ShopSlotsChecked covers checks made this session (written
                    // immediately on purchase) ArchipelagoClient.CheckedLocations is a
                    // snapshot taken at connect time (Session.Locations.AllLocationsChecked)
                    bool isChecked = false;
                    ArchipelagoClient.Progress.ShopSlotsChecked?.TryGetValue(checkName, out isChecked);

                    try
                    {
                        long locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);
                        if (isChecked || ArchipelagoClient.CheckedLocations.Contains(locationId))
                        {
                            continue;
                        }
                        _missing.Enqueue(locationId);
                    }
                    catch
                    {
                        LogUtility.Error($"ShopSanity: failed to resolve location id for {checkName}, skipped.");
                    }
                }
            }

            /// <summary>True while there are still unclaimed "Shop Slot N" locations left in this queue</summary>
            public bool HasMore => _missing.Count > 0;

            /// <summary>Pops the next unclaimed location ID off the queue</summary>
            public long GetNext() => _missing.Dequeue();
        }

        #endregion

        /// <summary>
        /// Looks up the item name, sending player, and classification for an
        /// archipelago location
        /// </summary>
        private static (string itemName, string playerName, ApItemClassification classification) ResolveApItem(long locationId)
        {
            string checkName = ArchipelagoClient.Session.Locations.GetLocationNameFromId(locationId);

            ScoutedItemInfo info;
            if (ArchipelagoClient.ScoutedLocations.TryGetValue(locationId, out info))
            {
                var classification =
                    info.Trap() ? ApItemClassification.Trap :
                    info.Advancement() ? ApItemClassification.Progression :
                    info.Useful() ? ApItemClassification.Useful :
                    ApItemClassification.Filler;
                return (info.ItemName, info.Player.Alias, classification);
            }

            LogUtility.Warn($"ShopSanity: no scouted info for location {locationId} ({checkName}), showing as generic Filler.");
            return (checkName, "???", ApItemClassification.Filler);
        }

        /// <summary>Records a shop slot's location as checked this session</summary>
        private static void MarkShopSlotChecked(long locationId)
        {
            string checkName = ArchipelagoClient.Session.Locations.GetLocationNameFromId(locationId);
            ArchipelagoClient.Progress.ShopSlotsChecked[checkName] = true;
        }

        /// <summary>How many of a category's slots are currently unlocked for real shop population</summary>
        private static int AvailableSlots(int categoryMax, int shuffledCount, int received)
            => Math.Min(categoryMax - Math.Min(shuffledCount, categoryMax) + received, categoryMax);

        /// <summary>Looks up a per-character received count, defaulting to 0 when the character has no entry yet</summary>
        private static int GetReceived(Dictionary<long, int> source, long id)
            => source.TryGetValue(id, out int v) ? v : 0;

        /// <summary>
        /// Reads whatever CalcCost() just computed (the vanilla-style rarity-tiered
        /// baseline) and reduces it per the ShopSanityCosts option. Values match
        /// options.py exactly: 0=Fixed(15g), 1=Super_Discount_Tiered(20%),
        /// 2=Discount_Tiered(50%), 3=Tiered(full baseline, no discount).
        /// </summary>
        private static void ApplyCostTier(MerchantEntry entry)
        {
            if (CostField == null)
            {
                return;
            }

            int baseline = (int)CostField.GetValue(entry)!;
            int final = ArchipelagoClient.Settings.ShopSanityCosts switch
            {
                0 => 15,
                1 => Math.Max(1, (int)Math.Round(baseline * 0.20f)),
                2 => Math.Max(1, (int)Math.Round(baseline * 0.50f)),
                _ => baseline,
            };
            CostField.SetValue(entry, final);
        }

        #region Slot Population

        private readonly record struct ApSlotCounts(int Cards, int Neutral, int Relics, int Potions);

        /// <summary>
        /// The configured category counts reserve AP-page positions. Card-removal sanity adds
        /// three generic checks, so let those borrow otherwise-unused positions in display order.
        /// </summary>
        private static ApSlotCounts GetApSlotCounts()
        {
            int cards = Math.Clamp(ArchipelagoClient.Settings.ShopCardSlots, 0, CardSlotMax);
            int neutral = Math.Clamp(ArchipelagoClient.Settings.ShopNeutralSlots, 0, NeutralSlotMax);
            int relics = Math.Clamp(ArchipelagoClient.Settings.ShopRelicSlots, 0, RelicSlotMax);
            int potions = Math.Clamp(ArchipelagoClient.Settings.ShopPotionSlots, 0, PotionSlotMax);
            int overflow = ArchipelagoClient.Settings.ShopRemoveSlots ? ArchipelagoProgress._maxShopRemoves : 0;
            
            // ShopRemoveSlots don't have a dedicated page on the AP page so it 'overflows'
            // in the priority of cards, colourless cards, relics, then potions.
            // which is where you then claim it and send the check
            AddOverflow(ref cards, CardSlotMax, ref overflow);
            AddOverflow(ref neutral, NeutralSlotMax, ref overflow);
            AddOverflow(ref relics, RelicSlotMax, ref overflow);
            AddOverflow(ref potions, PotionSlotMax, ref overflow);

            return new ApSlotCounts(cards, neutral, relics, potions);
        }

        private static void AddOverflow(ref int count, int maximum, ref int overflow)
        {
            int added = Math.Min(maximum - count, overflow);
            count += added;
            overflow -= added;
        }

        internal static bool TryGetApInventory(MerchantInventory vanillaInventory, out MerchantInventory apInventory)
            => ApInventories.TryGetValue(vanillaInventory, out apInventory!);

        private static MerchantInventory CreateApInventory(
            Player player,
            MerchantInventory vanillaInventory,
            ShopVisitContext ctx,
            ApSlotCounts counts)
        {
            EnsureInventoryReflectionAvailable();

            var apInventory = new MerchantInventory(player);
            PopulateCardCategory(
                vanillaInventory.CharacterCardEntries,
                GetMutableEntries<MerchantCardEntry>(apInventory, CharacterCardEntriesField),
                counts.Cards,
                ctx);
            PopulateCardCategory(
                vanillaInventory.ColorlessCardEntries,
                GetMutableEntries<MerchantCardEntry>(apInventory, ColorlessCardEntriesField),
                counts.Neutral,
                ctx);
            PopulateRelicCategory(
                vanillaInventory.RelicEntries,
                GetMutableEntries<MerchantRelicEntry>(apInventory, RelicEntriesField),
                counts.Relics,
                ctx);
            PopulatePotionCategory(
                vanillaInventory.PotionEntries,
                GetMutableEntries<MerchantPotionEntry>(apInventory, PotionEntriesField),
                counts.Potions,
                ctx);

            // Initialize the cloned scene's removal node, then keep it permanently empty/hidden.
            MerchantCardRemovalEntry sourceRemovalEntry = vanillaInventory.CardRemovalEntry
                ?? throw new InvalidOperationException("Normal merchant inventory had no card-removal entry.");
            MerchantCardRemovalEntry removalEntry = CloneEntry(sourceRemovalEntry);
            removalEntry.SetUsed();
            CardRemovalEntryProp!.SetValue(apInventory, removalEntry);

            return apInventory;
        }

        private static List<T> GetMutableEntries<T>(MerchantInventory inventory, FieldInfo? field)
            where T : MerchantEntry
            => field?.GetValue(inventory) as List<T>
               ?? throw new MissingFieldException(typeof(MerchantInventory).FullName, field?.Name ?? typeof(T).Name);

        /// <summary>
        /// Clone the already-rolled entry so the AP page gets independent state without rolling
        /// another shop and advancing the player's shop RNG.
        /// </summary>
        private static T CloneEntry<T>(T source) where T : MerchantEntry
        {
            var clone = (T)MemberwiseCloneMethod!.Invoke(source, null)!;
            PurchaseCompletedField!.SetValue(clone, null);
            PurchaseFailedField!.SetValue(clone, null);
            EntryUpdatedField!.SetValue(clone, null);
            return clone;
        }

        private static void PopulateCardCategory(
            IReadOnlyList<MerchantCardEntry> vanillaEntries,
            List<MerchantCardEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantCardEntry entry = CloneEntry(vanillaEntries[i]);
                if (i < candidateCount && ctx.HasMore)
                {
                    long locationId = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(locationId);
                    ApItemCardModelBase apCard = ApItemCardModelBase.CreateForSlot(itemName, playerName, classification, locationId);
                    CardCreationResultProp!.SetValue(entry, new CardCreationResult(apCard));
                    entry.CalcCost();
                    ApplyCostTier(entry);
                }
                else
                {
                    CardCreationResultProp!.SetValue(entry, null);
                }
                apEntries.Add(entry);
            }
        }

        private static void PopulateRelicCategory(
            IReadOnlyList<MerchantRelicEntry> vanillaEntries,
            List<MerchantRelicEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantRelicEntry entry = CloneEntry(vanillaEntries[i]);
                if (i < candidateCount && ctx.HasMore)
                {
                    long locationId = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(locationId);
                    RelicModelProp!.SetValue(entry, ApItemRelicModel.CreateForSlot(itemName, playerName, classification, locationId));
                    entry.CalcCost();
                    ApplyCostTier(entry);
                }
                else
                {
                    RelicModelProp!.SetValue(entry, null);
                }
                apEntries.Add(entry);
            }
        }

        private static void PopulatePotionCategory(
            IReadOnlyList<MerchantPotionEntry> vanillaEntries,
            List<MerchantPotionEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantPotionEntry entry = CloneEntry(vanillaEntries[i]);
                if (i < candidateCount && ctx.HasMore)
                {
                    long locationId = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(locationId);
                    PotionModelProp!.SetValue(entry, ApItemPotionModel.CreateForSlot(itemName, playerName, classification, locationId));
                    entry.CalcCost();
                    ApplyCostTier(entry);
                }
                else
                {
                    PotionModelProp!.SetValue(entry, null);
                }
                apEntries.Add(entry);
            }
        }

        private static void GateVanillaCategory<T>(
            IReadOnlyList<T> entries,
            int availableSlots,
            PropertyInfo itemProperty)
            where T : MerchantEntry
        {
            int lockedCount = Math.Clamp(entries.Count - availableSlots, 0, entries.Count);
            for (int i = 0; i < lockedCount; i++)
            {
                itemProperty.SetValue(entries[i], null);
            }
        }

        private static void EnsureInventoryReflectionAvailable()
        {
            if (CardCreationResultProp == null
                || RelicModelProp == null
                || PotionModelProp == null
                || CostField == null
                || PlayerField == null
                || CharacterCardEntriesField == null
                || ColorlessCardEntriesField == null
                || RelicEntriesField == null
                || PotionEntriesField == null
                || CardRemovalEntryProp == null
                || MemberwiseCloneMethod == null
                || PurchaseCompletedField == null
                || PurchaseFailedField == null
                || EntryUpdatedField == null)
            {
                throw new MissingMemberException("ShopSanity: required merchant inventory members could not be resolved.");
            }
        }

        #endregion

        /// <summary>
        /// After a normal merchant's inventory is rolled, builds an independent AP inventory
        /// and applies received slot unlocks only to the vanilla inventory.
        /// </summary>
        [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant))]
        public static class PopulateApShopSlots
        {
            [HarmonyPostfix]
            public static void Postfix(Player player, MerchantInventory __result)
            {
                if (!ArchipelagoClient.Settings.ShopSanity)
                {
                    return;
                }

                var charId = GameUtility.CurrentCharacterID;
                if (!charId.HasValue)
                {
                    LogUtility.Error("ShopSanity: couldn't resolve current character ID, leaving this shop visit untouched.");
                    return;
                }

                int act = Math.Min(player.RunState.CurrentActIndex + 1, 3);
                var ctx = new ShopVisitContext(player, act);

                int cardAvailable = AvailableSlots(CardSlotMax, ArchipelagoClient.Settings.ShopCardSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopCardSlotsReceived, charId.Value));
                int neutralAvailable = AvailableSlots(NeutralSlotMax, ArchipelagoClient.Settings.ShopNeutralSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopNeutralSlotsReceived, charId.Value));
                int relicAvailable = AvailableSlots(RelicSlotMax, ArchipelagoClient.Settings.ShopRelicSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopRelicSlotsReceived, charId.Value));
                int potionAvailable = AvailableSlots(PotionSlotMax, ArchipelagoClient.Settings.ShopPotionSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopPotionSlotsReceived, charId.Value));

                ApSlotCounts apSlots = GetApSlotCounts();

                LogUtility.Info(
                    $"ShopSanity: act={act} "
                    + $"vanilla(card={cardAvailable}/{CardSlotMax}, neutral={neutralAvailable}/{NeutralSlotMax}, relic={relicAvailable}/{RelicSlotMax}, potion={potionAvailable}/{PotionSlotMax}) "
                    + $"ap(card={apSlots.Cards}, neutral={apSlots.Neutral}, relic={apSlots.Relics}, potion={apSlots.Potions})");

                try
                {
                    MerchantInventory apInventory = CreateApInventory(player, __result, ctx, apSlots);

                    GateVanillaCategory(__result.CharacterCardEntries, cardAvailable, CardCreationResultProp!);
                    GateVanillaCategory(__result.ColorlessCardEntries, neutralAvailable, CardCreationResultProp!);
                    GateVanillaCategory(__result.RelicEntries, relicAvailable, RelicModelProp!);
                    GateVanillaCategory(__result.PotionEntries, potionAvailable, PotionModelProp!);

                    ApInventories.Remove(__result);
                    ApInventories.Add(__result, apInventory);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"ShopSanity: failed to prepare independent shop pages; leaving the vanilla shop untouched. {ex}");
                    return;
                }

                if (ArchipelagoClient.Settings.ShopRemoveSlots)
                {
                    int? removeLevel = ArchipelagoClient.Progress.MaxShopRemoveLevel(charId.Value);
                    bool removeUnlocked = (removeLevel ?? 0) >= act;
                    LogUtility.Info($"ShopSanity: card removal unlocked={removeUnlocked} (level={removeLevel}, act={act})");
                    if (!removeUnlocked)
                    {
                        __result.CardRemovalEntry?.SetUsed();
                    }
                }
            }
        }

        #region Purchase Interception

        /// <summary>Determines whether a merchant entry is one of our AP fakes and if so returns its backing location ID.</summary>
        internal static bool TryGetApLocationId(MerchantEntry entry, out long locationId)
        {
            locationId = -1;

            if (entry is MerchantCardEntry cardEntry)
            {
                if (cardEntry.CreationResult?.originalCard is ApItemCardModelBase apCard)
                {
                    locationId = apCard.ApLocationId;
                    return true;
                }
                return false;
            }

            if (entry is MerchantRelicEntry relicEntry)
            {
                if (relicEntry.Model is ApItemRelicModel apRelic)
                {
                    locationId = apRelic.ApLocationId;
                    return true;
                }
                return false;
            }

            if (entry is MerchantPotionEntry potionEntry)
            {
                if (potionEntry.Model is ApItemPotionModel apPotion)
                {
                    locationId = apPotion.ApLocationId;
                    return true;
                }
                return false;
            }

            return false;
        }

        private static void ClearApEntry(MerchantEntry entry)
        {
            switch (entry)
            {
                case MerchantCardEntry:
                    CardCreationResultProp!.SetValue(entry, null);
                    break;
                case MerchantRelicEntry:
                    RelicModelProp!.SetValue(entry, null);
                    break;
                case MerchantPotionEntry:
                    PotionModelProp!.SetValue(entry, null);
                    break;
            }
        }

        /// <summary>Convenience wrapper for the shop pages
        internal static bool IsApSlot(MerchantEntry entry) => TryGetApLocationId(entry, out _);

        /// <summary>
        /// Intercepts every card/relic/potion purchase attempt. AP-fake entries
        /// are redirected into DoApPurchase() (sends the location check instead
        /// of granting a real item) everything else falls through to vanilla
        /// </summary>
        [HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
        public static class InterceptApPurchase
        {
            [HarmonyPrefix]
            public static bool Prefix(MerchantEntry __instance, MerchantInventory? inventory, bool ignoreCost, ref Task<bool> __result)
            {
                if (!TryGetApLocationId(__instance, out long locationId))
                {
                    return true; // Not an AP slot run vanilla purchase logic untouched.
                }

                __result = DoApPurchase(__instance, inventory, locationId, ignoreCost);
                return false;
            }

            private static async Task<bool> DoApPurchase(MerchantEntry entry, MerchantInventory? inventory, long locationId, bool ignoreCost)
            {
                if (!entry.IsStocked)
                {
                    entry.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
                    return false;
                }
                if (!entry.EnoughGold && !ignoreCost)
                {
                    entry.InvokePurchaseFailed(PurchaseStatus.FailureGold);
                    return false;
                }

                if (PlayerField?.GetValue(entry) is not Player player)
                {
                    LogUtility.Error("ShopSanity: couldn't resolve owning Player for this purchase, aborting.");
                    return false;
                }

                int goldSpent = 0;
                if (!ignoreCost)
                {
                    goldSpent = entry.Cost;
                    await PlayerCmd.LoseGold(goldSpent, player, GoldLossType.Spent);
                }

                LogUtility.Info($"ShopSanity: sending check for location {locationId}");
                GameUtility.SendCheck(locationId);
                MarkShopSlotChecked(locationId);

                // AP checks are single-use even when The Courier would refill vanilla entries.
                ClearApEntry(entry);

                await Hook.AfterItemPurchased(player.RunState, player, entry, goldSpent);
                entry.InvokePurchaseCompleted(entry);
                return true;
            }
        }

        #endregion

        #region Post-Purchase Visual Fixes

        /// <summary>Skips the vanilla fly-to-deck animation for AP card fakes and cleans up the now-orphaned card node</summary>
        [HarmonyPatch(typeof(NMerchantCard), "OnSuccessfulPurchase")]
        public static class CardVisualFix
        {
            private static readonly FieldInfo? CardNodeField = AccessTools.Field(typeof(NMerchantCard), "_cardNode");
            private static readonly MethodInfo? UpdateVisualMethod = AccessTools.Method(typeof(NMerchantCard), "UpdateVisual");

            [HarmonyPrefix]
            public static bool Prefix(NMerchantCard __instance)
            {
                if (CardNodeField?.GetValue(__instance) is not NCard cardNode || cardNode.Model is not ApItemCardModelBase)
                {
                    return true;
                }
                cardNode.QueueFree();
                CardNodeField.SetValue(__instance, null);
                UpdateVisualMethod?.Invoke(__instance, null);
                return false;
            }
        }

        /// <summary>Skips the vanilla fly-to-inventory animation for AP relic fakes</summary>
        [HarmonyPatch(typeof(NMerchantRelic), "OnSuccessfulPurchase")]
        public static class RelicVisualFix
        {
            private static readonly FieldInfo? RelicCacheField = AccessTools.Field(typeof(NMerchantRelic), "_relic");
            private static readonly MethodInfo? UpdateVisualMethod = AccessTools.Method(typeof(NMerchantRelic), "UpdateVisual");

            [HarmonyPrefix]
            public static bool Prefix(NMerchantRelic __instance)
            {
                if (RelicCacheField?.GetValue(__instance) is not ApItemRelicModel)
                {
                    return true;
                }

                UpdateVisualMethod?.Invoke(__instance, null);
                RelicCacheField.SetValue(__instance, null);
                return false;
            }
        }

        /// <summary>Skips the vanilla fly-to-inventory animation for AP potion</summary>
        [HarmonyPatch(typeof(NMerchantPotion), "OnSuccessfulPurchase")]
        public static class PotionVisualFix
        {
            private static readonly FieldInfo? PotionCacheField = AccessTools.Field(typeof(NMerchantPotion), "_potion");
            private static readonly MethodInfo? UpdateVisualMethod = AccessTools.Method(typeof(NMerchantPotion), "UpdateVisual");

            [HarmonyPrefix]
            public static bool Prefix(NMerchantPotion __instance)
            {
                if (PotionCacheField?.GetValue(__instance) is not ApItemPotionModel)
                {
                    return true;
                }

                UpdateVisualMethod?.Invoke(__instance, null);
                PotionCacheField.SetValue(__instance, null);
                return false;
            }
        }

        [HarmonyPatch(typeof(RelicModel), "get_Pool")]
        public static class ApRelicPoolFix
        {
            [HarmonyPrefix]
            public static bool Prefix(RelicModel __instance, ref RelicPoolModel __result)
            {
                if (__instance is not ApItemRelicModel)
                {
                    return true;
                }
                __result = ModelDb.AllRelicPools.First();
                return false;
            }
        }

        [HarmonyPatch(typeof(PotionModel), "get_Pool")]
        public static class ApPotionPoolFix
        {
            [HarmonyPrefix]
            public static bool Prefix(PotionModel __instance, ref PotionPoolModel __result)
            {
                if (__instance is not ApItemPotionModel)
                {
                    return true;
                }
                __result = ModelDb.AllPotionPools.First();
                return false;
            }
        }

        /// <summary>Overwrites the potion icon TextureRect with the AP logo whenever an AP potion fake reloads</summary>
        [HarmonyPatch(typeof(NPotion), "Reload")]
        public static class PotionIconOverride
        {
            private static readonly FieldInfo? ModelField = AccessTools.Field(typeof(NPotion), "_model");
            private static Texture2D? _apLogoTexture;
            private const string ApLogoImportedPath = "res://.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.ctex";

            /// <summary>Lazily loads and caches the AP logo texture reusing Godot's resource cache</summary>
            private static Texture2D? ApLogoTexture
            {
                get
                {
                    _apLogoTexture ??= ResourceLoader.Load<Texture2D>(ApLogoImportedPath, null, ResourceLoader.CacheMode.Reuse);
                    return _apLogoTexture;
                }
            }

            [HarmonyPostfix]
            public static void Postfix(NPotion __instance)
            {
                if (!__instance.IsNodeReady())
                {
                    return;
                }
                if (ModelField?.GetValue(__instance) is not ApItemPotionModel)
                {
                    return;
                }

                if (ApLogoTexture == null)
                {
                    LogUtility.Error("ShopSanity: AP logo texture failed to load, check the import path.");
                    return;
                }

                __instance.Image.Texture = ApLogoTexture;
                __instance.Outline.Texture = null;
            }
        }

        #region Tooltip Text Fix
        /// <summary>
        /// goober stuff to bypass base game limits :p
        /// </summary>
        [HarmonyPatch(typeof(RelicModel), "get_Description")]
        public static class ApRelicDescriptionFix
        {
            [HarmonyPrefix]
            public static bool Prefix(RelicModel __instance, ref LocString __result)
            {
                if (__instance is not ApItemRelicModel apRelic)
                {
                    return true;
                }
                __result = BuildApLocString("relics", $"{__instance.Id.Entry}.description", apRelic.ApItemName, apRelic.ApPlayerName, apRelic.ApClassification);
                return false;
            }
        }

        [HarmonyPatch(typeof(PotionModel), "get_Title")]
        public static class ApPotionTitleFix
        {
            [HarmonyPrefix]
            public static bool Prefix(PotionModel __instance, ref LocString __result)
            {
                if (__instance is not ApItemPotionModel apPotion)
                {
                    return true;
                }
                __result = BuildApLocString("potions", $"{__instance.Id.Entry}.title", apPotion.ApItemName, apPotion.ApPlayerName, apPotion.ApClassification);
                return false;
            }
        }

        [HarmonyPatch(typeof(PotionModel), "get_Description")]
        public static class ApPotionDescriptionFix
        {
            [HarmonyPrefix]
            public static bool Prefix(PotionModel __instance, ref LocString __result)
            {
                if (__instance is not ApItemPotionModel apPotion)
                {
                    return true;
                }
                __result = BuildApLocString("potions", $"{__instance.Id.Entry}.description", apPotion.ApItemName, apPotion.ApPlayerName, apPotion.ApClassification);
                return false;
            }
        }

        /// <summary>Short label ("Prog.", "Useful", "Filler", "Trap"), matching the private
        /// ClassificationLabel switch already duplicated in ApItemRelicModel/ApItemPotionModel.</summary>
        private static string ClassificationLabel(ApItemClassification classification) => classification switch
        {
            ApItemClassification.Progression => "Prog.",
            ApItemClassification.Useful => "Useful",
            ApItemClassification.Trap => "Trap",
            _ => "Filler",
        };

        /// <summary>
        /// Builds a LocString against a real game loc table (e.g. "relics"/"potions"),
        /// populated with the same item_name/player_name/classification tokens
        /// ApItemRelicModel/ApItemPotionModel's ExtraHoverTips already use
        /// </summary>
        private static LocString BuildApLocString(string table, string key, string itemName, string playerName, ApItemClassification classification)
        {
            var locString = new LocString(table, key);
            locString.Add("item_name", itemName);
            locString.Add("player_name", playerName);
            locString.Add("classification", ClassificationLabel(classification));
            return locString;
        }

        #endregion
    }
    #endregion
}
