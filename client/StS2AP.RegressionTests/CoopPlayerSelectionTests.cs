using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CoopPlayerSelectionTests
{
    [Fact]
    public void OnlyTheSameApSlotNeedsConsent()
    {
        var first = Member(1);
        var selection = new CoopPlayerSelection();
        Assert.True(selection.RequiresConfirmation([first, Member(2)]));
        Assert.False(selection.RequiresConfirmation([first, Member(2) with { Slot = 2 }]));
        Assert.False(selection.RequiresConfirmation([first, Member(2) with { Team = 2 }]));
        Assert.False(selection.RequiresConfirmation([first, Member(2) with { RoomSeed = "other" }]));
        Assert.False(selection.RequiresConfirmation([
            Member(1) with { RoomSeed = null, Team = null, Slot = null },
            Member(2) with { RoomSeed = null, Team = null, Slot = null }]));
    }

    [Fact]
    public void ConsentAllowsASharedSlotAndSurvivesLobbyReordering()
    {
        var selection = new CoopPlayerSelection();
        CoopPlayerSelection.Member[] roster = [Member(1), Member(2)];
        Assert.True(selection.Confirm(roster, [roster[1], roster[0]]));
        Assert.False(selection.RequiresConfirmation(roster));
        Assert.False(selection.RequiresConfirmation([roster[1], roster[0]]));
    }

    [Fact]
    public void AChangedPeerOrApIdentityCannotReuseConsent()
    {
        var selection = new CoopPlayerSelection();
        CoopPlayerSelection.Member[] roster = [Member(1), Member(2)];
        Assert.True(selection.Confirm(roster, roster));
        Assert.True(selection.RequiresConfirmation([Member(1), Member(3)]));
        Assert.True(selection.RequiresConfirmation([
            Member(1) with { Slot = 2 }, Member(2) with { Slot = 2 }]));
    }

    [Fact]
    public void StalePopupCannotApproveTheReplacementRoster()
    {
        var selection = new CoopPlayerSelection();
        CoopPlayerSelection.Member[] displayed = [Member(1), Member(2)];
        CoopPlayerSelection.Member[] current = [Member(1), Member(3)];
        Assert.False(selection.Confirm(displayed, current));
        Assert.True(selection.RequiresConfirmation(current));
        Assert.True(selection.RequiresConfirmation(displayed));
    }

    private static CoopPlayerSelection.Member Member(ulong netId) => new(netId, "seed", 0, 1);
}
