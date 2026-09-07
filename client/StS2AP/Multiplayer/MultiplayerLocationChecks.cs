using Archipelago.MultiClient.Net;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Utils;

namespace StS2AP.Multiplayer;

/// <summary>
/// Resolves the AP slot which owns a player's location checks. Each AP participant uses their
/// staged settings and local AP connection, including when sharing a slot. Vanilla guests keep
/// native behavior.
/// </summary>
public static class MultiplayerLocationChecks
{
    public static bool TryGetCheckSettings(Player player, out ArchipelagoSettings settings)
    {
        if (!ApPlayerContextResolver.HasCharacterChecks(player))
        {
            settings = null!;
            return false;
        }

        return ApPlayerContextResolver.TryGetRewardSettings(player, out settings);
    }

    public static bool IsLocalProgressOwner(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return player == GameUtility.CurrentPlayer;

        try
        {
            return LocalContext.IsMe(player);
        }
        catch
        {
            return false;
        }
    }

    public static void PublishLocalProgress(Player player)
    {
        if (IsLocalProgressOwner(player))
            ApRunData.PublishLocalProgress(player);
    }

    /// <summary>
    /// Publishes the local player's canonical check progress for subsequent replicated construction.
    /// </summary>
    public static bool PublishEffectiveCheckProgress(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return true;
        if (!ApPlayerContextResolver.HasCharacterChecks(player))
            return false;
        if (player.RunState is not RunState runState
            || !ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state))
        {
            return false;
        }

        return state.Participation == ApParticipationKind.OwnApSlot
            && IsLocalProgressOwner(player) && ApRunData.PublishLocalProgress(player);
    }

    public static int IncrementCardRewards(Player player) =>
        IncrementCounter(player, Counter.Card);

    public static int IncrementRareCardRewards(Player player) =>
        IncrementCounter(player, Counter.RareCard);

    public static int IncrementGoldRewards(Player player) =>
        IncrementCounter(player, Counter.Gold);

    public static int IncrementPotionRewards(Player player) =>
        IncrementCounter(player, Counter.Potion);

    /// <summary>
    /// Marks one player's deterministic boss compensation in this replica's construction state.
    /// The local owner mirrors the marker into canonical progress for the next durable checkpoint.
    /// </summary>
    public static bool TryMarkBossCompensation(Player player, int act)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun || act is < 1 or > 3)
            return false;

        bool localOwner = IsLocalProgressOwner(player);
        if (!TryGetConstructionState(player, localOwner, out ApReplicaConstructionState construction)
            || !construction.TryMarkBossCompensation(act))
        {
            return false;
        }

        if (localOwner)
            ArchipelagoClient.Progress.MultiplayerBossCompensatedActs.Add(act);
        return true;
    }

    public static int GetRelicRewardsAttempted(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun || IsLocalProgressOwner(player))
            return ArchipelagoClient.Progress.RelicRewardsAttempted;
        return TryGetRemoteProgress(player, out ApRunProgressState progress)
            ? progress.RelicRewardsAttempted
            : int.MaxValue;
    }

    private static int IncrementCounter(Player player, Counter counter)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
        {
            return counter switch
            {
                Counter.Card => ++ArchipelagoClient.Progress.CardRewardsAttempted,
                Counter.RareCard => ++ArchipelagoClient.Progress.RareCardRewardsAttempted,
                Counter.Gold => ++ArchipelagoClient.Progress.GoldRewardsAttempted,
                Counter.Potion => ++ArchipelagoClient.Progress.PotionRewardsAttempted,
                _ => throw new ArgumentOutOfRangeException(nameof(counter)),
            };
        }

        bool localOwner = IsLocalProgressOwner(player);
        if (!TryGetConstructionState(player, localOwner, out ApReplicaConstructionState construction))
            return int.MaxValue;

        int result = counter switch
        {
            Counter.Card => construction.IncrementCardRewards(),
            Counter.RareCard => construction.IncrementRareCardRewards(),
            Counter.Gold => construction.IncrementGoldRewards(),
            Counter.Potion => construction.IncrementPotionRewards(),
            _ => throw new ArgumentOutOfRangeException(nameof(counter)),
        };

        if (localOwner)
        {
            switch (counter)
            {
                case Counter.Card:
                    ArchipelagoClient.Progress.CardRewardsAttempted = result;
                    break;
                case Counter.RareCard:
                    ArchipelagoClient.Progress.RareCardRewardsAttempted = result;
                    break;
                case Counter.Gold:
                    ArchipelagoClient.Progress.GoldRewardsAttempted = result;
                    break;
                case Counter.Potion:
                    ArchipelagoClient.Progress.PotionRewardsAttempted = result;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(counter));
            }
        }

        return result;
    }

    private static bool TryGetConstructionState(
        Player player,
        bool localOwner,
        out ApReplicaConstructionState construction)
    {
        if (player.RunState is RunState runState
            && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
            && state.Participation != ApParticipationKind.VanillaGuest)
        {
            ApRunProgressState baseline;
            if (state.Progress.Initialized)
                baseline = state.Progress;
            else if (localOwner)
                baseline = ArchipelagoClient.Progress.ToRunProgressState();
            else
            {
                construction = null!;
                return false;
            }

            construction = ApRunData.EnsureConstructionInitialized(state, baseline);
            return true;
        }

        construction = null!;
        return false;
    }

    internal static bool TryGetRemoteProgress(Player player, out ApRunProgressState progress)
    {
        if (player.RunState is RunState runState
            && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
            && state.Participation != ApParticipationKind.VanillaGuest)
        {
            progress = state.Progress;
            return true;
        }

        progress = null!;
        return false;
    }

    /// <summary>
    /// Resolves the player's Net-ID-keyed progress that owns their location checks.
    /// </summary>
    public static bool TryGetCheckProgress(
        Player player,
        out ApRunProgressState progress,
        out string reason)
    {
        if (!ApPlayerContextResolver.HasCharacterChecks(player))
        {
            progress = null!;
            reason = $"player {player.NetId} has no AP slot that owns character checks";
            return false;
        }

        return ApPlayerContextResolver.TryGetRewardProgress(
            player,
            out progress,
            out reason
        );
    }

    internal static IReadOnlyList<int> GetReplicatedRelicReceiptIndexes(
        Player player,
        ApRunProgressState progress)
    {
        long? characterOffset = player.GetAPCharacterNumber();
        if (!characterOffset.HasValue)
            return Array.Empty<int>();

        IEnumerable<int> indexes = progress.RelicReceiptIndexesByCharacter.TryGetValue(
            characterOffset.Value,
            out List<int>? liveIndexes)
                ? liveIndexes
                : Array.Empty<int>();
        if (player.RunState is RunState runState
            && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
            && state.InitialRelicReceiptIndexesByCharacter.TryGetValue(
                characterOffset.Value,
                out List<int>? initialIndexes))
        {
            indexes = indexes.Concat(initialIndexes);
        }
        return indexes.Distinct().Order().ToArray();
    }

    public static bool IsCheckWriter(Player player)
    {
        if (!ApPlayerContextResolver.HasCharacterChecks(player))
            return false;
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return player == GameUtility.CurrentPlayer;
        if (player.RunState is not RunState runState
            || !ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state))
        {
            return false;
        }

        return state.Participation == ApParticipationKind.OwnApSlot
            && MultiplayerSupport.IsLocalOwnApSlot
            && IsLocalProgressOwner(player);
    }

    public static long ResolveLocationId(Player player, string locationName)
    {
        if (!IsCheckWriter(player))
            return -1;

        ArchipelagoSession? session = ArchipelagoClient.Session;
        if (session == null)
        {
            LogUtility.Warn($"Could not resolve AP location '{locationName}': no active AP session");
            return -1;
        }

        try
        {
            return session.Locations.GetLocationIdFromName(
                "Slay the Spire II",
                CoopSlot.Name(locationName)
            );
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not resolve AP location '{locationName}': {ex.Message}");
            return -1;
        }
    }

    public static bool IsChecked(Player player, long locationId) =>
        locationId != -1
        && IsCheckWriter(player)
        && ArchipelagoClient.CheckedLocations.Contains(locationId);

    /// <summary>
    /// Records the exact location in the owning AP slot's existing durable multiplayer outbox.
    /// Non-writer replicas still complete the native reward selection.
    /// </summary>
    public static bool QueueCheck(Player player, string locationName, long locationId = -1)
    {
        if (!IsCheckWriter(player))
            return false;

        if (locationId == -1)
            locationId = ResolveLocationId(player, locationName);
        if (locationId == -1)
        {
            LogUtility.Warn($"Location '{locationName}' not found in the owning Archipelago slot");
            return false;
        }
        if (!CoopSlot.Owns(locationId))
        {
            LogUtility.Error($"Refusing location {locationId} belonging to another co-op player.");
            return false;
        }
        if (ArchipelagoClient.CheckedLocations.Contains(locationId))
            return false;

        ArchipelagoClient.CheckedLocations.Add(locationId);
        PendingCheckUtility.RecordAndSend(locationId);
        LogUtility.Success($"Recorded location check: {locationName} ({locationId})");
        return true;
    }

    private enum Counter
    {
        Card,
        RareCard,
        Gold,
        Potion,
    }
}
