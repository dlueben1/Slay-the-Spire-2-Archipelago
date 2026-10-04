using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Telemetry;

namespace StS2AP.Utils;

/// <summary>Opt-in compatibility experiment. RitsuLib owns consent, persistence and delivery.</summary>
internal static class ApTelemetry
{
    internal const string ApplicantId = "Archipelago.Diagnostics";
    private const string HarmonyOwner = "archipelago.patch";
    private static readonly ApTelemetryErrorBudget ErrorBudget = new();
    private static ITelemetryClient? _client;
    private static JsonObject _compatibility = new() { ["snapshot_ready"] = false };
    private static string _patchApplication = "pending";
    private static string? _patchFailureType;
    private static string _phase = "startup";
    private static string _mode = "unknown";

    internal static void Initialize()
    {
        try
        {
            foreach (TelemetryDataCategory category in new[]
                     { TelemetryDataCategory.BasicUsage, TelemetryDataCategory.Diagnostics })
                TelemetryRegistry.RegisterContributionProvider(new CompatibilityContribution(category));

            TelemetryRegistry.RegisterApplicant(new TelemetryApplicant
            {
                ApplicantId = ApplicantId,
                OwnerModId = ModEntry.ModId,
                DisplayName = "Archipelago bug reports (experimental)",
                Adapter = new ProxyAdapter(),
                Requests =
                [
                    new TelemetryRequest
                    {
                        RequestId = "basic_usage",
                        Category = TelemetryDataCategory.BasicUsage,
                        Description = "Game, mod and system details, session counts, and random IDs linking your reports.",
                        ContributionSubscriptions = ["compatibility_BasicUsage"],
                    },
                    new TelemetryRequest
                    {
                        RequestId = "diagnostics",
                        Category = TelemetryDataCategory.Diagnostics,
                        Description = "Game/mod error details to help fix bugs and conflicts. " +
                            "No names, connection details, file paths, full logs or gameplay stats.",
                        ContributionSubscriptions = ["compatibility_Diagnostics"],
                        CaptureFilter = FilterDiagnostics,
                    },
                ],
            });
            _client = TelemetryApi.GetClient(ApplicantId);
            RitsuLibFramework.SubscribeLifecycle<MainMenuReadyEvent>(_ => OnMainMenu());
            RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt => SetRunPhase(evt.IsMultiplayer));
            RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt => SetRunPhase(evt.IsMultiplayer));
            RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ => Volatile.Write(ref _phase, "run_ended"));
        }
        catch (Exception ex)
        {
            // Never recursively report a failure of the reporting system itself.
            LogUtility.Warn($"Telemetry unavailable ({ex.GetType().Name}).");
        }
    }

    internal static void RecordPatchResult(Exception? failure = null)
    {
        Volatile.Write(ref _patchApplication, failure is null ? "succeeded" : "failed");
        Volatile.Write(ref _patchFailureType, failure is null ? null : ApTelemetryException.Symbol(failure.GetType().FullName));
        if (failure != null) ReportException(failure, "ap_patch_application");
    }

    internal static void ReportException(Exception exception, string source)
    {
        if (_client?.IsEnabled("diagnostics") != true) return;
        try { CaptureError(ApTelemetryException.Create(exception, source)); }
        catch { /* Telemetry must not change the failure being diagnosed. */ }
    }

    private static bool FilterDiagnostics(TelemetryCaptureContext context)
    {
        // RitsuLib invokes this after consent and before building its raw exception payload.
        // Submit a separate symbol-only event, then reject the raw event. Never mutate SourceData.
        if (context.EventName == "ap.exception") return true;
        try
        {
            if (context.Exception is { } exception)
                ReportException(exception, context.Source);
            else if (context.EventName == "godot_engine_error" && context.SourceData is JsonObject error)
                CaptureError(ApTelemetryException.CreateEngine(error));
        }
        catch { /* Reject unexpected payloads without recursing into diagnostics. */ }
        return false;
    }

    private static void CaptureError(JsonObject error)
    {
        if (_client?.IsEnabled("diagnostics") != true) return;
        if (!ErrorBudget.TryAccept(error["fingerprint"]!.GetValue<string>(), DateTimeOffset.UtcNow)) return;
        _client.CapturePayload("ap.exception", "diagnostics", error);
    }

    private static void SetRunPhase(bool multiplayer)
    {
        Volatile.Write(ref _mode, multiplayer ? "multiplayer" : "singleplayer");
        Volatile.Write(ref _phase, "run");
    }

    private static void OnMainMenu()
    {
        Volatile.Write(ref _phase, "menu");
        Volatile.Write(ref _mode, "none");
        if (Volatile.Read(ref _compatibility)["snapshot_ready"]?.GetValue<bool>() == true) return;
        // Sample locally after mod initialization; RitsuLib also samples startup facts before consent.
        // Events and contributions remain gated by its independently revocable applicant permission.
        try
        {
            var mods = ModManager.GetLoadedMods().ToArray();
            var targets = Harmony.GetAllPatchedMethods()
                .Select(method => (Method: method, Info: Harmony.GetPatchInfo(method)))
                .Where(entry => entry.Info?.Owners.Contains(HarmonyOwner) == true)
                .OrderBy(entry => entry.Method.DeclaringType?.FullName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Method.ToString(), StringComparer.Ordinal).ToArray();
            var patches = new JsonArray();
            foreach (var (method, info) in targets.Take(256))
            {
                patches.Add(new JsonObject
                {
                    ["target"] = ApTelemetryException.Symbol($"{method.DeclaringType?.FullName}.{method}"),
                    ["owners"] = new JsonArray(info!.Owners.Order(StringComparer.Ordinal).Take(16)
                        .Select(owner => (JsonNode?)JsonValue.Create(ApTelemetryException.Symbol(owner, 128))).ToArray()),
                    ["prefixes"] = info.Prefixes.Count(patch => patch.owner == HarmonyOwner),
                    ["postfixes"] = info.Postfixes.Count(patch => patch.owner == HarmonyOwner),
                    ["transpilers"] = info.Transpilers.Count(patch => patch.owner == HarmonyOwner),
                    ["finalizers"] = info.Finalizers.Count(patch => patch.owner == HarmonyOwner),
                });
            }
            Volatile.Write(ref _compatibility, new JsonObject
            {
                ["snapshot_ready"] = true,
                ["loaded_mod_count"] = mods.Length,
                ["mods"] = new JsonArray(mods.Take(64).Select(mod => (JsonNode?)new JsonObject
                {
                    ["id"] = ApTelemetryException.Symbol(mod.manifest?.id),
                    ["version"] = ApTelemetryException.Symbol(mod.manifest?.version, 64),
                }).ToArray()),
                ["ap_patched_target_count"] = targets.Length,
                ["patches"] = patches,
                ["truncated"] = mods.Length > 64 || targets.Length > 256 || targets.Any(t => t.Info!.Owners.Count > 16),
            });
            _client?.Capture("ap.compatibility", "basic_usage");
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Telemetry compatibility snapshot unavailable ({ex.GetType().Name}).");
        }
    }

    internal sealed class ProxyAdapter : ITelemetryAdapter
    {
        private readonly PostHogTelemetryAdapter _postHog = new(
            "https://sts2-ap-telemetry.terairkgaming.workers.dev", "proxy");
        public string AdapterId => _postHog.AdapterId;
        public string EndpointDescription => "PostHog EU via Cloudflare";

        public async ValueTask<TelemetrySendResult> SendAsync(TelemetryApplicant applicant,
            IReadOnlyList<TelemetryEnvelope> events, CancellationToken cancellationToken = default)
        {
            // RitsuLib can flush 1,000 queued events. Small chunks keep bounded patch snapshots
            // below the proxy's body limit. Stable proxy UUIDs make partial-batch retries deduplicable.
            foreach (TelemetryEnvelope[] chunk in events.Chunk(4))
            {
                if (chunk.Any(evt => !TelemetryApi.GetClient(applicant.ApplicantId).IsEnabled(evt.RequestId)))
                    return TelemetrySendResult.Fail("Telemetry permission revoked.");
                var result = await _postHog.SendAsync(applicant, chunk, cancellationToken).ConfigureAwait(false);
                if (!result.Success) return result;
            }
            return TelemetrySendResult.Ok();
        }
    }

    private sealed class CompatibilityContribution(TelemetryDataCategory category) : ITelemetryContributionProvider
    {
        public string ContributorModId => ModEntry.ModId;
        public string ContributionId => "compatibility_" + category;
        public TelemetryDataCategory Category => category;
        public TelemetryContributionVisibility Visibility => TelemetryContributionVisibility.PrivateToApplicant;

        public JsonNode? Build(TelemetryContributionContext context)
        {
            if (context.ApplicantId != ApplicantId) return null;
            JsonObject snapshot = (JsonObject)Volatile.Read(ref _compatibility).DeepClone();
            snapshot["ap_version"] = typeof(ModEntry).Assembly.GetName().Version?.ToString();
            snapshot["patch_application"] = Volatile.Read(ref _patchApplication);
            snapshot["patch_failure_type"] = Volatile.Read(ref _patchFailureType);
            snapshot["game_phase"] = Volatile.Read(ref _phase);
            snapshot["game_mode"] = Volatile.Read(ref _mode);
            snapshot["telemetry_version"] = 1;
#if DEBUG
            snapshot["build_configuration"] = "Debug";
#else
            snapshot["build_configuration"] = "Release";
#endif
            return snapshot;
        }
    }
}
