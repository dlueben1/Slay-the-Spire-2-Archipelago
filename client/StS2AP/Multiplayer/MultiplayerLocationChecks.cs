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

    public static bool IsChecked(Player player, long locationId) =>
        locationId != -1
        && IsCheckWriter(player)
        && ArchipelagoClient.CheckedLocations.Contains(locationId);

    /// <summary>
    /// Records the exact location in the owning AP slot's existing durable multiplayer outbox.
    /// Non-writer replicas still complete the native reward selection.
    /// </summary>
    internal static bool QueueCheck(Player player, string locationName, long locationId)
    {
        if (!IsCheckWriter(player))
            return false;

        if (locationId == -1)
        {
            LogUtility.Warn($"Location '{locationName}' not found in the owning Archipelago slot");
            return false;
        }
        LocationCheckSendResult result = GameUtility.QueueCheck(locationId);
        if (result.AcceptedCount > 0)
            LogUtility.Success($"Recorded location check: {locationName} ({locationId})");
        else if (!result.WasRecorded)
            LogUtility.Error($"Could not record location {locationId}: {result.Dispatch}, not-in-slot={result.NotInSlotCount}");
        return result.WasRecorded;
    }

    private enum Counter
    {
        Card,
        RareCard,
        Gold,
        Potion,
    }
}
