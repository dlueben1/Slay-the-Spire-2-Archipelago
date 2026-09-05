using Godot;

namespace StS2AP.Utils;

/// <summary>
/// Keeps the authenticated AP slot connected without pausing gameplay.
/// Retry failures remain local; the five-minute threshold raises warning severity only.
/// </summary>
public static class ApReconnectController
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    };

    private static readonly object Sync = new();
    private static CancellationTokenSource? _cancellation;
    private static int _attempt;

    public static bool IsActive
    {
        get
        {
            lock (Sync)
                return _cancellation != null;
        }
    }

    public static void Begin()
    {
        CancellationToken token;
        lock (Sync)
        {
            if (_cancellation != null)
                return;

            _cancellation = new CancellationTokenSource();
            _attempt = 0;
            token = _cancellation.Token;
        }

        LogUtility.Warn("Starting automatic Archipelago reconnect attempts");
        ScheduleNextAttempt(token);
        _ = WarnAfterFiveMinutes(token);
    }

    public static void OnAttemptFailed()
    {
        CancellationToken token;
        lock (Sync)
        {
            if (_cancellation == null)
                return;
            token = _cancellation.Token;
        }

        ScheduleNextAttempt(token);
    }

    public static void OnConnected()
    {
        Stop();
        LogUtility.Info("Automatic Archipelago reconnect completed");
    }

    public static void Stop(string? reason = null)
    {
        CancellationTokenSource? cancellation;
        lock (Sync)
        {
            cancellation = _cancellation;
            _cancellation = null;
            _attempt = 0;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        if (!string.IsNullOrWhiteSpace(reason))
            LogUtility.Warn($"Stopped automatic Archipelago reconnect: {reason}");
    }

    private static void ScheduleNextAttempt(CancellationToken token)
    {
        TimeSpan delay;
        int attemptNumber;
        lock (Sync)
        {
            if (_cancellation == null || token.IsCancellationRequested)
                return;

            delay = RetryDelays[Math.Min(_attempt, RetryDelays.Length - 1)];
            attemptNumber = ++_attempt;
        }

        LogUtility.Info(
            $"Archipelago reconnect attempt {attemptNumber} scheduled in {delay.TotalSeconds:0} seconds"
        );
        _ = RetryAfterDelay(delay, token);
    }

    private static async Task RetryAfterDelay(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        Callable.From(() =>
        {
            if (!token.IsCancellationRequested)
                ArchipelagoClient.ConnectForAutomaticRetry();
        }).CallDeferred();
    }

    private static async Task WarnAfterFiveMinutes(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        LogUtility.Warn(
            "Archipelago has been disconnected for five minutes; gameplay remains available and AP checks stay queued"
        );
        Callable.From(() =>
        {
            if (!token.IsCancellationRequested)
                NotificationUtility.ShowRawText(
                    "Archipelago has been offline for five minutes. Gameplay remains available; AP checks stay queued."
                );
        }).CallDeferred();
    }
}
