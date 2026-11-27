using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Patches;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>
/// Shop purchases. Every purchase, including an AP shop check and a Blitz merchant pick, ends in
/// <see cref="Hook.AfterItemPurchased"/>. With Shop Sanity the AP checks sit on a page of their
/// own and the shop's normal stock stays, so the "buy an item" squares count the normal stock
/// only; AP checks still count as purchases.
/// </summary>
internal static class ShopDetectors
{
    // E15 "Remove a card at a shop" and I-M04 "Remove 3 cards at shops in one run": the game's
    // own per-run count of shop removals (saved with the run), which a Blitz merchant's removal
    // also raises.
    private const string RemoveAtShopSquare = "E15";
    private const string RemoveThreeAtShopsSquare = "I-M04";

    // E16 "Buy a relic from a shop": a relic bought from a shop's relic stock.
    private const string BuyRelicSquare = "E16";

    // I-E02 "Buy the on-sale card": the card entry the shop marked on sale, bought.
    private const string BuyOnSaleCardSquare = "I-E02";

    // I-M05 "Buy every potion in one shop": as many potions bought in this visit as the shop had
    // in stock before the visit's first purchase. Shop Sanity empties the potion slots it locks,
    // so only stocked slots count. Counting purchases, not empty slots, still works when The
    // Courier restocks them.
    private const string BuyEveryPotionSquare = "I-M05";

    // M13 "Buy a card, a relic and a potion in one shop visit".
    private const string CardRelicPotionSquare = "M13";

    // AP-E07 "Make 4 purchases in one shop": any purchase in this visit, AP checks and card
    // removal included.
    private const string FourPurchasesSquare = "AP-E07";
    private const int FourPurchases = 4;

    // AP-M07 "Buy from a Blitz merchant": a Blitz offer, or anything bought at a shop the mod
    // turned into a Blitz merchant (its AP checks page).
    private const string BlitzMerchantSquare = "AP-M07";

    /// <summary>What a purchase looked like before it happened.</summary>
    /// <param name="IsApCheck">The entry is a Shop Sanity check. Its item is cleared before the
    /// purchase hook runs, so it cannot be told apart afterwards.</param>
    /// <param name="WasOnSale">The entry was the shop's on-sale card. A restocked entry (The
    /// Courier) is no longer on sale by the time the purchase hook runs.</param>
    private sealed record PurchaseAttempt(bool IsApCheck, bool WasOnSale);

    private static readonly ConditionalWeakTable<MerchantEntry, PurchaseAttempt> Attempts = new();

    /// <summary>The local player's purchases in the current shop visit.</summary>
    /// <remarks>
    /// Not saved: reloading inside a shop starts the visit's counts again, as Loyal Customer's
    /// discount does.
    /// </remarks>
    private sealed class ShopVisit
    {
        public int Purchases;
        public bool BoughtCard;
        public bool BoughtRelic;
        public int PotionsBought;

        /// <summary>
        /// Stocked potion slots, taken at the visit's first purchase from the normal stock (AP
        /// checks pass no inventory), so it is taken before any potion is bought.
        /// </summary>
        public int? PotionsStocked;
    }

    private static ShopVisit _visit = new();

    public static void CheckShopCardRemovals(Player player)
    {
        int removals = player.ExtraFields.CardShopRemovalsUsed;
        EconomyDetector.CompleteIf(RemoveAtShopSquare, removals >= 1, player);
        BingoSquares.Reach(RemoveThreeAtShopsSquare, player, removals, 3);
    }

    private static void OnPurchased(IRunState runState, Player player, MerchantEntry entry)
    {
        Attempts.TryGetValue(entry, out PurchaseAttempt? attempt);
        Attempts.Remove(entry);
        bool isApCheck = attempt?.IsApCheck ?? false;

        _visit.Purchases++;
        BingoSquares.Reach(FourPurchasesSquare, player, _visit.Purchases, FourPurchases);
        if (BingoSquares.IsOpen(BlitzMerchantSquare))
            EconomyDetector.CompleteIf(BlitzMerchantSquare, IsBlitzMerchantPurchase(runState, entry), player);

        if (isApCheck)
            return;

        switch (entry)
        {
            case MerchantCardEntry:
                _visit.BoughtCard = true;
                EconomyDetector.CompleteIf(BuyOnSaleCardSquare, attempt?.WasOnSale ?? false, player);
                break;
            case MerchantRelicEntry:
                _visit.BoughtRelic = true;
                EconomyDetector.CompleteIf(BuyRelicSquare, true, player);
                break;
            case MerchantPotionEntry:
                _visit.PotionsBought++;
                int potionsStocked = _visit.PotionsStocked ?? 0;
                EconomyDetector.CompleteIf(
                    BuyEveryPotionSquare,
                    potionsStocked > 0 && _visit.PotionsBought >= potionsStocked,
                    player);
                break;
        }
        EconomyDetector.CompleteIf(
            CardRelicPotionSquare,
            _visit.BoughtCard && _visit.BoughtRelic && _visit.PotionsBought > 0,
            player);
    }

    private static bool IsBlitzMerchantPurchase(IRunState runState, MerchantEntry entry)
    {
#if STS2_AT_LEAST_0_113_0
        return entry is BlitzMerchantEntry
            || (runState.CurrentRoom is MerchantRoom && BlitzMerchantVisits.IsBlitzMerchant(runState));
#else
        return false;
#endif
    }

    /// <summary>
    /// Notes what the entry is before it is bought. Runs first so it sees an AP check before
    /// Shop Sanity's own prefix takes the purchase over and clears the entry.
    /// </summary>
    [HarmonyPatch(typeof(MerchantEntry), GameApiCompat.MerchantEntryPurchaseMethod)]
    private static class BeforePurchaseAttempt
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MerchantEntry __instance, MerchantInventory? inventory) =>
            EconomyDetector.Run(nameof(BeforePurchaseAttempt), (__instance, inventory), static args =>
            {
                (MerchantEntry entry, MerchantInventory? inventory) = args;
                if (!LocalContext.IsMe(entry._player))
                    return;
                if (_visit.PotionsStocked == null && inventory != null)
                    _visit.PotionsStocked = inventory.PotionEntries.Count(potion => potion.IsStocked);
                Attempts.AddOrUpdate(
                    entry,
                    new PurchaseAttempt(
                        IsApCheck: Patches_ShopSanity.IsApSlot(entry),
                        WasOnSale: entry is MerchantCardEntry { IsOnSale: true }));
            });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterItemPurchased))]
    private static class AfterItemPurchased
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, Player player, MerchantEntry itemPurchased) =>
            EconomyDetector.Run(nameof(AfterItemPurchased), (runState, player, itemPurchased), static args =>
            {
                (IRunState runState, Player player, MerchantEntry itemPurchased) = args;
                if (!LocalContext.IsMe(player))
                    return;
                OnPurchased(runState, player, itemPurchased);
                // A bought card, relic or potion, or a removed card, changes the state squares.
                EconomyDetector.CheckAll(player);
            });
    }

    /// <summary>Every room entered starts a new shop visit.</summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    private static class BeforeRoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix() => _visit = new ShopVisit();
    }
}
