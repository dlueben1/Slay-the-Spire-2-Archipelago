using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.UI;

namespace StS2AP.Utils;

/// <summary>
/// Delays beta StS2's local fast-multiplayer automation until this process has
/// connected to and prepared its own Archipelago slot. This is a developer-only,
/// one-shot launch path; ordinary <c>-fastmp</c> behavior remains game-owned.
/// </summary>
internal static class ApFastMpLaunchController
{
    private const string LaunchArgument = "apFastmp";
    private const string ServerArgument = "apServer";
    private const string SlotArgument = "apSlot";
    private const string HostSlotArgument = "apHostSlot";
    private const string ClientSlotArgument = "apClientSlot";
    private const string HostClientIdArgument = "apHostClientId";
    private const string ClientClientIdArgument = "apClientClientId";

    private enum LaunchRole
    {
        None,
        HostStandard,
        Join,
    }

    private enum LaunchState
    {
        Inactive,
        WaitingForAp,
        Resuming,
        Completed,
        Failed,
    }

    private static LaunchRole _role;
    private static LaunchState _state;

    /// <summary>
    /// Claims beta's startup fast-multiplayer dispatcher when the AP-specific
    /// argument is present. Returning true means the caller must suppress the
    /// original game method, including for invalid or already-consumed requests.
    /// </summary>
    public static bool TryBeginFromCommandLine()
    {
        if (!CommandLineHelper.HasArg(LaunchArgument))
            return false;

        if (_state != LaunchState.Inactive)
        {
            LogUtility.Debug($"Ignoring repeated AP fast multiplayer startup in state {_state}");
            return true;
        }

        if (!CommandLineHelper.TryGetValue("fastmp", out string? fastMpValue)
            || !string.IsNullOrEmpty(fastMpValue))
        {
            Fail(
                "-apFastmp requires bare -fastmp with no native action value. "
                    + "Put host_standard or join after -apFastmp instead."
            );
            return true;
        }

        if (!string.Equals(
                CommandLineHelper.GetValue("force-steam"),
                "off",
                StringComparison.OrdinalIgnoreCase))
        {
            Fail(
                "-apFastmp requires -force-steam off so each clientId receives separate "
                    + "account-scoped AP settings and persistence."
            );
            return true;
        }

        string? requestedRole = CommandLineHelper.GetValue(LaunchArgument);
        _role = requestedRole?.ToLowerInvariant() switch
        {
            "host_standard" => LaunchRole.HostStandard,
            "join" => LaunchRole.Join,
            _ => LaunchRole.None,
        };
        if (_role == LaunchRole.None)
        {
            Fail(
                $"Unsupported -apFastmp value '{requestedRole ?? "<missing>"}'. "
                    + "Expected host_standard or join."
            );
            return true;
        }

        string? server = NormalizeOptionalArgument(CommandLineHelper.GetValue(ServerArgument));
        string? slot = NormalizeOptionalArgument(CommandLineHelper.GetValue(SlotArgument));

        _state = LaunchState.WaitingForAp;
        MultiplayerSupport.BeginApBoundMultiplayerEntry();
        ArchipelagoConnectionUI.InjectUI(serverOverride: server, slotNameOverride: slot);
        ArchipelagoNotificationUI.InjectUI();

        string nextAction = _role == LaunchRole.HostStandard
            ? "host a Standard lobby"
            : "join the local lobby";
        ArchipelagoConnectionUI.SetStatus(
            $"AP local test: connect this process, then it will {nextAction}."
        );
        LogUtility.Info(
            $"AP fast multiplayer waiting for slot preparation: role={requestedRole}, "
                + $"clientId={CommandLineHelper.GetValue("clientId") ?? "default"}, "
                + $"server={server ?? "cached"}, slot={slot ?? "cached"}"
        );
        return true;
    }

    /// <summary>
    /// Maps the native non-Steam player IDs to the AP identities supplied by the
    /// two-process harness. This is presentation-only; native IDs remain authoritative.
    /// </summary>
    public static bool TryGetHarnessPlayerLabel(ulong playerId, out string label)
    {
        label = string.Empty;
        if (!CommandLineHelper.HasArg(LaunchArgument))
            return false;

        if (TryGetHarnessIdentity(
                HostClientIdArgument,
                HostSlotArgument,
                "HOST",
                playerId,
                out label))
        {
            return true;
        }

        return TryGetHarnessIdentity(
            ClientClientIdArgument,
            ClientSlotArgument,
            "CLIENT",
            playerId,
            out label
        );
    }

    /// <summary>
    /// Handles the successful AP-login continuation when an AP fast-multiplayer
    /// request is active. The request is consumed even when resumption fails so
    /// a later reconnect cannot launch a second lobby unexpectedly.
    /// </summary>
    public static bool TryResumeAfterApPrepared()
    {
        if (_state == LaunchState.Inactive)
            return false;

        if (_state != LaunchState.WaitingForAp)
        {
            LogUtility.Debug($"AP fast multiplayer continuation ignored in state {_state}");
            return true;
        }

        if (!MultiplayerSupport.CanEnterMultiplayerLobby(out string blockedReason))
        {
            LogUtility.Error($"AP fast multiplayer could not resume: {blockedReason}");
            ArchipelagoConnectionUI.Show();
            ArchipelagoConnectionUI.SetStatus(blockedReason);
            return true;
        }

        _state = LaunchState.Resuming;
        try
        {
            NMultiplayerSubmenu? submenu = MenuUtility.OpenMultiplayer();
            if (submenu == null)
            {
                Fail("AP fast multiplayer could not open the native multiplayer submenu.");
                return true;
            }

            switch (_role)
            {
                case LaunchRole.HostStandard:
                    submenu.FastHost(GameMode.Standard);
                    break;
                case LaunchRole.Join:
                    submenu.OnJoinFriendsPressed();
                    break;
                default:
                    Fail("AP fast multiplayer lost its requested launch role.");
                    return true;
            }

            _state = LaunchState.Completed;
            LogUtility.Info($"AP fast multiplayer resumed native action exactly once: role={_role}");
        }
        catch (Exception ex)
        {
            Fail($"AP fast multiplayer failed to resume the native action: {ex.GetBaseException().Message}");
        }

        return true;
    }

    private static string? NormalizeOptionalArgument(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryGetHarnessIdentity(
        string idArgument,
        string slotArgument,
        string role,
        ulong playerId,
        out string label)
    {
        label = string.Empty;
        if (!ulong.TryParse(CommandLineHelper.GetValue(idArgument), out ulong expectedId)
            || expectedId != playerId)
        {
            return false;
        }

        string? slot = NormalizeOptionalArgument(CommandLineHelper.GetValue(slotArgument));
        if (slot == null)
            return false;

        label = $"AP {slot} [{role}]";
        return true;
    }

    private static void Fail(string reason)
    {
        _state = LaunchState.Failed;
        LogUtility.Error(reason);
        Callable.From(() => NotificationUtility.ShowRawText(reason)).CallDeferred();
    }
}
