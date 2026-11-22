using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Models;

/// <summary>
/// Cards can be upgraded any number of times, as Dingy Dagger can in the base game. Each upgrade
/// re-applies the card's own upgrade, so Strike gains 3 damage every time and a cost reduction
/// stacks until the card is free. Upgrades that only add or remove a keyword change nothing after
/// the first. Cards that cannot be upgraded at all (curses, statuses) are unchanged.
/// The cap itself is lifted by <see cref="StS2AP.Patches.Patches_EndlessUpgrades"/>.
/// </summary>
public sealed class ApEndlessUpgrades : ApRunModifier
{
    /// <summary>
    /// The upgrade cap for cards in a run with this boon. Multiplayer messages carry a card's
    /// upgrade level in 8 bits, so it stays at 255 rather than Dingy Dagger's 999,999,999.
    /// </summary>
    public const int MaxUpgradeLevel = 255;

    protected override string IconModifierEntry => "specialized";

    /// <summary>Whether this card belongs to a run with the boon.</summary>
    public static bool AppliesTo(CardModel card) =>
        card.RunState?.Modifiers.Any(modifier => modifier is ApEndlessUpgrades) ?? false;
}
