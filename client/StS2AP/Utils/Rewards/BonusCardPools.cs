using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// One <c>Pools</c> entry of a CARDS bonus, such as "Regent Attack" or "Colorless Rare Upgraded":
/// at most one source, card type and rarity. With no source, cards come from the run's own
/// character. The APWorld's <c>parse_bonus_card_pool</c> accepts the same words and rejects
/// entries this parser would.
/// </summary>
internal sealed record BonusCardPool(
    string? Source, CardType? Type, CardRarity? Rarity, bool IsRandom, bool Upgraded)
{
    public const string Other = "Other";
    public const string Colorless = "Colorless";
    public const string Event = "Event";
    public const string Ancient = "Ancient";

    // Character sources by their game id. Fool exists only from game version 0.113.0; on older
    // versions it resolves to no character and its pools are skipped.
    private static readonly string[] Characters = ["Ironclad", "Silent", "Defect", "Necrobinder", "Regent", "Fool"];
    private static readonly string[] OtherSources = [Other, Colorless, Event, Ancient];

    public static readonly BonusCardPool OwnCharacter = new(null, null, null, false, false);

    public static BonusCardPool? Parse(string text)
    {
        string? source = null;
        CardType? type = null;
        CardRarity? rarity = null;
        bool random = false, upgraded = false;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string key = word.ToLowerInvariant();
            if (key == "colourless")
                key = "colorless";
            string? named = Characters.Concat(OtherSources)
                .FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (named != null && source == null)
                source = named;
            else if (Enum.TryParse(word, true, out CardType parsedType)
                     && parsedType is CardType.Attack or CardType.Skill or CardType.Power && type == null)
                type = parsedType;
            else if (Enum.TryParse(word, true, out CardRarity parsedRarity)
                     && parsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare && rarity == null)
                rarity = parsedRarity;
            else if (key == "random" && !random)
                random = true;
            else if (key == "upgraded" && !upgraded)
                upgraded = true;
            else
            {
                LogUtility.Warn($"Bonus cards pool '{text}' has an unknown or repeated word '{word}'");
                return null;
            }
        }
        if (random && (source != null || type != null || rarity != null))
        {
            LogUtility.Warn($"Bonus cards pool '{text}': Random can only be combined with Upgraded");
            return null;
        }
        return new BonusCardPool(source, type, rarity, random, upgraded);
    }

    /// <summary>The entry in its canonical spelling and order, for the reward's description.</summary>
    public string Label => IsRandom
        ? Upgraded ? "Upgraded Random" : "Random"
        : string.Join(' ', new[] { Upgraded ? "Upgraded" : null, Source, Rarity?.ToString(), Type?.ToString() }
            .Where(word => word != null)) is { Length: > 0 } label ? label : "Your Cards";

    public bool IsCharacterSource => Source is null or Other || Characters.Contains(Source);

    /// <summary>The character a named character source means, or null when this game version has none.</summary>
    public static CharacterModel? FindCharacter(string name) =>
        ModelDb.AllCharacters.FirstOrDefault(character =>
            character.IsPlayable && string.Equals(character.Id.Entry, name, StringComparison.OrdinalIgnoreCase));

    public static bool IsNamedCharacter(string? source) => source != null && Characters.Contains(source);
}

/// <summary>
/// Rolls the cards of a CARDS bonus. Each card picks one of the entry's pools; a Random pool
/// then picks a source of its own for that card. Character pools use the game's base card reward
/// rarity odds, colorless cards are half Uncommon and half Rare, and Event and Ancient cards are
/// drawn evenly. A pool with nothing left to offer falls back to fewer filters, then to the run's
/// own character, so a slot is filled whenever any card can be.
/// </summary>
internal static class BonusCardPools
{
    public const int CardCount = 3;

    private static readonly string?[] RandomSources =
        [null, BonusCardPool.Other, BonusCardPool.Colorless, BonusCardPool.Event];

    public static List<CardModel> Roll(BonusItemDefinition definition, Player player, Rng rng)
    {
        if (definition.HasExplicitValue)
        {
            CardModel? named = FindCard(definition.Value!);
            return named == null ? [] : [player.RunState.CreateCard(named, player)];
        }

        List<BonusCardPool> pools = definition.Pools
            .Select(BonusCardPool.Parse)
            .OfType<BonusCardPool>()
            .Where(pool => IsAvailable(pool, player))
            .ToList();
        if (pools.Count == 0)
        {
            LogUtility.Warn("No bonus cards pool is available in this game version; using your character's cards");
            pools.Add(BonusCardPool.OwnCharacter);
        }

        var chosen = new HashSet<CardModel>();
        var cards = new List<CardModel>();
        for (int index = 0; index < CardCount; index++)
        {
            BonusCardPool pool = pools[rng.NextInt(pools.Count)];
            if (pool.IsRandom)
                pool = pool with { IsRandom = false, Source = RandomSources[rng.NextInt(RandomSources.Length)] };

            CardModel? canonical = Fallbacks(pool)
                .Select(candidate => Pick(candidate, player, rng, chosen))
                .FirstOrDefault(card => card != null);
            if (canonical == null)
                break;
            chosen.Add(canonical);

            CardModel card = player.RunState.CreateCard(canonical, player);
            if (pool.Upgraded && card.IsUpgradable)
                CardCmd.Upgrade(card);
            cards.Add(card);
        }
        return cards;
    }

    /// <summary>The descriptions of an entry's pools, or null for an entry that names one card.</summary>
    public static string? Label(BonusItemDefinition definition) =>
        definition.HasExplicitValue
            ? null
            : string.Join(", ", definition.Pools.Select(BonusCardPool.Parse).OfType<BonusCardPool>().Select(pool => pool.Label));

    /// <summary>Matches 'Neows Fury', 'NeowsFury' and 'NEOWS_FURY' alike.</summary>
    public static CardModel? FindCard(string name)
    {
        string wanted = BonusEnchantmentCatalog.Normalize(name);
        CardModel? card = ModelDb.AllCards.FirstOrDefault(candidate =>
            BonusEnchantmentCatalog.Normalize(candidate.Id.Entry) == wanted
            || BonusEnchantmentCatalog.Normalize(candidate.GetType().Name) == wanted);
        if (card == null || card is MadScience)
            LogUtility.Error($"Bonus cards value '{name}' is not a card that can be given");
        return card is MadScience ? null : card;
    }

    private static bool IsAvailable(BonusCardPool pool, Player player) =>
        pool.IsRandom
        || !BonusCardPool.IsNamedCharacter(pool.Source)
        || BonusCardPool.FindCharacter(pool.Source!) != null;

    // Each step drops one more filter; the run's own character comes last.
    private static IEnumerable<BonusCardPool> Fallbacks(BonusCardPool pool)
    {
        yield return pool;
        yield return pool with { Rarity = null };
        yield return pool with { Rarity = null, Type = null };
        yield return pool with { Source = null };
        yield return BonusCardPool.OwnCharacter;
    }

    private static CardModel? Pick(BonusCardPool pool, Player player, Rng rng, HashSet<CardModel> chosen)
    {
        CardPoolModel? source = SourcePool(pool.Source, player, rng);
        if (source == null)
            return null;

        List<CardModel> candidates = source
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(card => FitsSource(pool.Source, card))
            .Where(card => pool.Type == null || card.Type == pool.Type)
            .Where(card => pool.Rarity == null || card.Rarity == pool.Rarity)
            .Where(card => !chosen.Contains(card))
#if STS2_AT_LEAST_0_113_0
            .Where(card => card.IsAllowedInRun(player.RunState))
#endif
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
            return null;

        CardRarity? rolled = pool.Rarity != null ? null
            : pool.IsCharacterSource ? RollCharacterRarity(rng)
            : pool.Source == BonusCardPool.Colorless ? (rng.NextInt(2) == 0 ? CardRarity.Uncommon : CardRarity.Rare)
            : null;
        if (rolled is CardRarity rarity)
            candidates = NearestRarity(candidates, rarity);
        return candidates[rng.NextInt(candidates.Count)];
    }

    private static CardPoolModel? SourcePool(string? source, Player player, Rng rng)
    {
        switch (source)
        {
            case null:
                return player.Character.CardPool;
            case BonusCardPool.Colorless:
                return ModelDb.CardPool<ColorlessCardPool>();
            case BonusCardPool.Event:
            case BonusCardPool.Ancient:
                return ModelDb.CardPool<EventCardPool>();
            case BonusCardPool.Other:
            {
                List<CardPoolModel> others = OtherCharacterPools(player);
                return others.Count == 0 ? null : others[rng.NextInt(others.Count)];
            }
            default:
                return BonusCardPool.FindCharacter(source)?.CardPool;
        }
    }

    // Every character but the run's own: the built-in ones and the modded ones the slot selected.
    private static List<CardPoolModel> OtherCharacterPools(Player player)
    {
        IEnumerable<CardPoolModel> all = CrossCharacterCardPoolUtility.TryGetPools(player, out var pools)
            ? pools
            : ModelDb.AllCharacters.Where(character => character.IsPlayable).Select(character => character.CardPool);
        return all.Where(pool => pool.Id != player.Character.CardPool.Id).ToList();
    }

    private static bool FitsSource(string? source, CardModel card) => source switch
    {
        // Tinker Time sets this card's type and effect when it builds one; a plain copy has neither.
        BonusCardPool.Event => card.Rarity == CardRarity.Event && card is not MadScience,
        BonusCardPool.Ancient => card.Rarity == CardRarity.Ancient,
        _ => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare,
    };

    // The game's card reward odds without its pity counter, so the run's own rewards are untouched.
    private static CardRarity RollCharacterRarity(Rng rng)
    {
        float roll = rng.NextFloat();
        if (roll < CardRarityOdds.RegularRareOdds)
            return CardRarity.Rare;
        return roll < CardRarityOdds.RegularRareOdds + CardRarityOdds.regularUncommonOdds
            ? CardRarity.Uncommon
            : CardRarity.Common;
    }

    // As the game does, a rarity with no cards steps up to the next one, wrapping to Common.
    private static List<CardModel> NearestRarity(List<CardModel> candidates, CardRarity rarity)
    {
        CardRarity[] order = [CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare];
        int start = Array.IndexOf(order, rarity);
        for (int step = 0; step < order.Length; step++)
        {
            CardRarity next = order[(start + step) % order.Length];
            List<CardModel> matching = candidates.Where(card => card.Rarity == next).ToList();
            if (matching.Count > 0)
                return matching;
        }
        return candidates;
    }
}
