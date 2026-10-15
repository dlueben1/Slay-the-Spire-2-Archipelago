using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace StS2AP.Entities.RestSite;

/// <summary>Provides a way out when progression locks or disabled actions leave no usable choice.</summary>
public sealed class FakeRestSiteOption : RestSiteOption
{
    public FakeRestSiteOption(Player owner) : base(owner) { }

    public override string OptionId => "NOTHING";

    public override LocString Description =>
        new("rest_site_ui", "OPTION_NOTHING.descriptionDisabled");

    public override Task<bool> OnSelect() => Task.FromResult(true);
}
