using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Telemetry;

namespace StS2AP.Utils;

internal static class ApGameplayTelemetry
{
    private const string ApplicantId = "Archipelago.Gameplay";
    private const string RequestId = "gameplay";
    private static ITelemetryClient? _client;
    private static readonly object CaptureLock = new();

    internal static void Initialize()
    {
        try
        {
            TelemetryRegistry.RegisterApplicant(new TelemetryApplicant
            {
                ApplicantId = ApplicantId,
                OwnerModId = ModEntry.ModId,
                DisplayName = "Archipelago gameplay stats (experimental)",
                Adapter = new ApTelemetry.ProxyAdapter(),
                Requests =
                [
                    new TelemetryRequest
                    {
                        RequestId = RequestId,
                        Category = TelemetryDataCategory.Custom,
                        Description = "Share selected YAML settings, characters and Ancient relic choices to help " +
                            "improve the mod. Includes system/version details and random IDs linking reports; no names or seeds.",
                    },
                ],
            });
            _client = TelemetryApi.GetClient(ApplicantId);
        }
        catch (Exception ex) { LogUtility.Warn($"Gameplay telemetry unavailable ({ex.GetType().Name})."); }
    }

    internal static void ObserveSettings() => Capture("ap.settings_observed", null);

    internal static void RunStarted(RunState run)
    {
        // Called only after successful new-run launch, never by the saved-run path.
        Player? player = GameUtility.CurrentPlayer;
        if (player != null && ReferenceEquals(player.RunState, run))
            Capture("ap.run_started", player);
    }

    internal static void AncientSelected(Player player, int receiptIndex, string selected, IReadOnlyList<string> offered) =>
        Capture("ap.ancient_selected", player, selected, offered, receiptIndex);

    private static void Capture(string eventName, Player? player, string? selected = null,
        IReadOnlyList<string>? offered = null, int? receiptIndex = null)
    {
        try
        {
            lock (CaptureLock)
            {
                if (_client?.IsEnabled(RequestId) != true || !ArchipelagoClient.IsConnected ||
                    ArchipelagoClient.Settings is not { } settings || MultiplayerSupport.IsLocalGuest) return;
                if (player != null && (!LocalContext.IsMe(player) || GameUtility.CurrentConfig == null ||
                    MultiplayerSupport.ClaimsInvalidated)) return;

                var session = ArchipelagoClient.Session!;
                if (string.IsNullOrWhiteSpace(session.RoomState.Seed)) return;
                // The AP identity is only a local file lookup. Only unrelated random IDs leave the device.
                string owner = JsonSerializer.Serialize(new object[] { session.RoomState.Seed,
                    session.ConnectionInfo.Team, session.ConnectionInfo.Slot, CoopSlot.PlayerNumber });
                string path = ProjectSettings.GlobalizePath(
                    $"user://ArchipelagoTelemetry/profile-{SaveManager.Instance.CurrentProfileId}/{ApGameplayLedger.Hash(owner)}.json");
                var ledger = ApGameplayLedger.Load(path);
                string? runId = null;
                if (player != null)
                {
                    if (player.RunState is not RunState run ||
                        !ApRunData.TryGetSharedState(run, out var shared) || shared.RunId == Guid.Empty) return;
                    runId = ledger.RunId(shared.RunId);
                }
                var options = ApGameplayOptions.Create(ArchipelagoClient.SlotData);
                string key = eventName switch
                {
                    "ap.settings_observed" => eventName + ":" + options.ToJsonString(),
                    "ap.run_started" => eventName + ":" + runId,
                    "ap.ancient_selected" when receiptIndex is >= 0 => eventName + ":" + runId + ":" + receiptIndex,
                    _ => throw new InvalidDataException("Missing gameplay event identity."),
                };
                string eventId = ledger.EventId(key);
                if (ledger.Events.Contains(eventId)) return;
                var payload = new JsonObject
                {
                    ["telemetry_version"] = 2,
                    ["event_id"] = eventId,
                    ["campaign_id"] = ledger.CampaignId,
                    ["ap_version"] = typeof(ModEntry).Assembly.GetName().Version?.ToString(),
                    ["apworld_version"] = settings.APWorldVersion.ToString(),
                    ["yaml_options"] = options,
#if DEBUG
                    ["build_configuration"] = "Debug",
#else
                    ["build_configuration"] = "Release",
#endif
                };
                if (player != null)
                {
                    payload["run_id"] = runId;
                    payload["character"] = ApTelemetryException.Symbol(player.Character.Id.Entry, 128);
                    payload["game_mode"] = MultiplayerSupport.IsRealMultiplayerRun ? "multiplayer" : "singleplayer";
                    AncientRewardSettings ancient = AncientSettingsUtility.Current;
                    payload["ancient_location"] = ancient.Location switch
                    {
                        AncientRelicLocation.StartOfAct => "start_of_act",
                        AncientRelicLocation.Anytime => "anytime",
                        _ => throw new InvalidDataException("Invalid Ancient location."),
                    };
                    payload["ancient_pool"] = ancient.Pool switch
                    {
                        AncientRelicPoolMode.Balanced => "balanced",
                        AncientRelicPoolMode.Chaos => "chaos",
                        AncientRelicPoolMode.TrueChaos => "true_chaos",
                        _ => throw new InvalidDataException("Invalid Ancient pool."),
                    };
                }
                if (selected != null && offered != null)
                {
                    if (offered.Count is < 1 or > 16 || !offered.Contains(selected)) return;
                    payload["selected_relic"] = ApTelemetryException.Symbol(selected, 128);
                    payload["offered_relics"] = new JsonArray(offered.Select(id =>
                        (JsonNode?)JsonValue.Create(ApTelemetryException.Symbol(id, 128))).ToArray());
                }
                if (ledger.Reserve(path, eventId))
                    _client.CapturePayload(eventName, RequestId, payload);
            }
        }
        catch (Exception ex)
        {
            // Analytics must never fail an AP connection, run launch or reward claim.
            LogUtility.Warn($"Gameplay telemetry skipped ({ex.GetType().Name}).");
        }
    }
}
