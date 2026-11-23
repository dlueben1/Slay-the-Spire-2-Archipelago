using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Torii boon, from the first game: unblocked attack damage of 5 or less is reduced to 1. It
/// works before Tungsten Rod's reduction, so with both a small hit costs nothing, as in the first
/// game. Only attacks from other creatures count, not the player's own HP loss.
/// <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApTorii : ApGameArtRelic
{
    private const decimal LargestReducedHit = 5m;

    protected override string ArtName => "torii";

    public override decimal ModifyHpLostBeforeOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature || dealer == null || dealer == Owner.Creature || !props.IsPoweredAttack())
            return amount;
        return amount is > 1m and <= LargestReducedHit ? 1m : amount;
    }

    public override Task AfterModifyingHpLostBeforeOsty()
    {
        Flash();
        return Task.CompletedTask;
    }
}
