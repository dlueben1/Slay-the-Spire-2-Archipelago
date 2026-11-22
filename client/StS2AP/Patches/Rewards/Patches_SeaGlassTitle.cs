using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Relics;

namespace StS2AP.Patches;

/// <summary>
/// Sea Glass titles itself with a per-character key such as
/// <c>SEA_GLASS.KAKAROTMOD-KAKAROT.title</c>. Vanilla characters have those entries, but
/// modded characters only do if their mod ships one, so otherwise the raw key is shown.
/// Fall back to the generic "Sea Glass" title; the description still names the character.
/// </summary>
[HarmonyPatch(typeof(SeaGlass), nameof(SeaGlass.Title), MethodType.Getter)]
public static class Patches_SeaGlassTitle
{
    [HarmonyPostfix]
    private static void Postfix(SeaGlass __instance, ref LocString __result)
    {
        if (!__result.Exists())
            __result = new LocString("relics", __instance.Id.Entry + ".title");
    }
}
