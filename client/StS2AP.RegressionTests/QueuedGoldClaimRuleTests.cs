using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class QueuedGoldClaimRuleTests
{
    [Fact(DisplayName = "The first queued gold claim takes the owner's cursor as its baseline")]
    public void TheFirstQueuedGoldClaimTakesTheOwnersCursorAsItsBaseline()
    {
        QueuedGoldClaimRule.Decision decision = QueuedGoldClaimRule.Evaluate(null, 40, 25, poverty: false);

        Assert.True(decision.Accepted);
        Assert.Equal(25, decision.GrantedAmount);
        Assert.Equal(65, decision.CursorAfter);
    }

    [Fact(DisplayName = "A repeated queued gold claim is refused")]
    public void ARepeatedQueuedGoldClaimIsRefused()
    {
        QueuedGoldClaimRule.Decision first = QueuedGoldClaimRule.Evaluate(null, 0, 30, poverty: false);
        QueuedGoldClaimRule.Decision repeat =
            QueuedGoldClaimRule.Evaluate(first.CursorAfter, 0, 30, poverty: false);

        Assert.True(first.Accepted);
        Assert.False(repeat.Accepted);
        Assert.NotNull(repeat.RefusalReason);
    }

    [Fact(DisplayName = "A queued gold claim made after gold was redeemed another way is accepted")]
    public void AClaimAfterGoldWasRedeemedAnotherWayIsAccepted()
    {
        // The action's cursor is a floor: the owner's cursor may be ahead of it.
        QueuedGoldClaimRule.Decision decision = QueuedGoldClaimRule.Evaluate(30, 50, 10, poverty: false);

        Assert.True(decision.Accepted);
        Assert.Equal(60, decision.CursorAfter);
    }

    [Fact(DisplayName = "Poverty on queued gold claims rounds over the cumulative total")]
    public void PovertyOnQueuedGoldClaimsRoundsOverTheCumulativeTotal()
    {
        int? cursor = null;
        int start = 0;
        int granted = 0;
        for (int i = 0; i < 8; i++)
        {
            QueuedGoldClaimRule.Decision decision =
                QueuedGoldClaimRule.Evaluate(cursor, start, 1, poverty: true);
            Assert.True(decision.Accepted);
            granted += decision.GrantedAmount;
            cursor = start = decision.CursorAfter;
        }

        Assert.Equal(QueuedGoldClaimRule.ApplyPoverty(8), granted);
    }

    [Theory(DisplayName = "Malformed queued gold claims are refused")]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    [InlineData(-1, 10)]
    [InlineData(int.MaxValue, 1)]
    public void MalformedQueuedGoldClaimsAreRefused(int cursorBefore, int sourceAmount)
    {
        Assert.False(QueuedGoldClaimRule.Evaluate(null, cursorBefore, sourceAmount, poverty: false).Accepted);
    }
}
