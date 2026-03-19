using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace StS2AP.Patches
{
    /// <summary>
    /// Forces all cards to always be available in the card pool for all characters.
    /// </summary>
    [HarmonyPatch]
    public static class Patches_UnlockAllCards
    {
        /// <summary>
        /// List of each character's Card Pool, including the Colorless Card Pool.
        /// </summary>
        private static readonly Type[] CardPoolTypes =
        [
            typeof(ColorlessCardPool),
            typeof(IroncladCardPool),
            typeof(SilentCardPool),
            typeof(DefectCardPool),
            typeof(NecrobinderCardPool),
            typeof(RegentCardPool)
        ];

        /// <summary>
        /// Identifies all the `FilterThroughEpochs` methods from each card pool class that should be patched.
        /// Harmony will apply the postfix patch to each of these methods.
        /// </summary>
        /// <returns>An enumerable of MethodBase objects representing each FilterThroughEpochs method to patch.</returns>
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in CardPoolTypes)
            {
                var method = AccessTools.Method(type, "FilterThroughEpochs");
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        /// <summary>
        /// Postfix patch that replaces the list of filtered cards with all available cards from the pool.
        /// This is applied to all character card pools, ensuring every card is available regardless of unlock status.
        /// </summary>
        /// <param name="__result">The original result from FilterThroughEpochs, which is replaced with all cards.</param>
        /// <param name="__instance">The card pool instance being patched (can be any of the pool types).</param>
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, object __instance)
        {
            // Use reflection to get AllCards from whatever pool type this is
            var allCardsProperty = AccessTools.Property(__instance.GetType(), "AllCards");
            if (allCardsProperty != null)
            {
                __result = ((IEnumerable<CardModel>)allCardsProperty.GetValue(__instance)).ToList();
            }
        }
    }
}
