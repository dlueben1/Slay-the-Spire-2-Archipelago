using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Managers;

#if STS2_0_107_1 && STS2_0_111_0
#error Only one exact STS2 target constant may be defined.
#elif !STS2_0_107_1 && !STS2_0_111_0
#error One exact STS2 target constant must be defined.
#endif

namespace StS2AP.Utils;

/// <summary>
/// Compile-time differences between the two explicitly supported STS2 APIs. Each release variant
/// compiles this file against the matching game reference assembly.
/// </summary>
public static class BetaMainCompatibility
{
    public static bool TrySkipCardRewardSelection(CardReward reward, NCardRewardSelectionScreen picker)
    {
        try
        {
            if (!ReferenceEquals(reward._currentlyShownScreen, picker))
                return false;
            if (picker._completionSource == null || picker._completionSource.Task.IsCompleted)
                return false;

            for (int i = 0; i < picker._extraOptions.Count; i++)
            {
                CardRewardAlternative alternative = picker._extraOptions[i];
                if (!string.Equals(alternative.OptionId, "Skip", StringComparison.OrdinalIgnoreCase)
                    || alternative.AfterSelected != PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward)
                    continue;

                picker.OnAlternateRewardSelected(i);
                LogUtility.Info($"Skipped AP card selection through native Skip for player {reward.Player.NetId}.");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not skip AP card selection for player {reward.Player.NetId}: {ex}");
            return false;
        }
    }

    public static bool IsActionSynchronizerCombatState(
        ActionSynchronizerCombatState state,
        ActionSynchronizerCombatState expected) => state == expected;

    public static string GetRunSavePath(int profileId, string fileName)
    {
#if STS2_0_107_1
        return RunSaveManager.GetRunSavePath(profileId, fileName);
#elif STS2_0_111_0
        return RunSaveManager.GetRunSavePath(profileId, fileName, null);
#else
#error BetaMainCompatibility requires one exact STS2 target constant.
#endif
    }

    public static bool TryGetHostNetId(INetGameService netService, out ulong hostNetId)
    {
        hostNetId = default;
        if (netService.Type == NetGameType.Singleplayer)
        {
            hostNetId = netService.NetId;
            return true;
        }
        if (!netService.IsConnected)
            return false;

        switch (netService.Type)
        {
            case NetGameType.Host:
                hostNetId = netService.NetId;
                return true;
            case NetGameType.Client when netService is NetClientGameService client:
                hostNetId = client.HostNetId;
                return true;
            default:
                return false;
        }
    }

    public static IReadOnlyList<ulong> GetLobbyPlayerNetIds(StartRunLobby lobby) =>
        lobby.Players.Select(player => player.id).ToArray();

    public static IReadOnlyList<(ulong NetId, string CharacterId)> GetLobbyPlayerCharacters(
        StartRunLobby lobby) =>
        lobby.Players.Select(player => (player.id, player.character.Id.Entry)).ToArray();

    public static IReadOnlyList<ulong> GetConnectedRunPlayerNetIds(RunLobby lobby)
    {
#if STS2_0_107_1
        return lobby.ConnectedPlayerIds.ToArray();
#elif STS2_0_111_0
        return lobby.PlayerIds.ToArray();
#endif
    }

    public static IReadOnlyList<ulong> GetConnectedRunPlayerNetIds(LoadRunLobby lobby)
    {
#if STS2_0_107_1
        return lobby.ConnectedPlayerIds.ToArray();
#elif STS2_0_111_0
        return lobby.PlayerIds.ToArray();
#endif
    }

    public static CardCreationOptions WithCombatRewardCompatibility(CardCreationOptions options)
    {
#if STS2_0_107_1
        return options;
#elif STS2_0_111_0
        return options.WithFlags(CardCreationFlags.IsFromCombat);
#endif
    }

    public static CharacterModel GetLocalCharacter(StartRunLobby lobby) =>
        lobby.LocalPlayer.character;

    public static bool IsLocalPlayerReady(StartRunLobby lobby) => lobby.LocalPlayer.isReady;
}
