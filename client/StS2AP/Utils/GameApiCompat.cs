using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace StS2AP.Utils;

/// <summary>
/// Game API differences between the versions the client builds against, one method per difference.
/// Branch on an STS2_AT_LEAST_x_y_z symbol here. Branch in place only when a patch or an override
/// itself differs and can't go through a method, as in Patches_LastingCandy and ApEnemyDamageScaling.
/// </summary>
public static class GameApiCompat
{
    public static string GetRunSavePath(int profileId, string fileName)
    {
#if STS2_AT_LEAST_0_111_0
        return RunSaveManager.GetRunSavePath(profileId, fileName, null);
#else
        return RunSaveManager.GetRunSavePath(profileId, fileName);
#endif
    }

    public static IReadOnlyList<ulong> GetConnectedRunPlayerNetIds(LoadRunLobby lobby)
    {
#if STS2_AT_LEAST_0_111_0
        return lobby.PlayerIds.ToArray();
#else
        return lobby.ConnectedPlayerIds.ToArray();
#endif
    }

    public static CardCreationOptions WithCombatRewardCompatibility(CardCreationOptions options)
    {
#if STS2_AT_LEAST_0_111_0
        return options.WithFlags(CardCreationFlags.IsFromCombat);
#else
        return options;
#endif
    }

    /// <summary>Older versions carry a custom card pool on the options, which 0.111 removed.</summary>
    public static bool HasCustomCardPool(CardCreationOptions options)
    {
#if STS2_AT_LEAST_0_111_0
        return false;
#else
        return options.CustomCardPool is not null;
#endif
    }

    public static CardCreationOptions WithCardPools(
        CardCreationOptions options,
        IEnumerable<CardPoolModel> pools)
    {
#if STS2_AT_LEAST_0_111_0
        return options.WithCardPools(pools);
#else
        // Older versions clear the filter unless it is explicitly passed back in.
        return options.WithCardPools(pools, options.CardPoolFilter);
#endif
    }
}
