using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
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
    private const string TravelBlockedMessage =
        "AP rewards are unavailable while traveling to another room.";
    internal const string PendingClaimMessage =
        "Wait for your AP reward to finish first.";

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

    // Locked card rows stay clickable so they can explain why they will not open.
    private static readonly Color LockedCardRewardModulate = new(1f, 1f, 1f, 0.4f);

    private static readonly Dictionary<RewardsSet, NativeMenuSession> Sessions = new();

    private static NRewardsScreen? _screen;
    private static RewardsSet? _set;
    private static bool _opening;
    private static bool _closing;
    private static bool _hotkeysRegistered;
    // Multiplayer map travel, from its start until its room is entered. The menu closes as travel
    // starts and stays closed until then; claims themselves are queued actions, ordered against
    // the move either way.
    private static bool _traveling;
    private static ReturnDestination _returnDestination;

    public static Action? OnScreenClosed;

    public static bool IsOpen =>
        _screen != null && GodotObject.IsInstanceValid(_screen) && _screen.IsInsideTree();

    internal static bool IsActive =>
        _screen != null && IsOpen && ActiveScreenContext.Instance.IsCurrent(_screen);

    private static bool IsTravelBlocked => MultiplayerSupport.IsRealMultiplayerRun
        && (_traveling || RunManager.Instance.NetService.IsGameLoading);

    /// <summary>Closes the menu as multiplayer map travel starts. False in singleplayer.</summary>
    internal static bool BeginTravel()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun)
            return false;

        _traveling = true;
        // An opening still in progress is abandoned (see CanBuildMenuAfterAwait).
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
        return true;
    }

    internal static void EndTravel() => _traveling = false;

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
        if (TryBlockTravel() || TryBlockMultiplayerCombatOpen() || TryBlockNativeChoiceOpen())
            return;

        _opening = true;
        Callable.From(() =>
        {
            TaskHelper.RunSafely(OpenOnMainThread());
        }).CallDeferred();
    }

    /// <summary>
    /// Whether a multiplayer menu may still open after awaiting its relic assignments: travel or
    /// leaving the run since it started abandons the opening.
    /// </summary>
    internal static bool CanBuildMenuAfterAwait() =>
        _opening
        && !IsTravelBlocked
        && !TryBlockMultiplayerCombatOpen()
        && !TryBlockNativeChoiceOpen();

    private static async Task OpenOnMainThread()
    {
        bool opened = false;
        bool destinationPrepared = false;
        try
        {
            if (IsOpen)
                return;
            // ShowRewards is deferred, so travel, combat or a native choice can begin after the
            // click-time guard; travel and leaving the run also clear _opening. Check again.
            if (!_opening || TryBlockTravel()
                || TryBlockMultiplayerCombatOpen()
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
            if (_opening && destinationPrepared && !opened && !IsOpen)
                RestoreDestination(_returnDestination);
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
        _traveling = false;
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

    /// <summary>
    /// Whether a set is a multiplayer AP menu: this player's alone, never begun on the reward-set
    /// synchronizer, its rows claiming through queued actions.
    /// </summary>
    internal static bool IsOwnerLocalSet(RewardsSet set) =>
        MultiplayerSupport.IsRealMultiplayerRun
        && Sessions.TryGetValue(set, out NativeMenuSession? session)
        && !session.Synchronized;

    /// <summary>Whether a reward row belongs to the open owner-local multiplayer AP menu.</summary>
    internal static bool IsOwnerLocalReward(Reward reward) =>
        reward is ApMirroredRewardDispatcher.IApNativeReward
        && IsOpen
        && _set != null
        && IsOwnerLocalSet(_set);

    internal static bool ShouldKeepEmptyScreenOpen(RewardsSet set) =>
        Sessions.TryGetValue(set, out NativeMenuSession? session) && session.InitiallyEmpty;

    internal static bool ShouldHandleProceedWithoutNativeSkip(RewardsSet set) =>
        Sessions.TryGetValue(set, out NativeMenuSession? session)
        && (!session.Synchronized || session.InitiallyEmpty);

    internal static bool CanSelectNativeReward(NRewardButton button)
    {
        if (button.Reward is not ApMirroredRewardDispatcher.IApNativeReward reward)
            return !TryBlockNativeChoiceWhileClaimPending();
        if (TryBlockTravel())
            return false;
        if (reward.CanClaim(out string reason))
            return true;

        string message = string.IsNullOrWhiteSpace(reason)
            ? "This AP reward cannot be claimed."
            : reason;
        bool blockedByCombat = MultiplayerSupport.IsSynchronizedCombatActive
            && message.Contains("outside combat", StringComparison.OrdinalIgnoreCase);
        // Strict Card Rewards' lock is a rule players run into, not a rare failure, so make it
        // stand out over other AP notifications.
        bool lockedByStrictCardRewards = reward is ApMirroredRewardDispatcher.ApNativeCardReward card
            && card.IsLockedByAnotherReveal();
        ShowBlockedMessage(message, blockedByCombat, emphasized: lockedByStrictCardRewards);
        return false;
    }

    /// <summary>
    /// Ghosts the card rows that Strict Card Rewards locks behind a revealed card reward, so
    /// the revealed one is the only card row at full colour.
    /// </summary>
    internal static void RefreshCardRewardLocks()
    {
        if (_screen == null || !GodotObject.IsInstanceValid(_screen))
            return;
        if (_screen.GetNodeOrNull<Control>("%RewardsContainer") is not { } container)
            return;
        foreach (NRewardButton button in container.GetChildren().OfType<NRewardButton>())
            ApplyCardRewardLock(button);
    }

    internal static void ApplyCardRewardLock(NRewardButton button)
    {
        if (button.Reward is ApMirroredRewardDispatcher.ApNativeCardReward card)
            button.Modulate = card.IsLockedByAnotherReveal() ? LockedCardRewardModulate : Colors.White;
    }

    /// <summary>
    /// Refuses a native choice while this player has a queued AP claim requested but not yet
    /// executed here. A card claim, or a relic that asks for a card when obtained, reserves choice
    /// ids inside its action, while a native reward pick, rest-site option, event option or shop
    /// purchase reserves them when its message arrives. Until the claim has executed, other peers
    /// could reserve the two in a different order and match a pick to the wrong choice.
    /// </summary>
    internal static bool TryBlockNativeChoiceWhileClaimPending()
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun || !ApQueuedClaims.HasPendingOwnerClaims)
            return false;
        ShowBlockedMessage(PendingClaimMessage, blockedByCombat: false, includeInDevConsole: false);
        return true;
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

        // Match the exact screen held by an AP card reward, not just an arbitrary top overlay. A
        // queued card claim's picker belongs to its action's reward, not to a menu row.
        foreach (CardReward reward in _set.Rewards.OfType<CardReward>())
            if (TrySkipCardRewardSelection(reward, picker))
                return true;
        return ApQueuedCardClaims.OwnerPickerReward is { } queued
            && TrySkipCardRewardSelection(queued, picker);
    }

    private static bool TrySkipCardRewardSelection(CardReward reward, NCardRewardSelectionScreen picker)
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
