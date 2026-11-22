using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace StS2AP.Models.Enchantments;

/// <summary>Attacks apply Vulnerable to what they hit, the way the game's Inky applies Weak.</summary>
public sealed class ApExposing : EnchantmentModel
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<VulnerablePower>()];

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Attack;

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay)
    {
        IReadOnlyList<Creature> targets = Card.TargetType == TargetType.AllEnemies
            ? Card.CombatState?.HittableEnemies ?? []
            : cardPlay?.Target is Creature target ? [target] : [];
        if (targets.Count == 0)
            return;
        await PowerCmd.Apply<VulnerablePower>(choiceContext, targets, Math.Max(Amount, 1), Card.Owner.Creature, Card);
    }
}
