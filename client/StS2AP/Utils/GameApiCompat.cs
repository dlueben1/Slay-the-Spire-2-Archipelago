using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.TestSupport;

namespace StS2AP.Utils;

/// <summary>
/// Game API differences between the versions the client builds against, one method per difference.
/// Branch on an STS2_AT_LEAST_x_y_z symbol here. Branch in place only when a patch or an override
/// itself differs and can't go through a method, as in Patches_LastingCandy and ApEnemyDamageScaling.
/// </summary>
public static class GameApiCompat
{
    /// <summary>
    /// The method every purchase of a merchant entry runs through. 0.113 renamed it when it added
    /// the MerchantSynchronizer, which replays each purchase on every player's copy of the shop.
    /// </summary>
    public const string MerchantEntryPurchaseMethod =
#if STS2_AT_LEAST_0_113_0
        nameof(MerchantEntry.OnTryPurchaseUnsynchronized);
#else
        nameof(MerchantEntry.OnTryPurchaseWrapper);
#endif

    /// <summary>A relic from the player's grab bag at a rarity rolled on their rewards RNG.</summary>
    public static RelicModel PullNextRewardRelic(Player player)
    {
#if STS2_AT_LEAST_0_113_0
        return RelicFactory.PullNextRelicForRewardFromFront(player);
#else
        return RelicFactory.PullNextRelicFromFront(player);
#endif
    }

    /// <summary>Answers the player's card selections with a selector until the result is disposed.</summary>
    public static IDisposable PushCardSelector(Player player, ICardSelector selector)
    {
#if STS2_AT_LEAST_0_113_0
        return RunManager.Instance.CardSelectorStack.PushSelector(player, selector);
#else
        return CardSelectCmd.PushSelector(selector);
#endif
    }

    /// <summary>
    /// Starts hosting a lobby. From 0.113 a custom run is a flag beside the game mode; before
    /// that it was a game mode of its own, so older versions ignore the flag.
    /// </summary>
    public static void StartHost(NMultiplayerHostSubmenu hostSubmenu, GameMode gameMode, bool isCustom)
    {
#if STS2_AT_LEAST_0_113_0
        hostSubmenu.StartHost(gameMode, isCustom);
#else
        hostSubmenu.StartHost(gameMode);
#endif
    }

    public static void FastHostStandard(NMultiplayerSubmenu submenu)
    {
#if STS2_AT_LEAST_0_113_0
        submenu.FastHost(GameMode.Standard, isCustom: false);
#else
        submenu.FastHost(GameMode.Standard);
#endif
    }

    /// <summary>Runs the screen's reaction to a character button being selected.</summary>
    public static void SelectCharacter(NCharacterSelectScreen screen, NCharacterSelectButton button)
    {
#if STS2_AT_LEAST_0_113_0
        screen.SelectCharacter(button);
#else
        screen.SelectCharacter(button, button.Character);
#endif
    }

    /// <summary>The name of an ascension level in a standard run. From 0.113 Blitz names its own.</summary>
    public static LocString GetAscensionTitle(int level)
    {
#if STS2_AT_LEAST_0_113_0
        return AscensionHelper.GetTitle(level, GameMode.Standard);
#else
        return AscensionHelper.GetTitle(level);
#endif
    }

    /// <summary>The finished run the game-over screen was opened for.</summary>
    public static SerializableRun? GetRun(NGameOverScreen screen)
    {
#if STS2_AT_LEAST_0_113_0
        return screen._result.run;
#else
        return screen._serializableRun;
#endif
    }

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
