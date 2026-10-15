using System.Text.Json;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class AncientSettingsTests
{
    [Fact]
    public void EachOverrideIsIndependentAndLeavesSlotDefaultsUntouched()
    {
        foreach (var mode in Enum.GetValues<AncientRelicLocation>())
        foreach (var pool in Enum.GetValues<AncientRelicPoolMode>())
        foreach (var modeOverride in new AncientRelicLocation?[] { null, AncientRelicLocation.StartOfAct, AncientRelicLocation.Anytime })
        foreach (var poolOverride in new AncientRelicPoolMode?[] { null, AncientRelicPoolMode.Balanced, AncientRelicPoolMode.Chaos, AncientRelicPoolMode.TrueChaos })
        {
            var slot = new AncientRewardSettings(mode, pool);
            var effective = slot.WithOverrides(new ClientSettings
            {
                AncientRelicLocationOverride = modeOverride,
                AncientRelicPoolOverride = poolOverride,
            });
            Assert.Equal(modeOverride ?? mode, effective.Location);
            Assert.Equal(poolOverride ?? pool, effective.Pool);
            Assert.Equal(new AncientRewardSettings(mode, pool), slot);
        }
    }

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
    public void ProgressReplicationPreservesIndependentPlayerPolicies()
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
        var initial = ApProgressDelta.Between(new(), guest).ApplyToCopy(new());
        Assert.Equal(guest.AncientSettingsForRun, initial.AncientSettingsForRun);
        var afterClaim = new ApProgressDelta().ApplyToCopy(guest);
        afterClaim.UsedItems.Add(42);
        var delta = ApProgressDelta.Between(guest, afterClaim);
        Assert.Null(delta.AncientSettingsForRun);
        var restoredDelta = JsonSerializer.Deserialize<ApProgressDelta>(JsonSerializer.Serialize(delta))!;
        Assert.Equal(guest.AncientSettingsForRun, restoredDelta.ApplyToCopy(initial).AncientSettingsForRun);
        Assert.NotEqual(host.AncientSettingsForRun, initial.AncientSettingsForRun);
        Assert.False(ApProgressDelta.Between(guest, new ApProgressDelta().ApplyToCopy(guest)).HasChanges);
    }
}
