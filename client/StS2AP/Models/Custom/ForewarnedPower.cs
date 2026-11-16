using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Intent Sight bonus, given at the start of each combat by <c>Patches_IntentSightBonus</c>.
/// When enemies attack on their turn and none of it gets through your Block, you draw 1 more card
/// next turn. Damage that Block stops still reaches this hook, so a fully blocked hit counts as an
/// attack. A turn with no attack gives nothing.
/// </summary>
[RegisterPower]
public sealed class ForewarnedPower : ModPowerTemplate
{
    private const int CardsDrawn = 1;

    private bool _attackedThisEnemyTurn;
    private bool _attackGotThrough;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile { get; } = new(
        IconPath: "res://images/cards/useful.png",
        BigIconPath: "res://images/cards/useful.png");

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Enemy)
        {
            _attackedThisEnemyTurn = false;
            _attackGotThrough = false;
        }
        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        // Move marks damage from an enemy's attack intent, not from Thorns-like powers.
        if (target != Owner || dealer is not { IsEnemy: true } || !props.HasFlag(ValueProp.Move))
            return Task.CompletedTask;
        _attackedThisEnemyTurn = true;
        if (result.UnblockedDamage > 0)
            _attackGotThrough = true;
        return Task.CompletedTask;
    }

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || !_attackedThisEnemyTurn || _attackGotThrough || Owner.IsDead)
            return;
        Flash();
        // The game's own next-turn draw, which counts toward the player's coming hand draw.
        await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, Owner, CardsDrawn, Owner, null);
    }
}
