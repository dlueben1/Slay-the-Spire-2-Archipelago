using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;
// could've made the trap this turn but then the hooks would be a nightmare for multiplayer
// and whispering earring so just make it next turn
/// <summary>
/// Schedules one additional Vakuu for next turn. Whispering Earring keeps its own first-turn hook;
/// this power neither equips the relic nor grants its energy bonus.
/// </summary>
[RegisterPower]
public sealed class VakuuTrapPower : ModPowerTemplate
{
    private int _activationTurn;
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile { get; } = new(
        IconPath: "res://images/cards/trap.png",
        BigIconPath: "res://images/cards/trap.png");

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        _activationTurn = Math.Max(2, (Owner.Player?.PlayerCombatState?.TurnNumber ?? 1) + 1);
        return Task.CompletedTask;
    }

    public override async Task AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player || player.PlayerCombatState is not { } playerCombat
            || playerCombat.TurnNumber < _activationTurn)
            return;

        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return;
        Flash();
        // Spend one stack even if the hand is unplayable; remaining stacks run on later turns.
        _activationTurn = playerCombat.TurnNumber + 1;
        await PowerCmd.Decrement(this);
        using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
        {
            int startTurn = playerCombat.TurnNumber;
            // Match Whispering Earring's resource spending, selection and safety bound.
            for (int played = 0; played < WhisperingEarring.maxCardsToPlay; played++)
            {
                if (CombatManager.Instance.IsOverOrEnding
                    || CombatManager.Instance.IsPlayerReadyToEndTurn(player)
                    || playerCombat.TurnNumber != startTurn)
                    break;
                var card = PileType.Hand.GetPile(player).Cards.FirstOrDefault(c => c.CanPlay());
                if (card == null)
                    break;
                var target = card.TargetType switch
                {
                    TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
                    TargetType.AnyAlly => player.RunState.Rng.CombatTargets.NextItem(
                        combatState.Allies.Where(c => c != null && c.IsAlive && c.IsPlayer && c != player.Creature)),
                    TargetType.AnyPlayer => player.Creature,
                    _ => null,
                };
                await card.SpendResources();
                await CardCmd.AutoPlay(choiceContext, card, target, AutoPlayType.Default, skipXCapture: true);
            }
        }
    }
}
