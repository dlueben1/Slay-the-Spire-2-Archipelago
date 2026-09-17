namespace StS2AP.Utils;

internal enum ApConnectionIndicatorState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

internal static class ApConnectionIndicatorPolicy
{
    public static ApConnectionIndicatorState Resolve(
        bool connected,
        bool connecting,
        bool reconnecting)
    {
        if (connected)
            return ApConnectionIndicatorState.Connected;
        if (reconnecting)
            return ApConnectionIndicatorState.Reconnecting;
        if (connecting)
            return ApConnectionIndicatorState.Connecting;
        return ApConnectionIndicatorState.Disconnected;
    }

    public static bool ShouldShow(
        bool playerBound,
        bool settingsAvailable,
        bool isLocalMultiplayerGuest) =>
        playerBound && settingsAvailable && !isLocalMultiplayerGuest;
}
