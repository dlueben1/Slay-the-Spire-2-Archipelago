using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;

namespace StS2AP.Models;

// The base game's own modifiers, as AP types. A separate type keeps them out of the Neow check
// and tells them apart from the same modifier in a Custom run, while the inherited behaviour,
// text and icon stay the base game's.

/// <summary>The base game's Cursed Run: a random curse at the start of each act.</summary>
public sealed class ApCursedRun : CursedRun
{
    public override LocString Title => ModelDb.Modifier<CursedRun>().Title;

    public override LocString Description => ModelDb.Modifier<CursedRun>().Description;

    protected override string IconPath =>
        ApRunModifiers.ModifierIconPath(ModelDb.Modifier<CursedRun>().Id.Entry);
}

/// <summary>
/// The base game's Deadly Events: ? rooms can hold elites and treasure appears more often.
/// </summary>
public sealed class ApDeadlyEvents : DeadlyEvents
{
    public override LocString Title => ModelDb.Modifier<DeadlyEvents>().Title;

    public override LocString Description => ModelDb.Modifier<DeadlyEvents>().Description;

    protected override string IconPath =>
        ApRunModifiers.ModifierIconPath(ModelDb.Modifier<DeadlyEvents>().Id.Entry);
}
