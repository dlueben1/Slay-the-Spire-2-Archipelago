using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>
/// Thin lifecycle adapter around MegaCrit's native reward screen. AP owns only the persistent
/// receipt catalog and close/reopen semantics; NRewardsScreen owns all presentation and input.
/// </summary>
public static class ArchipelagoRewardUI
{
    private const string MultiplayerCombatBlockedMessage =
        "Multiplayer AP rewards can only be claimed outside combat.";
    private const string NativeChoiceBlockedMessage =
        "Finish the current card or relic selection before opening AP rewards.";
    private const string TreasureRoomBlockedMessage =
        "Wait until every player is ready to proceed before opening AP rewards.";
    private const string TravelBlockedMessage =
        "AP rewards are unavailable while traveling to another room.";

    private enum ReturnDestination
    {
        Room,
        Map,
        Deck,
    }

    private sealed record NativeMenuSession(
        Guid MenuId,
        bool Synchronized,
        bool InitiallyEmpty);

    private static readonly Dictionary<RewardsSet, NativeMenuSession> Sessions = new();
    private static readonly ApRewardTravelGuard Travel = new();

    private static NRewardsScreen? _screen;
    private static RewardsSet? _set;
    private static bool _opening;
    private static bool _closing;
    private static bool _hotkeysRegistered;
    private static ReturnDestination _returnDestination;

    public static Action? OnScreenClosed;

    public static bool IsOpen =>
        _screen != null && GodotObject.IsInstanceValid(_screen) && _screen.IsInsideTree();

    internal static bool IsActive =>
        _screen != null && IsOpen && ActiveScreenContext.Instance.IsCurrent(_screen);

    internal static int ApLifecycleVersion => Travel.ApLifecycleVersion;

    private static bool IsTravelBlocked => MultiplayerSupport.IsRealMultiplayerRun
        && !Travel.CanOpen(Travel.ApLifecycleVersion, RunManager.Instance.NetService.IsGameLoading);

    internal static int? BeginTravel()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return null;

        int apLifecycleVersion = Travel.BeginTravel();
        _opening = false;
        _returnDestination = ReturnDestination.Room;
        try
        {
            if (IsOpen)
            {
                LogUtility.Info("Closing AP rewards at map travel start.");
                // Use the native Skip choice before closing its parent set. Deleting the beta
                // picker instead throws TaskCanceledException before SyncLocalChoice can run.
                TrySkipOwnedCardPicker();
                if (ReferenceEquals(NOverlayStack.Instance?.Peek(), _screen))
                    Hide();
                else
                {
                    // Native selection continuations may finish later in this frame. Never skip
                    // an unrelated/nested RewardsSet from the top of the synchronizer's stack.
                    RewardsSet? closingSet = _set;
                    Callable.From(() =>
                    {
                        if (ReferenceEquals(_set, closingSet)
                            && ReferenceEquals(NOverlayStack.Instance?.Peek(), _screen))
                            Hide();
                    }).CallDeferred();
                }
            }
        }
        catch (Exception ex)
        {
            // Keep the input gate closed, but let the base game's travel/cleanup continue.
            LogUtility.Error($"Could not close AP rewards at travel start: {ex}");
        }
        return apLifecycleVersion;
    }

    internal static void EndTravel(int apLifecycleVersion) => Travel.EndTravel(apLifecycleVersion);

    public static void Toggle()
    {
        if (!IsOpen)
        {
            ShowRewards();
            return;
        }

        if (IsActive)
        {
            Hide();
            return;
        }

        if (!TrySkipOwnedCardPicker())
            LogUtility.Debug("Ignoring AP reward toggle while a nested overlay is active");
    }

    /// <summary>
    /// Opens a fixed snapshot. Calls made for newly received items while an existing menu is open
    /// intentionally do nothing; those receipts appear on the next opening.
    /// </summary>
    public static void ShowRewards()
    {
        if (IsOpen || _opening)
            return;
        if (TryBlockTravel() || TryBlockMultiplayerCombatOpen()
            || TryBlockTreasureRoomOpen() || TryBlockNativeChoiceOpen())
            return;

        _opening = true;
        int apLifecycleVersion = Travel.ApLifecycleVersion;
        Callable.From(() =>
        {
            TaskHelper.RunSafely(OpenOnMainThread(apLifecycleVersion));
        }).CallDeferred();
    }

    internal static bool CanBuildMenuAfterAwait(int apLifecycleVersion) =>
        Travel.CanOpen(apLifecycleVersion, RunManager.Instance.NetService.IsGameLoading)
        && !TryBlockMultiplayerCombatOpen()
        && !TryBlockTreasureRoomOpen()
        && !TryBlockNativeChoiceOpen();

    private static async Task OpenOnMainThread(int apLifecycleVersion)
    {
        bool opened = false;
        bool destinationPrepared = false;
        try
        {
            if (IsOpen)
                return;
            // ShowRewards is deferred, so combat or a native choice can begin after the click-time
            // guard. Repeat both checks before OpenMenu creates a synchronized RewardsSet.
            if (apLifecycleVersion != Travel.ApLifecycleVersion || TryBlockTravel()
                || TryBlockMultiplayerCombatOpen()
                || TryBlockTreasureRoomOpen()
                || TryBlockNativeChoiceOpen())
                return;
            PrepareForOpen();
            destinationPrepared = true;
            opened = await ApMirroredRewardDispatcher.OpenMenu();
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to open AP reward menu: {ex}");
        }
        finally
        {
            if (apLifecycleVersion == Travel.ApLifecycleVersion && destinationPrepared && !opened && !IsOpen)
                RestoreDestination(_returnDestination);
            if (apLifecycleVersion == Travel.ApLifecycleVersion)
                _opening = false;
        }
    }

    internal static void ShowNativeMenu(
        RewardsSet set,
        Guid menuId,
        bool synchronized,
        bool initiallyEmpty)
    {
        if (GameUtility.CurrentPlayer?.RunState == null)
            throw new InvalidOperationException("Cannot show AP rewards without an active run.");
        if (IsOpen)
            throw new InvalidOperationException("An AP reward menu is already open.");

        var session = new NativeMenuSession(menuId, synchronized, initiallyEmpty);
        Sessions[set] = session;
        _set = set;
        _closing = false;

        try
        {
            _screen = NRewardsScreen.ShowScreen(
                set,
                isTerminal: false,
                GameUtility.CurrentPlayer.RunState
            );
            RegisterHotkeys();
            LogUtility.Success(
                $"Native AP reward screen opened: menu={menuId}, rewards={set.Rewards.Count}"
            );
        }
        catch
        {
            Sessions.Remove(set);
            _set = null;
            _screen = null;
            throw;
        }
    }

    public static void Hide()
    {
        if (_closing || !IsOpen || _screen == null || _set == null)
            return;

        _closing = true;
        UnregisterHotkeys();
        if (Sessions.TryGetValue(_set, out NativeMenuSession? session)
            && session.Synchronized
            && !session.InitiallyEmpty
            && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(_set))
        {
            try
            {
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Could not close synchronized AP reward menu: {ex.Message}");
                MultiplayerSupport.InvalidateRunClaims(
                    "the native AP reward menu could not complete its synchronized close"
                );
            }
        }

        NOverlayStack.Instance?.Remove(_screen);
    }

    internal static void CloseToMap()
    {
        _returnDestination = ReturnDestination.Map;
        Hide();
    }

    internal static void CloseToDeck()
    {
        _returnDestination = ReturnDestination.Deck;
        Hide();
    }

    public static void RemoveUI()
    {
        Travel.Reset();
        UnregisterHotkeys();
        NRewardsScreen? screen = _screen;
        // Clear AP's return destination before native close callbacks run. Teardown must
        // not reopen the map/deck or notify listeners as if gameplay were continuing.
        if (_set != null)
            Sessions.Remove(_set);
        _screen = null;
        _set = null;
        _opening = false;
        _closing = false;
        _returnDestination = ReturnDestination.Room;

        if (screen == null || !GodotObject.IsInstanceValid(screen))
            return;

        // ReturnToMainMenu is async: its Harmony postfix runs before RunManager.CleanUp.
        // Remove through the owning stack so Clear cannot later visit a freed screen.
        if (screen.GetParent() is NOverlayStack stack)
        {
            stack.Remove(screen);
            LogUtility.Debug("Removed native AP reward screen from overlay stack during teardown");
        }
        else if (!screen.IsQueuedForDeletion())
        {
            screen.QueueFreeSafely();
        }
    }

    internal static bool IsApRewardSet(RewardsSet set) => Sessions.ContainsKey(set);

    internal static bool ShouldKeepEmptyScreenOpen(RewardsSet set) =>
        Sessions.TryGetValue(set, out NativeMenuSession? session) && session.InitiallyEmpty;

    internal static bool ShouldHandleProceedWithoutNativeSkip(RewardsSet set) =>
        Sessions.TryGetValue(set, out NativeMenuSession? session)
        && (!session.Synchronized || session.InitiallyEmpty);

    internal static bool CanSelectNativeReward(NRewardButton button)
    {
        if (button.Reward is not ApMirroredRewardDispatcher.IApNativeReward reward)
            return true;
        if (TryBlockTravel())
            return false;
        if (reward.CanClaim(out string reason))
            return true;

        string message = string.IsNullOrWhiteSpace(reason)
            ? "This AP reward cannot be claimed."
            : reason;
        bool blockedByCombat = MultiplayerSupport.IsSynchronizedCombatActive
            && message.Contains("outside combat", StringComparison.OrdinalIgnoreCase);
        ShowBlockedMessage(message, blockedByCombat);
        return false;
    }

    private static bool TryBlockMultiplayerCombatOpen()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsSynchronizedCombatActive)
        {
            return false;
        }

        ShowBlockedMessage(
            MultiplayerCombatBlockedMessage,
            blockedByCombat: true,
            includeInDevConsole: false
        );
        return true;
    }

    private static bool TryBlockTravel()
    {
        if (!IsTravelBlocked)
            return false;
        ShowBlockedMessage(
            TravelBlockedMessage,
            blockedByCombat: false,
            includeInDevConsole: false
        );
        return true;
    }

    private static bool TryBlockNativeChoiceOpen()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !IsNativePlayerChoiceScreen(ActiveScreenContext.Instance.GetCurrentScreen()))
        {
            return false;
        }

        ShowBlockedMessage(
            NativeChoiceBlockedMessage,
            blockedByCombat: false,
            includeInDevConsole: false
        );
        return true;
    }

    private static bool TryBlockTreasureRoomOpen()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun || NRun.Instance?.TreasureRoom == null)
            return false;
        if (RunManager.Instance.DebugOnlyGetState() is RunState run
            && RelicReceiptMultiplayer.IsTreasureProceedReadyForApMenu(run))
        {
            return false;
        }

        // TreasureRoom.DoExtraRewardsIfNeeded constructs one native RewardsSet per player.
        // Different replicas can reach that construction several seconds apart, so inserting
        // an AP RewardsSet anywhere in this room can give the same set ID different meanings.
        ShowBlockedMessage(
            TreasureRoomBlockedMessage,
            blockedByCombat: false,
            emphasized: true,
            includeInDevConsole: false
        );
        return true;
    }

    private static bool IsNativePlayerChoiceScreen(IScreenContext? screen) =>
        screen is NCardGridSelectionScreen
            or NCardRewardSelectionScreen
            or NChooseACardSelectionScreen
            or NChooseABundleSelectionScreen
            or NChooseARelicSelection;

    private static void ShowBlockedMessage(
        string message,
        bool blockedByCombat,
        bool emphasized = false,
        bool includeInDevConsole = true)
    {
        bool useLargePresentation = blockedByCombat || emphasized;
        NotificationUtility.ShowRawText(
            useLargePresentation ? $"[font_size=60]{message}[/font_size]" : message,
            timeout: useLargePresentation ? 3.5 : 3.0,
            priority: useLargePresentation
                ? NotificationUtility.NotificationPriority.High
                : NotificationUtility.NotificationPriority.Normal,
            includeInDevConsole: includeInDevConsole
        );
    }

    internal static void CloseWithoutNativeSkip(NRewardsScreen screen)
    {
        if (!ReferenceEquals(screen, _screen))
            return;
        _closing = true;
        UnregisterHotkeys();
        NOverlayStack.Instance?.Remove(screen);
    }

    internal static void NotifyNativeScreenClosed(NRewardsScreen screen, RewardsSet set)
    {
        if (!Sessions.Remove(set) || !ReferenceEquals(screen, _screen))
            return;

        UnregisterHotkeys();
        _screen = null;
        _set = null;
        _closing = false;
        ReturnDestination destination = _returnDestination;
        _returnDestination = ReturnDestination.Room;
        OnScreenClosed?.Invoke();
        RestoreDestination(destination);
    }

    private static void RegisterHotkeys()
    {
        if (_hotkeysRegistered || NHotkeyManager.Instance == null)
            return;
        NHotkeyManager.Instance.PushHotkeyPressedBinding(MegaInput.cancel, Hide);
        NHotkeyManager.Instance.PushHotkeyPressedBinding(MegaInput.pauseAndBack, Hide);
        NHotkeyManager.Instance.PushHotkeyReleasedBinding(MegaInput.viewMap, CloseToMap);
        NHotkeyManager.Instance.PushHotkeyReleasedBinding(MegaInput.viewDeckAndTabLeft, CloseToDeck);
        _hotkeysRegistered = true;
    }

    private static void UnregisterHotkeys()
    {
        if (!_hotkeysRegistered || NHotkeyManager.Instance == null)
            return;
        NHotkeyManager.Instance.RemoveHotkeyPressedBinding(MegaInput.cancel, Hide);
        NHotkeyManager.Instance.RemoveHotkeyPressedBinding(MegaInput.pauseAndBack, Hide);
        NHotkeyManager.Instance.RemoveHotkeyReleasedBinding(MegaInput.viewMap, CloseToMap);
        NHotkeyManager.Instance.RemoveHotkeyReleasedBinding(MegaInput.viewDeckAndTabLeft, CloseToDeck);
        _hotkeysRegistered = false;
    }

    private static bool TrySkipOwnedCardPicker()
    {
        if (!IsOpen
            || _set == null
            || NOverlayStack.Instance?.Peek() is not NCardRewardSelectionScreen picker)
        {
            return false;
        }

        // Match the exact screen held by an AP card reward, not just an arbitrary top overlay.
        foreach (CardReward reward in _set.Rewards.OfType<CardReward>())
            if (BetaMainCompatibility.TrySkipCardRewardSelection(reward, picker))
                return true;
        return false;
    }

    private static void PrepareForOpen()
    {
        _returnDestination = ReturnDestination.Room;
        NCapstoneContainer? capstoneContainer = NCapstoneContainer.Instance;
        ICapstoneScreen? currentCapstone = capstoneContainer?.CurrentCapstoneScreen;
        if (currentCapstone is NDeckViewScreen)
            _returnDestination = ReturnDestination.Deck;
        if (currentCapstone != null)
            capstoneContainer!.Close();

        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen?.IsOpen != true)
            return;
        if (currentCapstone == null)
            _returnDestination = ReturnDestination.Map;
        mapScreen.Close(animateOut: false);
    }

    private static void RestoreDestination(ReturnDestination destination)
    {
        if (IsTravelBlocked)
            return;
        switch (destination)
        {
            case ReturnDestination.Map:
                NMapScreen.Instance?.Open(isOpenedFromTopBar: true);
                break;
            case ReturnDestination.Deck:
                Player? player = GameUtility.CurrentPlayer;
                if (player != null)
                {
                    NDeckViewScreen.ShowScreen(player);
                    NRun.Instance?.GlobalUi.TopBar.Deck.ToggleAnimState();
                }
                break;
        }
    }
}
