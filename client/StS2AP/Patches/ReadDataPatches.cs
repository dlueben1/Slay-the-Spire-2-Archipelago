using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
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
    /// Collection of Harmony Patches related to reading data from Slay the Spire to make it accessible in the Archipelago Mod
    /// </summary>
    public static class ReadDataPatches
    {
        /// <summary>
        /// Grabs a reference to the player character
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun),
            new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
        public class CachePlayerPatch
        {
            static void Postfix(Player __result)
            {
                LogUtility.Debug("Caching Player");
                GameUtility.CurrentPlayer = __result;
            }
        }
    }
}
