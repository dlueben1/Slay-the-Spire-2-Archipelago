using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    /// <summary>
    /// Allows us to control the unlock state of characters, acts, ascension levels, etc. in the game.
    /// </summary>
    [HarmonyPatch(typeof(UnlockState))]
    public static class UnlockStatePatches
    {
        /// <summary>
        /// Allows us to control which characters are registered as unlocked, using local state instead of in-game data/saves
        /// </summary>
        [HarmonyPatch("get_Characters")]
        [HarmonyPostfix]
        static void OverrideCharacters(ref IEnumerable<CharacterModel> __result)
        {
            __result = GameUtility.UnlockedCharacters;
        }
    }
}
