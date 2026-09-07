using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using StS2AP.Data;
using StS2AP.Patches;
using StS2AP.UI;
using StS2AP.Utils;
using STS2RitsuLib;
using STS2RitsuLib.Data;

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
        Reconnecting,
    }

    /// <summary>
    /// Handles the state of our Archipelago Multiworld, including connection details and gameplay data
    /// </summary>
    public static class ArchipelagoClient
    {
        /// <summary>
        /// Slot-data contract supported by this client. Increment only when a future client can
        /// no longer safely consume worlds using the previous contract.
        /// </summary>
        public const int SupportedCompatFlag = 1;

        private const string ModManifestResourceName = "StS2AP.Archipelago.json";
        private const string ApWorldManifestResourceName = "StS2AP.Spire2Archipelago.json";
        private static readonly Lazy<System.Version> ModManifestVersion =
            new(ReadModManifestVersion);
        private static readonly Lazy<System.Version> BundledApWorldManifestVersion =
            new(ReadBundledApWorldManifestVersion);

        /// <summary>
        /// The version of the Archipelago Mod (semantic version: major.minor.patch)
        /// </summary>
        public static string Version
        {
            get
            {
                System.Version version = GetClientSemanticVersion();
                return $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        #region Connection Info

        public static string ServerAddress { get; set; } = string.Empty;
        public static string ServerPassword { get; set; } = string.Empty;
        public static string PlayerName { get; set; } = string.Empty;
        public static string Seed { get; set; } = string.Empty;

        /// <summary>
        /// The name of the Game
        /// </summary>
        public const string Game = "Slay the Spire II";

        /// <summary>
        /// Minimum Archipelago Version that's supported by the mod.
        /// </summary>
        public const string APVersion = "0.6.7";

        /// <summary>
        /// The current connection state of the client.
        /// </summary>
        public static ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Convenience property: `true` when fully connected to the Archipelago server.
        /// </summary>
        public static bool IsConnected =>
            State == ConnectionState.Connected && Session?.Socket?.Connected == true;

        #endregion

        #region Session Information

        /// <summary>
        /// The local settings for the client, as configured by the player.
        ///
        /// This contains overrides for the server-provided settings, which are stored in <seealso cref="Settings"/>,
        /// and allows the player to customize their experience without affecting the server's authoritative configuration,
        /// changing non-YAML settings such as notification frequency, etc.
        /// </summary>
        public static ModDataStoreCache<ClientSettings> LocalSettings { get; set; } =
            RitsuLibFramework
                .GetDataStore(ModEntry.ModId)
                .CreateCache<ClientSettings>("apsettings");

        /// <summary>
        /// The Archipelago Slot's settings, returned from the Server and initially configured from the player's YAML.
        ///
        /// Unless overridden using local settings, this is the default source of truth for the session's settings.
        ///
        /// It should not be written to after initialization, as it represents the server's authoritative configuration for this slot,
        /// which we can't change.
        /// </summary>
        public static ArchipelagoSettings? Settings { get; private set; }

        // Retain YAML defaults even when multiplayer installs an effective settings snapshot.
        public static AncientRewardSettings AncientSlotDefaults { get; private set; } =
            new(AncientRelicLocation.StartOfAct, AncientRelicPoolMode.Balanced);

        /// <summary>Restores the fixed host's frozen settings on its own process.</summary>
        internal static bool TryUseMultiplayerHostSettings(
            ArchipelagoSettings settings,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (settings.PlayerNumber != CoopSlot.PlayerNumber)
            {
                reason = $"The saved host uses Player {settings.PlayerNumber}, but this connection uses Player {CoopSlot.PlayerNumber}.";
                return false;
            }

            // RitsuLib's JSON round-trip does not preserve the comparer from the initialized
            // ConcurrentDictionary. Native character IDs are upper-case while AP slot-data keys
            // use title case, so normalize the map again whenever frozen settings are installed.
            settings.Characters = new System.Collections.Concurrent.ConcurrentDictionary<
                string,
                CharacterConfig
            >(
                settings.Characters,
                StringComparer.InvariantCultureIgnoreCase
            );

            if (!TryValidateConfiguredCharacters(settings, out reason))
                return false;

            Settings = settings;
            return true;
        }

        internal static bool TryValidateConfiguredCharacters(
            ArchipelagoSettings settings,
            out string reason)
        {
            var installedCharacterIds = ModelDb.AllCharacters
                .Select(character => character.Id.Entry)
                .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
            var missingCharacterIds = settings.Characters.Values
                .Select(config => config.OfficialName)
                .Where(characterId =>
                    string.IsNullOrWhiteSpace(characterId)
                    || !installedCharacterIds.Contains(characterId)
                )
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .Order(StringComparer.InvariantCultureIgnoreCase)
                .ToArray();

            if (missingCharacterIds.Length > 0)
            {
                reason = "The AP slot configures character model(s) that are not loaded: "
                    + string.Join(", ", missingCharacterIds.Select(characterId =>
                        string.IsNullOrWhiteSpace(characterId) ? "<missing id>" : characterId
                    ))
                    + ". Enable the matching character mod(s) and reconnect.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static void RebuildUnlockedCharactersFromSettings()
        {
            Progress.UnlockedCharacters.Clear();
            SetupUnlockedCharacters();
        }

        /// <summary>
        /// Validates a character against the current slot rather than the reused native
        /// character-select button state.
        /// </summary>
        internal static bool CanSelectCharacter(CharacterModel character, out string reason)
        {
            if (Settings?.Characters == null)
            {
                reason = "The Archipelago slot has not finished preparing its characters.";
                return false;
            }

            if (!Settings.Characters.ContainsKey(character.Id.Entry))
            {
                reason = $"Character {character.Id.Entry} is not configured for this AP slot.";
                return false;
            }

            if (!Progress.UnlockedCharacters.Any(unlocked => unlocked.Id == character.Id))
            {
                reason = $"Character {character.Id.Entry} is not unlocked for this AP slot.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static ArchipelagoSession? Session { get; set; }

        /// <summary>
        /// Progress of the player through their Archipelago game.
        /// Some of this data resets every run.
        /// </summary>
        public static ArchipelagoProgress Progress { get; set; } = new();

        /// <summary>
        /// Represents how caught up we are with Archipelago's sent items
        /// </summary>
        private static int Index;

        public static Dictionary<string, object> SlotData { get; set; } = new();

        /// <summary>
        /// Archipelago Item Locations that we've already found so far, collected by their Location ID
        /// </summary>
        // The SDK may publish its initial checked locations before login preparation runs.
        public static List<long> CheckedLocations { get; set; } = new();

        #endregion

        /// <summary>
        /// Spinlock for processing incoming items to ensure that we don't have multiple threads trying to process items at the same time
        /// </summary>
        private static readonly object _itemLock = new();

        // RitsuLib polls top-bar counts every frame. Cache the derived reward count and only
        // re-enumerate item history when one of its inexpensive inputs changes.
        private static ArchipelagoProgress? _rewardCountProgress;
        private static long? _rewardCountCharacterOffset;
        private static long _rewardCountItemRevision = -1;
        private static int _rewardCountGoldRemaining = int.MinValue;
        private static int _rewardCountRelicChoiceAssignments = -1;
        private static int _rewardCountRelicsAvailableAnytime = -1;
        private static int _rewardCountDeferredMultiplayerItems = -1;
        private static int _cachedAvailableRewardCount;

        /// <summary>
        /// Safely reads whether a character has enough of the requested progressive campfire item
        /// for the supplied one-based Act. Incoming AP items may be processed off the Godot main
        /// thread, so top-bar UI reads share the item-processing lock.
        /// TODO: @Platando: if/once there's clear separation between consumption and producing:
        /// this lock will stay here but most likely can be removed later
        /// </summary>
        internal static bool HasProgressiveCampfireAccess(long characterOffset, int act, bool smith)
        {
            lock (_itemLock)
            {
                var source = smith ? Progress.ProgressiveSmiths : Progress.ProgressiveRests;
                return source.TryGetValue(characterOffset, out var maxAct) && maxAct >= act;
            }
        }

        /// <summary>
        /// Returns the number shown on the RitsuLib Archipelago Rewards button. RitsuLib polls
        /// this from the Godot main thread while incoming items may be processed in the background.
        /// @Platando same with this stuff as above, lock can probably be removed in the future
        /// </summary>
        internal static int GetAvailableRewardCount()
        {
            // TODO: doesn't this depend on what type of guest you are:
            if (MultiplayerSupport.IsLocalGuest)
                return 0;

            lock (_itemLock)
            {
                long? characterOffset = GameUtility.CurrentConfig?.CharOffset;
                long itemRevision = Progress.Items.Revision;
                int goldRemaining = Progress.GoldRemaining;
                int relicChoiceAssignments = Progress.RelicChoiceAssignments.Count;
                int relicsAvailableAnytime = Progress.RelicRewardsAvailableAnytimeForRun;
                int deferredMultiplayerItems =
                    MultiplayerSupport.PendingUnsupportedItems.Count(item =>
                        ArchipelagoIdCodec.IsUniversalItemId(item.Item.ItemId)
                        || item.Item.GetAPCharacterNumber() == characterOffset
                    );

                if (ReferenceEquals(_rewardCountProgress, Progress) &&
                    _rewardCountCharacterOffset == characterOffset &&
                    _rewardCountItemRevision == itemRevision &&
                    _rewardCountGoldRemaining == goldRemaining &&
                    _rewardCountRelicChoiceAssignments == relicChoiceAssignments &&
                    _rewardCountRelicsAvailableAnytime == relicsAvailableAnytime &&
                    _rewardCountDeferredMultiplayerItems == deferredMultiplayerItems)
                {
                    return _cachedAvailableRewardCount;
                }

                int count = Progress.UnusedItemCount;
                if (goldRemaining > 0)
                    count++;
                count += deferredMultiplayerItems;

                _rewardCountProgress = Progress;
                _rewardCountCharacterOffset = characterOffset;
                _rewardCountItemRevision = itemRevision;
                _rewardCountGoldRemaining = goldRemaining;
                _rewardCountRelicChoiceAssignments = relicChoiceAssignments;
                _rewardCountRelicsAvailableAnytime = relicsAvailableAnytime;
                _rewardCountDeferredMultiplayerItems = deferredMultiplayerItems;
                _cachedAvailableRewardCount = count;
                return _cachedAvailableRewardCount;
            }
        }

        /// <summary>
        /// Fires when the connection state changes
        /// </summary>
        public static event Action<ConnectionState>? ConnectionStateChanged;

        /// <summary>
        /// Pre-scouted location data. Key is location ID, value is a tuple of (ItemName, PlayerName).
        /// Populated on connection to avoid async calls during gameplay.
        /// </summary>
        public static Dictionary<long, ScoutedItemInfo> ScoutedLocations { get; set; } = new();

        #region Death Link Information

        /// <summary>
        /// Handles Death Link functionality, which allows players to share deaths across the multiworld.
        /// </summary>
        public static DeathLinkService? DeathLinkController { get; set; }

        /// <summary>
        /// A cache of the last Death Link message received, which will be loaded into a clone of the Death Link Curse after it
        /// goes from "canonical" to "mutable" (i.e. instanced)
        /// </summary>
        public static string? LastDeathLinkMessage { get; set; }

        private static DateTime? _lastDeathLinkReceivedAt;
        // Locking because we're reading/writing across threads, and caching can happen
        private static readonly object _deathLinkLock = new();
        /// <summary>
        /// The UTC timestamp of the most recently received Death Link.
        ///
        /// Used to suppress re-triggering a Death Link when the player dies
        /// as a direct result of receiving one.
        ///
        /// Null if no Death Link has been received this session,
        /// or if we're in Curse mode (which doesn't warrant suppression).
        /// </summary>
        public static DateTime? LastDeathLinkReceivedAt { get {
            lock(_deathLinkLock)
            {
                return _lastDeathLinkReceivedAt;
            }
        } set {
            lock(_deathLinkLock)
            {
                _lastDeathLinkReceivedAt = value;
            }
        } }

        #endregion

        #region Networking

        private static ReaderWriterLock ConnectionLock { get; } = new ReaderWriterLock();
        private static readonly object _connectionStateLock = new();
        private static bool _currentAttemptIsAutomaticReconnect;
        private static SessionCallbacks? _sessionCallbacks;

        /// <summary>Runs session callbacks on Godot's thread only while their owner is current.</summary>
        internal static void RunForSession(ArchipelagoSession session, Action action) =>
            Callable.From(() =>
            {
                if (ReferenceEquals(Session, session))
                    action();
            }).CallDeferred();

        private static void PublishConnectionState()
        {
            var session = Session;
            var state = State;
            Callable.From(() =>
            {
                if (ReferenceEquals(Session, session) && State == state)
                    ConnectionStateChanged?.Invoke(state);
            }).CallDeferred();
        }

        /// <summary>Only the home screen can discard a slot; live runs retain their AP identity.</summary>
        internal static bool CanLeaveSlot =>
            MenuUtility.MainMenu is { } menu
            && GodotObject.IsInstanceValid(menu) && menu.IsInsideTree() && menu.IsVisibleInTree()
            && !menu.SubmenuStack.SubmenusOpen
            && !MegaCrit.Sts2.Core.Runs.RunManager.Instance.IsInProgress
            && !GameUtility.IsInRun && !MultiplayerSupport.IsRealMultiplayerRun
            && !MultiplayerSupport.TryGetObservedStartLobby(out _);

        internal static bool HasSlotConnection =>
            State != ConnectionState.Disconnected || Settings != null || ApReconnectController.IsActive;

        /// <summary>Intentional home-screen departure, distinct from a recoverable socket loss.</summary>
        internal static bool TryLeaveSlot()
        {
            if (!CanLeaveSlot)
            {
                LogUtility.Warn("[AP Session] Refused slot switch outside the home screen");
                return false;
            }
            if (!PendingCheckUtility.PreserveForSlotSwitch())
                return false;

            LogUtility.Info($"[AP Session] Leaving slot {PlayerName}, seed {Seed}; saved runs are preserved");
            ApReconnectController.Stop();
            Disconnect(showMultiplayerNotice: false);
            ResetSlotState();
            ArchipelagoConnectionUI.CancelPendingAttempt();
            ArchipelagoRewardUI.RemoveUI();
            ArchipelagoCharTrackerUI.RemoveUI();
            ArchipelagoGoalTrackerUI.RemoveUI();
            ArchipelagoNotificationUI.RemoveUI();
            PublishConnectionState();
            return true;
        }

        private static void ResetSlotState()
        {
            // The item callback checks its session under this same lock. An old callback
            // cannot repopulate the queue after this reset, even if it was already in flight.
            lock (_itemLock)
            {
                Patches_ItemProcessor.ClearQueue();
                Index = 0;
                Progress = new ArchipelagoProgress();
            }
            Settings = null;
            SlotData = new();
            CheckedLocations = new();
            ScoutedLocations = new();
            Seed = string.Empty;
            DeathLinkController = null;
            LastDeathLinkMessage = null;
            LastDeathLinkReceivedAt = null;
            _rewardCountProgress = null;
            BuffUtility.ResetSlotState();
            NotificationUtility.ClearQueue();
            GameUtility.ResetSlotState();
            MultiplayerSupport.ForgetApSession();
            LogUtility.Info("[AP Session] Cleared slot caches and receipt indexes");
        }

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            ApReconnectController.Stop();
            BeginConnect(isAutomaticReconnect: false);
        }

        internal static void ConnectForAutomaticRetry()
        {
            if (!ApReconnectController.IsActive)
                return;
            BeginConnect(isAutomaticReconnect: true);
        }

        private static void BeginConnect(bool isAutomaticReconnect)
        {
            lock (_connectionStateLock)
            {
                // Ignore if we're already connected or connecting
                if (State != ConnectionState.Disconnected)
                    return;
                State = isAutomaticReconnect
                    ? ConnectionState.Reconnecting
                    : ConnectionState.Connecting;
                _currentAttemptIsAutomaticReconnect = isAutomaticReconnect;
            }

            // A live run can continue earning checks during asynchronous reconnection.
            // Retain its location cache until login validates the replacement session.
            // Intentional slot departure already clears these caches in ResetSlotState.

            // Attempt to create the AP Session
            ArchipelagoSession connectionSession;
            try
            {
                connectionSession = ArchipelagoSessionFactory.CreateSession(ServerAddress);
            }
            catch (Exception e)
            {
                LogUtility.Error($"Failed to create Archipelago session: {e.Message}");
                Disconnect(showMultiplayerNotice: !isAutomaticReconnect);
                if (isAutomaticReconnect)
                    ApReconnectController.OnAttemptFailed();
                return;
            }

            lock (_connectionStateLock)
            {
                if (State is not ConnectionState.Connecting and not ConnectionState.Reconnecting)
                {
                    LogUtility.Debug("Discarding an Archipelago session after connection was cancelled");
                    _ = Task.Run(() => connectionSession.Socket.DisconnectAsync());
                    return;
                }
                Session = connectionSession;
            }

            DeathLinkController = connectionSession.CreateDeathLinkService();
            _sessionCallbacks = new SessionCallbacks(connectionSession, DeathLinkController);
            PublishConnectionState();
            string playerName = PlayerName;
            string password = ServerPassword;

            // Login is blocking in the SDK. Keep it off Godot's thread so the home-screen
            // Cancel action remains usable, but hold incoming receipts until preparation
            // has completed on the main thread (the same ordering as the original login).
            try
            {
                _ = Task.Run(() =>
                    {
                        if (!ReferenceEquals(Session, connectionSession))
                            return;
                        try
                        {
                            ConnectionLock.AcquireWriterLock(30000);
                            try
                            {
                                if (!ReferenceEquals(Session, connectionSession))
                                    return;
                                LoginResult loginResult;
                                try
                                {
                                    loginResult = connectionSession.TryConnectAndLogin(
                                        Game,
                                        playerName,
                                        ItemsHandlingFlags.AllItems,
                                        new Version(APVersion),
                                        password: password,
                                        requestSlotData: true
                                    );
                                }
                                catch (Exception ex)
                                {
                                    loginResult = new LoginFailure(ex.ToString());
                                }

                                var prepared = new TaskCompletionSource();
                                Callable.From(() =>
                                {
                                    try
                                    {
                                        HandleConnectResult(connectionSession, loginResult);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogUtility.Error($"Failed to prepare Archipelago connection: {ex}");
                                        if (ReferenceEquals(Session, connectionSession))
                                        {
                                            ApReconnectController.Stop();
                                            Disconnect();
                                        }
                                    }
                                    finally
                                    {
                                        prepared.SetResult();
                                    }
                                }).CallDeferred();
                                prepared.Task.GetAwaiter().GetResult();
                            }
                            finally
                            {
                                ConnectionLock.ReleaseWriterLock();
                            }
                        }
                        catch (Exception ex)
                        {
                            RunForSession(connectionSession, () => HandleConnectResult(
                                connectionSession, new LoginFailure(ex.ToString())));
                        }
                    });
            }
            catch (Exception e)
            {
                Callable
                    .From(() =>
                        HandleConnectResult(connectionSession, new LoginFailure(e.ToString()))
                    )
                    .CallDeferred();
            }
        }

        /// <summary>
        /// Handle the outcome of a connection attempt
        /// </summary>
        private static void HandleConnectResult(
            ArchipelagoSession connectionSession,
            LoginResult result
        )
        {
            if (result.Successful && !connectionSession.Socket.Connected)
                result = new LoginFailure("The Archipelago connection closed during login.");
            string outText;
            bool wasAutomaticReconnect;
            lock (_connectionStateLock)
            {
                if (
                    State is not ConnectionState.Connecting and not ConnectionState.Reconnecting
                    || !ReferenceEquals(Session, connectionSession)
                )
                {
                    LogUtility.Debug("Ignoring a stale Archipelago login result");
                    return;
                }

                wasAutomaticReconnect = _currentAttemptIsAutomaticReconnect;

                if (result.Successful)
                {
                    State = ConnectionState.Connected;
                }
            }

            if (result.Successful)
            {
                var success = (LoginSuccessful)result;

                int apTeamId = connectionSession.ConnectionInfo.Team;
                int apSlotId = connectionSession.ConnectionInfo.Slot;
                if (!MultiplayerSupport.ValidateApSessionIdentity(
                        connectionSession.RoomState.Seed,
                        apTeamId,
                        apSlotId,
                        out string identityError))
                {
                    LogUtility.Error($"Refusing Archipelago reconnect: {identityError}");
                    ApReconnectController.Stop(identityError);
                    Disconnect();
                    NotificationUtility.ShowRawText(
                        "Archipelago reconnected to a different room or slot. This run remains disconnected."
                    );
                    return;
                }

                // Validate ownership before replacing the active run's authenticated identity.
                SlotData = success.SlotData;
                Seed = connectionSession.RoomState.Seed;

                // Log all slot data
                LogUtility.Info("Dumping Slot Data:");
                foreach (var kvp in SlotData)
                {
                    LogUtility.Info($"KEY: {kvp.Key}");
                    LogUtility.Info($"VAL: {kvp.Value.ToString()}");
                }

                if (!TryReadApWorldCompatibility(
                        out System.Version apWorldVersion,
                        out int apWorldCompatFlag,
                        out string compatibilityError
                    ))
                {
                    RejectIncompatibleConnection(compatibilityError, wasAutomaticReconnect);
                    return;
                }

                System.Version bundledApWorldVersion = BundledApWorldManifestVersion.Value;
                LogUtility.Info($"APWorld Version: v{apWorldVersion}");
                LogUtility.Info($"Bundled APWorld Version: v{bundledApWorldVersion}");
                LogUtility.Info($"Client Version: {Version}");
                LogUtility.Info(
                    $"APWorld CompatFlag: {apWorldCompatFlag}; client CompatFlag: {SupportedCompatFlag}"
                );

                if (apWorldCompatFlag != SupportedCompatFlag)
                {
                    RejectIncompatibleConnection(
                        $"Incompatible APWorld contract: the APWorld uses CompatFlag "
                            + $"{apWorldCompatFlag}, but this client requires {SupportedCompatFlag}.",
                        wasAutomaticReconnect
                    );
                    return;
                }

                try
                {
                    Settings = GetPlayerSettings(apWorldVersion);
                }
                catch (Exception ex)
                {
                    RejectIncompatibleConnection($"Invalid AP player settings: {ex.Message}", wasAutomaticReconnect);
                    return;
                }
                LogUtility.Info($"Using co-op Player {Settings.PlayerNumber}/{Settings.PlayerCount} in AP slot {apSlotId}.");
                if (!TryValidateConfiguredCharacters(Settings, out string characterError))
                {
                    RejectIncompatibleConnection(characterError, wasAutomaticReconnect);
                    return;
                }

                int apWorldAgeComparison = CompareMajorMinor(
                    bundledApWorldVersion,
                    apWorldVersion
                );
                if (apWorldAgeComparison > 0)
                {
                    string warning =
                        $"The server's APWorld v{apWorldVersion} is older than the bundled APWorld "
                            + $"v{bundledApWorldVersion}. CompatFlag {SupportedCompatFlag} still matches, "
                            + "but updating the APWorld is recommended.";
                    LogUtility.Warn(warning);

                    if (wasAutomaticReconnect)
                    {
                        NotificationUtility.ShowRawText(
                            warning,
                            timeout: 8.0,
                            priority: NotificationUtility.NotificationPriority.High
                        );
                        OnConnected();
                        return;
                    }

                    var warningBody = new LocString("main_menu_ui", "APWORLD_OLDER.body");
                    warningBody.Add("server", $"v{apWorldVersion}");
                    warningBody.Add("bundled", $"v{bundledApWorldVersion}");
                    var popup = new ConfirmPopup
                    {
                        Header = new LocString("main_menu_ui", "APWORLD_OLDER.header"),
                        Body = warningBody,
                        ButtonPressed = continueConnecting =>
                        {
                            if (continueConnecting)
                                OnConnected();
                            else
                                RejectIncompatibleConnection(
                                    "Connection cancelled. Update the APWorld before trying again."
                                );
                        },
                    };

                    ArchipelagoConnectionUI.Hide();
                    popup.Show();
                    return;
                }

                // Patch-only differences within one major/minor line are intentionally silent.
                OnConnected();
            }
            else
            {
                // Log the error
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerAddress} as {PlayerName}.";
                outText = failure.Errors.Aggregate(
                    outText,
                    (current, error) => current + $"\n    {error}"
                );

                // End the connection
                Disconnect(showMultiplayerNotice: !wasAutomaticReconnect);
                if (wasAutomaticReconnect)
                    ApReconnectController.OnAttemptFailed();
            }
        }

        private static bool TryReadApWorldCompatibility(
            out System.Version apWorldVersion,
            out int compatFlag,
            out string error
        )
        {
            apWorldVersion = new System.Version(0, 0, 0);
            compatFlag = SupportedCompatFlag;
            error = string.Empty;

            if (!SlotData.TryGetValue("mod_compat_version", out object? versionValue)
                || !System.Version.TryParse(
                    Convert.ToString(versionValue)?.TrimStart('v', 'V'),
                    out System.Version? parsedVersion
                )
                || parsedVersion == null)
            {
                error = "The APWorld did not provide a valid semantic version.";
                return false;
            }
            apWorldVersion = parsedVersion;

            if (!SlotData.TryGetValue("CompatFlag", out object? compatValue))
            {
                LogUtility.Info("APWorld omitted CompatFlag; defaulting to contract 1.");
                return true;
            }

            try
            {
                compatFlag = Convert.ToInt32(compatValue);
                if (compatFlag < 1)
                    throw new InvalidDataException("CompatFlag must be a positive integer.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"The APWorld supplied an invalid CompatFlag "
                    + $"('{Convert.ToString(compatValue)}'): {ex.Message}";
                return false;
            }
        }

        private static System.Version GetClientSemanticVersion() => ModManifestVersion.Value;

        private static System.Version ReadModManifestVersion() => ReadEmbeddedSemanticVersion(
            ModManifestResourceName,
            "version",
            "mod manifest"
        );

        private static System.Version ReadBundledApWorldManifestVersion() =>
            ReadEmbeddedSemanticVersion(
                ApWorldManifestResourceName,
                "world_version",
                "bundled APWorld manifest"
            );

        private static System.Version ReadEmbeddedSemanticVersion(
            string resourceName,
            string propertyName,
            string manifestLabel
        )
        {
            using Stream stream = typeof(ArchipelagoClient).Assembly.GetManifestResourceStream(
                resourceName
            ) ?? throw new InvalidDataException(
                $"Embedded {manifestLabel} '{resourceName}' was not found."
            );
            using JsonDocument manifest = JsonDocument.Parse(stream);
            if (!manifest.RootElement.TryGetProperty(propertyName, out JsonElement versionElement))
            {
                throw new InvalidDataException(
                    $"Embedded {manifestLabel} has no {propertyName} field."
                );
            }

            string? versionText = versionElement.GetString();
            string semanticCore = versionText?.Split('-', '+')[0] ?? string.Empty;
            if (!System.Version.TryParse(semanticCore, out System.Version? version)
                || version.Build < 0)
            {
                throw new InvalidDataException(
                    $"Embedded {manifestLabel} version '{versionText}' is not semantic X.Y.Z."
                );
            }
            return version;
        }

        private static int CompareMajorMinor(System.Version left, System.Version right)
        {
            int majorComparison = left.Major.CompareTo(right.Major);
            return majorComparison != 0
                ? majorComparison
                : left.Minor.CompareTo(right.Minor);
        }

        private static void RejectIncompatibleConnection(
            string reason,
            bool wasAutomaticReconnect = false
        )
        {
            LogUtility.Error($"Archipelago compatibility check failed: {reason}");
            ApReconnectController.Stop(reason);
            if (wasAutomaticReconnect)
            {
                Disconnect(showMultiplayerNotice: false);
                NotificationUtility.ShowRawText(
                    reason,
                    timeout: 8.0,
                    priority: NotificationUtility.NotificationPriority.High
                );
                return;
            }

            ArchipelagoConnectionUI.Show();
            Disconnect(showMultiplayerNotice: false);
            ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
            ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
            ArchipelagoConnectionUI.SetStatus(reason);
        }

        /// <summary>
        /// Initializes the character-select unlock state from authoritative slot data.
        /// This must happen before the initial received-item queue is allowed to run.
        /// </summary>
        private static void SetupUnlockedCharacters()
        {
            ArchipelagoSettings? settings = Settings;
            if (settings == null)
            {
                LogUtility.Error("Cannot set up unlocked characters without AP slot settings.");
                return;
            }

            var characters = settings.Characters;
            var ids = new HashSet<string>(
                Progress.UnlockedCharacters.Select(c => c.Id.Entry),
                StringComparer.InvariantCultureIgnoreCase
            );

            // Initial item callbacks are blocked by ConnectionLock until OnConnected
            // completes, so the starting Unlock item has not been processed yet. Use
            // the authoritative slot-data flag to initialize every starting character.
            foreach (var config in characters.Values.Where(config => !config.Locked))
            {
                // The character may already be present after a reconnect or save restore.
                if (ids.Contains(config.OfficialName))
                {
                    continue;
                }

                // ModelDb should also work for modded characters to register here
                var model = ModelDb.AllCharacters.FirstOrDefault(character =>
                    string.Equals(
                        character.Id.Entry,
                        config.OfficialName,
                        StringComparison.InvariantCultureIgnoreCase
                    )
                );
                if (model == null)
                {
                    LogUtility.Warn(
                        $"Could not resolve starting AP character '{config.OfficialName}'"
                    );
                    continue;
                }

                Progress.UnlockedCharacters.Add(model);
                ids.Add(model.Id.Entry);
                LogUtility.Info($"Unlocking starting character {model.Id.Entry} from slot data");
            }

            bool someoneUnlocked = characters.Keys.Any(ids.Contains);
            if (!someoneUnlocked)
            {
                // A configured starting character could not be resolved, most likely
                // because a modded character ID is wrong or its mod is not loaded.
                // Keep the existing fail-safe so the character screen is still usable.
                foreach (var c in ModelDb.AllCharacters)
                {
                    if (characters.ContainsKey(c.Id.Entry))
                    {
                        Progress.UnlockedCharacters.Add(c);
                        break;
                    }
                }
                if (Progress.UnlockedCharacters.Count == 0)
                {
                    LogUtility.Error(
                        $"No valid AP characters found to unlock!  Valid characters: {string.Join(",", characters.Keys)}; Existing: {
                        string.Join(",", ModelDb.AllCharacters.Select(c => c.Id.Entry))}"
                    );
                }
                else
                {
                    LogUtility.Info(
                        $"Force unlocking character {Progress.UnlockedCharacters.First().Id.Entry}"
                    );
                }
            }
        }

        /// <summary>
        /// Fires on a successful Archipelago connection.
        /// </summary>
        public static void OnConnected()
        {
            LogUtility.Success("Successfully Connected to Archipelago Server");
            ArchipelagoSession? session = Session;
            ArchipelagoSettings? settings = Settings;
            DeathLinkService? deathLinkController = DeathLinkController;
            if (session == null || settings == null || deathLinkController == null)
            {
                string reason = "The Archipelago connection completed without a fully initialized "
                    + "session, slot settings, and Death Link service.";
                LogUtility.Error(reason);
                ApReconnectController.Stop(reason);
                Disconnect();
                ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                ArchipelagoConnectionUI.SetStatus(reason);
                return;
            }

            int apTeamId = session.ConnectionInfo.Team;
            int apSlotId = session.ConnectionInfo.Slot;
            MultiplayerSupport.NoteApSessionConnected(Seed, apTeamId, apSlotId);

            // Bind durable external effects only after login has authenticated the exact room,
            // team, and slot represented by this session.
            PendingCheckUtility.BindAuthenticatedSession(session, ServerAddress, Seed);

            // Restore checked locations from server so "Claimed" state survives restarts
            CheckedLocations = session.Locations.AllLocationsChecked.Where(CoopSlot.Owns).ToList();
            LogUtility.Info(
                $"Restored {CheckedLocations.Count} previously checked location(s) from server."
            );

            // A fresh session's checked-location list is authoritative, so this is the safe
            // point to discard confirmed outbox entries and replay anything still missing.
            PendingCheckUtility.ReconcileAndSend();

            try
            {
                // Enable/Disable the Death Link Service based on user settings
                LogUtility.Info(
                    $"SLOT - Is Death Link Enabled: {settings.IsDeathLinkEnabled.ToString()}"
                );
                LogUtility.Info(
                    $"SLOT - Death Link Damage Percentage: {settings.DeathLinkDamagePercent.ToString()}%"
                );
                LogUtility.Info(
                    $"SLOT - Death Link Curse Enabled: {settings.EnableDeathFragments.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Settings Override: {LocalSettings.Value.OverrideDeathLinkOptions.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Opt-In to Death Link: {LocalSettings.Value.EnableDeathLink.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Override Damage Percentage: {LocalSettings.Value.DeathLinkPercentDamage.ToString()}%"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Override Curse Enabled: {LocalSettings.Value.EnableDeathFragments.ToString()}"
                );
                if (DeathLinkUtility.IsDeathLinkEnabled)
                {
                    deathLinkController.EnableDeathLink();
                }
                else
                {
                    deathLinkController.DisableDeathLink();
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load player settings: {ex.Message}");
                ApReconnectController.Stop("AP slot settings could not be prepared");
                Disconnect();
                ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                ArchipelagoConnectionUI.SetStatus($"Failed to load settings: {ex.Message}");
                return;
            }

            if (MultiplayerSupport.IsMultiplayerScope)
            {
                if (!TryPrepareCurrentMultiplayerSession(out string preparationError))
                {
                    LogUtility.Error($"AP multiplayer preparation failed: {preparationError}");
                    ApReconnectController.Stop(preparationError);
                    Disconnect();
                    ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                    ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                    ArchipelagoConnectionUI.SetStatus(preparationError);
                    return;
                }
            }
            else
            {
                SetupUnlockedCharacters();
            }

            // Pre-scout all locations so we have item info available for notifications
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations(session));

            // Restore goaled characters from DataStorage so cross-session goal tracking works
            _ = GameUtility.RestoreGoaledCharsFromStorage();

            // Multiplayer progress is checkpointed only in the native host save. Do not attach
            // the singleplayer AP DataStorage save mirror to a multiplayer session.
            if (!MultiplayerSupport.IsMultiplayerScope)
                _ = GameUtility.SetupOnChangedSaves();

            // Load the set of already-consumed buff indices from DataStorage before item processing begins.
            if (!MultiplayerSupport.IsMultiplayerScope)
                _ = BuffUtility.LoadFromStorageAsync();

            // Let the game know that we've connected
            PublishConnectionState();
            if (ApReconnectController.IsActive)
                ApReconnectController.OnConnected();
        }

        /// <summary>
        /// Rebuilds the approved multiplayer receipt profile from authoritative SDK history,
        /// then advances both callback watermarks so the SDK's initial replay cannot double-count it.
        /// </summary>
        internal static bool TryPrepareCurrentMultiplayerSession(out string reason)
        {
            reason = string.Empty;
            if (!IsConnected || Session == null)
            {
                reason = "Archipelago is not connected.";
                return false;
            }

            IReadOnlyList<ItemInfo> receivedItems = Session.Items.AllItemsReceived;
            // A different AP owner may have used this process previously. Rebuild the
            // selectable set only from this slot's settings and authoritative history.
            RebuildUnlockedCharactersFromSettings();
            if (!MultiplayerSupport.PrepareApSession(
                    Seed,
                    Session.ConnectionInfo.Team,
                    Session.ConnectionInfo.Slot,
                    receivedItems,
                    out reason))
            {
                return false;
            }

            Patches_ItemProcessor.ClearQueue();
            Index = receivedItems.Count;
            Patches_ItemProcessor.LastIndexHandled = Index;
            MultiplayerSupport.RestoreFrozenHostSettingsForActiveRun();
            AscensionMultiplayer.QueueReconnectReconciliation();
            return true;
        }

        /// <summary>
        /// Pre-scouts all locations in the game and stores the results.
        /// This gives us the ability to show item and player names in location/check notifications without having to make async calls during gameplay.
        /// This runs on a background thread, triggered on connection before gameplay starts.
        /// </summary>
        private static void PreScoutAllLocations(ArchipelagoSession session)
        {
            try
            {
                if (!ReferenceEquals(Session, session))
                {
                    LogUtility.Debug("Ignoring scouting for a departed Archipelago session");
                    return;
                }

                // Get all location IDs for our game
                var allLocationIds = session.Locations.AllLocations.ToArray();

                if (allLocationIds.Length == 0)
                {
                    LogUtility.Warn("No locations found to scout");
                    return;
                }

                LogUtility.Info($"Pre-scouting {allLocationIds.Length} locations...");

                // Scout all locations at once (blocking call on this thread)
                var scoutTask = session.Locations.ScoutLocationsAsync(allLocationIds);
                scoutTask.Wait(); // Block until complete. Async doesn't play well with Harmony Patches
                var scoutedLocations = scoutTask.Result;

                // Add all scouted locations to the game's localization tables so they can be shown as rewards (which require `LocString`)
                Dictionary<string, string> locationLocalizations = new();
                foreach (var loc in scoutedLocations)
                {
                    // Add the Item at this location to the localization table with the keys "AP_LOC_{LocationID}"
                    string locKey = $"AP_LOC_{loc.Key}";
                    string locText = $"{loc.Value.ItemDisplayName} for {loc.Value.Player.Name}";
                    locationLocalizations.Add(locKey, locText);
                    LogUtility.Warn(
                        $"{loc.Key}:{loc.Value.LocationName}:{loc.Value.LocationDisplayName}"
                    );
                }
                RunForSession(session, () =>
                {
                    ScoutedLocations = scoutedLocations;
                    TextUtility.RegisterLocTableAtRuntime("ap", locationLocalizations);
                    LogUtility.Success($"Pre-scouted {ScoutedLocations.Count} locations successfully");
                });
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-scout locations: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up our Session with Archipelago
        /// </summary>
        public static void Disconnect(bool showMultiplayerNotice = true)
        {
            ArchipelagoSession? session;
            lock (_connectionStateLock)
            {
                if (State == ConnectionState.Disconnected)
                {
                    LogUtility.Debug("Ignoring duplicate Archipelago disconnect request");
                    return;
                }

                LogUtility.Debug("Disconnecting from Archipelago...");
                session = Session;
                Session = null;
                State = ConnectionState.Disconnected;
                _currentAttemptIsAutomaticReconnect = false;
            }

            if (session != null)
            {
                // Stop the socket-close callback from re-entering this workflow after an
                // intentional disconnect, and release the other session event handlers.
                _sessionCallbacks?.Dispose();
                _sessionCallbacks = null;
                Task.Run(() => session.Socket.DisconnectAsync());
            }

            // Clear session queues so stale entries don't carry over after reconnecting
            BuffUtility.ClearQueue();
            NotificationUtility.ClearQueue();
            MultiplayerSupport.OnApDisconnected();

            // Let the game know that we've disconnected
            PublishConnectionState();

            // An already-received AP item remains authoritative. The experimental multiplayer
            // slice may therefore claim banked gold while AP itself is offline. MegaCrit restores
            // an absent peer from the host's rejoin snapshot; permanent claim invalidation is
            // reserved for an actual unrecoverable binding or grant failure.
            if (MultiplayerSupport.IsMultiplayerScope)
            {
                if (showMultiplayerNotice)
                {
                    string message = MultiplayerSupport.IsRealMultiplayerRun
                        ? "Disconnected from Archipelago. Already received rewards remain available."
                        : "Disconnected from Archipelago. Embark is disabled until reconnection completes.";
                    Callable.From(() => NotificationUtility.ShowRawText(message)).CallDeferred();
                }
            }
            else if (showMultiplayerNotice)
            {
                // Existing singleplayer behavior prompts the user to leave or recover the run.
                Callable.From(GameUtility.ShowOptionsOnLostConnection).CallDeferred();
            }
        }

        /// <summary>
        /// Log errors to the console and handle connection-terminating errors
        /// </summary>
        private static void OnErrorReceived(Exception? e, string message)
        {
            LogUtility.Error($"Archipelago Error: {message}");
            if (e != null)
            {
                LogUtility.Error($"Exception: {e.Message}");
            }

            // Check if this is a connection-terminating error that requires manual cleanup
            if (IsConnectionTerminatingError(e, message))
            {
                LogUtility.Warn("Connection-terminating error detected. Initiating disconnect...");
                HandleUnexpectedDisconnect();
            }
        }

        /// <summary>
        /// Determines if an error represents a connection-terminating condition.
        /// These errors indicate the WebSocket connection is irreversibly broken and requires cleanup.
        ///
        /// I wrote this function because apparently, if the AP Server *abruptly* disconnects (e.g. server crash, force quit, network loss),
        /// only `OnErrorReceived` gets called and not `OnSocketSessionEnd`.
        /// This check allows us to know if we need to trigger the disconnection workflow or not.
        ///
        /// And yeah, there are probably more elegant ways to check this - feel free to refactor in the future :)
        /// </summary>
        private static bool IsConnectionTerminatingError(Exception? e, string message)
        {
            if (e == null || string.IsNullOrEmpty(message))
                return false;

            // Only disconnect if we're actually connected
            if (State != ConnectionState.Connected)
                return false;

            // Check for WebSocket protocol errors that indicate connection loss
            string errorLower = message.ToLower();

            return errorLower.Contains("closed the websocket connection")
                || errorLower.Contains("connection closed")
                || errorLower.Contains("connection reset")
                || e.GetType().Name == "WebSocketException"
                || e.GetType().Name == "OperationCanceledException"
                    && message.Contains("WebSocket");
        }

        /// <summary>
        /// When we end our Session, disconnect from the Archipelago server
        /// </summary>
        private static void OnSocketSessionEnd(string reason)
        {
            LogUtility.Warn($"Socket session ended: {reason}");
            HandleUnexpectedDisconnect();
        }

        private static void HandleUnexpectedDisconnect()
        {
            bool shouldReconnect;
            lock (_connectionStateLock)
            {
                // SocketClosed can arrive after an intentional Disconnect, and ErrorReceived
                // plus SocketClosed may describe the same failure. Claim the transition once.
                if (State == ConnectionState.Disconnected)
                    return;

                shouldReconnect =
                    MultiplayerSupport.IsMultiplayerScope
                    && MultiplayerSupport.PreparedApRoomSeed != null;
                Disconnect();
            }

            if (shouldReconnect)
                ApReconnectController.Begin();
        }

        /// <summary>
        /// Handle incoming items that come from Archipelago
        /// </summary>
        private static void OnItemReceived(ArchipelagoSession session, ReceivedItemsHelper helper)
        {
            ConnectionLock.AcquireReaderLock(120000);

            try
            {
                // Deal with this Item
                lock (_itemLock)
                {
                    if (!ReferenceEquals(Session, session))
                        return;
                    // Grab the item data
                    var receivedItem = helper.DequeueItem();

                    // Ignore if this item is an old message
                    if (helper.Index <= Index)
                        return;

                    // Process on Godot main thread
                    Patches_ItemProcessor.AddToQueue(new IndexedItemInfo(receivedItem, helper.Index));

                    // Keep track of how many messages we've had so far
                    Index++;
                }
            }
            finally
            {
                ConnectionLock.ReleaseReaderLock();
            }
        }

        private static void OnMessageReceived(LogMessage message)
        {
            LogUtility.Info($"Got PrintJson packet {message.GetType().Name} {message.ToString()}");
            switch (message)
            {
                case ItemSendLogMessage itemSend:
                    NotificationUtility.HandleItemSend(itemSend);
                    break;
                case CountdownLogMessage:
                    NotificationUtility.HandleOtherAPMessages(message, false, 0.5);
                    break;
                // This caused the result messages to not come through, probably because the say packets get echoed
                //case PlayerSpecificLogMessage:
                //    NotificationUtility.HandleOtherAPMessages(message, true);
                //    break;
                case CommandResultLogMessage:
                case AdminCommandResultLogMessage:
                    NotificationUtility.HandleOtherAPMessages(message, true, 3.0);
                    break;
                default:
                    return;
            }
        }

        /// <summary>Owns and detaches every callback for one SDK session.</summary>
        private sealed class SessionCallbacks : IDisposable
        {
            private readonly ArchipelagoSession _session;
            private readonly DeathLinkService _deathLink;

            public SessionCallbacks(ArchipelagoSession session, DeathLinkService deathLink)
            {
                _session = session;
                _deathLink = deathLink;
                session.Items.ItemReceived += ItemReceived;
                session.Socket.ErrorReceived += ErrorReceived;
                session.Socket.SocketClosed += SocketClosed;
                session.MessageLog.OnMessageReceived += MessageReceived;
                session.Locations.CheckedLocationsUpdated += LocationsUpdated;
                deathLink.OnDeathLinkReceived += DeathLinkReceived;
            }

            private void ItemReceived(ReceivedItemsHelper helper) => OnItemReceived(_session, helper);
            private void ErrorReceived(Exception error, string message) =>
                RunForSession(_session, () => OnErrorReceived(error, message));
            private void SocketClosed(string reason) =>
                RunForSession(_session, () =>
                {
                    // Login owns failure/retry scheduling until preparation finishes.
                    // Racing its result here would discard OnAttemptFailed and stall retries.
                    if (State == ConnectionState.Connected)
                        OnSocketSessionEnd(reason);
                });
            private void MessageReceived(LogMessage message) =>
                RunForSession(_session, () => OnMessageReceived(message));
            private void DeathLinkReceived(DeathLink deathLink) =>
                RunForSession(_session, () => DeathLinkUtility.OnDeathLinkReceived(deathLink));
            private void LocationsUpdated(System.Collections.ObjectModel.ReadOnlyCollection<long> locations)
            {
                long[] ids = locations.ToArray();
                RunForSession(_session, () =>
                {
                    foreach (long id in ids)
                        if (CoopSlot.Owns(id) && !CheckedLocations.Contains(id))
                            CheckedLocations.Add(id);
                    // This SDK event also includes optimistic local checks. Do not use it
                    // to acknowledge durable outbox entries; fresh login still owns that.
                    int previousCampfireCount = Progress.CheckedCampfireLocationIds.Count;
                    Progress.RefreshCheckedCampfiresFromClient();
                    int addedCampfires = Progress.CheckedCampfireLocationIds.Count - previousCampfireCount;
                    if (addedCampfires > 0
                        && MultiplayerSupport.IsRealMultiplayerRun
                        && MultiplayerSupport.IsLocalOwnApSlot
                        && GameUtility.CurrentPlayer is { } player)
                    {
                        // !collect bypasses the local check writer. Publish its campfire
                        // changes so every replica builds options from the updated owner state.
                        if (!MultiplayerLocationChecks.PublishEffectiveCheckProgress(player))
                        {
                            LogUtility.Error(
                                $"AP location update added {addedCampfires} campfire check(s), "
                                    + $"but progress for player {player.NetId} could not be published"
                            );
                        }
                        else
                        {
                            LogUtility.Info(
                                $"Published {addedCampfires} campfire check(s) from an AP location "
                                    + $"update for player {player.NetId}"
                            );
                        }
                    }
                });
            }

            public void Dispose()
            {
                _session.Items.ItemReceived -= ItemReceived;
                _session.Socket.ErrorReceived -= ErrorReceived;
                _session.Socket.SocketClosed -= SocketClosed;
                _session.MessageLog.OnMessageReceived -= MessageReceived;
                _session.Locations.CheckedLocationsUpdated -= LocationsUpdated;
                _deathLink.OnDeathLinkReceived -= DeathLinkReceived;
            }
        }

        #endregion

        #region Slot Information

        /// <summary>
        /// Get all of the Player's Settings for their Archipelago Slot
        /// </summary>
        private static ArchipelagoSettings GetPlayerSettings(System.Version apWorldVersion)
        {
            // Use the SlotData that was already retrieved during login
            // instead of calling Session.DataStorage.GetSlotData() which performs
            // a synchronous network call that can deadlock/timeout when the websocket
            // thread is busy processing incoming item packets (e.g. on reconnect).
            var slotData = SlotData;
            if (slotData == null || slotData.Count == 0)
            {
                LogUtility.Error("No slot data found for this player!");
                throw new InvalidDataException("No slot data found for this player!");
            }
            ArchipelagoSettings settings = new()
            {
                APWorldVersion = apWorldVersion,
                PlayerCount = Convert.ToInt32(slotData["player_count"]),
                PlayerNumber = LocalSettings.Value.MultiplayerPlayerNumber,
            };
            if (!CoopPlayerSelection.IsValid(settings.PlayerCount, settings.PlayerNumber))
                throw new InvalidDataException($"Player {settings.PlayerNumber} is outside this slot's player_count={settings.PlayerCount}. Change Player Number in Multiplayer Settings before connecting.");
            if (slotData["players"] is not JObject players
                || players[settings.PlayerNumber.ToString()] is not JArray playerCharacters)
                throw new InvalidDataException("The AP slot is missing the selected player's character configuration.");

            // Apply all found settings
            if (slotData.ContainsKey("seeded"))
                settings.IsSeeded = Convert.ToBoolean(slotData["seeded"]);
            if (slotData.ContainsKey("death_link"))
                settings.IsDeathLinkEnabled = Convert.ToBoolean(slotData["death_link"]);
            if (slotData.ContainsKey("shuffle_all_cards"))
                settings.ShouldShuffleAllCards = Convert.ToBoolean(slotData["shuffle_all_cards"]);
            if (slotData.ContainsKey("lock_characters"))
                settings.NoCharactersLocked = Convert.ToInt32(slotData["lock_characters"]) == 0;
            if (slotData.ContainsKey("enable_death_fragments"))
                settings.EnableDeathFragments =
                    Convert.ToInt32(slotData["enable_death_fragments"]) == 1;
            if (slotData.ContainsKey("death_link_damage_percent"))
                settings.DeathLinkDamagePercent = Convert.ToInt32(
                    slotData["death_link_damage_percent"]
                );
            if (slotData.ContainsKey("num_chars_goal"))
                settings.NumCharsGoal = Convert.ToInt32(slotData["num_chars_goal"]);
            if (playerCharacters is System.Collections.IList charsList)
            {
                // Grab the total number of characters
                settings.TotalCharacters = charsList.Count;

                // Go through each character and add it to the list of Characters in our settings.
                // Slot data from Archipelago.MultiClient.Net is deserialized via Newtonsoft.Json,
                // so each entry arrives as a JObject, NOT a Dictionary<string, object>.
                foreach (var charData in charsList)
                {
                    if (charData is JObject characterData)
                    {
                        var config = CharacterConfig.fromJObject(
                            characterData,
                            settings.APWorldVersion
                        );
                        if (config != null)
                        {
                            settings.Characters.Add(config.OfficialName, config);
                        }
                    }
                }

            }

            if (slotData.ContainsKey("neow_sanity"))
                settings.NeowSanity = Convert.ToInt32(slotData["neow_sanity"]) != 0;

            if(slotData.ContainsKey("ancient_relic_location"))
                settings.AncientRelicLocation = (AncientRelicLocation)Convert.ToInt32(slotData["ancient_relic_location"]);
            if(slotData.ContainsKey("ancient_relic_pool"))
                settings.AncientRelicPool = (AncientRelicPoolMode)Convert.ToInt32(slotData["ancient_relic_pool"]);
            AncientSlotDefaults = new(settings.AncientRelicLocation, settings.AncientRelicPool);
            // These keys are one APWorld/client contract. Missing values should reject the slot
            // instead of silently changing the run's reward rules.
            if(slotData.ContainsKey("relic_rewards_available_anytime"))
                settings.RelicRewardsAvailableAnytime = Convert.ToInt32(slotData["relic_rewards_available_anytime"]);
            if(slotData.ContainsKey("release_on_victory"))
                settings.ReleaseOnVictory = Convert.ToBoolean(slotData["release_on_victory"]);

            if (slotData.ContainsKey("campfire_sanity"))
                settings.CampfireSanity = Convert.ToInt32(slotData["campfire_sanity"]) != 0;

            if (slotData.ContainsKey("gold_sanity"))
                settings.GoldSanity = Convert.ToInt32(slotData["gold_sanity"]) != 0;

            if (slotData.ContainsKey("potion_sanity"))
                settings.PotionSanity = Convert.ToInt32(slotData["potion_sanity"]) != 0;

            if (slotData.ContainsKey("include_floor_checks"))
                settings.Floorsanity = Convert.ToInt32(slotData["include_floor_checks"]) != 0;

            if(slotData.ContainsKey("progressive_starter_card"))
                settings.ProgressiveStarterCard =
                    Convert.ToInt32(slotData["progressive_starter_card"]) != 0;
            if(slotData.ContainsKey("progressive_starter_relic"))
                settings.ProgressiveStarterRelic =
                    Convert.ToInt32(slotData["progressive_starter_relic"]) != 0;

            if (slotData.ContainsKey("shop_sanity"))
                settings.ShopSanity = Convert.ToInt32(slotData["shop_sanity"]) != 0;
                
            if (slotData.ContainsKey("shop_sanity_options") && slotData["shop_sanity_options"] is Newtonsoft.Json.Linq.JObject shopOptions)
            {
                if (shopOptions.TryGetValue("card_slots", out var cardSlotsToken))
                    settings.ShopCardSlots = Convert.ToInt32(cardSlotsToken);

                if (shopOptions.TryGetValue("neutral_slots", out var neutralSlotsToken))
                    settings.ShopNeutralSlots = Convert.ToInt32(neutralSlotsToken);

                if (shopOptions.TryGetValue("relic_slots", out var relicSlotsToken))
                    settings.ShopRelicSlots = Convert.ToInt32(relicSlotsToken);

                if (shopOptions.TryGetValue("potion_slots", out var potionSlotsToken))
                    settings.ShopPotionSlots = Convert.ToInt32(potionSlotsToken);

                if (shopOptions.TryGetValue("card_remove", out var cardRemoveToken))
                    settings.ShopRemoveSlots = Convert.ToBoolean(cardRemoveToken);

                if (shopOptions.TryGetValue("costs", out var costsToken))
                    settings.ShopSanityCosts = Convert.ToInt32(costsToken);
            }
            else if (settings.ShopSanity)
            {
                LogUtility.Warn("ShopSanity is enabled but 'shop_sanity_options' was missing or not the expected object shape — all shop slots will read as unlocked.");
            }
            // And return it
            return settings;
        }

        #endregion
    }
}
