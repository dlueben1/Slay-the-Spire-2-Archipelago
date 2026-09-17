using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApConnectionIndicatorPolicyTests
{
    [Theory]
    [InlineData(true, false, false, "Connected")]
    [InlineData(false, true, false, "Connecting")]
    [InlineData(false, false, true, "Reconnecting")]
    [InlineData(false, false, false, "Disconnected")]
    [InlineData(true, true, true, "Connected")]
    [InlineData(false, true, true, "Reconnecting")]
    public void ResolvesTheEffectiveConnectionPresentation(
        bool connected,
        bool connecting,
        bool reconnecting,
        string expected)
    {
        Assert.Equal(
            Enum.Parse<ApConnectionIndicatorState>(expected),
            ApConnectionIndicatorPolicy.Resolve(connected, connecting, reconnecting)
        );
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void ShowsOnlyForBoundNonGuestApPlayers(
        bool playerBound,
        bool settingsAvailable,
        bool isLocalMultiplayerGuest,
        bool expected)
    {
        Assert.Equal(
            expected,
            ApConnectionIndicatorPolicy.ShouldShow(
                playerBound,
                settingsAvailable,
                isLocalMultiplayerGuest
            )
        );
    }
}
