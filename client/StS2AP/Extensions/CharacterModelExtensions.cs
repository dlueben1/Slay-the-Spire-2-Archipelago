using MegaCrit.Sts2.Core.Models;
using StS2AP.Utils;

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
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings != null
                && settings.Characters.TryGetValue(character.Id.Entry, out var config))
            {
                return config.Name;
            }
            return character.Id.Entry;
        }

        /// <summary>Returns the one-based AP character number assigned in slot data.</summary>
        public static long? GetAPCharacterNumber(this CharacterModel character)
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings != null
                && settings.Characters.TryGetValue(character.Id.Entry, out var config))
            {
                return config.CharOffset;
            }
            else
            {
                var msg = $"Character {character.APName()} does not have a valid AP character number.";
                LogUtility.Error(msg);
                return null;
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
