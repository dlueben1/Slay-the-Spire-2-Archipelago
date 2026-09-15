using Archipelago.MultiClient.Net;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Extensions;
using StS2AP.Utils;

namespace StS2AP.Data
{
    public static class LocationData
    {
        private const long FirstCampfireBaseId = 89;
        private const int CampfiresPerAct = 2;

        /// <summary>
        /// Combines a base location ID with a one-based AP character number.
        /// </summary>
        /// <param name="locationId">The base ID of a location</param>
        /// <param name="character">The character whose AP location block should be used</param>
        /// <returns>The combined location ID.</returns>
        /// <example>If the AP character number is 1 and locationId is 88, this returns 88.</example>
        private static long CombineLocationAndCharacterIds(long locationId, CharacterModel character)
        {
            var apCharacterNumber = character.GetAPCharacterNumber();
            if (!apCharacterNumber.HasValue)
            {
                LogUtility.Error($"Got unsupported character {character.APName()}");
                return -1;
            }

            if (!ArchipelagoIdCodec.TryComposeLocationId(
                locationId,
                apCharacterNumber.Value,
                out var combinedLocationId
            ))
            {
                LogUtility.Error(
                    $"Could not compose location {locationId} for AP character #{apCharacterNumber.Value}"
                );
                return -1;
            }

            return CoopSlot.Location(combinedLocationId);
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
        /// The slot-data lock flag is authoritative; scouting completes asynchronously and is
        /// therefore not a reliable way to decide whether the location exists.
        /// </summary>
        public static bool DoesThisCharacterHavePressStartLocation(CharacterModel character)
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            return settings != null && settings.Characters.TryGetValue(
                character.Id.Entry,
                out var config
            ) && config.Locked;
        }

        /// <summary>
        /// Returns all location IDs for Card Rewards for a given character, based on user settings.
        /// </summary>
        /// <param name="character">The character to get Card Reward locations for.</param>
        /// <returns>A list of location IDs for the specified character's Card Rewards.</returns>
        public static List<long> GetCardRewardLocations(CharacterModel character)
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null)
            {
                LogUtility.Error("Cannot enumerate Card Reward locations without AP slot settings.");
                return new List<long>();
            }
            // Get the number of Card Rewards based on user settings
            var numCardRewards = settings.ShouldShuffleAllCards ? ArchipelagoProgress._maxCardRewards : (ArchipelagoProgress._maxCardRewards / 2);
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
        /// Returns all location IDs for Ancient Rewards for a given character.
        /// </summary>
        /// <param name="character">The character to get Ancient Reward locations for.</param>
        /// <returns>A list of location IDs for the specified character's Ancient Rewards.</returns>
        public static List<long> GetAncientRewardLocations(CharacterModel character)
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null)
            {
                LogUtility.Error("Cannot enumerate Ancient Reward locations without AP slot settings.");
                return new List<long>();
            }
            var start = settings.NeowSanity ? 1 : 2;
            return GetLocationsByPattern($"{character.APName()} Ancient Act #", ArchipelagoProgress._maxAncientChecks, start);
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
            ArchipelagoSession? session = ArchipelagoClient.Session;
            if (session == null)
            {
                LogUtility.Error("Cannot enumerate Campfiresanity locations without an AP session.");
                return ids;
            }
            const int acts = 3;
            const int campfiresPerAct = 2;
            for(int a = 1; a <= acts; a++)
            {
                for(int c = 1; c <= campfiresPerAct; c++)
                {
                    try
                    {
                        var id = session.Locations.GetLocationIdFromName("Slay the Spire II", CoopSlot.Name($"{character.APName()} Act {a} Campfire {c}"));
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
        /// Resolves a Campfiresanity location without consulting a process-local AP session.
        /// Every multiplayer replica can therefore construct the same owner-specific option list.
        /// </summary>
        public static long GetCampfireLocationId(long apCharacterNumber, int act, int campfire)
        {
            if (apCharacterNumber < 1 || act is < 1 or > 3 || campfire is < 1 or > 2)
                return -1;

            long baseId = FirstCampfireBaseId
                + ((act - 1) * CampfiresPerAct)
                + (campfire - 1);
            return ArchipelagoIdCodec.TryComposeLocationId(
                baseId,
                apCharacterNumber,
                out var locationId
            ) ? locationId : -1;
        }

        public static bool IsCampfireLocationId(long locationId)
        {
            long baseId = ArchipelagoIdCodec.GetBaseLocationId(locationId);
            return baseId is >= FirstCampfireBaseId
                and < FirstCampfireBaseId + (3 * CampfiresPerAct);
        }
        public static List<long> GetShopsanityLocations(CharacterModel character)
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null)
            {
                LogUtility.Error("Cannot enumerate ShopSanity locations without AP slot settings.");
                return new List<long>();
            }
            return GetLocationsByPattern(
                $"{character.APName()} Shop Slot #",
                settings.TotalShopLocations);
        }

        /// <summary>
        /// Returns a list of location IDs that match a given pattern, up to a specified count.
        /// </summary>
        /// <param name="pattern">The pattern to match location names against, where '#' will be replaced by the index.</param>
        /// <param name="count">The maximum number of locations to return.</param>
        /// <returns>A list of location IDs that match the pattern. May be empty if something went wrong.</returns>
        private static List<long> GetLocationsByPattern(string pattern, int count, int start = 1 )
        {
            List<long> ids = new();
            ArchipelagoSession? session = ArchipelagoClient.Session;
            if (session == null)
            {
                LogUtility.Error($"Cannot enumerate locations matching '{pattern}' without an AP session.");
                return ids;
            }
            for(int i = start; i <= count; i++)
            {
                string locationName = CoopSlot.Name(pattern.Replace("#", i.ToString()));
                long id = session.Locations.GetLocationIdFromName(
                    "Slay the Spire II",
                    locationName
                );
                // GetLocationIdFromName returns -1 on no location found so handle it
                if (id < 0)
                {
                    LogUtility.Warn($"Could not resolve AP location '{locationName}'; skipping it.");
                    continue;
                }
                ids.Add(id);
            }
            return ids;
        }
    }
}
