using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class SharedCharacterSelectionTests
{
    [Theory]
    [InlineData("Ironclad", "Tectonic")]
    [InlineData("Tectonic", "Ironclad")]
    public void OppositeSavedFormCannotHideTheOnlyConfiguredMember(string remembered, string configured)
    {
        Assert.Equal(configured, SharedCharacterSelectionPolicy.Choose(
            [configured], remembered, _ => true, preferUnlocked: true));
        // Init runs after the mod restores its remembered form as well.
        Assert.Equal(configured, SharedCharacterSelectionPolicy.Choose(
            [configured], remembered, _ => true, preferUnlocked: false));
    }

    [Fact]
    public void MenuUsesUnlockedAlternateWhenTheBaseIsLocked()
    {
        Assert.Equal("Tectonic", SharedCharacterSelectionPolicy.Choose(
            ["Ironclad", "Tectonic"], "Ironclad", name => name == "Tectonic", preferUnlocked: true));
    }

    [Fact]
    public void ExplicitCyclingCanDisplayALockedMemberWithoutUnlockingIt()
    {
        var unlocked = new HashSet<string> { "Ironclad" };
        var selected = SharedCharacterSelectionPolicy.Choose(
            ["Ironclad", "Tectonic"], "Tectonic", unlocked.Contains, preferUnlocked: false);
        Assert.Equal("Tectonic", selected);
        Assert.DoesNotContain(selected!, unlocked);

        // Once the item arrives, refreshing the menu retains the alternate.
        unlocked.Add("Tectonic");
        Assert.Equal("Tectonic", SharedCharacterSelectionPolicy.Choose(
            ["Ironclad", "Tectonic"], selected!, unlocked.Contains, preferUnlocked: true));
    }

    [Fact]
    public void NoConfiguredMemberProducesNoVisibleSelection()
    {
        Assert.Null(SharedCharacterSelectionPolicy.Choose<string>(
            [], "Ironclad", _ => true, preferUnlocked: true));
    }

    [Fact]
    public void EntirelyLockedFamilyStillHasAVisibleLockedMember()
    {
        Assert.Equal("Tectonic", SharedCharacterSelectionPolicy.Choose(
            ["Tectonic"], "Ironclad", _ => false, preferUnlocked: true));
    }

    [Fact]
    public void SwitchingSlotsOrReturningToGuestDoesNotReuseTheFilteredRoster()
    {
        string[] wholeFamily = ["Ironclad", "Tectonic"];
        var alternateOnly = wholeFamily.Where(name => name == "Tectonic").ToArray();
        var vanillaOnly = wholeFamily.Where(name => name == "Ironclad").ToArray();
        var selected = SharedCharacterSelectionPolicy.Choose(alternateOnly, "Ironclad", _ => true, true)!;
        selected = SharedCharacterSelectionPolicy.Choose(vanillaOnly, selected, _ => true, true)!;
        Assert.Equal("Ironclad", selected);
        Assert.Equal("Tectonic", SharedCharacterSelectionPolicy.Choose(wholeFamily, "Tectonic", _ => true, false));
    }
}
