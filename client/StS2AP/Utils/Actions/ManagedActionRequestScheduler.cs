using Godot;

namespace StS2AP.Utils;

/// <summary>
/// Holds requests outside the native action queue until admission and network transport are ready.
/// </summary>
public static class ManagedActionRequestScheduler
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<Guid, PendingRequest> Pending = new();

    private static SceneTree? _sceneTree;
    private static bool _processFrameHooked;

    private sealed record PendingRequest(
        Guid ActionId,
        string Description,
        Func<bool> TryRequest,
        Func<bool> CanRequest,
        Func<bool> IsStillCurrent,
        Action OnSuccess,
        Action<string> OnFailure,
        DateTime DeadlineUtc
    );

    public static void RequestOrDefer(
        Guid actionId,
        string description,
        Func<bool> tryRequest,
        Func<bool> isStillCurrent,
        Action onSuccess,
        Action<string> onFailure,
        Func<bool>? canRequest = null)
    {
        Func<bool> requestAllowed = canRequest ?? AlwaysAllowRequest;
        if (requestAllowed() && tryRequest())
        {
            onSuccess();
            return;
        }

        Pending[actionId] = new PendingRequest(
            actionId,
            description,
            tryRequest,
            requestAllowed,
            isStillCurrent,
            onSuccess,
            onFailure,
            DateTime.UtcNow + RequestTimeout
        );
        LogUtility.Warn(
            $"Managed action {description} is waiting for a safe action slot or network transport."
        );

        if (!TryHookProcessFrame())
        {
            Pending.Remove(actionId);
            onFailure($"could not schedule managed action {description} for a transport retry");
        }
    }

    public static void EndRun()
    {
        Pending.Clear();
        UnhookProcessFrame();
    }

    private static bool TryHookProcessFrame()
    {
        if (_processFrameHooked)
            return true;
        if (Engine.GetMainLoop() is not SceneTree sceneTree)
            return false;

        _sceneTree = sceneTree;
        _sceneTree.ProcessFrame += ProcessPending;
        _processFrameHooked = true;
        return true;
    }

    private static void ProcessPending()
    {
        foreach (PendingRequest request in Pending.Values.ToArray())
        {
            if (!Pending.ContainsKey(request.ActionId))
                continue;
            if (!request.IsStillCurrent())
            {
                Pending.Remove(request.ActionId);
                continue;
            }

            // Non-combat managed actions must not enter a native player queue during combat:
            // they sit at the front but are ineligible to execute, blocking that player's cards
            // and end-turn action. Time spent waiting for the safe boundary does not consume the
            // transport timeout.
            if (!request.CanRequest())
            {
                Pending[request.ActionId] = request with
                {
                    DeadlineUtc = DateTime.UtcNow + RequestTimeout,
                };
                continue;
            }

            bool requested;
            try
            {
                requested = request.TryRequest();
            }
            catch (Exception ex)
            {
                Pending.Remove(request.ActionId);
                request.OnFailure(
                    $"managed action {request.Description} retry failed: {ex.Message}"
                );
                continue;
            }

            if (requested)
            {
                Pending.Remove(request.ActionId);
                request.OnSuccess();
                continue;
            }
            if (DateTime.UtcNow < request.DeadlineUtc)
                continue;

            Pending.Remove(request.ActionId);
            request.OnFailure(
                $"could not enqueue managed action {request.Description} within "
                    + $"{RequestTimeout.TotalSeconds:0} seconds"
            );
        }

        if (Pending.Count == 0)
            UnhookProcessFrame();
    }

    private static void UnhookProcessFrame()
    {
        if (_processFrameHooked && _sceneTree != null)
            _sceneTree.ProcessFrame -= ProcessPending;
        _sceneTree = null;
        _processFrameHooked = false;
    }

    private static bool AlwaysAllowRequest() => true;
}
