using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Medical Kit boon, from the first game: Status cards can be played for 0 energy, and are
/// exhausted when played. <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApMedicalKit : ApPlayableCardTypeRelic
{
    protected override string ArtName => "medical_kit";

    protected override CardType PlayableType => CardType.Status;
}
