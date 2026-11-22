using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Entities.RestSite;

namespace StS2AP.Models;

/// <summary>
/// Campfires offer a Brew option that gives a random potion and leaves the campfire's other options
/// available, the way Miniature Tent keeps them. Once per campfire, while a potion slot is free.
/// Singleplayer only.
/// </summary>
public sealed class ApCampfireBrewing : ApRunModifier
{
    // Set by the Brew option just before the rest site asks whether to clear its other options.
    private bool _keepRemainingOptions;

    protected override string IconModifierEntry => "all_star";

    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options)
    {
        options.Add(new ApBrewOption(player, this));
        return true;
    }

    public override bool ShouldDisableRemainingRestSiteOptions(Player player)
    {
        if (!_keepRemainingOptions)
            return true;
        _keepRemainingOptions = false;
        return false;
    }

    internal void KeepRemainingOptions() => _keepRemainingOptions = true;
}

/// <summary>The Brew campfire option that Campfire Brewing adds.</summary>
public sealed class ApBrewOption(Player owner, ApCampfireBrewing boon) : ApBoonRestSiteOption(owner)
{
    public override string OptionId => "AP_BREW";

    protected override string BorrowedIconEntry => "cook";

    public override bool IsFreeAction => true;

    public override bool IsEnabled => Owner.HasOpenPotionSlots;

    public override async Task<bool> OnSelect()
    {
        if (!Owner.HasOpenPotionSlots)
            return false;
        PotionModel potion = PotionFactory.CreateRandomPotionOutOfCombat(Owner, Owner.PlayerRng.Rewards).ToMutable();
        PotionProcureResult result = await PotionCmd.TryToProcure(potion, Owner);
        if (!result.success)
            return false;
        boon.KeepRemainingOptions();
        return true;
    }
}
