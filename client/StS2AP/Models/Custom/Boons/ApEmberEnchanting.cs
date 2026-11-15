using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Random;
using StS2AP.Entities.RestSite;
using StS2AP.Models.Singleton;

namespace StS2AP.Models;

/// <summary>
/// Campfires offer an Enchant option with an enchantment rolled for that campfire. Choosing it
/// opens the game's own enchant screen for a card of the player's choice and uses up the
/// campfire. It shares Smith's Progressive Smith lock. Singleplayer only.
/// </summary>
public sealed class ApEmberEnchanting : ApRunModifier
{
    private enum Tier { Common, Uncommon, Rare }

    private sealed record Offer(EnchantmentModel Enchantment, int Amount, Tier Tier);

    // Common amounts are the ones the base game's Blitz merchant sells. Only one enchantment fits on
    // a card, so already-enchanted cards cannot be picked.
    private static IReadOnlyList<Offer> Pool =>
    [
        new(ModelDb.Enchantment<Sharp>(), 2, Tier.Common),
        new(ModelDb.Enchantment<Nimble>(), 3, Tier.Common),
        new(ModelDb.Enchantment<Adroit>(), 3, Tier.Common),
        new(ModelDb.Enchantment<Steady>(), 1, Tier.Common),
        new(ModelDb.Enchantment<Swift>(), 2, Tier.Common),
        new(ModelDb.Enchantment<Vigorous>(), 5, Tier.Common),
        new(ModelDb.Enchantment<Inky>(), 1, Tier.Uncommon),
        new(ModelDb.Enchantment<Momentum>(), 2, Tier.Uncommon),
        new(ModelDb.Enchantment<RoyallyApproved>(), 1, Tier.Uncommon),
        new(ModelDb.Enchantment<Sown>(), 1, Tier.Rare),
        new(ModelDb.Enchantment<Glam>(), 1, Tier.Rare),
        new(ModelDb.Enchantment<Instinct>(), 1, Tier.Rare),
        new(ModelDb.Enchantment<Spiral>(), 1, Tier.Rare),
    ];

    // Out of 100: the chance that a campfire's roll comes from each tier.
    private const int CommonWeight = 60;
    private const int UncommonWeight = 30;

    protected override string IconModifierEntry => "vintage";

    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options)
    {
        if (!ApRestSiteModel.IsSmithUnlocked(player) || RollOffer(player) is not Offer offer)
            return false;
        options.Add(new ApEmberEnchantOption(player, offer.Enchantment, offer.Amount));
        return true;
    }

    /// <summary>
    /// Rolls this campfire's enchantment from those that fit at least one card in the deck. The
    /// stream is named for the floor, so a reload offers the same one and other rolls don't shift.
    /// </summary>
    private Offer? RollOffer(Player player)
    {
        List<Offer> usable = Pool
            .Where(offer => player.Deck.Cards.Any(offer.Enchantment.CanEnchant))
            .ToList();
        if (usable.Count == 0)
            return null;

        var rng = new Rng(RunState.Rng.Seed, $"ap_ember_enchanting_{RunState.TotalFloor}");
        int roll = rng.NextInt(100);
        Tier tier = roll < CommonWeight ? Tier.Common
            : roll < CommonWeight + UncommonWeight ? Tier.Uncommon
            : Tier.Rare;
        // Fall back to any usable enchantment when the rolled tier has none that fit.
        List<Offer> candidates = usable.Where(offer => offer.Tier == tier).ToList();
        if (candidates.Count == 0)
            candidates = usable;
        return candidates[rng.NextInt(candidates.Count)];
    }
}

/// <summary>The Enchant campfire option that Ember Enchanting adds.</summary>
public sealed class ApEmberEnchantOption(Player owner, EnchantmentModel enchantment, int amount)
    : ApBoonRestSiteOption(owner)
{
    public override string OptionId => "AP_EMBER_ENCHANT";

    protected override string BorrowedIconEntry => "kindle";

    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            string name = enchantment.Title.GetFormattedText();
            description.Add("Enchantment", enchantment.ShowAmount ? $"{name} {amount}" : name);
            return description;
        }
    }

    public override async Task<bool> OnSelect()
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        CardModel? card = (await CardSelectCmd.FromDeckForEnchantment(Owner, enchantment, amount, prefs))
            .FirstOrDefault();
        // Cancelling returns false, which leaves the campfire's options as they were.
        if (card == null)
            return false;
        CardCmd.Enchant(enchantment.ToMutable(), card, amount);
        CardCmd.Preview(card);
        return true;
    }
}
