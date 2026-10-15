using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using StS2AP.Utils;

namespace StS2AP.Patches;

[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Init))]
internal static class Patches_SharedCharacterButtons
{
    [HarmonyPrefix]
    [HarmonyAfter("IntoTheSpireverse")]
    private static void Prefix(ref CharacterModel character, ICharacterSelectButtonDelegate del) =>
        IntoTheSpireverseSelection.ResolveInit(ref character, del);

    [HarmonyPostfix]
    [HarmonyAfter("IntoTheSpireverse")]
    private static void Postfix(NCharacterSelectButton __instance, ICharacterSelectButtonDelegate del) =>
        IntoTheSpireverseSelection.RefreshArrow(__instance, del);
}
