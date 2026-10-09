using StS2AP.Persistence;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

internal sealed record CombatEffectActionMessage(Guid RunId, int ItemIndex, APItem EffectType,
    CombatEffectKey? Combat)
{
    public bool Matches(Guid runId, CombatEffectKey? currentCombat, int consumedIndex, bool trap) =>
        RunId != Guid.Empty && RunId == runId
        && ItemIndex > 0 && ItemIndex > consumedIndex
        && Combat != null && Combat == currentCombat
        && (trap ? Combat.Turn > 0 : Combat.Turn == 0)
        && (trap ? IsUniversalTrap((long)EffectType) : IsUniversalCombatBuff((long)EffectType))
        && !IsMultiplayerBuffGoldFallback((long)EffectType);
}
