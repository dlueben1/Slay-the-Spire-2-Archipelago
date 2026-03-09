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
    /// Patches for `Player`
    /// </summary>
    public static class PlayerPatches
    {
        /// <summary>
        /// Grabs a reference to the player character that we can access globally.
        /// The Player is the entry point to many things, such as giving gold, damage, rewards, etc. so having a reference to it is important.
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
