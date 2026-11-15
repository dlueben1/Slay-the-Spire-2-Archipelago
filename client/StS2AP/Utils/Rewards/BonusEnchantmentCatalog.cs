using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using StS2AP.Models.Enchantments;
using StS2AP.Multiplayer;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>One enchantment that ENCHANTMENT and ENCHANTED_CARDS bonus items can give.</summary>
/// <param name="Name">The YAML name, the enchantment's class name (for example RoyallyApproved).</param>
/// <param name="Tier">The YAML pool that rolls it, or null when it may only be named as a Value.</param>
/// <param name="Amount">The badge amount it is applied with.</param>
/// <param name="SingleplayerOnly">Not rolled in multiplayer, where it would do nothing.</param>
internal sealed record BonusEnchantment(
    string Name, string? Tier, int Amount, Func<EnchantmentModel> Canonical, bool SingleplayerOnly = false);

/// <summary>
/// The enchantments bonus items may give, with their tiers and amounts. The tiers match
/// BONUS_ENCHANTMENT_TIERS in the APWorld's items.py, which validates YAML entries, and the web
/// builder's copy. Amounts start from the game's own Blitz shop offers (Sharp 2, Nimble 3, Adroit 3,
/// Steady 1, Swift 2, Vigorous 5); the rest use the amounts the game's own relics and events give.
/// Tezcatara's Ember is never offered.
/// </summary>
internal static class BonusEnchantmentCatalog
{
    public const string Common = "Common";
    public const string Uncommon = "Uncommon";
    public const string Rare = "Rare";

    public static readonly IReadOnlyList<BonusEnchantment> All =
    [
        Entry<Sharp>(Common, 2),
        Entry<Nimble>(Common, 3),
        Entry<Adroit>(Common, 3),
        Entry<Steady>(Common, 1),
        Entry<Swift>(Common, 2),
        Entry<Vigorous>(Common, 5),
        Entry<Inky>(Uncommon, 1),
        Entry<Momentum>(Uncommon, 2),
        Entry<RoyallyApproved>(Uncommon, 1),
        Entry<Goopy>(Uncommon, 1),
        // The mod's own enchantments (Models/Custom/Enchantments).
        Entry<ApEager>(Uncommon, 1, "Eager"),
        Entry<ApExposing>(Uncommon, 1, "Exposing"),
        Entry<Sown>(Rare, 1),
        Entry<Glam>(Rare, 1),
        Entry<Imbued>(Rare, 1),
        Entry<SoulsPower>(Rare, 1),
        Entry<Instinct>(Rare, 1),
        Entry<Spiral>(Rare, 1),
        // Its bonus needs the AP session, which multiplayer guests do not have.
        Entry<ApCheckbound>(Rare, 1, "Checkbound", singleplayerOnly: true),
        // Allowed only by name: they come with a drawback, or need a rest site (Clone).
        Entry<Corrupted>(null, 1),
        Entry<Slither>(null, 1),
        Entry<SlumberingEssence>(null, 1),
        Entry<PerfectFit>(null, 1),
        Entry<Clone>(null, 4),
    ];

    private static BonusEnchantment Entry<T>(
        string? tier, int amount, string? name = null, bool singleplayerOnly = false) where T : EnchantmentModel =>
        new(name ?? typeof(T).Name, tier, amount, ModelDb.Enchantment<T>, singleplayerOnly);

    /// <summary>Matches 'RoyallyApproved', 'royally approved' and 'ROYALLY_APPROVED' alike.</summary>
    public static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public static BonusEnchantment? FindByName(string name) =>
        All.FirstOrDefault(entry => Normalize(entry.Name) == Normalize(name));

    public static BonusEnchantment? FindById(ModelId id) =>
        All.FirstOrDefault(entry => entry.Canonical().Id == id);

    /// <summary>The enchantments a YAML entry may give: its named Value, or every one in its Pools.</summary>
    public static IReadOnlyList<BonusEnchantment> Candidates(BonusItemDefinition definition)
    {
        if (definition.HasExplicitValue)
        {
            BonusEnchantment? named = FindByName(definition.Value!);
            if (named == null)
                LogUtility.Error($"Bonus enchantment '{definition.Value}' is not one bonus items can give");
            return named == null ? [] : [named];
        }

        return All
            .Where(entry => entry.Tier != null
                && definition.Pools.Any(pool => string.Equals(pool, entry.Tier, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => !entry.SingleplayerOnly || !MultiplayerSupport.IsMultiplayerScope)
            .ToList();
    }
}
