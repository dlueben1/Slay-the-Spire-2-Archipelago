using MegaCrit.Sts2.Core.Models;
using StS2AP.Extensions;
using StS2AP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Data
{
    public static class LocationData
    {
        /// <summary>
        /// Combines a base Location ID with a character's offset ID
        /// </summary>
        /// <param name="locationId">The base ID of a location</param>
        /// <param name="character">The character to offset the location by</param>
        /// <returns>The combined location ID.</returns>
        /// <example>If characterOffset=1 and locationId=88, then we'd return 10088</example>
        private static long CombineLocationAndCharacterIds(long locationId, CharacterModel character)
        {
            // Character offset (for locations this is zero-based, so it needs to be shifted)
            long _characterOffset = character.GetAPLocationCharID();

            // Place the character offset in the leftmost position and location ID in the rightmost 4 digits (zero-padded)
            return (_characterOffset * 10000) + locationId;
        }

        /// <summary>
        /// Get the "Press Start" Location for a given character.
        /// </summary>
        public static long GetPressStartLocation(CharacterModel character)
        {
            // The location ID, to be combined with the character offset
            const long _baseLocationId = 88;

            return CombineLocationAndCharacterIds(_baseLocationId, character);
        }
        
        /// <summary>
        /// Returns whether or not the character has a "Press Start" location.
        /// Locked characters have this location.
        /// </summary>
        public static bool DoesThisCharacterHavePressStartLocation(CharacterModel character)
        {
            // Get the location ID
            long id = GetPressStartLocation(character);

            // If the ID isn't valid, assume the location doesn't exist
            if (id == -1) return false;

            // If it's valid, see if it's in our Scouted Locations
            return ArchipelagoClient.ScoutedLocations.ContainsKey(id);
        }

        /// <summary>
        /// Returns all location IDs for Card Rewards for a given character, based on user settings.
        /// </summary>
        /// <param name="character">The character to get Card Reward locations for.</param>
        /// <returns>A list of location IDs for the specified character's Card Rewards.</returns>
        public static List<long> GetCardRewardLocations(CharacterModel character)
        {
            // Get the number of Card Rewards based on user settings
            var numCardRewards = ArchipelagoClient.Settings.ShouldShuffleAllCards ? ArchipelagoProgress._maxCardRewards : (ArchipelagoProgress._maxCardRewards / 2);
            return GetLocationsByPattern($"{character.APName()} Card Reward #", numCardRewards);
        }

        /// <summary>
        /// Returns all location IDs for Rare Card Rewards for a given character.
        /// </summary>
        /// <param name="character">The character to get Rare Card Reward locations for.</param>
        /// <returns>A list of location IDs for the specified character's Rare Card Rewards.</returns>
        public static List<long> GetRareCardRewardLocations(CharacterModel character)
        {
            return GetLocationsByPattern($"{character.APName()} Rare Card Reward #", ArchipelagoProgress._maxBossRewards);
        }

        /// <summary>
        /// Returns all location IDs for Floorsanity
        /// </summary>
        /// <param name="character">The character to get Floorsanity locations for.</param>
        /// <returns>A list of location IDs for the specified character's Floorsanity.</returns>
        public static List<long> GetFloorsanityLocations(CharacterModel character)
        {
            return GetLocationsByPattern($"{character.APName()} Reached Floor #", ArchipelagoProgress._maxFloorRewards);
        }

        /// <summary>
        /// Returns all location IDs for Relic Rewards for a given character.
        /// </summary>
        /// <param name="character">The character to get Relic Reward locations for.</param>
        /// <returns>A list of location IDs for the specified character's Relic Rewards.</returns>
        public static List<long> GetRelicRewardLocations(CharacterModel character)
        {
            return GetLocationsByPattern($"{character.APName()} Relic #", ArchipelagoProgress._maxRelicRewards);
        }

        /// <summary>
        /// Returns all location IDs for Goldsanity for a given character.
        /// </summary>
        /// <param name="character">The character to get Goldsanity locations for.</param>
        /// <returns>A list of location IDs for the specified character's gold rewards.</returns>
        public static List<long> GetGoldsanityLocations(CharacterModel character)
        {
            return GetLocationsByPattern($"{character.APName()} Combat Gold #", ArchipelagoProgress._maxGoldRewards);
        }

        /// <summary>
        /// Returns all location IDs for Potionsanity for a given character.
        /// </summary>
        /// <param name="character">The character to get Potionsanity locations for.</param>
        /// <returns>A list of location IDs for the specified character's potion drops.</returns>
        public static List<long> GetPotionsanityLocations(CharacterModel character)
        {
            return GetLocationsByPattern($"{character.APName()} Potion Drop #", ArchipelagoProgress._maxPotionRewards);
        }

        /// <summary>
        /// Returns all location IDs for Campfiresanity for a given character.
        /// </summary>
        /// <param name="character">The character to get Campfiresanity locations for.</param>
        /// <returns>A list of location IDs for the specified character's campfires.</returns>
        public static List<long> GetCampfiresanityLocations(CharacterModel character)
        {
            List<long> ids = new();
            const int acts = 3;
            const int campfiresPerAct = 2;
            for(int a = 1; a <= acts; a++)
            {
                for(int c = 1; c <= campfiresPerAct; c++)
                {
                    try
                    {
                        var id = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", $"{character.APName()} Act {a} Campfire {c}");
                        ids.Add(id);
                    } 
                    catch 
                    {
                        LogUtility.Error($"Failed to get location ID for {character.APName()} Act {a} Campfire {c}. This location will be skipped.");
                    }
                }
            }
            return ids;
        }

        /// <summary>
        /// Returns a list of location IDs that match a given pattern, up to a specified count.
        /// </summary>
        /// <param name="pattern">The pattern to match location names against, where '#' will be replaced by the index.</param>
        /// <param name="count">The maximum number of locations to return.</param>
        /// <returns>A list of location IDs that match the pattern. May be empty if something went wrong.</returns>
        private static List<long> GetLocationsByPattern(string pattern, int count)
        {
            List<long> ids = new();
            for(int i = 1; i <= count; i++)
            {
                try
                {
                    var id = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", pattern.Replace("#", i.ToString()));
                    ids.Add(id);
                } catch { }
            }
            return ids;
        }
    }
}
