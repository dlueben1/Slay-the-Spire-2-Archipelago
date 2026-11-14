using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;

namespace StS2AP.Entities.RestSite;

/// <summary>
/// A campfire option added by a Boon. It shows a base-game rest-site icon instead of shipping its
/// own: the game builds an option's icon path from its ID, so
/// <c>Patches_BoonRestSiteIcons</c> swaps in the borrowed one.
/// </summary>
public abstract class ApBoonRestSiteOption(Player owner) : RestSiteOption(owner)
{
    /// <summary>The base-game rest-site icon to show, such as "kindle" for option_kindle.png.</summary>
    protected abstract string BorrowedIconEntry { get; }

    /// <summary>
    /// Whether choosing this option leaves the campfire's other options available. The Boon that
    /// adds a free option keeps them through ShouldDisableRemainingRestSiteOptions.
    /// </summary>
    public virtual bool IsFreeAction => false;

    public string BorrowedIconPath =>
        ImageHelper.GetImagePath($"ui/rest_site/option_{BorrowedIconEntry}.png");

    public override IEnumerable<string> AssetPaths => [BorrowedIconPath];
}
