using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
//test

namespace StS2AP
{
    public class ResultEventArgs : EventArgs
    {
        public bool Value;
    }

    /// <summary>
    /// Handles the state of our Archipelago Multiworld, including connection details and gameplay data
    /// </summary>
    public static class ArchipelagoClient
    {
        #region Connection Info

        public static string ServerAddress { get; set; }
        public static string ServerPassword { get; set; }
        public static string PlayerName { get; set; }
        public static string Seed { get; set; }
        
        /// <summary>
        /// The name of the Game
        /// </summary>
        private const string Game = "Slay the Spire II";

        /// <summary>
        /// Minimum Archipelago Version that's supported by the mod.
        /// </summary>
        public const string APVersion = "0.6.6";

        #endregion

        public static bool Authenticated { get; set; }
        public static bool Connecting { get; set; }
        public static bool IsConnected => Authenticated && Session?.Socket?.Connected == true;

        public static ArchipelagoSession Session { get; set; }

        /// <summary>
        /// Represents how caught up we are with Archipelago's sent items
        /// </summary>
        private static int Index;

        public static Dictionary<string, object> SlotData { get; set; }

        /// <summary>
        /// Fires when the connection state changes
        /// </summary>
        public static event EventHandler<ResultEventArgs> ConnectionStateChanged;

        public static List<long> CheckedLocations { get; set; }

        #region Networking

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            // Ignore if we're already authenticated
            if (Authenticated || Connecting) return;
            Connecting = true;

            // Setup Data
            SlotData?.Clear();
            SlotData = new Dictionary<string, object>();
            CheckedLocations = new List<long>();

            // Attempt to create the AP Session
            try
            {
                // Setup the Session
                Session = ArchipelagoSessionFactory.CreateSession(ServerAddress);
            }
            catch (Exception e)
            {
                return;
            }

            // Listen for received items
            Session.Items.ItemReceived += OnItemReceived;

            // Listen for errors
            Session.Socket.ErrorReceived += OnErrorReceived;

            // Listen for connection termination
            Session.Socket.SocketClosed += OnSocketSessionEnd;

            // Attempt to connect to the server
            try
            {
                // it's safe to thread this function call but unity notoriously hates threading so do not use excessively
                ThreadPool.QueueUserWorkItem(
                    _ => HandleConnectResult(
                        Session.TryConnectAndLogin(
                            Game,
                            PlayerName,
                            ItemsHandlingFlags.AllItems,
                            new Version(APVersion),
                            password: ServerPassword,
                            requestSlotData: SlotData.Count == 0
                        )));
            }
            catch (Exception e)
            {
                HandleConnectResult(new LoginFailure(e.ToString()));
            }
        }

        /// <summary>
        /// Handle the outcome of a connection attempt
        /// </summary>
        private static void HandleConnectResult(LoginResult result)
        {
            string outText;
            if (result.Successful)
            {
                // We are now connected!
                var success = (LoginSuccessful)result;
                Authenticated = true;

                // Store Session information
                SlotData = success.SlotData;
                Seed = Session.RoomState.Seed;

                // Complete any locations that we have
                //Session.Locations.CompleteLocationChecksAsync(null, CheckedLocations.ToArray());
                outText = $"Successfully connected to {ServerAddress} as {PlayerName}!";

                // Let the game know that we've connected
                OnConnected();
            }
            else
            {
                // Log the error
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerAddress} as {PlayerName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                // Mark us as un-authenticated and disconnect
                Authenticated = false;
                Disconnect();
            }
            Connecting = false;
        }

        /// <summary>
        /// @todo clean this lol
        /// </summary>
        public static void OnConnected()
        {
            LogUtility.Success("Successfully Connected to Archipelago Server");

            // Log all slot data
            foreach (var kvp in SlotData)
            {
                LogUtility.Debug($"KEY: {kvp.Key}");
                LogUtility.Debug($"VAL: {kvp.Value.ToString()}");
            }

            // Let the game know that we've connected
            ConnectionStateChanged?.Invoke(null, new ResultEventArgs { Value = true });
        }

        /// <summary>
        /// Cleans up our Session with Archipelago
        /// </summary>
        public static void Disconnect()
        {
            LogUtility.Debug("Disconnecting from Archipelago...");
            Task.Run(() => Session?.Socket.DisconnectAsync());
            Session = null;
            Authenticated = false;

            // Let the game know that we've disconnected
            ConnectionStateChanged?.Invoke(null, new ResultEventArgs { Value = false });
        }

        /// <summary>

        /// Log errors to the console
        /// </summary>
        private static void OnErrorReceived(Exception e, string message)
        {
            LogUtility.Error($"Archipelago Error: {message}");
            if (e != null)
            {
                LogUtility.Error($"Exception: {e.Message}");
            }
        }

        /// <summary>
        /// When we end our Session, disconnect from the Archipelago server
        /// </summary>
        private static void OnSocketSessionEnd(string reason)
        {
            LogUtility.Warn($"Socket session ended: {reason}");
            Disconnect();
        }

        /// <summary>
        /// Handle incoming items that come from Archipelago
        /// </summary>
        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            // Grab the item data
            var receivedItem = helper.DequeueItem();

            // Ignore if this item is an old message
            if (helper.Index <= Index) return;

            // Add the Item
            ProcessItem(receivedItem);

            // Log the item
            LogUtility.Success($"Received: {receivedItem.ItemName} from {receivedItem.Player.Name} (ID: {receivedItem.ItemId})");

            // Keep track of how many messages we've had so far
            Index++;
        }

        #endregion

        #region Item Processing

        /// <summary>
        /// Determines what to do with an Item that we've received from Archipelago.
        /// </summary>
        /// <param name="item">Received Item</param>
        private static void ProcessItem(ItemInfo item)
        {
            // In the first pass the only thing you can really get is Gold, so this will be updated later.
            switch (item.ItemId)
            {
                default:
                    {
                        // Crappy temporary way to scrape the gold amount from the item name
                        var goldAmt = int.Parse(item.ItemDisplayName.Replace("Gold", "").Trim());
                        PlayerCmd.GainGold(goldAmt, GameUtility.CurrentPlayer, false);
                        break;
                    }
            }
        }

        #endregion
    }
}