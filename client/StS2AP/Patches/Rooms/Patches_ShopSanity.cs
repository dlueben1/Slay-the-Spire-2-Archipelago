using System.Reflection;
using System.Runtime.CompilerServices;
using Archipelago.MultiClient.Net.Enums;
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
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Utils;

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

        // This is the one intentional reflection boundary left in shop handling. The CLR clone
        // preserves already-rolled entries without advancing the player's shop RNG a second time.
        private static readonly MethodInfo? MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        private static readonly ConditionalWeakTable<MerchantInventory, MerchantInventory> ApInventories = new();

        /// <summary>
        /// Creates free server hints for the AP checks that are actually stocked on this visit's
        /// shop page.
        /// </summary>
        private static void HintApInventory(MerchantInventory apInventory)
        {
            var session = ArchipelagoClient.Session;
            if (session == null || !ArchipelagoClient.IsConnected)
                return;

            long[] locationIds = apInventory.AllEntries
                .Select(entry => TryGetApLocationId(entry, out long locationId) ? locationId : -1)
                .Where(locationId => locationId != -1)
                .Distinct()
                .ToArray();

            if (locationIds.Length == 0)
            {
                return;
            }

            try
            {
                session.Hints.CreateHints(HintStatus.Unspecified, locationIds);
                LogUtility.Info($"ShopSanity: automatically hinted {locationIds.Length} stocked shop check(s).");
            }
            catch (Exception ex)
            {
                // Hinting is QoL only. A transient AP/socket failure must not block shop entry.
                LogUtility.Error($"ShopSanity: failed to automatically hint stocked shop checks; continuing without hints. {ex}");
            }
        }
        private static readonly ConditionalWeakTable<MerchantEntry, ShopCheckTarget> ApCheckTargets = new();

        #region Unclaimed-Location Queue
        /// <summary>
        /// Per-visit queue of not-yet-checked "Shop Slot N" location IDs for one
        /// player/act, shared across all four merchant categories as they populate.
        /// </summary>
        private sealed class ShopVisitContext
        {
            private readonly Queue<ShopCheckTarget> _missing = new();
            public ShopVisitContext(Player player, ArchipelagoSettings settings, int act)
            {
                int ceiling = Math.Min(
                    SlotCeilingForAct(act),
                    settings.TotalShopLocations);
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
                        long locationId = ResolveLocationId(checkName);
                        if (isChecked || ArchipelagoClient.CheckedLocations.Contains(locationId))
                        {
                            continue;
                        }
                        _missing.Enqueue(new ShopCheckTarget(locationId, checkName));
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
            public ShopCheckTarget GetNext() => _missing.Dequeue();

            private static long ResolveLocationId(string checkName)
            {
                checkName = CoopSlot.Name(checkName);
                if (ArchipelagoClient.Session != null)
                {
                    return ArchipelagoClient.Session.Locations.GetLocationIdFromName(
                        "Slay the Spire II",
                        checkName
                    );
                }

                foreach ((long locationId, ScoutedItemInfo info) in
                    ArchipelagoClient.ScoutedLocations)
                {
                    if (string.Equals(
                        info.LocationName,
                        checkName,
                        StringComparison.Ordinal))
                    {
                        return locationId;
                    }
                }

                throw new InvalidOperationException(
                    $"No cached location identity exists for {checkName}."
                );
            }
        }

        #endregion

        /// <summary>
        /// Looks up the item name, sending player, and classification for an
        /// archipelago location
        /// </summary>
        private static (string itemName, string playerName, ApItemClassification classification) ResolveApItem(
            ShopCheckTarget target)
        {
            if (ArchipelagoClient.ScoutedLocations.TryGetValue(
                    target.LocationId,
                    out ScoutedItemInfo? info)
                && info != null)
            {
                var classification =
                    info.Trap() ? ApItemClassification.Trap :
                    info.Advancement() ? ApItemClassification.Progression :
                    info.Useful() ? ApItemClassification.Useful :
                    ApItemClassification.Filler;
                return (info.ItemName, info.Player.Alias, classification);
            }

            LogUtility.Warn(
                $"ShopSanity: no scouted info for location {target.LocationId} "
                    + $"({target.LocationName}), showing as generic Filler."
            );
            return (target.LocationName, "???", ApItemClassification.Filler);
        }

        /// <summary>Records a shop slot's location as checked this session</summary>
        private static void MarkShopSlotChecked(ShopCheckTarget target)
        {
            ArchipelagoClient.Progress.ShopSlotsChecked[target.LocationName] = true;
        }

        /// <summary>How many of a category's slots are currently unlocked for real shop population</summary>
        private static int AvailableSlots(int categoryMax, int shuffledCount, int received)
            => Math.Min(categoryMax - Math.Min(shuffledCount, categoryMax) + received, categoryMax);

        /// <summary>Looks up a per-character received count, defaulting to 0 when the character has no entry yet</summary>
        private static int GetReceived(Dictionary<long, int> source, long id)
            => source.TryGetValue(id, out int v) ? v : 0;

        /// <summary>
        /// Resolves the local shop owner's slot settings. Multiplayer uses the settings frozen
        /// in that player's run contribution, including when several players share an AP slot.
        /// </summary>
        private static bool TryGetLocalShopSettings(
            Player player,
            out ArchipelagoSettings settings)
        {
            settings = null!;
            if (!MultiplayerSupport.ShouldApplyLocalShopUnlocks(player))
                return false;

            if (!MultiplayerSupport.IsRealMultiplayerRun)
            {
                ArchipelagoSettings? currentSettings = ArchipelagoClient.Settings;
                if (currentSettings == null)
                    return false;

                settings = currentSettings;
                return true;
            }

            if (player.RunState is not RunState runState
                || !ApRunData.TryGetPlayerState(
                    runState,
                    player.NetId,
                    out ApPlayerRunState playerState))
            {
                LogUtility.Error(
                    $"ShopSanity: no frozen AP player state for local player {player.NetId}; "
                        + "leaving this shop visit untouched."
                );
                return false;
            }

            if (playerState.Participation == ApParticipationKind.OwnApSlot
                && playerState.SlotSettings != null)
            {
                settings = playerState.SlotSettings;
                return true;
            }

            LogUtility.Error(
                $"ShopSanity: no usable AP shop settings for local player {player.NetId}; "
                    + "leaving this shop visit untouched."
            );
            return false;
        }

        /// <summary>
        /// Reads whatever CalcCost() just computed (the vanilla-style rarity-tiered
        /// baseline) and reduces it per the ShopSanityCosts option. Values match
        /// options.py exactly: 0=Fixed(15g), 1=Super_Discount_Tiered(20%),
        /// 2=Discount_Tiered(50%), 3=Tiered(full baseline, no discount).
        /// </summary>
        private static void ApplyCostTier(MerchantEntry entry, ArchipelagoSettings settings)
        {
            int baseline = entry._cost;
            int final = settings.ShopSanityCosts switch
            {
                0 => 15,
                1 => Math.Max(1, (int)Math.Round(baseline * 0.20f)),
                2 => Math.Max(1, (int)Math.Round(baseline * 0.50f)),
                _ => baseline,
            };
            entry._cost = final;
        }

        #region Slot Population

        private readonly record struct ApSlotCounts(int Cards, int Neutral, int Relics, int Potions);

        private sealed record ShopCheckTarget(long LocationId, string LocationName);

        /// <summary>
        /// The configured category counts reserve AP-page positions. Card-removal sanity adds
        /// three generic checks, so let those borrow otherwise-unused positions in display order.
        /// </summary>
        private static ApSlotCounts GetApSlotCounts(ArchipelagoSettings settings)
        {
            int cards = Math.Clamp(settings.ShopCardSlots, 0, CardSlotMax);
            int neutral = Math.Clamp(settings.ShopNeutralSlots, 0, NeutralSlotMax);
            int relics = Math.Clamp(settings.ShopRelicSlots, 0, RelicSlotMax);
            int potions = Math.Clamp(settings.ShopPotionSlots, 0, PotionSlotMax);
            int overflow = settings.ShopRemoveSlots ? ArchipelagoProgress._maxShopRemoves : 0;
            
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
            ApSlotCounts counts,
            ArchipelagoSettings settings)
        {
            EnsureCloneAvailable();

            var apInventory = new MerchantInventory(player);
            PopulateCardCategory(
                vanillaInventory.CharacterCardEntries,
                apInventory._characterCardEntries,
                counts.Cards,
                ctx,
                settings,
                vanillaInventory);
            PopulateCardCategory(
                vanillaInventory.ColorlessCardEntries,
                apInventory._colorlessCardEntries,
                counts.Neutral,
                ctx,
                settings,
                vanillaInventory);
            PopulateRelicCategory(
                vanillaInventory.RelicEntries,
                apInventory._relicEntries,
                counts.Relics,
                ctx,
                settings,
                vanillaInventory);
            PopulatePotionCategory(
                vanillaInventory.PotionEntries,
                apInventory._potionEntries,
                counts.Potions,
                ctx,
                settings,
                vanillaInventory);

            // Initialize the cloned scene's removal node, then keep it permanently empty/hidden.
            MerchantCardRemovalEntry sourceRemovalEntry = vanillaInventory.CardRemovalEntry
                ?? throw new InvalidOperationException("Normal merchant inventory had no card-removal entry.");
            MerchantCardRemovalEntry removalEntry = CloneEntry(sourceRemovalEntry, vanillaInventory);
            removalEntry.SetUsed();
            apInventory.CardRemovalEntry = removalEntry;

            return apInventory;
        }

        /// <summary>
        /// Clone the already-rolled entry so the AP page gets independent state without rolling
        /// another shop and advancing the player's shop RNG.
        /// </summary>
        private static T CloneEntry<T>(T source, MerchantInventory vanillaInventory) where T : MerchantEntry
        {
            var clone = (T)MemberwiseCloneMethod!.Invoke(source, null)!;
            clone.PurchaseCompleted -= vanillaInventory.UpdateEntries;
            return clone;
        }

        private static void PopulateCardCategory(
            IReadOnlyList<MerchantCardEntry> vanillaEntries,
            List<MerchantCardEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx,
            ArchipelagoSettings settings,
            MerchantInventory vanillaInventory)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantCardEntry entry = CloneEntry(vanillaEntries[i], vanillaInventory);
                if (i < candidateCount && ctx.HasMore)
                {
                    ShopCheckTarget target = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(target);
                    ApItemCardModelBase apCard = ApItemCardModelBase.CreateForSlot(
                        itemName,
                        playerName,
                        classification,
                        target.LocationId
                    );
                    entry.CreationResult = new CardCreationResult(apCard);
                    ApCheckTargets.Add(entry, target);
                    entry.CalcCost();
                    ApplyCostTier(entry, settings);
                }
                else
                {
                    entry.CreationResult = null;
                }
                apEntries.Add(entry);
            }
        }

        private static void PopulateRelicCategory(
            IReadOnlyList<MerchantRelicEntry> vanillaEntries,
            List<MerchantRelicEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx,
            ArchipelagoSettings settings,
            MerchantInventory vanillaInventory)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantRelicEntry entry = CloneEntry(vanillaEntries[i], vanillaInventory);
                if (i < candidateCount && ctx.HasMore)
                {
                    ShopCheckTarget target = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(target);
                    entry.Model = ApItemRelicModel.CreateForSlot(
                            itemName,
                            playerName,
                            classification,
                            target.LocationId
                        );
                    ApCheckTargets.Add(entry, target);
                    entry.CalcCost();
                    ApplyCostTier(entry, settings);
                }
                else
                {
                    entry.Model = null;
                }
                apEntries.Add(entry);
            }
        }

        private static void PopulatePotionCategory(
            IReadOnlyList<MerchantPotionEntry> vanillaEntries,
            List<MerchantPotionEntry> apEntries,
            int candidateCount,
            ShopVisitContext ctx,
            ArchipelagoSettings settings,
            MerchantInventory vanillaInventory)
        {
            for (int i = 0; i < vanillaEntries.Count; i++)
            {
                MerchantPotionEntry entry = CloneEntry(vanillaEntries[i], vanillaInventory);
                if (i < candidateCount && ctx.HasMore)
                {
                    ShopCheckTarget target = ctx.GetNext();
                    var (itemName, playerName, classification) = ResolveApItem(target);
                    entry.Model = ApItemPotionModel.CreateForSlot(
                            itemName,
                            playerName,
                            classification,
                            target.LocationId
                        );
                    ApCheckTargets.Add(entry, target);
                    entry.CalcCost();
                    ApplyCostTier(entry, settings);
                }
                else
                {
                    entry.Model = null;
                }
                apEntries.Add(entry);
            }
        }

        private static void GateVanillaCategory<T>(
            IReadOnlyList<T> entries,
            int availableSlots)
            where T : MerchantEntry
        {
            int lockedCount = Math.Clamp(entries.Count - availableSlots, 0, entries.Count);
            for (int i = 0; i < lockedCount; i++)
            {
                switch (entries[i])
                {
                    case MerchantCardEntry card: card.CreationResult = null; break;
                    case MerchantRelicEntry relic: relic.Model = null; break;
                    case MerchantPotionEntry potion: potion.Model = null; break;
                }
            }
        }

        private static void EnsureCloneAvailable()
        {
            if (MemberwiseCloneMethod == null)
            {
                throw new MissingMemberException("ShopSanity: object.MemberwiseClone could not be resolved.");
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
                if (!TryGetLocalShopSettings(player, out ArchipelagoSettings settings))
                    return;

                if (!settings.ShopSanity)
                {
                    return;
                }

                long? charId = player.GetAPCharacterNumber();
                if (!charId.HasValue)
                {
                    LogUtility.Error(
                        "ShopSanity: couldn't resolve the local player's character ID, "
                            + "leaving this shop visit untouched."
                    );
                    return;
                }

                int act = Math.Min(player.RunState.CurrentActIndex + 1, 3);

                int cardAvailable = AvailableSlots(CardSlotMax, settings.ShopCardSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopCardSlotsReceived, charId.Value));
                int neutralAvailable = AvailableSlots(NeutralSlotMax, settings.ShopNeutralSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopNeutralSlotsReceived, charId.Value));
                int relicAvailable = AvailableSlots(RelicSlotMax, settings.ShopRelicSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopRelicSlotsReceived, charId.Value));
                int potionAvailable = AvailableSlots(PotionSlotMax, settings.ShopPotionSlots,
                    GetReceived(ArchipelagoClient.Progress.ShopPotionSlotsReceived, charId.Value));

                bool showApChecks = MultiplayerSupport.ShouldShowLocalShopChecks(player);
                ApSlotCounts apSlots = showApChecks
                    ? GetApSlotCounts(settings)
                    : new ApSlotCounts(0, 0, 0, 0);

                LogUtility.Info(
                    $"ShopSanity: player={player.NetId} act={act} checks={showApChecks} "
                    + $"vanilla(card={cardAvailable}/{CardSlotMax}, neutral={neutralAvailable}/{NeutralSlotMax}, relic={relicAvailable}/{RelicSlotMax}, potion={potionAvailable}/{PotionSlotMax}) "
                    + $"ap(card={apSlots.Cards}, neutral={apSlots.Neutral}, relic={apSlots.Relics}, potion={apSlots.Potions})");

                try
                {
                    EnsureCloneAvailable();
                    MerchantInventory? apInventory = null;
                    if (showApChecks)
                    {
                        var ctx = new ShopVisitContext(player, settings, act);
                        apInventory = CreateApInventory(
                            player,
                            __result,
                            ctx,
                            apSlots,
                            settings
                        );
                    }

                    GateVanillaCategory(__result.CharacterCardEntries, cardAvailable);
                    GateVanillaCategory(__result.ColorlessCardEntries, neutralAvailable);
                    GateVanillaCategory(__result.RelicEntries, relicAvailable);
                    GateVanillaCategory(__result.PotionEntries, potionAvailable);

                    if (apInventory != null)
                    {
                        ApInventories.Remove(__result);
                        ApInventories.Add(__result, apInventory);
                        // showApChecks already restricts this inventory to the local AP check
                        // writer. Hint through that player's connection, never a remote replica.
                        HintApInventory(apInventory);
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Error(
                        $"ShopSanity: failed to prepare the local shop; leaving this visit "
                            + $"untouched. {ex}"
                    );
                    return;
                }

                if (settings.ShopRemoveSlots)
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
                case MerchantCardEntry card:
                    card.CreationResult = null;
                    break;
                case MerchantRelicEntry relic:
                    relic.Model = null;
                    break;
                case MerchantPotionEntry potion:
                    potion.Model = null;
                    break;
            }
        }

        /// <summary>Convenience wrapper for the shop pages.</summary>
        internal static bool IsApSlot(MerchantEntry entry) => TryGetApLocationId(entry, out _);

        /// <summary>
        /// Validates the local shop check before gold is committed. Concurrent purchases from
        /// another connection to the same slot are left to AP's location deduplication.
        /// </summary>
        private static bool CanPurchaseMultiplayerCheck(
            Player player,
            ShopCheckTarget target)
        {
            if (!MultiplayerLocationChecks.IsCheckWriter(player))
            {
                LogUtility.Error(
                    $"ShopSanity: player {player.NetId} is not the local AP check writer."
                );
                return false;
            }
            if (ArchipelagoClient.CheckedLocations.Contains(target.LocationId))
            {
                LogUtility.Warn(
                    $"ShopSanity: backing location {target.LocationId} was already checked; "
                        + "rejecting the stale purchase."
                );
                return false;
            }

            return true;
        }

        private static void SendShopCheck(
            Player player,
            ShopCheckTarget target)
        {
            if (!MultiplayerSupport.IsRealMultiplayerRun)
            {
                GameUtility.QueueCheck(target.LocationId);
                return;
            }

            MultiplayerLocationChecks.QueueCheck(player, target.LocationName, target.LocationId);
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
                // AP shop entries are local-only; their concrete gold loss is synchronized below.
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.Shops))
                    return true;

                if (!TryGetApLocationId(__instance, out long locationId))
                {
                    return true; // Not an AP slot run vanilla purchase logic untouched.
                }

                if (!ApCheckTargets.TryGetValue(
                        __instance,
                        out ShopCheckTarget? target)
                    || target == null)
                {
                    LogUtility.Error(
                        $"ShopSanity: AP entry for location {locationId} has no bound check "
                            + "identity; rejecting the purchase."
                    );
                    __instance.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
                    __result = Task.FromResult(false);
                    return false;
                }

                __result = DoApPurchase(__instance, inventory, target, ignoreCost);
                return false;
            }

            private static async Task<bool> DoApPurchase(
                MerchantEntry entry,
                MerchantInventory? inventory,
                ShopCheckTarget target,
                bool ignoreCost)
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

                Player player = entry._player;

                if (MultiplayerSupport.IsRealMultiplayerRun
                    && !CanPurchaseMultiplayerCheck(player, target))
                {
                    entry.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
                    return false;
                }

                int goldSpent = 0;
                if (!ignoreCost)
                {
                    goldSpent = entry.Cost;
                    await PlayerCmd.LoseGold(goldSpent, player, GoldLossType.Spent);
                    if (MultiplayerSupport.IsRealMultiplayerRun)
                        RunManager.Instance.RewardSynchronizer.SyncLocalGoldLost(goldSpent);
                }

                LogUtility.Info($"ShopSanity: sending check for location {target.LocationId}");
                SendShopCheck(player, target);
                MarkShopSlotChecked(target);

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
            [HarmonyPrefix]
            public static bool Prefix(NMerchantCard __instance)
            {
                if (__instance._cardNode is not NCard cardNode || cardNode.Model is not ApItemCardModelBase)
                {
                    return true;
                }
                cardNode.QueueFree();
                __instance._cardNode = null;
                __instance.Entry.OnMerchantInventoryUpdated();
                return false;
            }
        }

        /// <summary>Skips the vanilla fly-to-inventory animation for AP relic fakes</summary>
        [HarmonyPatch(typeof(NMerchantRelic), "OnSuccessfulPurchase")]
        public static class RelicVisualFix
        {
            [HarmonyPrefix]
            public static bool Prefix(NMerchantRelic __instance)
            {
                if (__instance._relic is not ApItemRelicModel)
                {
                    return true;
                }

                __instance.Entry.OnMerchantInventoryUpdated();
                __instance._relic = null;
                return false;
            }
        }

        /// <summary>Skips the vanilla fly-to-inventory animation for AP potion</summary>
        [HarmonyPatch(typeof(NMerchantPotion), "OnSuccessfulPurchase")]
        public static class PotionVisualFix
        {
            [HarmonyPrefix]
            public static bool Prefix(NMerchantPotion __instance)
            {
                if (__instance._potion is not ApItemPotionModel)
                {
                    return true;
                }

                __instance.Entry.OnMerchantInventoryUpdated();
                __instance._potion = null;
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
                if (__instance._model is not ApItemPotionModel)
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
