using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace StS2AP.Patches;

/// <summary>
/// AP character locks control run selection, not Kaleidoscope's other-character choices.
/// Replace only this relic's pool lookup and retain its native generation and reward lifecycle.
/// </summary>
[HarmonyPatch]
public static class Patches_Kaleidoscope
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type? stateMachine = AccessTools.Method(typeof(Kaleidoscope), nameof(Kaleidoscope.AfterObtained))
            ?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        MethodInfo? moveNext = stateMachine == null ? null : AccessTools.Method(stateMachine, "MoveNext");
        // A game update changed Kaleidoscope; fail the mod's startup rather than keep AP's
        // character locks on its choices without telling anyone.
        yield return moveNext
            ?? throw new MissingMethodException("Could not locate Kaleidoscope's async AfterObtained body to patch.");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo getter = AccessTools.PropertyGetter(typeof(UnlockState), nameof(UnlockState.CharacterCardPools));
        List<CodeInstruction> lookups = code.Where(instruction => instruction.Calls(getter)).ToList();
        FieldInfo? relicField = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        if (lookups.Count != 1 || relicField == null)
        {
            throw new InvalidOperationException(
                $"Could not safely replace Kaleidoscope card pools (lookups={lookups.Count}, ownerField={relicField != null}).");
        }

        // Pass the relic itself so replicas resolve its owner, even if players share an
        // UnlockState. Keep the original getter instruction's labels and exception blocks.
        int lookupIndex = code.IndexOf(lookups[0]);
        lookups[0].opcode = OpCodes.Ldarg_0;
        lookups[0].operand = null;
        code.InsertRange(lookupIndex + 1,
        [
            new CodeInstruction(OpCodes.Ldfld, relicField),
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(Patches_Kaleidoscope), nameof(GetCardPools))),
        ]);
        return code;
    }

    private static IEnumerable<CardPoolModel> GetCardPools(UnlockState unlockState, Kaleidoscope relic)
    {
        var player = relic.Owner;
        if (!CrossCharacterCardPoolUtility.TryGetPools(player, out var pools))
            return unlockState.CharacterCardPools;

        LogUtility.Info($"Kaleidoscope: using {pools.Count} owner-scoped character card pools for player {player.NetId}");
        return pools;
    }
}
