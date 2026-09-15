using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.TestSupport;
using StS2AP.Utils;
using STS2RitsuLib.Combat.Rewards;

namespace StS2AP.Models;

/// <summary>
/// A serializable AP location reward. MegaCrit synchronizes the selected reward index; each
/// replica completes selection, while only the AP slot writer records the location check.
/// </summary>
public sealed class ArchipelagoReward : ModCustomReward
{
    private const string ApIconPath = "res://images/APIcon.png";
    private const string RewardStem = "LOCATION";
    private static ImageTexture? _rewardIconTexture;
    private static RewardType _registeredRewardType;
    private static bool _initialized;

    private readonly string _locationName;
    private readonly string _descriptionKey;
    private readonly long _locationId;
    private readonly bool _isChecked;

    public bool IsChecked => _isChecked;

    public override RewardType ModRewardType => _registeredRewardType;
    public override int RewardsSetIndex => 8;
    public override LocString Description => new("ap", _descriptionKey);
    protected override string? RewardIconPath => ApIconPath;

    public static void Initialize()
    {
        if (_initialized)
            return;

        ModRewardDefinition definition = ModRewardRegistry.For(ModEntry.ModId).RegisterOwned(
            RewardStem,
            (_, player, json) =>
            {
                string? locationName = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<string>(json);
                return new ArchipelagoReward(
                    player,
                    locationName ?? "Unknown Archipelago Location"
                );
            }
        );
        _registeredRewardType = definition.RewardType;
        _initialized = true;
    }

    public ArchipelagoReward(Player player, string locationName) : base(player)
    {
        _locationName = locationName;
        _locationId = MultiplayerLocationChecks.ResolveLocationId(player, locationName);
        _isChecked = MultiplayerLocationChecks.IsChecked(player, _locationId);
        _descriptionKey = BuildDescriptionKey(player.NetId, locationName);

        string displayName = locationName;
        if (_locationId != -1
            && ArchipelagoClient.ScoutedLocations.TryGetValue(_locationId, out var location))
        {
            displayName = $"{location.ItemDisplayName} for {location.Player.Name}";
        }
        if (_isChecked)
            displayName += " (Claimed)";
        TextUtility.RegisterLocString(_descriptionKey, displayName, "ap");
    }

    public override string ToModRewardJson() => JsonSerializer.Serialize(_locationName);

    public override Control? CreateIcon()
    {
        if (TestMode.IsOn)
            return null;

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

    public override void OnSkipped()
    {
    }

    protected override Task<bool> OnSelect()
    {
        MultiplayerLocationChecks.QueueCheck(Player, _locationName, _locationId);
        return Task.FromResult(true);
    }

    public override void MarkContentAsSeen()
    {
    }

    private static string BuildDescriptionKey(ulong ownerNetId, string locationName)
    {
        // Multiple multiplayer owners can produce the same numbered location name. A non-writer
        // replica cannot scout that owner's AP slot and registers the raw location name, so sharing
        // a key would overwrite the local owner's scouted item text. Keep repeated instances for
        // one owner stable while isolating each player's presentation.
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{ownerNetId}:{locationName}")
        );
        return $"AP_LOC_{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }
}
