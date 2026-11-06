using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace StS2AP.Patches;

/// <summary>
/// AP character locks must not restrict Splash's other-character attack choices.
/// Keep its native pool exclusion, combat RNG, upgrades, and card-selection lifecycle.
/// </summary>
[HarmonyPatch]
public static class Patches_Splash
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        // The async body and its captured card field are compiler-generated, so Harmony
        // must locate them through reflection rather than directly referencing game members.
        Type? stateMachine = AccessTools.DeclaredMethod(typeof(Splash), "OnPlay")
            ?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        MethodInfo? moveNext = stateMachine == null ? null : AccessTools.Method(stateMachine, "MoveNext");
        // A game update changed Splash; fail the mod's startup rather than keep AP's
        // character locks on its choices without telling anyone.
        yield return moveNext
            ?? throw new MissingMethodException("Could not locate Splash's async OnPlay body to patch.");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo getter = AccessTools.PropertyGetter(typeof(UnlockState), nameof(UnlockState.CharacterCardPools));
        List<CodeInstruction> lookups = code.Where(instruction => instruction.Calls(getter)).ToList();
        FieldInfo? cardField = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        if (lookups.Count != 1 || cardField?.FieldType != typeof(Splash))
        {
            throw new InvalidOperationException(
                $"Could not safely replace Splash card pools (lookups={lookups.Count}, cardField={cardField != null}).");
        }

        // As with Kaleidoscope, pass the effect's owner rather than resolving a player
        // from UnlockState, which can be shared. Preserve the getter's labels and blocks.
        int lookupIndex = code.IndexOf(lookups[0]);
        lookups[0].opcode = OpCodes.Ldarg_0;
        lookups[0].operand = null;
        code.InsertRange(lookupIndex + 1,
        [
            new CodeInstruction(OpCodes.Ldfld, cardField),
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(Patches_Splash), nameof(GetCardPools))),
        ]);
        return code;
    }

    private static IEnumerable<CardPoolModel> GetCardPools(UnlockState unlockState, Splash card) =>
        CrossCharacterCardPoolUtility.TryGetPools(card.Owner, out var pools)
            ? pools
            : unlockState.CharacterCardPools;
}
