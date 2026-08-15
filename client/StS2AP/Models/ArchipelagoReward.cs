using System.Collections.Generic;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.TestSupport;
using StS2AP.Utils;
using System.Threading.Tasks;

namespace StS2AP.Models;

/// <summary>
/// Represents an Archipelago Location as a reward.
/// Clicking it sends the location to the multiworld.
/// </summary>
public class ArchipelagoReward : Reward
{
    // This fixes some weird error introduced at some point. Maybe this PR
    // But essentially multiple images used the same UID and caching things didn't work
    // this is the simplest fix I could find to solve the caching/lifetime issues. 
    private const string ApIconPath = "res://images/APIcon.png";
    private static ImageTexture? _rewardIconTexture;

    #region Archipelago Data

    /// <summary>
    /// The ID of the Location in the Multiworld
    /// </summary>
    private readonly long _locationId;

    /// <summary>
    /// The Location Info for this Location, which contains the name and item received from this Location.
    /// </summary>
    private readonly ScoutedItemInfo? _location;

    /// <summary>
    /// Whether or not the item has already been checked, which controls whether or not the reward appears as "claimed"
    /// in the UI and whether or not clicking it will do anything.
    /// </summary>
    public bool IsChecked => _isChecked;
    private readonly bool _isChecked;

    #endregion

    #region Reward Overrides

    protected override RewardType RewardType => RewardType.None;

    public override int RewardsSetIndex => 8;

    public override LocString Description => GetDescription();

    public override bool IsPopulated => true;

    #endregion

    protected override string? IconPath => ApIconPath;

    /// <summary>
    /// Uses an AP-owned copy of the icon instead of Reward.CreateIcon's room-scoped
    /// PreloadManager cache. That cache disposes assets it loaded on demand at the
    /// next room transition, while RitsuLib retains the same source texture for the
    /// AP top-bar hover tip. Basically the game runs fine but this is for suppressing Godot
    /// Engine errors.
    /// </summary>
    public override Control? CreateIcon()
    {
        if (TestMode.IsOn)
        {
            return null;
        }

        if (_rewardIconTexture == null || !GodotObject.IsInstanceValid(_rewardIconTexture))
        {
            using Texture2D sourceTexture = ResourceLoader.Load<Texture2D>(
                ApIconPath,
                cacheMode: ResourceLoader.CacheMode.Ignore
            );
            using Image sourceImage = sourceTexture.GetImage();
            _rewardIconTexture = ImageTexture.CreateFromImage(sourceImage);
        }

        var textureRect = new TextureRect
        {
            Texture = _rewardIconTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        textureRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return textureRect;
    }

    /// <summary>
    /// Constructor that takes in the name of a Location
    /// </summary>
    /// <param name="locationName">The name of the AP Location we're giving a reward for</param>
    public ArchipelagoReward(string locationName) : base(GameUtility.CurrentPlayer)
    {
        // Try and find this location
        _locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", locationName);
        if(!ArchipelagoClient.ScoutedLocations.TryGetValue(_locationId, out _location))
        {
            LogUtility.Warn($"Could not find scouted info for {locationName}");
        }

        // If it's already been found, keep track of it
        _isChecked = ArchipelagoClient.CheckedLocations.Contains(_locationId);
        if(_isChecked)
        {
            // And update the name of the location
            var key = $"AP_LOC_{_locationId}";
            TextUtility.RegisterLocString(key, $"{_location?.ItemDisplayName ?? locationName} (Claimed)", "ap");
        }
    }

    public LocString GetDescription()
    {
        return new LocString("ap", $"AP_LOC_{_locationId}");
    }

    public override void OnSkipped()
    {
        return;
    }

    /// <summary>
    /// Required to override, but there's nothing to do here, so it's a no-op...
    /// </summary>
    public override void Populate() { }

    protected override async Task<bool> OnSelect()
    {
        // Handle reward selection logic
        if (!ArchipelagoClient.CheckedLocations.Contains(_locationId))
        {
            // Check the location off and let the server know
            GameUtility.SendCheck(_locationId);

            LogUtility.Success($"Sent location check: {_locationId}");
        }
        return true;
    }

    public override void MarkContentAsSeen()
    {
        return;
    }
}
