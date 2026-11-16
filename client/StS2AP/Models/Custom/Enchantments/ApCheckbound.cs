using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Multiplayer;
using StS2AP.Utils;

namespace StS2AP.Models.Enchantments;

/// <summary>
/// An Archipelago-only enchantment that grows with the seed: +1 damage per hit and +1 Block per
/// block gain at each of <see cref="CheckboundMilestones"/>. Milestones are a share of this
/// character's own locations, so seed size does not matter, and the cap of +6 keeps multi-hit
/// attacks in check (user decisions, 2026-11-14).
/// </summary>
public sealed class ApCheckbound : EnchantmentModel
{
    public override bool ShowAmount => true;
    public override int DisplayAmount => CurrentBonus;

    /// <summary>Only cards that deal damage or gain Block can use the bonus.</summary>
    public override bool CanEnchant(CardModel card) =>
        base.CanEnchant(card) && (card.Type == CardType.Attack || card.GainsBlock);

    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props) =>
        props.IsPoweredAttack() ? CurrentBonus : 0m;

    public override decimal EnchantBlockAdditive(decimal originalBlock) => CurrentBonus;

    private int CurrentBonus
    {
        get
        {
            // Canonical models (hover tips) have no card; they show the local player's bonus.
            Player? owner = IsMutable && HasCard ? Card.Owner : GameUtility.CurrentPlayer;
            return BonusFor(owner?.GetAPCharacterNumber());
        }
    }

    private static (long Character, int Checked, int Total, int Bonus) _cache = (-1, -1, -1, 0);

    private static int BonusFor(long? characterNumber)
    {
        // Multiplayer guests have no AP session, so every peer would compute a different
        // bonus and combat would desync. Checkbound gives nothing there for now.
        if (characterNumber is not long character || MultiplayerSupport.IsMultiplayerScope)
            return 0;

        IReadOnlySet<long> slotLocations = ArchipelagoClient.SlotLocationIds;
        HashSet<long> checkedLocations = ArchipelagoClient.CheckedLocations;
        // Damage previews ask often; recount only when the check lists change.
        if (_cache.Character == character && _cache.Checked == checkedLocations.Count
            && _cache.Total == slotLocations.Count)
            return _cache.Bonus;

        int total = slotLocations.Count(id => ArchipelagoIdCodec.GetAPCharacterNumberFromLocationId(id) == character);
        int sent = checkedLocations.Count(id =>
            slotLocations.Contains(id) && ArchipelagoIdCodec.GetAPCharacterNumberFromLocationId(id) == character);
        int bonus = CheckboundMilestones.Bonus(sent, total);
        _cache = (character, checkedLocations.Count, slotLocations.Count, bonus);
        return bonus;
    }
}
