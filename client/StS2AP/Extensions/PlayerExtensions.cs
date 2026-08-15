using MegaCrit.Sts2.Core.Entities.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Extensions
{
    public static class PlayerExtensions
    {
        /// <summary>
        /// Returns the name of the current character, as their name appears in the Archipelago's APWorld.
        /// </summary>
        /// <example>An Ironclad instance returns "Ironclad", because items for that character include "Ironclad Card Reward", "Ironclad Relic", etc.</example>
        public static string APName(this Player player)
        {
            var config = ArchipelagoClient.Settings.Characters[player.getInternalName()];
            if(config == null)
            {
                LogUtility.Warn($"Could not find character id for {player.getInternalName()}");
                return player.Character.GetType().Name;
            }

            if(config.ModNum == 0)
            {
                return config.Name;
            }
            else
            {
                return $"Custom Character {config.ModNum}";
            }

        }

        /// <summary>
        /// What the game thinks the character's name is.
        /// </summary>
        public static string getInternalName(this Player player)
        {
            return player.Character.Id.Entry;
        }
    }
}
