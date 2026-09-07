namespace StS2AP.Utils;

/// <summary>
/// Run-local DeathLink deduplication and lethal-damage echo suppression. The caller serializes
/// access. AP event identity is source + timestamp; delivery identity also includes the STS
/// recipient, so players sharing a slot never consume each other's incoming events.
/// </summary>
internal sealed class DeathLinkEventLedger
{
    private static readonly TimeSpan EchoFallbackWindow = TimeSpan.FromSeconds(6);
    private readonly HashSet<(ulong Owner, string Source, long Timestamp)> _accepted = new();
    private readonly HashSet<(string Source, long Timestamp)> _sent = new();
    private readonly HashSet<ulong> _activeDamage = new();
    private readonly Dictionary<ulong, DateTime> _recentLethalDamage = new();

    public bool TryAcceptInbound(ulong owner, string source, long timestamp) =>
        _accepted.Add((owner, source, timestamp));

    public void RecordSent(string source, long timestamp) => _sent.Add((source, timestamp));

    // Only the sending process uses this check. Other players should receive the same event.
    public bool WasSent(string source, long timestamp) => _sent.Contains((source, timestamp));

    public void BeginDamage(ulong player, bool lethal, DateTime now)
    {
        _activeDamage.Add(player);
        if (lethal)
            _recentLethalDamage[player] = now;
    }

    public void EndDamage(ulong player, bool isDead)
    {
        _activeDamage.Remove(player);
        if (!isDead)
            _recentLethalDamage.Remove(player);
    }

    public bool ShouldSuppressOutgoing(ulong player, DateTime now, out string reason)
    {
        if (_activeDamage.Contains(player))
        {
            _recentLethalDamage.Remove(player);
            reason = "the death is being applied by an incoming DeathLink";
            return true;
        }
        if (_recentLethalDamage.Remove(player, out DateTime receivedAt)
            && now - receivedAt <= EchoFallbackWindow)
        {
            reason = $"incoming lethal damage was received {(now - receivedAt).TotalSeconds:F2}s ago";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public void Clear()
    {
        _accepted.Clear();
        _sent.Clear();
        _activeDamage.Clear();
        _recentLethalDamage.Clear();
    }
}
