using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class DeathLinkTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "Duplicate AP callbacks affect each same-slot player once")]
    public void DuplicateAPCallbacksAffectEachSameSlotPlayerOnce()
    {
        var ledger = new DeathLinkEventLedger();
        Assert.True(ledger.TryAcceptInbound(11, "External player", Now.Ticks));
        Assert.False(ledger.TryAcceptInbound(11, "External player", Now.Ticks));
        Assert.True(ledger.TryAcceptInbound(22, "External player", Now.Ticks));
        Assert.False(ledger.TryAcceptInbound(22, "External player", Now.Ticks));
    }

    [Fact(DisplayName = "Independent deaths in the same second are not coalesced")]
    public void IndependentDeathsInTheSameSecondAreNotCoalesced()
    {
        var ledger = new DeathLinkEventLedger();
        Assert.True(ledger.TryAcceptInbound(11, "Shared slot", Now.Ticks));
        Assert.True(ledger.TryAcceptInbound(11, "Shared slot", Now.AddMilliseconds(1).Ticks));
        Assert.True(ledger.TryAcceptInbound(11, "Another slot", Now.Ticks));
    }

    [Fact(DisplayName = "Older self-echoes stay suppressed without blocking another recipient")]
    public void OlderSelfEchoesStaySuppressedWithoutBlockingAnotherRecipient()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.RecordSent("Shared slot", Now.Ticks);
        ledger.RecordSent("Shared slot", Now.AddSeconds(2).Ticks);
        Assert.True(ledger.WasSent("Shared slot", Now.Ticks));
        Assert.True(ledger.WasSent("Shared slot", Now.AddSeconds(2).Ticks));
        Assert.False(ledger.WasSent("Shared slot", Now.AddMilliseconds(1).Ticks));
        Assert.True(ledger.TryAcceptInbound(22, "Shared slot", Now.Ticks));
    }

    [Fact(DisplayName = "Lethal incoming damage does not echo through a same-slot peer")]
    public void LethalIncomingDamageDoesNotEchoThroughASameSlotPeer()
    {
        var host = new DeathLinkEventLedger();
        host.RecordSent("Shared slot", Now.Ticks);
        Assert.True(host.TryAcceptInbound(22, "Shared slot", Now.Ticks));
        host.BeginDamage(22, lethal: true, Now);
        Assert.True(host.ShouldSuppressOutgoing(22, Now, out _));
        Assert.False(host.ShouldSuppressOutgoing(11, Now, out _));
        host.EndDamage(22, isDead: true);
        Assert.False(host.TryAcceptInbound(22, "Shared slot", Now.Ticks));
    }

    [Fact(DisplayName = "An incoming death scope is not limited by the six-second fallback")]
    public void AnIncomingDeathScopeIsNotLimitedByTheSixSecondFallback()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.BeginDamage(11, lethal: true, Now);
        Assert.True(ledger.ShouldSuppressOutgoing(11, Now.AddSeconds(20), out _));
        ledger.EndDamage(11, isDead: true);
    }

    [Fact(DisplayName = "Delayed lethal callback is suppressed once")]
    public void DelayedLethalCallbackIsSuppressedOnce()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.BeginDamage(11, lethal: true, Now);
        ledger.EndDamage(11, isDead: true);
        Assert.True(ledger.ShouldSuppressOutgoing(11, Now.AddSeconds(1), out _));
        Assert.False(ledger.ShouldSuppressOutgoing(11, Now.AddSeconds(2), out _));
    }

    [Fact(DisplayName = "Death prevention clears the lethal fallback")]
    public void DeathPreventionClearsTheLethalFallback()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.BeginDamage(11, lethal: true, Now);
        ledger.EndDamage(11, isDead: false);
        Assert.False(ledger.ShouldSuppressOutgoing(11, Now.AddSeconds(1), out _));
    }

    [Fact(DisplayName = "Nonlethal DeathLink does not suppress a later legitimate death")]
    public void NonlethalDeathLinkDoesNotSuppressALaterLegitimateDeath()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.BeginDamage(11, lethal: false, Now);
        ledger.EndDamage(11, isDead: false);
        Assert.False(ledger.ShouldSuppressOutgoing(11, Now.AddMilliseconds(1), out _));
    }

    [Fact(DisplayName = "Expired lethal fallback does not suppress future deaths")]
    public void ExpiredLethalFallbackDoesNotSuppressFutureDeaths()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.BeginDamage(11, lethal: true, Now);
        ledger.EndDamage(11, isDead: true);
        Assert.False(ledger.ShouldSuppressOutgoing(11, Now.AddSeconds(7), out _));
    }

    [Fact(DisplayName = "New-run reset clears event and suppression state")]
    public void NewRunResetClearsEventAndSuppressionState()
    {
        var ledger = new DeathLinkEventLedger();
        ledger.RecordSent("Slot", Now.Ticks);
        ledger.TryAcceptInbound(11, "Slot", Now.Ticks);
        ledger.BeginDamage(11, lethal: true, Now);
        ledger.Clear();
        Assert.False(ledger.WasSent("Slot", Now.Ticks));
        Assert.True(ledger.TryAcceptInbound(11, "Slot", Now.Ticks));
        Assert.False(ledger.ShouldSuppressOutgoing(11, Now, out _));
    }
}
