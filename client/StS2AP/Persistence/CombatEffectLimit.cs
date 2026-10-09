namespace StS2AP.Persistence;

// Room IDs restart at each map point; nested event combats have distinct room IDs.
// Buffs use turn zero for a combat-wide allowance; traps use the player's turn number.
public sealed record CombatEffectKey(int ActIndex, int Floor, int RoomId, int Turn = 0);

/// <summary>
/// Per-player combat or turn allowance, saved with the run and advanced by the same action on
/// every peer. The transient reservation prevents overlapping asynchronous applications.
/// </summary>
public sealed class CombatEffectLimit
{
    public CombatEffectKey? LastConsumedCombat { get; set; }
    private CombatEffectKey? _applying;

    public bool CanConsume(CombatEffectKey combat) =>
        _applying == null && LastConsumedCombat != combat;

    public bool TryBegin(CombatEffectKey combat)
    {
        if (!CanConsume(combat))
            return false;
        _applying = combat;
        return true;
    }

    public void Complete(CombatEffectKey combat)
    {
        if (_applying != combat)
            throw new InvalidOperationException("No effect application is reserved for this combat/turn.");
        LastConsumedCombat = combat;
        _applying = null;
    }

    public void Cancel() => _applying = null;
}
