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
            if(ArchipelagoClient.Settings.Characters.TryGetValue(character.Id.Entry, out var config))
            {
                return config.Name;
            }
            return character.Id.Entry;
        }

        // /// <summary>
        // /// Gets the `APItemCharID` for this character.
        // /// For Items, this is one-based.
        // /// </summary>
        // public static APItemCharID? GetAPItemCharID(this CharacterModel character)
        // {
        //     return GameUtility.GetCharacterIDByName(character.APName());
        // }

        public static long? GetCharacterOffset(this CharacterModel character)
        {
            if (ArchipelagoClient.Settings.Characters.TryGetValue(character.Id.Entry, out var config))
            {
                return config.CharOffset;
            }
            else
            {
                var msg = $"Character {character.APName()} does not have a valid Character Offset.";
                LogUtility.Error(msg);
                // throw new NullReferenceException(msg);
                return null;
            }
        }


        /// <summary>
        /// Gets the Location ID offset used for this character.
        /// For Locations, this is zero-based.
        /// </summary>
        public static long GetAPLocationCharID(this CharacterModel character)
        {
            var config = ArchipelagoClient.Settings.Characters[character.Id.Entry];
            if (config != null)
            {
                return config.CharOffset;
            }
            else
            {
                var msg = $"Character {character.APName()} does not have a valid APItemCharID. It's likely that a new character was added that we aren't handling properly.";
                LogUtility.Error(msg);
                throw new NullReferenceException(msg);
            }
        }

        /// <summary>
        /// Whether or not this character has cleared the game at least once.
        /// </summary>
        public static bool HasCleared(this CharacterModel character)
        {
            return GameUtility.HasCharacterGoaled(character.Id.Entry);
        }
    }
}
