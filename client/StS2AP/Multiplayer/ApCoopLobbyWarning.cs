using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Multiplayer;

/// <summary>Host consent at the final ready boundary, before the native begin-run message.</summary>
public static class ApCoopLobbyWarning
{
    private sealed class LobbyConsent
    {
        public CoopPlayerSelection Selection { get; } = new();
        public bool Pending { get; set; }
    }

    private static readonly ConditionalWeakTable<StartRunLobby, LobbyConsent> Consents = new();

    public static bool AllowLaunch(StartRunLobby lobby)
    {
        LobbyConsent consent = Consents.GetValue(lobby, _ => new LobbyConsent());
        if (consent.Pending)
            return false;
        CoopPlayerSelection.Member[] roster = ReadRoster(lobby);
        if (!consent.Selection.RequiresConfirmation(roster))
            return true;

        consent.Pending = true;
        // This boundary can be reached from a peer's ready message. Defer UI work and
        // native unready until OnEmbarkPressed has finished updating its controls.
        Callable.From(() => ShowWarning(lobby, consent, roster)).CallDeferred();
        return false;
    }

    private static void ShowWarning(
        StartRunLobby lobby, LobbyConsent consent, CoopPlayerSelection.Member[] roster)
    {
        var screen = MultiplayerSupport.GetObservedStartLobbyScreen(lobby);
        if (screen == null)
        {
            consent.Pending = false;
            return;
        }

        try
        {
            screen.OnUnreadyPressed(null!);
            // Native Add refuses a second modal. Do not bind our confirmation to an
            // unrelated popup that is already open (for example a disconnect notice).
            if (NModalContainer.Instance is not { OpenModal: null })
                throw new InvalidOperationException("The game's modal container is unavailable or busy.");
            string numbers = string.Join(", ", CoopPlayerSelection.GetDuplicates(roster)
                .Select(member => $"P{member.PlayerNumber} (AP slot {member.Slot})"));
            TextUtility.RegisterLocString("AP_COOP_DUPLICATES_HEADER", "Share AP Player Progress?", "ap");
            TextUtility.RegisterLocString("AP_COOP_DUPLICATES_BODY",
                $"Multiple people selected {numbers}. People using the same AP player number "
                + "receive that player's items and send the same checks. Completing a check twice "
                + "does not create another check, and the other numbered players' goals still need "
                + "to be completed. Continue with these shared player numbers?", "ap");
            var popup = new ConfirmPopup
            {
                Header = TextUtility.GetLocString("AP_COOP_DUPLICATES_HEADER", "ap"),
                Body = TextUtility.GetLocString("AP_COOP_DUPLICATES_BODY", "ap"),
                ButtonPressed = confirmed =>
                {
                    consent.Pending = false;
                    try
                    {
                        if (!confirmed || MultiplayerSupport.GetObservedStartLobbyScreen(lobby) != screen)
                            return;
                        if (!ApRunData.TryValidateHostLobbyContributions(lobby, out string reason))
                        {
                            NotificationUtility.ShowRawText($"AP multiplayer launch blocked: {reason}");
                            return;
                        }
                        if (!consent.Selection.Confirm(roster, ReadRoster(lobby)))
                        {
                            NotificationUtility.ShowRawText("The AP lobby lineup changed. Press Embark again to review it.");
                            return;
                        }
                        LogUtility.Info($"Host confirmed shared AP player numbers: {numbers}");
                        // Re-enter all character/campaign checks and the authoritative final guard.
                        screen.OnEmbarkPressed(null!);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Warn($"Could not resume AP embark after shared-player confirmation: {ex.Message}");
                        NotificationUtility.ShowRawText("Could not resume embarking. Press Embark to retry.");
                    }
                },
            };
            if (popup.Popup == null)
                throw new InvalidOperationException("The game's confirmation popup is unavailable.");
            popup.Show();
        }
        catch (Exception ex)
        {
            consent.Pending = false;
            LogUtility.Warn($"Could not confirm shared AP player numbers: {ex.Message}");
            NotificationUtility.ShowRawText("Could not show the shared-player warning. Press Embark to retry.");
        }
    }

    private static CoopPlayerSelection.Member[] ReadRoster(StartRunLobby lobby) =>
        BetaMainCompatibility.GetLobbyPlayerNetIds(lobby).Select(netId =>
        {
            ApRunData.TryGetLobbyPlayerState(lobby, netId, out ApPlayerRunState state);
            bool ownsSlot = state?.Participation == ApParticipationKind.OwnApSlot;
            return new CoopPlayerSelection.Member(netId,
                ownsSlot ? state!.ApRoomSeed : null,
                ownsSlot ? state!.ApTeamId : null,
                ownsSlot ? state!.ApSlotId : null,
                state?.SlotSettings?.PlayerCount ?? 0,
                state?.SlotSettings?.PlayerNumber ?? 0);
        }).ToArray();
}
