using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>The reward a bonus receipt was assigned: a relic, or a payload for the other kinds.</summary>
internal sealed record BonusAssignment(RelicModel? Relic, BonusRewardPayload? Payload);

internal static class BonusRewardUtility
{
    private static readonly IReadOnlyDictionary<ItemTable.APItem, string> Categories =
        new Dictionary<ItemTable.APItem, string>
        {
            [ItemTable.APItem.BonusWaxRelic] = BonusItemDefinition.WaxRelicCategory,
            [ItemTable.APItem.BonusRelic] = BonusItemDefinition.RelicCategory,
            [ItemTable.APItem.BonusEnchantment] = BonusItemDefinition.EnchantmentCategory,
            [ItemTable.APItem.BonusEnchantedCards] = BonusItemDefinition.EnchantedCardsCategory,
            [ItemTable.APItem.BonusMaxHp] = BonusItemDefinition.MaxHpCategory,
            [ItemTable.APItem.BonusCards] = BonusItemDefinition.CardsCategory,
        };

    private const int EnchantedCardCount = 3;

    /// <summary>Whether this universal item is a bonus item claimed from the AP reward menu.</summary>
    public static bool IsBonusItem(ItemTable.APItem item) => Categories.ContainsKey(item);

    public static bool IsBonusItem(long itemId) =>
        ArchipelagoIdCodec.IsUniversalItemId(itemId) && IsBonusItem((ItemTable.APItem)itemId);

    /// <summary>In multiplayer, bonus items arrive as gold unless the experimental Bonus Items feature is on.</summary>
    public static bool ConvertToGold(long itemId) =>
        IsBonusItem(itemId)
        && MultiplayerSupport.IsMultiplayerScope
        && !MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.BonusItems);

    public static BonusAssignment? GetOrAssign(IndexedItemInfo receipt, Player player)
    {
        var progress = ArchipelagoClient.Progress;
        if (progress.BonusRelicAssignments.TryGetValue(receipt.Index, out string? saved))
            return Restore(saved, receipt.Index);

        if (!Categories.TryGetValue(receipt.Item.GetUniversalItemId(), out string? category)
            || !ApPlayerContextResolver.TryGetRewardSettings(player, out var settings))
            return null;
        // The Nth copy of a category unlocks the Nth YAML entry of that category.
        int ordinal = progress.AllReceivedItems.Count(other =>
            other.Index < receipt.Index && other.Item.ItemId == receipt.Item.ItemId);
        var definitions = settings.BonusItemsFor(category);
        if (ordinal >= definitions.Count)
            return null;

        BonusItemDefinition definition = definitions[ordinal];
        BonusAssignment? assignment = category switch
        {
            BonusItemDefinition.WaxRelicCategory => AssignRelic(definition, player, ordinal, category, isWax: true),
            BonusItemDefinition.RelicCategory => AssignRelic(definition, player, ordinal, category, isWax: false),
            BonusItemDefinition.EnchantmentCategory => AssignEnchantment(definition, player, ordinal),
            BonusItemDefinition.EnchantedCardsCategory => AssignEnchantedCards(definition, player, ordinal),
            BonusItemDefinition.MaxHpCategory => AssignMaxHp(definition),
            BonusItemDefinition.CardsCategory => AssignCards(definition, player, ordinal),
            _ => null,
        };
        if (assignment == null)
            return null;

        progress.BonusRelicAssignments[receipt.Index] = assignment.Relic != null
            ? JsonSerializer.Serialize(assignment.Relic.ToSerializable(), SerializationUtility.CombinedOptions)
            : assignment.Payload!.ToJson();
        LogUtility.Info(
            $"Assigned bonus {category} #{ordinal}: player={player.NetId}, receipt={receipt.Index}, "
                + $"{(assignment.Relic != null ? assignment.Relic.Id.ToString() : progress.BonusRelicAssignments[receipt.Index])}");
        return assignment;
    }

    private static BonusAssignment Restore(string saved, int receiptIndex)
    {
        BonusRewardPayload? payload = BonusRewardPayload.TryParse(saved);
        if (payload != null)
            return new BonusAssignment(null, payload);

        var serialized = JsonSerializer.Deserialize<SerializableRelic>(saved, SerializationUtility.CombinedOptions)
            ?? throw new InvalidOperationException($"Invalid bonus assignment at receipt {receiptIndex}.");
        return new BonusAssignment(RelicModel.FromSerializable(serialized), null);
    }

    private static BonusAssignment? AssignRelic(
        BonusItemDefinition definition, Player player, int ordinal, string category, bool isWax)
    {
        RelicModel? selected;
        if (definition.HasExplicitValue)
        {
            selected = BonusRelicResolver.ResolveValue(definition.Value!, rejectPickupEffectRelics: isWax);
            if (selected != null && !selected.IsAllowed(player.RunState))
                selected = null;
        }
        else
        {
            selected = BonusRelicResolver.BuildPoolCandidates(definition.Pools, rejectPickupEffectRelics: isWax)
                .Where(relic => relic.IsAllowed(player.RunState))
                .Where(relic => relic.Pool == player.Character.RelicPool
                    || !ModelDb.CharacterRelicPools.Contains(relic.Pool))
                .OrderBy(relic => BonusRewardSelectionKey.Create(
                    player.RunState.Rng.StringSeed, ordinal, relic.Id.ToString(), PlayerSlotIndex(player), category),
                    StringComparer.Ordinal)
                .FirstOrDefault();
        }
        if (selected == null)
            return null;

        RelicModel mutable = selected.ToMutable();
        mutable.IsWax = isWax;
        return new BonusAssignment(mutable, null);
    }

    /// <summary>
    /// Picks the enchantment once. From Pools, it skips any that no card in the deck can take right
    /// now, so a Sharp is not handed to a deck without Attacks.
    /// </summary>
    private static BonusAssignment? AssignEnchantment(BonusItemDefinition definition, Player player, int ordinal)
    {
        IReadOnlyList<BonusEnchantment> candidates = BonusEnchantmentCatalog.Candidates(definition);
        BonusEnchantment? chosen = candidates
            .OrderBy(entry => !player.Deck.Cards.Any(entry.Canonical().CanEnchant))
            .ThenBy(entry => BonusRewardSelectionKey.Create(
                player.RunState.Rng.StringSeed, ordinal, entry.Name, PlayerSlotIndex(player),
                BonusItemDefinition.EnchantmentCategory), StringComparer.Ordinal)
            .FirstOrDefault();
        return chosen == null
            ? null
            : new BonusAssignment(null, new BonusRewardPayload
            {
                Category = BonusItemDefinition.EnchantmentCategory,
                Enchantment = chosen.Name,
                Amount = chosen.Amount,
            });
    }

    /// <summary>
    /// Rolls three cards from the character's pool and enchants each with a different enchantment
    /// from the entry where it can. With a named Value, only cards that can take it are offered.
    /// </summary>
    private static BonusAssignment? AssignEnchantedCards(BonusItemDefinition definition, Player player, int ordinal)
    {
        IReadOnlyList<BonusEnchantment> candidates = BonusEnchantmentCatalog.Candidates(definition);
        if (candidates.Count == 0)
            return null;

        Rng rng = CreateRng(player, ordinal, BonusItemDefinition.EnchantedCardsCategory);
        Func<CardModel, bool>? filter = candidates.Count == 1 ? candidates[0].Canonical().CanEnchant : null;
        CardCreationOptions options = new CardCreationOptions(
                [player.Character.CardPool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter, filter)
            .WithFlags(CardCreationFlags.IsCardReward)
            .WithRngOverride(rng);
        List<CardModel> cards = CardFactory.CreateForReward(player, EnchantedCardCount, options)
            .Select(result => result.Card)
            .ToList();

        var used = new HashSet<string>();
        foreach (CardModel card in cards)
        {
            List<BonusEnchantment> fitting = candidates.Where(entry => entry.Canonical().CanEnchant(card)).ToList();
            List<BonusEnchantment> fresh = fitting.Where(entry => !used.Contains(entry.Name)).ToList();
            List<BonusEnchantment> pool = fresh.Count > 0 ? fresh : fitting;
            if (pool.Count == 0)
                continue;
            BonusEnchantment chosen = pool[rng.NextInt(pool.Count)];
            used.Add(chosen.Name);
            CardCmd.Enchant(chosen.Canonical().ToMutable(), card, chosen.Amount);
        }

        return new BonusAssignment(null, new BonusRewardPayload
        {
            Category = BonusItemDefinition.EnchantedCardsCategory,
            Cards = SerializeAndRelease(cards, player),
        });
    }

    /// <summary>Rolls the cards of a CARDS bonus from its pools, or offers the one card it names.</summary>
    private static BonusAssignment? AssignCards(BonusItemDefinition definition, Player player, int ordinal)
    {
        Rng rng = CreateRng(player, ordinal, BonusItemDefinition.CardsCategory);
        List<CardModel> cards = BonusCardPools.Roll(definition, player, rng);
        if (cards.Count == 0)
            return null;
        return new BonusAssignment(null, new BonusRewardPayload
        {
            Category = BonusItemDefinition.CardsCategory,
            Cards = SerializeAndRelease(cards, player),
            Label = BonusCardPools.Label(definition),
        });
    }

    private static List<string> SerializeAndRelease(List<CardModel> cards, Player player)
    {
        List<string> serialized = cards
            .Select(card => JsonSerializer.Serialize(card.ToSerializable(), SerializationUtility.CombinedOptions))
            .ToList();
        // The reward loads its own copies from this payload, as multiplayer replicas do, so these
        // generated originals must not stay registered in the run.
        if (player.RunState is RunState runState)
        {
            foreach (CardModel card in cards)
                runState.RemoveCard(card);
        }
        return serialized;
    }

    private static BonusAssignment? AssignMaxHp(BonusItemDefinition definition)
    {
        if (!int.TryParse(definition.Value, out int amount) || amount <= 0)
        {
            LogUtility.Error($"Bonus max HP value '{definition.Value}' is not a positive number");
            return null;
        }
        return new BonusAssignment(null, new BonusRewardPayload
        {
            Category = BonusItemDefinition.MaxHpCategory,
            Amount = amount,
        });
    }

    private static int? PlayerSlotIndex(Player player) =>
        MultiplayerSupport.IsRealMultiplayerRun ? player.RunState.GetPlayerSlotIndex(player) : null;

    private static Rng CreateRng(Player player, int ordinal, string category)
    {
        string material = BonusRewardSelectionKey.Create(
            player.RunState.Rng.StringSeed, ordinal, "rng", PlayerSlotIndex(player), category);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        // The main build takes a 32-bit seed and the beta build a wider one; both accept this.
        return new Rng(BinaryPrimitives.ReadUInt32LittleEndian(digest));
    }
}
