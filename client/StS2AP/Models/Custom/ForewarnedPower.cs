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
/// Each enemy is judged on its own: for every enemy that attacked on the enemy turn and got none
/// of it through your Block, you draw 1 more card next turn. Damage that Block
/// stops still reaches this hook, so a fully blocked hit counts as an attack. An enemy that
/// doesn't attack gives nothing.
/// </summary>
[RegisterPower]
public sealed class ForewarnedPower : ModPowerTemplate
{
    private const int CardsPerBlockedEnemy = 1;

    // Each enemy that attacked this enemy turn, and whether any of its damage got through.
    private readonly Dictionary<Creature, bool> _attackGotThrough = new();

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile { get; } = new(
        IconPath: "res://images/cards/useful.png",
        BigIconPath: "res://images/cards/useful.png");

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Enemy)
            _attackGotThrough.Clear();
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
        bool gotThrough = result.UnblockedDamage > 0;
        _attackGotThrough[dealer] = _attackGotThrough.GetValueOrDefault(dealer) || gotThrough;
        return Task.CompletedTask;
    }

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || Owner.IsDead)
            return;
        int blockedEnemies = _attackGotThrough.Count(entry => !entry.Value);
        if (blockedEnemies == 0)
            return;
        Flash();
        // The game's own next-turn draw, which counts toward the player's coming hand draw.
        await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, Owner, CardsPerBlockedEnemy * blockedEnemies, Owner, null);
    }
}
