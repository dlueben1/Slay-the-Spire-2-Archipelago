using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace StS2AP.Entities.RestSite;

/// <summary>
/// Do Nothing: lets the player leave an AP rest site without resting or smithing. Campfire Sanity
/// always offers it, which also covers progression locks or disabled actions leaving no other choice.
/// </summary>
public sealed class FakeRestSiteOption : RestSiteOption
{
    public FakeRestSiteOption(Player owner) : base(owner) { }

    public override string OptionId => "NOTHING";

    public override LocString Description =>
        new("rest_site_ui", "OPTION_NOTHING.descriptionDisabled");

    public override Task<bool> OnSelect() => Task.FromResult(true);
}
