using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Random Shop Relics and Random Shop Potions: each of a normal merchant's 3 relic and 3 potion
/// slots holds an item of a kind rolled from the slot's weights. The entries stay in the same
/// two lists, which <c>NMerchantInventory</c> fills by index and Shop Sanity gates by position.
/// </summary>
/// <remarks>
/// Every replica builds every player's shop, so the layout comes from that player's frozen slot
/// settings and all rolls use <c>Player.PlayerRng.Shops</c>. A restock after a purchase (The
/// Courier) stays vanilla on purpose: restocking the rolled kind would chain Ancient relics, and
/// Foul Potions, which pay out 100 gold when thrown at the merchant.
/// </remarks>
public static class Patches_RandomShopStock
{
    private const int RelicSlotCount = 3;
    private const int PotionSlotCount = 3;

    /// <summary>
    /// <c>RelicModel.MerchantCost</c> for the rarities the game never sells. Fake relics share the
    /// Event rarity but override the cost, so they keep their own price.
    /// </summary>
    private const int UnsoldRelicCost = 999999999;

    private const int AncientRelicPrice = 350;
    private const int EventRelicPrice = 250;
    private const int EventPotionPrice = 100;

    private enum ShopRelicKind
    {
        Common,
        Uncommon,
        Rare,
        Shop,
        Event,
        Ancient,
    }

    private enum ShopPotionKind
    {
        Common,
        Uncommon,
        Rare,
        OtherClass,
        Event,
    }

    private static readonly ShopPotionKind[] PotionKinds = Enum.GetValues<ShopPotionKind>();

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.PopulateRelicEntries))]
    private static class PopulateRandomRelics
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantInventory __instance)
        {
            if (!TryGetRelicWeights(__instance.Player, out Dictionary<ShopRelicKind, int> weights, out ArchipelagoSettings settings))
                return true;

            PopulateRelics(__instance, weights, settings);
            return false;
        }
    }

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.PopulatePotionEntries))]
    private static class PopulateRandomPotions
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantInventory __instance)
        {
            if (!TryGetPotionWeights(__instance.Player, out Dictionary<ShopPotionKind, int> weights))
                return true;

            PopulatePotions(__instance, weights);
            return false;
        }
    }

    /// <summary>Ancient and Event relics have no real shop price, so this mod supplies one.</summary>
    [HarmonyPatch(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.CalcCost))]
    private static class PriceUnsoldRelics
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantRelicEntry __instance)
        {
            if (__instance.Model is not { MerchantCost: UnsoldRelicCost } relic
                || !TryGetRelicWeights(__instance._player, out _, out _))
            {
                return true;
            }

            int? price = relic.Rarity switch
            {
                RelicRarity.Ancient => AncientRelicPrice,
                RelicRarity.Event => EventRelicPrice,
                _ => null,
            };
            if (price == null)
                return true;

            // The same spread as vanilla relic prices.
            __instance._cost = (int)Math.Round(price.Value * __instance._player.PlayerRng.Shops.NextFloat(0.85f, 1.15f));
            return false;
        }
    }

    /// <summary>
    /// Vanilla never sells Event potions and would charge a Common's price. Foul Potion must cost
    /// at least its 100 gold payout at the merchant.
    /// </summary>
    [HarmonyPatch(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.CalcCost))]
    private static class PriceEventPotions
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantPotionEntry __instance)
        {
            if (__instance.Model is not { Rarity: PotionRarity.Event }
                || !TryGetPotionWeights(__instance._player, out _))
            {
                return true;
            }

            // The same spread as vanilla potion prices.
            __instance._cost = Mathf.RoundToInt(EventPotionPrice * __instance._player.PlayerRng.Shops.NextFloat(0.95f, 1.05f));
            return false;
        }
    }

    private static bool TryGetRelicWeights(
        Player player,
        out Dictionary<ShopRelicKind, int> weights,
        out ArchipelagoSettings settings)
    {
        weights = new Dictionary<ShopRelicKind, int>();
        if (!TryGetShopSettings(player, out settings))
            return false;

        ParseWeights(settings.ShopRelicWeights, weights);
        return weights.Count > 0;
    }

    private static bool TryGetPotionWeights(Player player, out Dictionary<ShopPotionKind, int> weights)
    {
        weights = new Dictionary<ShopPotionKind, int>();
        if (!TryGetShopSettings(player, out ArchipelagoSettings settings))
            return false;

        ParseWeights(settings.ShopPotionWeights, weights);
        return weights.Count > 0;
    }

    private static bool TryGetShopSettings(Player player, out ArchipelagoSettings settings)
    {
        settings = null!;
        return MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.Shops)
            && ApPlayerContextResolver.TryGetRewardSettings(player, out settings);
    }

    private static void ParseWeights<TKind>(Dictionary<string, int> source, Dictionary<TKind, int> weights)
        where TKind : struct, Enum
    {
        foreach ((string name, int weight) in source)
        {
            if (Enum.TryParse(name, ignoreCase: true, out TKind kind) && weight > 0)
                weights[kind] = weight;
        }
    }

    private static TKind RollKind<TKind>(Player player, Dictionary<TKind, int> weights)
        where TKind : struct, Enum
    {
        int roll = player.PlayerRng.Shops.NextInt(weights.Values.Sum());
        // Walk the kinds in a fixed order so every replica maps the roll to the same kind.
        foreach (TKind kind in Enum.GetValues<TKind>())
        {
            if (!weights.TryGetValue(kind, out int weight))
                continue;
            if (roll < weight)
                return kind;
            roll -= weight;
        }
        throw new InvalidOperationException("Random shop stock: weighted roll fell outside the weights.");
    }

    private static void PopulateRelics(
        MerchantInventory inventory,
        Dictionary<ShopRelicKind, int> weights,
        ArchipelagoSettings settings)
    {
        Player player = inventory.Player;
        var rolled = new List<string>();

        for (int slot = 0; slot < RelicSlotCount; slot++)
        {
            ShopRelicKind kind = RollKind(player, weights);
            MerchantRelicEntry entry = CreateRelicEntry(inventory, kind, slot, settings);
            inventory.AddRelicEntry(entry);
            rolled.Add($"{kind}:{entry.Model?.Id.Entry}");
        }

        LogUtility.Info($"Random Shop Relics: player={player.NetId} [{string.Join(", ", rolled)}]");
    }

    private static MerchantRelicEntry CreateRelicEntry(
        MerchantInventory inventory,
        ShopRelicKind kind,
        int slot,
        ArchipelagoSettings settings)
    {
        Player player = inventory.Player;
        HashSet<ModelId> onSale = inventory.RelicEntries
            .Select(entry => entry.Model?.Id)
            .OfType<ModelId>()
            .ToHashSet();

        RelicModel? relic = kind switch
        {
            ShopRelicKind.Event => CreateEventRelic(player, onSale),
            ShopRelicKind.Ancient => AncientRelicPool.CreateForShopSlot(
                player,
                $"shop|{player.RunState.TotalFloor}|{slot}",
                onSale,
                settings),
            _ => null,
        };
        if (relic != null)
            return new MerchantRelicEntry(relic, player);

        // The grab bag moves on to the next rarity by itself when one runs dry.
        RelicRarity rarity = kind switch
        {
            ShopRelicKind.Common => RelicRarity.Common,
            ShopRelicKind.Uncommon => RelicRarity.Uncommon,
            ShopRelicKind.Shop => RelicRarity.Shop,
            // An empty Event or Ancient pool, which owning most of them can reach, sells a Rare.
            _ => RelicRarity.Rare,
        };
        return new MerchantRelicEntry(rarity, player);
    }

    private static RelicModel? CreateEventRelic(Player player, IReadOnlySet<ModelId> onSale)
    {
        HashSet<ModelId> owned = player.Relics.Select(relic => relic.Id).ToHashSet();
        // AllRelics is sorted, so the same RNG roll means the same relic on every replica.
        List<RelicModel> candidates = ModelDb.AllRelics
            .Where(relic => relic.Rarity == RelicRarity.Event
                // Unlike the other Fake relics, the rug has no hooks and no shop price of its own.
                && relic is not FakeMerchantsRug
                && relic.IsAllowedInShops
                && relic.IsAllowed(player.RunState)
                && !owned.Contains(relic.Id)
                && !onSale.Contains(relic.Id))
            .ToList();

        return player.PlayerRng.Shops.NextItem(candidates)?.ToMutable();
    }

    private static void PopulatePotions(MerchantInventory inventory, Dictionary<ShopPotionKind, int> weights)
    {
        Player player = inventory.Player;
        var rolled = new List<string>();

        for (int slot = 0; slot < PotionSlotCount; slot++)
        {
            ShopPotionKind kind = RollKind(player, weights);
            HashSet<PotionModel> onSale = inventory.PotionEntries
                .Select(entry => entry.Model?.CanonicalInstance)
                .OfType<PotionModel>()
                .ToHashSet();

            // The rolled kind first, then any other kind if it has nothing left to sell.
            List<PotionModel> candidates = new[] { kind }
                .Concat(PotionKinds)
                .Select(candidateKind => PotionPool(player, candidateKind).Where(potion => !onSale.Contains(potion)).ToList())
                .First(pool => pool.Count > 0);

            PotionModel potion = player.PlayerRng.Shops.NextItem(candidates)!.ToMutable();
            inventory._potionEntries.Add(new MerchantPotionEntry(potion, player));
            rolled.Add($"{kind}:{potion.Id.Entry}");
        }

        LogUtility.Info($"Random Shop Potions: player={player.NetId} [{string.Join(", ", rolled)}]");
    }

    /// <summary>The potions a kind can sell, sorted so every replica sees the same order.</summary>
    private static IEnumerable<PotionModel> PotionPool(Player player, ShopPotionKind kind)
    {
        IEnumerable<PotionModel> pool = kind switch
        {
            ShopPotionKind.Common => OwnPotions(player).Where(potion => potion.Rarity == PotionRarity.Common),
            ShopPotionKind.Uncommon => OwnPotions(player).Where(potion => potion.Rarity == PotionRarity.Uncommon),
            ShopPotionKind.Rare => OwnPotions(player).Where(potion => potion.Rarity == PotionRarity.Rare),
            ShopPotionKind.OtherClass => ModelDb.AllCharacters
                .Where(character => character.Id != player.Character.Id)
                .SelectMany(character => character.PotionPool.AllPotions),
            ShopPotionKind.Event => ModelDb.AllPotions
                .Where(potion => potion.Rarity is PotionRarity.Event or PotionRarity.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return pool.Distinct().OrderBy(potion => potion.Id.Entry, StringComparer.Ordinal);
    }

    /// <summary>The player's class potions and the shared ones, as the vanilla shop offers.</summary>
    private static IEnumerable<PotionModel> OwnPotions(Player player) =>
        player.Character.PotionPool.GetUnlockedPotions(player.UnlockState)
            .Concat(ModelDb.PotionPool<SharedPotionPool>().GetUnlockedPotions(player.UnlockState));
}
