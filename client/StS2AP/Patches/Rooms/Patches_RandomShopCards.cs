using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Random Shop Cards: each of a normal merchant's 7 card slots holds a card of a kind rolled from
/// the slot's weights, instead of the vanilla Attack, Attack, Skill, Skill, Power and colorless
/// Uncommon and Rare layout. The 7 entries still go into the 5 character and 2 colorless entry
/// lists, which <c>NMerchantInventory</c> fills by index and Shop Sanity gates by position.
/// </summary>
/// <remarks>
/// Every replica builds every player's shop, so the layout comes from that player's frozen slot
/// settings and all rolls use <c>Player.PlayerRng.Shops</c>, like the vanilla shop.
/// </remarks>
public static class Patches_RandomShopCards
{
    private const int CharacterSlotCount = 5;
    private const int ColorlessSlotCount = 2;

    private const int EventCardPrice = 100;

    private enum ShopCardKind
    {
        Attack,
        Skill,
        Power,
        Common,
        Uncommon,
        Rare,
        Colorless,
        Event,
    }

    private enum ShopCardPool
    {
        Character,
        Colorless,
        Event,
    }

    private static readonly ShopCardKind[] Kinds = Enum.GetValues<ShopCardKind>();

    /// <summary>
    /// A merchant card entry's constructor arguments: a pool plus either a card type (with the
    /// shop's rarity roll) or a fixed rarity.
    /// </summary>
    private sealed record CardRequest(ShopCardPool Pool, CardType? Type, CardRarity? Rarity)
    {
        public override string ToString() =>
            (Pool == ShopCardPool.Colorless ? "colorless " : "") + (Type?.ToString() ?? Rarity?.ToString());
    }

    private static readonly CardRequest[] CharacterTypeRequests =
    {
        new(ShopCardPool.Character, CardType.Attack, null),
        new(ShopCardPool.Character, CardType.Skill, null),
        new(ShopCardPool.Character, CardType.Power, null),
    };

    private static readonly CardRequest[] CharacterRarityRequests =
    {
        new(ShopCardPool.Character, null, CardRarity.Common),
        new(ShopCardPool.Character, null, CardRarity.Uncommon),
        new(ShopCardPool.Character, null, CardRarity.Rare),
    };

    private static readonly CardRequest[] ColorlessRequests =
    {
        new(ShopCardPool.Colorless, null, CardRarity.Uncommon),
        new(ShopCardPool.Colorless, null, CardRarity.Rare),
    };

    /// <summary>The event pool's Event-rarity cards; its Ancient cards stay out of shops.</summary>
    private static readonly CardRequest EventRequest = new(ShopCardPool.Event, null, CardRarity.Event);

    /// <summary>Inventories whose cards this patch rolled, so the colorless pass is skipped.</summary>
    private static readonly ConditionalWeakTable<MerchantInventory, object> RandomInventories = new();

    /// <summary>Entries this patch created, which may restock from a pool that has run dry.</summary>
    private static readonly ConditionalWeakTable<MerchantCardEntry, CardRequest> RandomEntries = new();

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.PopulateCharacterCardEntries))]
    private static class PopulateRandomCards
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantInventory __instance)
        {
            if (!TryGetWeights(__instance.Player, out Dictionary<ShopCardKind, int> weights))
                return true;

            PopulateAll(__instance, weights);
            RandomInventories.AddOrUpdate(__instance, weights);
            return false;
        }
    }

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.PopulateColorlessCardEntries))]
    private static class SkipColorlessPass
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantInventory __instance) =>
            !RandomInventories.TryGetValue(__instance, out _);
    }

    /// <summary>
    /// The vanilla restock after a purchase (The Courier) rolls the same request again and throws
    /// or picks from nothing when no card is left. Leave the slot empty instead.
    /// </summary>
    [HarmonyPatch(typeof(MerchantCardEntry), nameof(MerchantCardEntry.Populate))]
    private static class EmptyExhaustedRestock
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantCardEntry __instance)
        {
            if (!RandomEntries.TryGetValue(__instance, out CardRequest? request)
                || __instance._inventory is not MerchantInventory inventory
                || HasCandidates(inventory, request))
            {
                return true;
            }

            LogUtility.Info($"Random Shop Cards: no {request} card left to restock; the slot stays empty.");
            __instance.CreationResult = null;
            return false;
        }
    }

    /// <summary>Vanilla never sells Event cards and would charge a Common's price.</summary>
    [HarmonyPatch(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CalcCost))]
    private static class PriceEventCards
    {
        [HarmonyPrefix]
        private static bool Prefix(MerchantCardEntry __instance)
        {
            if (__instance.CreationResult?.Card is not { Rarity: CardRarity.Event }
                || !RandomEntries.TryGetValue(__instance, out _))
            {
                return true;
            }

            // The same spread and sale discount as vanilla card prices.
            __instance._cost = Mathf.RoundToInt(EventCardPrice * __instance._player.PlayerRng.Shops.NextFloat(0.95f, 1.05f));
            if (__instance.IsOnSale)
                __instance._cost /= 2;
            return false;
        }
    }

    private static bool TryGetWeights(Player player, out Dictionary<ShopCardKind, int> weights)
    {
        weights = new Dictionary<ShopCardKind, int>();
        if (!MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.Shops)
            || !ApPlayerContextResolver.TryGetRewardSettings(player, out ArchipelagoSettings settings))
        {
            return false;
        }

        foreach ((string name, int weight) in settings.ShopCardWeights)
        {
            if (Enum.TryParse(name, ignoreCase: true, out ShopCardKind kind) && weight > 0)
                weights[kind] = weight;
        }
        return weights.Count > 0;
    }

    private static void PopulateAll(MerchantInventory inventory, Dictionary<ShopCardKind, int> weights)
    {
        Player player = inventory.Player;
        // Vanilla rolls the sale slot before the cards; here it can be any of the 7.
        int saleSlot = player.PlayerRng.Shops.NextInt(CharacterSlotCount + ColorlessSlotCount);
        var rolled = new List<string>();

        for (int slot = 0; slot < CharacterSlotCount + ColorlessSlotCount; slot++)
        {
            ShopCardKind kind = RollKind(player, weights);
            CardRequest preferred = PreferredRequest(player, kind);
            CardRequest? request = FallbackOrder(preferred)
                .FirstOrDefault(candidate => HasCandidates(inventory, candidate));

            MerchantCardEntry entry;
            if (request == null)
            {
                // Nothing at all is left to sell, which only a tiny modded pool can reach.
                entry = CreateEntry(inventory, preferred);
                rolled.Add($"{kind}->empty");
            }
            else
            {
                entry = CreateEntry(inventory, request);
                RandomEntries.AddOrUpdate(entry, request);
                entry.Populate();
                if (slot == saleSlot)
                    entry.SetOnSale();
                rolled.Add(request == preferred ? $"{kind}" : $"{kind}->{request}");
            }

            if (slot < CharacterSlotCount)
                inventory._characterCardEntries.Add(entry);
            else
                inventory._colorlessCardEntries.Add(entry);
        }

        LogUtility.Info(
            $"Random Shop Cards: player={player.NetId} sale={saleSlot} [{string.Join(", ", rolled)}]");
    }

    private static ShopCardKind RollKind(Player player, Dictionary<ShopCardKind, int> weights)
    {
        int roll = player.PlayerRng.Shops.NextInt(weights.Values.Sum());
        // Walk the kinds in a fixed order so every replica maps the roll to the same kind.
        foreach (ShopCardKind kind in Kinds)
        {
            if (!weights.TryGetValue(kind, out int weight))
                continue;
            if (roll < weight)
                return kind;
            roll -= weight;
        }
        throw new InvalidOperationException("Random Shop Cards: weighted roll fell outside the weights.");
    }

    private static CardRequest PreferredRequest(Player player, ShopCardKind kind) => kind switch
    {
        ShopCardKind.Attack => CharacterTypeRequests[0],
        ShopCardKind.Skill => CharacterTypeRequests[1],
        ShopCardKind.Power => CharacterTypeRequests[2],
        ShopCardKind.Common => CharacterRarityRequests[0],
        ShopCardKind.Uncommon => CharacterRarityRequests[1],
        ShopCardKind.Rare => CharacterRarityRequests[2],
        // Even odds, like vanilla's one colorless Uncommon and one colorless Rare.
        ShopCardKind.Colorless => player.PlayerRng.Shops.NextBool()
            ? ColorlessRequests[0]
            : ColorlessRequests[1],
        ShopCardKind.Event => EventRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The preferred request first, then the closest looser ones: another rarity or type from the
    /// same pool, then anything from either pool.
    /// </summary>
    private static IEnumerable<CardRequest> FallbackOrder(CardRequest preferred)
    {
        IEnumerable<CardRequest> samePool = preferred switch
        {
            { Pool: ShopCardPool.Colorless or ShopCardPool.Event } => ColorlessRequests,
            { Type: not null } => CharacterTypeRequests,
            // The nearest rarity first: Common and Uncommon move up, Rare moves down.
            { Rarity: CardRarity.Rare } =>
                new[] { CharacterRarityRequests[1], CharacterRarityRequests[0] },
            { Rarity: CardRarity.Uncommon } =>
                new[] { CharacterRarityRequests[2], CharacterRarityRequests[0] },
            _ => CharacterRarityRequests,
        };

        return new[] { preferred }
            .Concat(samePool)
            .Concat(CharacterTypeRequests)
            .Concat(ColorlessRequests)
            .Distinct();
    }

    private static MerchantCardEntry CreateEntry(MerchantInventory inventory, CardRequest request)
    {
        Player player = inventory.Player;
        List<CardModel> pool = Pool(player, request);
        return request.Type is CardType type
            ? new MerchantCardEntry(player, inventory, pool, type)
            : new MerchantCardEntry(player, inventory, pool, request.Rarity!.Value);
    }

    private static List<CardModel> Pool(Player player, CardRequest request)
    {
        CardPoolModel pool = request.Pool switch
        {
            ShopCardPool.Colorless => ModelDb.CardPool<ColorlessCardPool>(),
            ShopCardPool.Event => ModelDb.CardPool<EventCardPool>(),
            _ => player.Character.CardPool,
        };
        return pool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            // Tinker Time sets this card's type and effect when it builds one; a plain copy has neither.
            .Where(card => card is not MadScience)
            .ToList();
    }

    /// <summary>
    /// Whether <c>CardFactory.CreateForMerchant</c> can make a card for this request: the same
    /// pool hook and filters, minus the cards already on sale in this inventory. A type request
    /// accepts any of the three rarities the factory cycles through after its rarity roll.
    /// </summary>
    private static bool HasCandidates(MerchantInventory inventory, CardRequest request)
    {
        Player player = inventory.Player;
        HashSet<CardModel> onSale = inventory.CardEntries
            .Select(entry => entry.CreationResult?.Card.CanonicalInstance)
            .OfType<CardModel>()
            .ToHashSet();
        IEnumerable<CardModel> options = Hook.ModifyMerchantCardPool(
            player.RunState,
            player,
            Pool(player, request).Except(onSale));
        options = CardFactory.FilterForPlayerCount(
            player.RunState,
            options.Where(card => card.Rarity != CardRarity.Basic));

        if (request.Type is CardType type)
            return options.Any(card => card.Type == type
                && card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare);

        CardRarity rarity = Hook.ModifyMerchantCardRarity(player.RunState, player, request.Rarity!.Value);
        return options.Any(card => card.Rarity == rarity);
    }
}
