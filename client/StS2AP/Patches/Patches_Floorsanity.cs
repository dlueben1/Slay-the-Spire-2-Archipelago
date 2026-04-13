using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for `AbstractRoom` and all of its derived classes.
    /// Sends Archipelago location checks when entering rooms to track floor progress.
    /// </summary>
    public static class Patches_Floorsanity
    {
        /// <summary>
        /// Sends an Archipelago location check when entering any room.
        /// Patches all room types (Combat, Event, Treasure, Rest Site, Merchant) since abstract classes cannot be patched directly.
        /// </summary>
        [HarmonyPatch]
        public static class OnRoomEnter
        {
            /// <summary>
            /// List of all room types that should trigger floor checks when entered.
            /// </summary>
            private static readonly Type[] RoomTypes =
            [
                typeof(CombatRoom),
                typeof(EventRoom),
                typeof(TreasureRoom),
                typeof(RestSiteRoom),
                typeof(MerchantRoom)
            ];

            /// <summary>
            /// Identifies all the `Enter` methods from each room type that should be patched.
            /// Harmony will apply the postfix patch to each of these methods.
            /// </summary>
            /// <returns>An enumerable of MethodBase objects representing each Enter method to patch.</returns>
            [HarmonyTargetMethods]
            static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var type in RoomTypes)
                {
                    var method = AccessTools.Method(type, nameof(CombatRoom.Enter));
                    if (method != null)
                    {
                        yield return method;
                    }
                }
            }

            /// <summary>
            /// Postfix patch that sends a floor check when entering any room type.
            /// It also forces a refresh of the Archipelago Unused Item Count, for run start sync issues.
            /// </summary>
            /// <param name="runState">The current run state.</param>
            /// <param name="isRestoringRoomStackBase">Whether the room is being restored from save.</param>
            [HarmonyPostfix]
            public static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                // Force a Refresh of the Archipelago Unused Item Count, for run start sync issues.
                ArchipelagoTopBarUI.RefreshCount();

                // Attempt to send a check for the current room we're on
                if(ArchipelagoClient.Settings.Floorsanity)
                {
                    TrySendFloorCheck(runState);
                }
            }
        }

        /// <summary>
        /// The logic to determine if we need to send a location check
        /// </summary>
        /// <param name="runState">The current state of the run</param>
        static void TrySendFloorCheck(IRunState? runState)
        {
            // Null checks to shut compiler up
            if (GameUtility.CurrentPlayer == null || runState == null)
            {
                LogUtility.Error("CurrentPlayer or runState is null, skipping Archipelago check");
                return;
            }

            // Try to get floor information from runState using reflection
            var floorProperty = runState.GetType().GetProperty("TotalFloor");

            if (floorProperty == null)
            {
                LogUtility.Error("fail");
                return;
            }

            // Create the Location/Check name to send
            var floorValue = floorProperty.GetValue(runState);
            var name = GameUtility.CurrentPlayer.APName();
            var locationName = $"{name} Reached Floor {floorValue}";

            LogUtility.Debug($"Attempting to send Archipelago location check: {locationName}");

            // Get the location ID from the name
            if (ArchipelagoClient.Session?.Locations.GetLocationIdFromName("Slay the Spire II", locationName) is long locationId && locationId != -1)
            {
                // Make sure this is the first time we've hit this location, otherwise we might be sending duplicates
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId))
                {
                    // Check the location off and let the server know
                    ArchipelagoClient.CheckedLocations.Add(locationId);
                    _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(locationId);

                    // Log it and notify the user (uses pre-scouted data)
                    LogUtility.Success($"Sent location check: {locationName}");
                }
                else
                {
                    LogUtility.Warn($"Location '{locationName}' already checked, skipping Archipelago check");
                }
            }
            else
            {
                LogUtility.Warn($"Location '{locationName}' not found in Archipelago");
            }
        }
    }
}

