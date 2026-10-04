using System.Runtime.CompilerServices;
using StS2AP.Utils;
using StS2AP.Persistence;
using System.Text.Json;
using Xunit;

namespace StS2AP.RegressionTests;

public class TelemetryTests
{
    [Fact]
    public void GameplayLedgerSurvivesRestartAndCheckpointRollback()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "campaign.json");
        try
        {
            Guid savedRun = Guid.NewGuid();
            var first = ApGameplayLedger.Load(path);
            string run = first.RunId(savedRun);
            string settings = first.EventId("ap.settings_observed:{options}");
            string choice = first.EventId($"ap.ancient_selected:{run}:42");
            Assert.True(first.Reserve(path, settings));
            Assert.True(first.Reserve(path, choice));

            // Exercise the actual checkpoint envelope and source-generated serializer: native
            // run JSON alone does not carry RitsuLib's attached run identity.
            var checkpoint = new SerializableAP { RunId = savedRun };
            string json = JsonSerializer.Serialize(checkpoint, ApSerializationContext.Default.SerializableAP);
            Assert.Contains("\"run_id\"", json);
            var reloaded = JsonSerializer.Deserialize(json, ApSerializationContext.Default.SerializableAP)!;
            Assert.Equal(savedRun, reloaded.RunId);
            Assert.Equal(Guid.Empty, JsonSerializer.Deserialize("{}",
                ApSerializationContext.Default.SerializableAP)!.RunId);

            var restarted = ApGameplayLedger.Load(path);
            Assert.Equal(first.CampaignId, restarted.CampaignId);
            Assert.Equal(run, restarted.RunId(reloaded.RunId));
            Assert.Equal(choice, restarted.EventId($"ap.ancient_selected:{run}:42"));
            Assert.False(restarted.Reserve(path, settings));
            Assert.False(restarted.Reserve(path, choice)); // Restored save, even a different choice.
            Assert.True(restarted.Reserve(path, restarted.EventId($"ap.ancient_selected:{run}:43")));
            string nextRun = restarted.RunId(Guid.NewGuid());
            Assert.NotEqual(run, nextRun);
            Assert.True(restarted.Reserve(path, restarted.EventId($"ap.ancient_selected:{nextRun}:42")));
            var otherCampaign = ApGameplayLedger.Load(Path.Combine(directory, "other.json"));
            Assert.NotEqual(first.CampaignId, otherCampaign.CampaignId);
            Assert.NotEqual(run, otherCampaign.RunId(savedRun));
            Assert.NotEqual(settings, otherCampaign.EventId("ap.settings_observed:{options}"));

            File.WriteAllText(path, "{}"); // Missing identity must not silently reset the ledger.
            Assert.Throws<System.Text.Json.JsonException>(() => ApGameplayLedger.Load(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void GameplayOptionsAllowOnlyResolvedSettingsAndRejectInvalidValues()
    {
        var slot = new Dictionary<string, object>
        {
            ["progressive_starter_card"] = 1L, ["progressive_starter_relic"] = false,
            ["shop_sanity"] = 0, ["ancient_relic_location"] = 1, ["ancient_relic_pool"] = 2,
            ["seed"] = "SECRET", ["player_name"] = "SECRET", ["bonus_items"] = "SECRET",
            ["characters"] = new[] { new { name = "IRONCLAD", seed = "SECRET" } },
        };
        var report = ApGameplayOptions.Create(slot);
        Assert.True(report["progressive_starter_card"]!.GetValue<bool>());
        Assert.False(report["progressive_starter_relic"]!.GetValue<bool>());
        Assert.Equal("anytime", report["ancient_relic_location"]!.GetValue<string>());
        Assert.Equal("true_chaos", report["ancient_relic_pool"]!.GetValue<string>());
        Assert.Equal(5, report.Count);
        Assert.DoesNotContain("SECRET", report.ToJsonString());
        Assert.False(report.ContainsKey("gold_sanity")); // Missing is unknown, not disabled.
        slot["shop_sanity"] = 42;
        Assert.Throws<InvalidDataException>(() => ApGameplayOptions.Create(slot));
    }

    [Fact]
    public void ExceptionReportKeepsSymbolsButNotMessagesPathsOrCustomData()
    {
        Exception exception = CapturedException();
        exception.Data["server"] = "private.example:1234";
        var report = ApTelemetryException.Create(exception, "ap_patch_application");
        string json = report.ToJsonString();
        Assert.Contains("InvalidOperationException", json);
        Assert.Contains(nameof(CapturedException), json);
        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain("private.example", json);
        Assert.DoesNotContain("TelemetryTests.cs", json);
        Assert.DoesNotContain("/home/", json);
        Assert.DoesNotContain("C:", json);
        Assert.Equal(64, report["fingerprint"]!.GetValue<string>().Length);
    }

    [Fact]
    public void EngineReportDoesNotTransmitUnstructuredText()
    {
        var report = ApTelemetryException.CreateEngine(new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = 1, ["function"] = "process_frame", ["message"] = "SECRET",
            ["code"] = "SECRET", ["file"] = "C:/Users/SECRET/game.cs",
            ["script_backtrace"] = "SECRET",
        });
        Assert.Contains("process_frame", report.ToJsonString());
        Assert.DoesNotContain("SECRET", report.ToJsonString());
    }

    [Fact]
    public void BudgetSuppressesDuplicatesAndBoundsMinuteAndSessionWork()
    {
        var budget = new ApTelemetryErrorBudget();
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 20; i++) Assert.True(budget.TryAccept(i.ToString(), now));
        Assert.False(budget.TryAccept("20", now));
        Assert.False(budget.TryAccept("0", now.AddMinutes(1)));
        for (int i = 20; i < 200; i++) Assert.True(budget.TryAccept(i.ToString(), now.AddMinutes(i / 20)));
        Assert.False(budget.TryAccept("200", now.AddHours(1)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception CapturedException()
    {
        try { throw new InvalidOperationException("SECRET C:/Users/SECRET", new Exception("SECRET inner")); }
        catch (Exception ex) { return ex; }
    }
}
