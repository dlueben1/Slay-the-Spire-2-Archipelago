using System.Text.Json;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class AncientSettingsTests
{
    [Fact]
    public void ContinueRetainsPolicyAndAssignmentsWhileNextRunUsesNewPreferences()
    {
        var defaults = new AncientRewardSettings(AncientRelicLocation.StartOfAct, AncientRelicPoolMode.Balanced);
        var preferences = new ClientSettings
        {
            AncientRelicLocationOverride = AncientRelicLocation.Anytime,
            AncientRelicPoolOverride = AncientRelicPoolMode.TrueChaos,
        };
        var run = new ApRunProgressState
        {
            Initialized = true,
            AncientSettingsForRun = defaults.WithOverrides(preferences),
            AncientRelicChoiceAssignments = new() { [42] = new() { "A", "B", "C" } },
            UsedItems = new() { 42 },
        };
        string saved = JsonSerializer.Serialize(run);
        preferences.AncientRelicLocationOverride = null;
        preferences.AncientRelicPoolOverride = null;
        var continued = JsonSerializer.Deserialize<ApRunProgressState>(saved)!;
        Assert.Equal(run.AncientSettingsForRun, continued.AncientSettingsForRun);
        Assert.Equal(run.AncientRelicChoiceAssignments[42], continued.AncientRelicChoiceAssignments[42]);
        Assert.Equal(run.UsedItems, continued.UsedItems);
        Assert.Equal(defaults, defaults.WithOverrides(preferences));
        Assert.NotEqual(continued.AncientSettingsForRun, defaults.WithOverrides(preferences));
    }

    [Fact]
    public void ProgressCheckpointsPreserveIndependentPlayerPolicies()
    {
        var host = new ApRunProgressState
        {
            Initialized = true,
            AncientSettingsForRun = new(AncientRelicLocation.StartOfAct, AncientRelicPoolMode.Balanced),
        };
        var guest = new ApRunProgressState
        {
            Initialized = true,
            AncientSettingsForRun = new(AncientRelicLocation.Anytime, AncientRelicPoolMode.Chaos),
        };
        guest.UsedItems.Add(42);
        var restored = JsonSerializer.Deserialize<ApRunProgressState>(JsonSerializer.Serialize(guest))!;
        Assert.Equal(guest.AncientSettingsForRun, restored.AncientSettingsForRun);
        Assert.NotEqual(host.AncientSettingsForRun, restored.AncientSettingsForRun);
        Assert.Equal([42], restored.UsedItems);
    }
}
