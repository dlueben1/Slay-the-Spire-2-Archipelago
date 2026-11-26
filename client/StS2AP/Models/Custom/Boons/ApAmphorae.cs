using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Amphorae boon: at the start of each act, including the first, fill every empty potion slot
/// with a random potion, as Delicate Frond does before each fight. Loading a save does not enter
/// the act again, so a reload cannot refill the slots. <see cref="ApRunModifiers"/> grants it at
/// run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApAmphorae : ApGameArtRelic
{
    protected override string ArtName => "amphorae";

    public override async Task AfterActEntered()
    {
        if (!Owner.HasOpenPotionSlots)
            return;
        Flash();
        while (Owner.HasOpenPotionSlots)
        {
            // The same roll Campfire Brewing uses, so every multiplayer peer makes the same potions.
            PotionModel potion = PotionFactory.CreateRandomPotionOutOfCombat(Owner, Owner.PlayerRng.Rewards).ToMutable();
            if (!(await PotionCmd.TryToProcure(potion, Owner)).success)
                break;
        }
    }
}
