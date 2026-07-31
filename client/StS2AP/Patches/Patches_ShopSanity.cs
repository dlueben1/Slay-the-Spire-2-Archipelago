using System;
using System.Collections.Generic;
using System.Reflection;
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
                int ceiling = SlotCeilingForAct(act);
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
        private static int GetReceived(Dictionary<APItemCharID, int> source, APItemCharID id)
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

        /// <summary>Fills a character/colorless card category's candidate slots with AP fakes</summary>
        private static void PopulateCardCategory(IReadOnlyList<MerchantCardEntry> entries, int categoryMax, int availableSlots, ShopVisitContext ctx)
        {
            int candidateCount = Math.Min(categoryMax - availableSlots, entries.Count);
            for (int i = 0; i < candidateCount; i++)
            {
                var entry = entries[i];
                if (!ctx.HasMore)
                {
                    CardCreationResultProp?.SetValue(entry, null);
                    continue;
                }

                long locationId = ctx.GetNext();
                var (itemName, playerName, classification) = ResolveApItem(locationId);
                ApItemCardModelBase apCard = ApItemCardModelBase.CreateForSlot(itemName, playerName, classification, locationId);

                CardCreationResultProp?.SetValue(entry, new CardCreationResult(apCard));
                entry.CalcCost();
                ApplyCostTier(entry);
            }
        }

        /// <summary>Fills a relic category's candidate slots with AP fakes</summary>
        private static void PopulateRelicCategory(IReadOnlyList<MerchantRelicEntry> entries, int categoryMax, int availableSlots, ShopVisitContext ctx)
        {
            int candidateCount = Math.Min(categoryMax - availableSlots, entries.Count);
            for (int i = 0; i < candidateCount; i++)
            {
                var entry = entries[i];
                if (!ctx.HasMore)
                {
                    RelicModelProp?.SetValue(entry, null);
                    continue;
                }

                long locationId = ctx.GetNext();
                var (itemName, playerName, classification) = ResolveApItem(locationId);
                ApItemRelicModel apRelic = ApItemRelicModel.CreateForSlot(itemName, playerName, classification, locationId);

                RelicModelProp?.SetValue(entry, apRelic);
                entry.CalcCost();
                ApplyCostTier(entry);
            }
        }

        /// <summary>Fills a potion category's candidate slots with AP fakes</summary>
        private static void PopulatePotionCategory(IReadOnlyList<MerchantPotionEntry> entries, int categoryMax, int availableSlots, ShopVisitContext ctx)
        {
            int candidateCount = Math.Min(categoryMax - availableSlots, entries.Count);
            for (int i = 0; i < candidateCount; i++)
            {
                var entry = entries[i];
                if (!ctx.HasMore)
                {
                    PotionModelProp?.SetValue(entry, null);
                    continue;
                }

                long locationId = ctx.GetNext();
                var (itemName, playerName, classification) = ResolveApItem(locationId);
                ApItemPotionModel apPotion = ApItemPotionModel.CreateForSlot(itemName, playerName, classification, locationId);

                PotionModelProp?.SetValue(entry, apPotion);
                entry.CalcCost();
                ApplyCostTier(entry);
            }
        }

        /// <summary>Runs a category's populate step, swallowing exceptions so one bad category can't crash the whole room transition(because of my goober self)</summary>
        private static void PopulateCategorySafely(string categoryName, Action populate)
        {
            try
            {
                populate();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"ShopSanity: populating '{categoryName}' slots threw and was aborted for this visit, that category will show as vanilla/unlocked rather than crash the room transition. {ex}");
            }
        }

        #endregion

        /// <summary>
        /// after a normal merchant's inventory is
        /// rolled, replaces the appropriate portion of each category with AP
        /// location fakes and applies the Progressive Shop Remove gate
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

                LogUtility.Info($"ShopSanity: act={act} card={cardAvailable}/{CardSlotMax} neutral={neutralAvailable}/{NeutralSlotMax} relic={relicAvailable}/{RelicSlotMax} potion={potionAvailable}/{PotionSlotMax}");

                PopulateCategorySafely("card", () => PopulateCardCategory(__result.CharacterCardEntries, CardSlotMax, cardAvailable, ctx));
                PopulateCategorySafely("neutral", () => PopulateCardCategory(__result.ColorlessCardEntries, NeutralSlotMax, neutralAvailable, ctx));
                PopulateCategorySafely("relic", () => PopulateRelicCategory(__result.RelicEntries, RelicSlotMax, relicAvailable, ctx));
                PopulateCategorySafely("potion", () => PopulatePotionCategory(__result.PotionEntries, PotionSlotMax, potionAvailable, ctx));

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
        private static bool TryGetApLocationId(MerchantEntry entry, out long locationId)
        {
            locationId = -1;

            if (entry is MerchantCardEntry cardEntry)
            {
                if (cardEntry.CreationResult?.Card is ApItemCardModelBase apCard)
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

                __result = DoApPurchase(__instance, locationId, ignoreCost);
                return false;
            }

            /// <summary>Charges gold as usual (unless ignored), sends the Archipelago check, and clears the slot instead of restocking it</summary>
            private static async Task<bool> DoApPurchase(MerchantEntry entry, long locationId, bool ignoreCost)
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

                AccessTools.Method(entry.GetType(), "ClearAfterPurchase")?.Invoke(entry, null);

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

        #endregion
    }
}
