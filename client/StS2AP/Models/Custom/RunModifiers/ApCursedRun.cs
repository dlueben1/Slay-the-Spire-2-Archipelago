using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;

namespace StS2AP.Models;

/// <summary>
/// The base game's Cursed Run, a random curse at the start of every act including Act 1, as an
/// AP type. A separate type keeps it out of the Neow check and tells it apart from the same
/// modifier in a Custom run, while the inherited behaviour, text and icon stay the base game's.
/// </summary>
public sealed class ApCursedRun : CursedRun
{
    public override LocString Title => ModelDb.Modifier<CursedRun>().Title;

    public override LocString Description => ModelDb.Modifier<CursedRun>().Description;

    protected override string IconPath =>
        ApRunModifiers.ModifierIconPath(ModelDb.Modifier<CursedRun>().Id.Entry);
}
