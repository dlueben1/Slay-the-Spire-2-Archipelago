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
            || !settings.TryGetCharacterConfig(
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

        characterName = config.Name;
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
