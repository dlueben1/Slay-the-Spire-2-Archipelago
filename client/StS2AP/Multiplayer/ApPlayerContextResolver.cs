using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Multiplayer;

/// <summary>
/// Resolves one STS player's direct AP connection and progress. Several players may connect
/// to the same slot, but their reward consumption remains keyed by their individual Net IDs.
/// </summary>
public static class ApPlayerContextResolver
{
    public static bool IsVanillaGuest(Player player) =>
        MultiplayerSupport.IsRealMultiplayerRun
        && TryGetPlayerState(player, out ApPlayerRunState state)
        && state.Participation == ApParticipationKind.VanillaGuest;

    public static bool TryGetRewardSettings(
        Player player,
        out ArchipelagoSettings settings)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
        {
            ArchipelagoSettings? currentSettings = ArchipelagoClient.Settings;
            if (currentSettings == null)
            {
                settings = null!;
                return false;
            }

            settings = currentSettings;
            return true;
        }

        settings = null!;
        if (!TryGetPlayerState(player, out ApPlayerRunState state))
            return false;

        if (state.Participation == ApParticipationKind.OwnApSlot
            && state.SlotSettings != null)
        {
            settings = state.SlotSettings;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the character configuration through the same per-player AP context as rewards.
    /// This must be used instead of the process-global settings during replicated construction:
    /// another own-slot player can have a different character table on this replica.
    /// </summary>
    public static bool TryGetCharacterConfig(
        Player player,
        out CharacterConfig config)
    {
        config = null!;
        if (!TryGetRewardSettings(player, out ArchipelagoSettings settings)
            || !settings.Characters.TryGetValue(
                player.Character.Id.Entry,
                out CharacterConfig? resolved
            )
            || resolved == null)
        {
            return false;
        }

        config = resolved;
        return true;
    }

    public static bool TryGetApCharacterName(Player player, out string characterName)
    {
        characterName = string.Empty;
        if (!TryGetCharacterConfig(player, out CharacterConfig config))
            return false;

        characterName = config.ModNum == 0
            ? config.Name
            : $"Custom Character {config.ModNum}";
        return true;
    }

    public static bool TryGetRewardProgress(
        Player player,
        out ApRunProgressState progress,
        out string reason)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
        {
            progress = ArchipelagoClient.Progress.ToRunProgressState();
            reason = string.Empty;
            return true;
        }

        progress = null!;
        if (!TryGetRewardProgressSource(player, out ApPlayerRunState source, out reason))
            return false;
        if (!source.Progress.Initialized)
        {
            reason = $"AP reward progress for player {player.NetId} is not initialized";
            return false;
        }

        progress = source.Progress;
        return true;
    }

    /// <summary>
    /// Returns whether this player's character-specific checks belong to an AP slot. This says
    /// nothing about which process may write them; <see cref="MultiplayerLocationChecks.IsCheckWriter"/>
    /// remains the mutation authority.
    /// </summary>
    public static bool HasCharacterChecks(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return true;
        if (!TryGetPlayerState(player, out ApPlayerRunState state))
            return false;
        return state.Participation == ApParticipationKind.OwnApSlot;
    }

    /// <summary>
    /// Resolves the player's canonical run-data record, even when another player uses the same slot.
    /// </summary>
    internal static bool TryGetRewardProgressSource(
        Player player,
        out ApPlayerRunState source,
        out string reason)
    {
        source = null!;
        reason = string.Empty;
        if (!TryGetPlayerState(player, out ApPlayerRunState state))
        {
            reason = $"no canonical AP run state exists for player {player.NetId}";
            return false;
        }

        if (state.Participation == ApParticipationKind.OwnApSlot)
        {
            source = state;
            return true;
        }

        reason = $"player {player.NetId} has no direct AP connection";
        return false;
    }

    private static bool TryGetPlayerState(
        Player player,
        out ApPlayerRunState state)
    {
        state = null!;
        if (player.RunState is not RunState playerRunState
            || !ApRunData.TryGetPlayerState(
                playerRunState,
                player.NetId,
                out ApPlayerRunState playerState
            ))
        {
            return false;
        }

        state = playerState;
        return true;
    }
}
