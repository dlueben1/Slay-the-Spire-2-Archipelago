using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Data;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static StS2AP.Data.ItemTable;
using static System.Collections.Specialized.BitVector32;

namespace StS2AP
{
    /// <summary>
    /// Represents the connection lifecycle of the Archipelago client.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    /// <summary>
    /// Handles the state of our Archipelago Multiworld, including connection details and gameplay data
    /// </summary>
    public static class ArchipelagoClient
    {
        /// <summary>
        /// The version of the Archipelago Mod (semantic version: major.minor.patch)
        /// </summary>
        public static string Version
        {
            get
            {
                var version = typeof(ArchipelagoClient).Assembly.GetName().Version;
                if (version == null) return "Version Unknown";
                return $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

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

        /// <summary>
        /// The current connection state of the client.
        /// </summary>
        public static ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Convenience property: true when fully connected to the Archipelago server.
        /// </summary>
        public static bool IsConnected => State == ConnectionState.Connected && Session?.Socket?.Connected == true;

        #endregion

        #region Session Information

        /// <summary>
        /// The Player's settings, mostly from the YAML
        /// </summary>
        public static ArchipelagoSettings Settings { get; private set; }

        public static ArchipelagoSession Session { get; set; }

        /// <summary>
        /// Progress of the player through their Archipelago game.
        /// Some of this data resets every run.
        /// </summary>
        public static ArchipelagoProgress Progress { get; set; } = new();


        /// <summary>
        /// Represents how caught up we are with Archipelago's sent items
        /// </summary>
        private static int Index;

        public static Dictionary<string, object> SlotData { get; set; }

        public static List<long> CheckedLocations { get; set; }

        /// <summary>
        /// Location checks collected while disconnected. Flushed on reconnect.
        /// </summary>
        private static readonly List<long> PendingLocationChecks = new();
        private static readonly object _pendingLocLock = new();

        /// <summary>
        /// DataStorage write operations queued while disconnected. Flushed on reconnect.
        /// Each action receives the (now-live) session to execute against.
        /// </summary>
        private static readonly ConcurrentQueue<Action<ArchipelagoSession>> PendingDataStorageWrites = new();

        /// <summary>
        /// Holds the most recent save write while disconnected. Only the latest save is kept
        /// because saves happen frequently and only the newest matters.
        /// </summary>
        private static Action<ArchipelagoSession>? _pendingSaveWrite;
        private static readonly object _pendingSaveLock = new();

        #endregion

        /// <summary>
        /// Spinlock for processing incoming items to ensure that we don't have multiple threads trying to process items at the same time
        /// </summary>
        private static readonly object _itemLock = new();

        /// <summary>
        /// Fires when the connection state changes
        /// </summary>
        public static event Action<ConnectionState> ConnectionStateChanged;


        /// <summary>
        /// Pre-scouted location data. Key is location ID, value is a tuple of (ItemName, PlayerName).
        /// Populated on connection to avoid async calls during gameplay.
        /// </summary>
        public static Dictionary<long, ScoutedItemInfo> ScoutedLocations { get; set; } = new();

        /// <summary>
        /// Cancels the active reconnect loop.
        /// Not currently used to trigger manual disconnects, but wired up for when that is supported.
        /// </summary>
        private static CancellationTokenSource? _reconnectCts;

        /// <summary>
        /// Current reconnect attempt number, visible to UI for status display.
        /// </summary>
        public static int ReconnectAttempt { get; private set; }

        /// <summary>
        /// Timestamp (UTC ticks) set when a reconnection succeeds.
        /// Socket error/close events that arrive within <see cref="ReconnectGraceMs"/>
        /// of this timestamp are treated as stale artefacts of the old connection
        /// and are silently ignored, preventing the "reconnect → stale SocketClosed
        /// → reconnect" loop.
        /// </summary>
        private static long _reconnectGraceUntilTicks;

        /// <summary>
        /// How long (in milliseconds) after a successful reconnect we ignore
        /// socket error/close events.  3 seconds is generous enough to absorb
        /// the burst of events the AP library fires while tearing down the old
        /// internal WebSocket.
        /// </summary>
        private const int ReconnectGraceMs = 3_000;

        #region Networking

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            // Ignore if we're already connected or connecting
            if (State == ConnectionState.Connected || State == ConnectionState.Connecting) return;
            State = ConnectionState.Connecting;

            // Setup Data
            SlotData?.Clear();
            SlotData = new Dictionary<string, object>();
            CheckedLocations = new List<long>();
            ScoutedLocations.Clear();

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

            // Listen for errors and connection termination.
            Session.Socket.ErrorReceived += OnErrorReceived;
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
                State = ConnectionState.Connected;

                // Store Session information
                SlotData = success.SlotData;
                Seed = Session.RoomState.Seed;

                // Before we tell the user everything is okay, let's make sure that the mod version is correct
                var apWorldVersion = "v" + (SlotData["mod_compat_version"] as string);
                LogUtility.Info($"APWorld Version: {apWorldVersion}");
                LogUtility.Info($"Client Version: {Version}");
                if (apWorldVersion == null || apWorldVersion != Version)
                {
                    // Log the issue
                    LogUtility.Error($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                    // Disconnect from the server since we can't guarantee compatibility
                    Disconnect();

                    // Re-Enable the UI
                    ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                    ArchipelagoConnectionUI.SetCloseButtonEnabled(true);

                    // Tell the user they need to update their mod
                    ArchipelagoConnectionUI.SetStatus($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                    return;
                }

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

                // Mark us as disconnected and disconnect
                State = ConnectionState.Disconnected;
                Disconnect();
            }
            // No longer in transient "Connecting" state on failure path
            // (State is set to Disconnected inside HandleConnectResult on failure)
        }

        /// <summary>
        /// Fires on a successful Archipelago connection.
        /// </summary>
        public static void OnConnected()
        {
            LogUtility.Success("Successfully Connected to Archipelago Server");

            // Restore checked locations from server so "Claimed" state survives restarts
            CheckedLocations = new List<long>(Session.Locations.AllLocationsChecked);
            LogUtility.Info($"Restored {CheckedLocations.Count} previously checked location(s) from server.");

            try
            {
                // Get all settings for this player
                Settings = GetPlayerSettings();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load player settings: {ex.Message}");
                Disconnect();
                ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                ArchipelagoConnectionUI.SetStatus($"Failed to load settings: {ex.Message}");
                return;
            }

            // If all characters should be unlocked, set that up
            if (Settings.NoCharactersLocked)
            {
                CharacterModel[] characters = new CharacterModel[]
                {
                    ModelDb.Character<Ironclad>(),
                    ModelDb.Character<Silent>(),
                    ModelDb.Character<Regent>(),
                    ModelDb.Character<Necrobinder>(),
                    ModelDb.Character<Defect>()
                };
                Progress.UnlockedCharacters.AddRange(characters);
            }

            // Log all slot data
            foreach (var kvp in SlotData)
            {
                LogUtility.Debug($"KEY: {kvp.Key}");
                LogUtility.Debug($"VAL: {kvp.Value.ToString()}");
            }

            // Pre-scout all locations so we have item info available for notifications
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations());

            // Restore goaled characters from DataStorage so cross-session goal tracking works
            _ = GameUtility.RestoreGoaledCharsFromStorage();

            _ = GameUtility.SetupOnChangedSaves();
            // Let the game know that we've connected
            ConnectionStateChanged?.Invoke(ConnectionState.Connected);
        }

        /// <summary>
        /// Pre-scouts all locations in the game and stores the results.
        /// This gives us the ability to show item and player names in location/check notifications without having to make async calls during gameplay.
        /// This runs on a background thread, triggered on connection before gameplay starts.
        /// </summary>
        private static void PreScoutAllLocations()
        {
            try
            {
                if (Session == null)
                {
                    LogUtility.Error("Cannot pre-scout locations: Session is null");
                    return;
                }

                // Get all location IDs for our game
                var allLocationIds = Session.Locations.AllLocations.ToArray();

                if (allLocationIds.Length == 0)
                {
                    LogUtility.Warn("No locations found to scout");
                    return;
                }

                LogUtility.Info($"Pre-scouting {allLocationIds.Length} locations...");

                // Scout all locations at once (blocking call on this thread)
                var scoutTask = Session.Locations.ScoutLocationsAsync(allLocationIds);
                scoutTask.Wait(); // Block until complete. Async doesn't play well with Harmony Patches
                ScoutedLocations = scoutTask.Result;

                // Add all scouted locations to the game's localization tables so they can be shown as rewards (which require `LocString`)
                Dictionary<string, string> locationLocalizations = new();
                foreach(var loc in ScoutedLocations)
                {
                    // Add the Item at this location to the localization table with the keys "AP_LOC_{LocationID}"
                    string locKey = $"AP_LOC_{loc.Key}";
                    string locText = $"{loc.Value.ItemDisplayName} for {loc.Value.Player.Name}";
                    locationLocalizations.Add(locKey, locText);
                    LogUtility.Warn($"{loc.Key}:{loc.Value.LocationName}:{loc.Value.LocationDisplayName}");
                }
                TextUtility.RegisterLocTableAtRuntime("ap", locationLocalizations);

                LogUtility.Success($"Pre-scouted {ScoutedLocations.Count} locations successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-scout locations: {ex.Message}");
            }
        }

        /// <summary>
        /// Fully disconnects from Archipelago (manual / intentional disconnect).
        /// Stops any reconnect loop, tears down session, clears all state.
        /// </summary>
        public static void Disconnect()
        {
            LogUtility.Debug("Disconnecting from Archipelago...");

            // Cancel any in-flight reconnect loop
            // Note: manual disconnect is not yet surfaced in the UI, but the token is wired up for when it is.
            _reconnectCts?.Cancel();
            _reconnectCts = null;

            Task.Run(() => Session?.Socket.DisconnectAsync());
            Session = null;
            State = ConnectionState.Disconnected;
            ReconnectAttempt = 0;

            // Let the game know that we've disconnected
            ConnectionStateChanged?.Invoke(ConnectionState.Disconnected);
        }

        /// <summary>
        /// Called when the connection drops unexpectedly.
        /// Transitions to Reconnecting state and starts auto-reconnect.
        /// </summary>
        private static void OnConnectionLost(string reason)
        {
            // Guard: don't start a second reconnect loop
            if (State == ConnectionState.Reconnecting || State == ConnectionState.Disconnected) return;

            LogUtility.Warn($"Connection lost: {reason}");
            State = ConnectionState.Reconnecting;
            ReconnectAttempt = 0;
            ConnectionStateChanged?.Invoke(ConnectionState.Reconnecting);

            // Notify the player that the connection was lost
            NotificationUtility.ShowRawText("[color=red]Connection to Archipelago lost.[/color] Attempting to reconnect...");

            // Start the reconnect loop on a background thread
            _reconnectCts = new CancellationTokenSource();
            _ = ReconnectAsync(_reconnectCts.Token);
        }

        /// <summary>
        /// Auto-reconnect loop: 3 rapid retries at ~3 s, then every 30 s.
        /// After 10 minutes slows to 60 s intervals.
        /// Reuses the existing Session object (event handlers stay registered).
        /// </summary>
        private static async Task ReconnectAsync(CancellationToken ct)
        {
            const int rapidRetries = 3;
            const int rapidDelayMs = 3_000;
            const int normalDelayMs = 30_000;
            const int slowDelayMs = 60_000;
            const int slowdownAfterMs = 10 * 60 * 1_000; // 10 minutes

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                ReconnectAttempt++;

                // Determine delay for this attempt
                int delayMs;
                if (ReconnectAttempt <= rapidRetries)
                    delayMs = rapidDelayMs;
                else if (stopwatch.ElapsedMilliseconds < slowdownAfterMs)
                    delayMs = normalDelayMs;
                else
                    delayMs = slowDelayMs;

                LogUtility.Info($"Reconnect attempt #{ReconnectAttempt} in {delayMs / 1000}s...");
                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (ct.IsCancellationRequested) return;

                // Try to reconnect using the existing session
                try
                {
                    if (Session == null)
                    {
                        LogUtility.Warn("Session is null during reconnect — cannot reuse session.");
                        return;
                    }

                    LogUtility.Info($"Attempting reconnect #{ReconnectAttempt}...");

                    var result = Session.TryConnectAndLogin(
                        Game,
                        PlayerName,
                        ItemsHandlingFlags.AllItems,
                        new Version(APVersion),
                        password: ServerPassword,
                        requestSlotData: false
                    );

                    if (ct.IsCancellationRequested) return;

                    if (result.Successful)
                    {
                        OnReconnected();
                        return;
                    }
                    else
                    {
                        var failure = (LoginFailure)result;
                        var errors = string.Join(", ", failure.Errors);
                        LogUtility.Warn($"Reconnect attempt #{ReconnectAttempt} failed: {errors}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Reconnect attempt #{ReconnectAttempt} threw: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Called after a successful reconnection. Restores state and flushes pending data.
        /// </summary>
        private static void OnReconnected()
        {
            LogUtility.Success("Reconnected to Archipelago server!");

            // Start a grace period so stale SocketClosed / ErrorReceived events
            // from the old internal WebSocket are silently ignored.
            _reconnectGraceUntilTicks = DateTime.UtcNow.AddMilliseconds(ReconnectGraceMs).Ticks;

            // Notify the player before any other reconnection work enqueues notifications
            NotificationUtility.ShowRawText("[color=green]Reconnected to Archipelago![/color]");

            // Restore checked locations from server
            CheckedLocations = new List<long>(Session.Locations.AllLocationsChecked);

            // Flush any location checks that were queued while disconnected
            FlushPendingLocationChecks();

            // Flush any DataStorage writes that were queued while disconnected
            FlushPendingDataStorageWrites();

            // Re-scout locations on a background thread
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations());

            // Transition state
            State = ConnectionState.Connected;
            ReconnectAttempt = 0;
            ConnectionStateChanged?.Invoke(ConnectionState.Connected);
        }

        /// <summary>
        /// Centralized method to send a location check. Buffers the check if disconnected.
        /// Always adds to CheckedLocations immediately for local tracking.
        /// </summary>
        public static void SendLocationCheck(long locationId)
        {
            if (locationId == -1) return;

            // Skip if already checked
            if (CheckedLocations.Contains(locationId)) return;

            // Track locally immediately
            CheckedLocations.Add(locationId);

            if (IsConnected && Session != null)
            {
                _ = Session.Locations.CompleteLocationChecksAsync(locationId);
            }
            else
            {
                // Buffer for later
                lock (_pendingLocLock)
                {
                    PendingLocationChecks.Add(locationId);
                }
                LogUtility.Info($"Buffered location check {locationId} (offline)");
            }
        }

        /// <summary>
        /// Sends all buffered location checks to the server.
        /// </summary>
        private static void FlushPendingLocationChecks()
        {
            long[] pending;
            lock (_pendingLocLock)
            {
                if (PendingLocationChecks.Count == 0) return;
                pending = PendingLocationChecks.ToArray();
                PendingLocationChecks.Clear();
            }

            LogUtility.Info($"Flushing {pending.Length} buffered location check(s)...");
            _ = Session.Locations.CompleteLocationChecksAsync(pending);
        }

        /// <summary>
        /// Queues a DataStorage write for execution. If connected, runs immediately.
        /// If disconnected/reconnecting, stores the action for later replay.
        /// </summary>
        public static void EnqueueDataStorageWrite(Action<ArchipelagoSession> writeAction)
        {
            if (IsConnected && Session != null)
            {
                try
                {
                    writeAction(Session);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"DataStorage write failed: {ex.Message}");
                }
            }
            else
            {
                PendingDataStorageWrites.Enqueue(writeAction);
                LogUtility.Info("Buffered DataStorage write (offline)");
            }
        }

        /// <summary>
        /// Queues a save write. If connected, runs immediately.
        /// If disconnected/reconnecting, only keeps the latest save (overwrites previous).
        /// </summary>
        public static void EnqueueSaveWrite(Action<ArchipelagoSession> writeAction)
        {
            if (IsConnected && Session != null)
            {
                try
                {
                    writeAction(Session);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Save DataStorage write failed: {ex.Message}");
                }
            }
            else
            {
                lock (_pendingSaveLock)
                {
                    _pendingSaveWrite = writeAction;
                }
                LogUtility.Info("Buffered save write (offline, replacing previous)");
            }
        }

        /// <summary>
        /// Replays all buffered DataStorage writes against the current session,
        /// then flushes the latest pending save write (if any).
        /// </summary>
        private static void FlushPendingDataStorageWrites()
        {
            int count = 0;
            while (PendingDataStorageWrites.TryDequeue(out var writeAction))
            {
                try
                {
                    writeAction(Session);
                    count++;
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Failed to replay DataStorage write: {ex.Message}");
                }
            }

            // Flush the latest save write (only one is kept)
            Action<ArchipelagoSession>? saveWrite;
            lock (_pendingSaveLock)
            {
                saveWrite = _pendingSaveWrite;
                _pendingSaveWrite = null;
            }
            if (saveWrite != null)
            {
                try
                {
                    saveWrite(Session);
                    count++;
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Failed to replay pending save write: {ex.Message}");
                }
            }

            if (count > 0)
            {
                LogUtility.Info($"Flushed {count} buffered DataStorage write(s)");
            }
        }

        /// <summary>
        /// Log errors from the Archipelago socket to the console.
        /// These are typically message deserialization or protocol errors, NOT
        /// connection-loss events.  Only <see cref="OnSocketSessionEnd"/> triggers
        /// the reconnect flow.
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
        /// When the socket closes unexpectedly, start auto-reconnect.
        /// Events that arrive during the post-reconnect grace period are treated
        /// as stale artefacts of the old WebSocket teardown and do not trigger
        /// a new reconnect.
        /// </summary>
        private static void OnSocketSessionEnd(string reason)
        {
            LogUtility.Warn($"Socket session ended: {reason}");

            if (DateTime.UtcNow.Ticks < _reconnectGraceUntilTicks)
            {
                LogUtility.Debug($"Ignoring socket-close event during post-reconnect grace period");
                return;
            }

            OnConnectionLost(reason);
        }

        /// <summary>
        /// Handle incoming items that come from Archipelago
        /// </summary>
        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            // Deal with this Item
            lock (_itemLock)
            {
                // Grab the item data
                var receivedItem = helper.DequeueItem();

                // Ignore if this item is an old message
                if (helper.Index <= Index) return;

                // Process it
                ProcessItem(receivedItem, helper.Index);
                
                // Keep track of how many messages we've had so far
                Index++;
            }

        }

        #endregion

        #region Item Processing

        /// <summary>
        /// Determines what to do with an Item that we've received from Archipelago.
        /// This function is controlled by a Spinlock, and can only process one item at a time.
        /// </summary>
        /// <param name="item">Received Item</param>
        /// <param name="index">The index of the item in the Archipelago Multiworld</param>
        private static void ProcessItem(ItemInfo item, int index)
        {
            // Log the item
            LogUtility.Success($"Received: {item.ItemName} from {item.Player.Name} (ID: {item.ItemId} / LocID: {item.LocationId} / Index: {index})");

            // Show Notification for the item
            NotificationUtility.ShowItemReceived(item);

            // Apply the item to the game
            switch(item.GetRawItemID())
            {
                // Character Unlocks
                case APItem.Unlock:
                    {
                        GameUtility.UnlockCharacter(item);
                        break;
                    }
                // Progressive Smiths/Rests
                case APItem.ProgressiveSmith:
                case APItem.ProgressiveRest:
                    {
                        // Get the IDs for storing the item
                        var itemId = item.GetRawItemID();
                        var playerId = item.GetStSCharID();

                        // Add the Smith/Rest to the amount we've received for this character
                        var source = itemId == APItem.ProgressiveSmith ? Progress.ProgressiveSmiths : Progress.ProgressiveRests;

                        // Increment the reward
                        try
                        {
                            var haveKey = source.TryGetValue(playerId, out int amount);
                            if (!haveKey) amount = 0;
                            source[playerId] = amount + 1;
                            LogUtility.Success($"New Value for {(itemId == APItem.ProgressiveSmith ? "ProgressiveSmiths" : "ProgressiveRests")} is {source[playerId]}");
                        }
                        catch (KeyNotFoundException e)
                        {
                            LogUtility.Error($"ProgressiveSmiths/ProgressiveRests does not have a value for this character! ({item.ItemDisplayName} from {item.Player.Name})");
                        }
                        catch
                        {
                            LogUtility.Error($"Failed to process Progressive Smith/Rest when this item was received: ({item.ItemDisplayName} from {item.Player.Name})");
                        }

                        break;
                    }
                // Gold is condensed into a single reward pool
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem._15Gold:
                case APItem._30Gold:
                case APItem.BossGold:
                    {
                        // Get the IDs for storing the item
                        var playerId = item.GetStSCharID();
                        var itemId = item.GetRawItemID();

                        // Add the Gold to the amount we've received
                        try
                        {
                            var haveKey = Progress.GoldReceived.TryGetValue(playerId, out int gold);
                            if (!haveKey) gold = 0;
                            Progress.GoldReceived[playerId] = gold + ItemTable.GoldItemAmounts[itemId];
                        }
                        catch (KeyNotFoundException e)
                        {
                            LogUtility.Error($"GoldItemAmounts does not have a value for this item! ({item.ItemDisplayName} from {item.Player.Name})");
                        }
                        catch
                        {
                            LogUtility.Error($"Failed to process Gold when this item was received: ({item.ItemDisplayName} from {item.Player.Name})");
                        }

                        break;
                    }
                // Everything else ends up in the "reward pool"
                default:
                    {
                        Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));
                        break;
                    }
            }

            // Refresh the unused item count
            ArchipelagoTopBarUI.RefreshCount();
        }

        #endregion

        #region Slot Information

        /// <summary>
        /// Get all of the Player's Settings for their Archipelago Slot
        /// </summary>
        private static ArchipelagoSettings GetPlayerSettings()
        {
            /// Use the SlotData that was already retrieved during login
            /// instead of calling Session.DataStorage.GetSlotData() which performs
            /// a synchronous network call that can deadlock/timeout when the websocket
            /// thread is busy processing incoming item packets (e.g. on reconnect).
            var slotData = SlotData;
            if(slotData == null || slotData.Count == 0)
            {
                LogUtility.Error("No slot data found for this player!");
                throw new InvalidDataException("No slot data found for this player!");
            }
            ArchipelagoSettings settings = new();

            // Apply all found settings
            if (slotData.ContainsKey("ascension")) settings.AscensionLevel = Convert.ToInt32(slotData["ascension"]);
            if (slotData.ContainsKey("seeded")) settings.IsSeeded = Convert.ToBoolean(slotData["seeded"]);
            if (slotData.ContainsKey("shuffle_all_cards")) settings.ShouldShuffleAllCards = Convert.ToBoolean(slotData["shuffle_all_cards"]);
            if (slotData.ContainsKey("lock_characters")) settings.NoCharactersLocked = Convert.ToString(slotData["lock_characters"]) == "unlocked";
            if (slotData.ContainsKey("num_chars_goal")) settings.NumCharsGoal = Convert.ToInt32(slotData["num_chars_goal"]);
            if (slotData.ContainsKey("characters") && slotData["characters"] is System.Collections.IList charsList) settings.TotalCharacters = charsList.Count;

            if (slotData.ContainsKey("campfire_sanity"))
                settings.CampfireSanity = Convert.ToInt32(slotData["campfire_sanity"]) != 0;

            if (slotData.ContainsKey("gold_sanity"))
                settings.GoldSanity = Convert.ToInt32(slotData["gold_sanity"]) != 0;
                
            if (slotData.ContainsKey("potion_sanity"))
                settings.PotionSanity = Convert.ToInt32(slotData["potion_sanity"]) != 0;

            if (slotData.ContainsKey("include_floor_checks"))
                settings.Floorsanity = Convert.ToInt32(slotData["include_floor_checks"]) != 0;

            // And return it
            return settings;
        }

        #endregion
    }
}