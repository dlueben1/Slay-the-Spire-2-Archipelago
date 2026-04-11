using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StS2AP.Data.CharTable;

namespace StS2AP.Extensions
{
    public static class CharacterModelExtensions
    {
        /// <summary>
        /// Returns the name of the character, as their name appears in the Archipelago's APWorld.
        /// </summary>
        /// <example>An Ironclad instance returns "Ironclad", because items for that character include "Ironclad Card Reward", "Ironclad Relic", etc.</example>
        public static string APName(this CharacterModel character)
        {
            return character.GetType().Name;
        }

        /// <summary>
        /// Gets the `APItemCharID` for this character
        /// </summary>
        public static APItemCharID? GetAPItemCharID(this CharacterModel character)
        {
            return GameUtility.GetCharacterIDByName(character.APName());
        }
    }
}
