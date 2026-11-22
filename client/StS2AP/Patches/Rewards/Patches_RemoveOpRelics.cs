using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Applies the Rare remove_op_relics category to every relic source that honors
/// RelicModel.IsAllowed: native grab bags (elites, chests, shops, Shovel, Pael's Wing and
/// capsules) and AP's own relic selector. White Star overrides IsAllowed without calling the
/// base method, so each relic's own implementation is patched rather than only the base.
/// </summary>
[HarmonyPatch]
public static class Patches_RemoveOpRelics
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods() =>
        OpRelicPolicy.RareRelicTypes
            .Select(type => AccessTools.Method(type, nameof(RelicModel.IsAllowed), [typeof(IRunState)]))
            .Distinct();

    [HarmonyPostfix]
    private static void Postfix(RelicModel __instance, IRunState runState, ref bool __result)
    {
        if (__result && OpRelicPolicy.IsRemovedFromRun(__instance, runState))
            __result = false;
    }
}
