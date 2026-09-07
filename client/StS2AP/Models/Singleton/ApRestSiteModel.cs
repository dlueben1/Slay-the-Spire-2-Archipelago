using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using StS2AP.Data;
using StS2AP.Entities.RestSite;
using StS2AP.Utils;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace StS2AP.Models.Singleton;

/// <summary>
/// Adds AP Campfire checks and progressive Rest/Smith locks through the base game's run-model
/// hook. RestSiteSynchronizer remains the sole owner of multiplayer selection semantics.
/// </summary>
[RegisterSingleton]
public sealed class ApRestSiteModel : HookedSingletonModel
{
    public ApRestSiteModel() : base(HookType.Run) { }

    public override bool TryModifyRestSiteOptions(
        Player player,
        ICollection<RestSiteOption> options)
    {
        if (!MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.RestSites)
            || !ApPlayerContextResolver.TryGetRewardSettings(
                player,
                out ArchipelagoSettings settings
            )
            || !settings.CampfireSanity
            || !ApPlayerContextResolver.TryGetCharacterConfig(
                player,
                out CharacterConfig config
            ))
        {
            return false;
        }

        if (!TryGetProgress(
                player,
                config.CharOffset,
                out int restLevel,
                out int smithLevel,
                out IReadOnlySet<long> checkedLocations,
                out string reason
            ))
        {
            LogUtility.Error(
                $"Leaving native rest-site options unchanged for player {player.NetId}: {reason}"
            );
            return false;
        }

        var policy = new RestSitePolicy(player.RunState.CurrentActIndex + 1, restLevel, smithLevel);
        bool needsFallback = policy.ApplyLocks(
            options,
            option => option.OptionId,
            option => option.IsEnabled,
            () => new FakeRestSiteOption(player));

        if (ApPlayerContextResolver.HasCharacterChecks(player))
        {
            string characterName = config.ModNum == 0
                ? config.Name
                : $"Custom Character {config.ModNum}";
            foreach (var (act, campfire, locationId) in policy.GetAvailableChecks(
                         checkedLocations,
                         (act, campfire) => ArchipelagoIdCodec.ForPlayer(
                             LocationData.GetCampfireLocationId(config.CharOffset, act, campfire), settings.PlayerNumber)))
            {
                string locationName = $"{characterName} Act {act} Campfire {campfire}";
                options.Add(new ApRestSiteOption(player, locationId, locationName));
            }
        }

        LogUtility.Info(
            $"Applied AP rest-site options for player {player.NetId}: act={policy.CurrentAct}, "
                + $"restLevel={restLevel}, smithLevel={smithLevel}, fallback={needsFallback}"
        );
        return true;
    }

    private static bool TryGetProgress(
        Player player,
        long characterOffset,
        out int restLevel,
        out int smithLevel,
        out IReadOnlySet<long> checkedLocations,
        out string reason)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
        {
            restLevel = ArchipelagoClient.Progress.MaxRestLevel(characterOffset) ?? 0;
            smithLevel = ArchipelagoClient.Progress.MaxSmithLevel(characterOffset) ?? 0;
            checkedLocations = ArchipelagoClient.Progress.CheckedCampfireLocationIds;
            reason = string.Empty;
            return true;
        }

        restLevel = 0;
        smithLevel = 0;
        checkedLocations = new HashSet<long>();
        if (!ApPlayerContextResolver.TryGetRewardProgress(
                player,
                out var progress,
                out reason
            ))
        {
            return false;
        }

        progress.ProgressiveRests.TryGetValue(characterOffset, out restLevel);
        progress.ProgressiveSmiths.TryGetValue(characterOffset, out smithLevel);
        checkedLocations = progress.CheckedCampfireLocationIds;
        return true;
    }
}
