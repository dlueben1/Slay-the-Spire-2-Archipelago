using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Models;

/// <summary>
/// In each shop, every purchase makes the rest of that shop 15% cheaper, up to 45%. AP shop checks
/// fire the same purchase hook and their price passes through the same price hook, so they count
/// and are discounted like any other item.
/// </summary>
public sealed class ApLoyalCustomer : ApRunModifier
{
    private const decimal DiscountPerPurchase = 0.15m;
    private const int MaxDiscountedPurchases = 3;

    // Purchases in the current shop by player. Created on first use so the canonical model and its
    // run copies never share one. Not saved: reloading inside a shop only restarts the discount.
    private Dictionary<ulong, int>? _purchasesHere;

    protected override string IconModifierEntry => "hoarder";

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        _purchasesHere?.Clear();
        return Task.CompletedTask;
    }

    public override Task AfterItemPurchased(Player player, MerchantEntry itemPurchased, int goldSpent)
    {
        _purchasesHere ??= new Dictionary<ulong, int>();
        _purchasesHere[player.NetId] = PurchasesHere(player) + 1;
        return Task.CompletedTask;
    }

    public override decimal ModifyMerchantPrice(Player player, MerchantEntry entry, decimal cost) =>
        cost * (1m - DiscountPerPurchase * Math.Min(PurchasesHere(player), MaxDiscountedPurchases));

    private int PurchasesHere(Player player) =>
        _purchasesHere != null && _purchasesHere.TryGetValue(player.NetId, out int count) ? count : 0;
}
