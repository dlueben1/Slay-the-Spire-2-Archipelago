using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace StS2AP.Patches
{
    public static class Patches_DisableTutorial
    {
        /// <summary>
        /// Disable the "Enable Tutorials?" dialog
        /// </summary>
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenFtue))]
        public static class DisableTutorialDialogPatch
        {
            [HarmonyPostfix]
            public static void Postfix(string ftueKey, ref bool __result)
            {
                // Always return true for the accept_tutorials_ftue check
                if (ftueKey == "accept_tutorials_ftue")
                {
                    __result = true;
                }
            }
        }

        /// <summary>
        /// Disables the tutorial rewards - when the game is first played, the rewards you get are fixed rather than dynamic.
        /// </summary>
        [HarmonyPatch(typeof(RewardsSet), "TryGenerateTutorialRewards")]
        public class TurnOffTutorialRewardsDuringArchipelagoPatch
        {
            /// <summary>
            /// Skip the original function and set the result to false
            /// </summary>
            static bool Prefix(ref bool __result, Player player, AbstractRoom room)
            {
                __result = false;
                return false;
            }
        }
    }
}
