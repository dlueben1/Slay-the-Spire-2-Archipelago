using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Entities.RestSite;

/// <summary>A generic rest-site option representing one Archipelago Campfire check.</summary>
public sealed class ApRestSiteOption : RestSiteOption
{
    private readonly long _locationId;
    private readonly string _locationName;

    public ApRestSiteOption(Player owner, long locationId, string locationName) : base(owner)
    {
        _locationId = locationId;
        _locationName = locationName;
    }

    // Only the owning player's replica has authoritative scout data for this AP slot. Presentation
    // may therefore differ between replicas, while the stable location ID below remains the
    // synchronized option identity.
    public override string OptionId
    {
        get
        {
            if (!TryGetLocalScoutedItem(out ScoutedItemInfo item))
                return "FILLER";
            if (item.Trap())
                return "TRAP";
            if (item.Advancement())
                return "PROGRESSION";
            return item.Useful() ? "USEFUL" : "FILLER";
        }
    }

    public override LocString Description
    {
        get
        {
            var description = new LocString("rest_site_ui", "OPTION_CHECK.description");
            string displayText = TryGetLocalScoutedItem(out ScoutedItemInfo item)
                ? $"{item.ItemDisplayName} for {item.Player.Name}"
                : _locationName;
            description.Add("description", displayText);
            return description;
        }
    }

    public override IEnumerable<string> AssetPaths =>
        base.AssetPaths
            .Concat(NRestSmokeVfx.AssetPaths)
            .Concat(NDesaturateTransitionVfx.AssetPaths);

    public override Task<bool> OnSelect()
    {
        // RestSiteSynchronizer invokes this on every replica. Only the process that owns the AP
        // check mutates the AP slot; all replicas report native success for the same option index.
        if (!MultiplayerSupport.IsRealMultiplayerRun)
        {
            ArchipelagoClient.Progress.CheckedCampfireLocationIds.Add(_locationId);
            GameUtility.QueueCheck(_locationId);
        }
        else if (MultiplayerLocationChecks.IsCheckWriter(Owner))
        {
            ArchipelagoClient.Progress.CheckedCampfireLocationIds.Add(_locationId);
            MultiplayerLocationChecks.QueueCheck(Owner, _locationName, _locationId);
            if (!MultiplayerLocationChecks.PublishEffectiveCheckProgress(Owner))
            {
                LogUtility.Error(
                    $"Campfire check {_locationId} was queued, but its AP progress could not be published"
                );
            }
        }

        return Task.FromResult(true);
    }

    // RestSiteOption compares by OptionId and owner. AP needs a distinct hover/index identity for
    // each generic option belonging to the same player.
    public override bool Equals(object? obj) =>
        obj is ApRestSiteOption other
        && other._locationId == _locationId
        && Owner == other.Owner;

    public override int GetHashCode() => (_locationId, Owner).GetHashCode();

    private bool TryGetLocalScoutedItem(out ScoutedItemInfo item)
    {
        item = null!;
        if (MultiplayerSupport.IsRealMultiplayerRun
            && Owner.NetId != RunManager.Instance.NetService.NetId)
        {
            return false;
        }

        if (!ArchipelagoClient.ScoutedLocations.TryGetValue(
                _locationId,
                out ScoutedItemInfo? resolved
            )
            || resolved == null)
        {
            return false;
        }

        item = resolved;
        return true;
    }
}
