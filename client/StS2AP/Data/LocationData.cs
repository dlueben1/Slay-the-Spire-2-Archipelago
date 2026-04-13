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
        /// Get the "Press Start" Location for a given character.
        /// 
        /// Note: I can't seem to figure out what the location ID is for this, so it isn't used right now.
        /// </summary>
        public static long GetPressStartLocation(CharacterModel character)
        {
            try
            {
                return ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", $"{character.APName()} Press Start");
            } 
            catch
            {
                LogUtility.Error($"Could not find Press Start location for {character.APName()}");
                return -1;
            }
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
                    } catch { }
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
