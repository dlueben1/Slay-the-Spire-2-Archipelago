using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Turnip boon, from the first game: the player can't become Frail. Works the way Artifact
/// blocks a debuff, by bringing the amount received to 0, but without being used up.
/// <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApTurnip : ApGameArtRelic
{
    protected override string ArtName => "turnip";

    public override bool TryModifyPowerAmountReceived(
        PowerModel canonicalPower, Creature target, decimal amount, Creature? applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;
        if (canonicalPower is not FrailPower || target != Owner.Creature || amount <= 0m)
            return false;
        modifiedAmount = 0m;
        return true;
    }

    public override Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        Flash();
        return Task.CompletedTask;
    }
}
