using MegaCrit.Sts2.Core.Models.Modifiers;

namespace StS2AP.Models;

/// <summary>
/// A random curse at the start of Act 2 and of Act 3. It reuses the base game's Cursed Run, which
/// also curses the start of Act 1; this version skips Act 1.
/// </summary>
public sealed class ApMountingCurses : CursedRun
{
    protected override string IconPath => ApRunModifiers.ModifierIconPath("cursed_run");

    public override Task AfterActEntered() =>
        RunState.CurrentActIndex == 0 ? Task.CompletedTask : base.AfterActEntered();
}
