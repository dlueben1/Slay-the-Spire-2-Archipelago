using StS2AP.Data;
using Archipelago.MultiClient.Net;
using System.Text.Json;

namespace StS2AP.Utils
{
    /// <summary>
    /// Persists location checks before they are sent so a check earned while a dead
    /// connection is still timing out can be replayed after the next successful login.
    /// </summary>
    public static class PendingCheckUtility
    {
        private const string OutboxPrefix = "user://sts_ap_pending_checks_v2_";
        private static readonly object _stateLock = new();
        private static BoundApSession? _boundSession;
        internal static bool HasAuthenticatedSlot => GetBoundSession() != null;

        private sealed record BoundApSession(
            ArchipelagoSession Session,
            ApSessionIdentity Identity
        );

        /// <summary>
        /// Captures the authenticated AP destination used by every later outbox operation. This
        /// must be called only after login has supplied authoritative room, team, and slot data.
        /// </summary>
        internal static void BindAuthenticatedSession(
            ArchipelagoSession session,
            string serverAddress,
            string roomSeed
        )
        {
            ArgumentNullException.ThrowIfNull(session);
            var identity = ApSessionIdentity.Create(
                serverAddress,
                roomSeed,
                session.ConnectionInfo.Team,
                session.ConnectionInfo.Slot,
                CoopSlot.PlayerNumber
            );

            lock (_stateLock)
            {
                _boundSession = new BoundApSession(session, identity);
            }

            LogUtility.Debug($"Bound pending-check outbox to AP session {identity}");
        }

        internal static void ClearSlotBinding()
        {
            lock (_stateLock)
                _boundSession = null;
        }

        /// <summary>
        /// Retains unsent multiplayer checks when leaving a slot after returning to the menu.
        /// The next login replays this existing slot/seed outbox, even if no run is continued.
        /// </summary>
        internal static bool PreserveForSlotSwitch()
        {
            var checks = ArchipelagoClient.Progress.PendingLocationChecks;
            if (checks.Count == 0)
                return true;
            try
            {
                BoundApSession bound = GetBoundSession()
                    ?? throw new IOException("The pending checks have no authenticated AP identity.");
                lock (_stateLock)
                {
                    string path = GetPendingCheckPath(bound.Identity);
                    PendingCheckOutbox outbox = Load(path, bound.Identity);
                    outbox.LocationIds.UnionWith(checks);
                    Save(path, outbox);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[AP Session] Cannot leave slot without preserving pending checks: {ex}");
                NotificationUtility.ShowRawText("Could not preserve pending AP checks. Disconnect cancelled; see the log.");
                return false;
            }
        }

        internal static LocationCheckSendResult.DispatchStatus RecordAndSend(IEnumerable<long> locationIds)
        {
            long[] requested = locationIds.Distinct().ToArray();
            if (requested.Length == 0)
                return LocationCheckSendResult.DispatchStatus.None;
            if (requested.Any(id => id < 0 || !CoopSlot.Owns(id)))
                return LocationCheckSendResult.DispatchStatus.NoAuthenticatedSlot;
            if (MultiplayerSupport.IsMultiplayerScope)
                return RecordAndSendMultiplayer(requested);

            BoundApSession? bound = GetBoundSession();
            if (bound == null)
                return LocationCheckSendResult.DispatchStatus.NoAuthenticatedSlot;
            if (!TryRecord(bound.Identity, requested, out long[] newlyRecorded))
                return LocationCheckSendResult.DispatchStatus.PersistenceFailed;
            if (!IsCurrentConnectedSession(bound) || newlyRecorded.Length == 0)
                return LocationCheckSendResult.DispatchStatus.Queued;

            _ = SendAsync(bound, newlyRecorded, replaying: false);
            return LocationCheckSendResult.DispatchStatus.Submitted;
        }

        /// <summary>
        /// Reconciles the current identity's durable outbox against authoritative state from a
        /// fresh login, then resends only checks that the current slot recognizes and still lacks.
        /// </summary>
        /// <remarks>
        /// This must run before any new checks are submitted through the new session. At that
        /// point, <c>AllLocationsChecked</c> contains only server-confirmed locations and can
        /// safely distinguish acknowledged checks from checks that need to be replayed.
        /// </remarks>
        public static void ReconcileAndSend()
        {
            if (MultiplayerSupport.IsMultiplayerScope)
            {
                ReconcileAndSendMultiplayer();
                return;
            }

            BoundApSession? bound = GetBoundSession();
            if (bound == null || !IsCurrentConnectedSession(bound))
                return;

            HashSet<long> pending;
            try
            {
                lock (_stateLock)
                {
                    string path = GetPendingCheckPath(bound.Identity);
                    PendingCheckOutbox outbox = Load(path, bound.Identity);
                    outbox.LocationIds.ExceptWith(bound.Session.Locations.AllLocationsChecked);
                    Save(path, outbox);
                    pending = new HashSet<long>(outbox.LocationIds);
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to reconcile pending location checks: {ex}");
                return;
            }

            if (pending.Count == 0)
                return;

            var recognized = pending
                .Where(id => ArchipelagoIdCodec.GetPlayerNumber(id) == bound.Identity.PlayerNumber)
                .Where(bound.Session.Locations.AllLocations.Contains)
                .ToHashSet();
            int unrecognizedCount = pending.Count - recognized.Count;
            if (unrecognizedCount > 0)
            {
                LogUtility.Warn(
                    $"Kept {unrecognizedCount} pending location check(s) that are not present "
                        + $"in AP session {bound.Identity}"
                );
            }
            if (recognized.Count == 0)
                return;

            foreach (long locationId in recognized)
            {
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId))
                    ArchipelagoClient.CheckedLocations.Add(locationId);
            }

            LogUtility.Info(
                $"Replaying {recognized.Count} pending location check(s) after reconnecting"
            );
            _ = SendAsync(bound, recognized.ToArray(), replaying: true);
        }

        private static LocationCheckSendResult.DispatchStatus RecordAndSendMultiplayer(long[] locationIds)
        {
            if (!MultiplayerSupport.IsLocalOwnApSlot)
                return LocationCheckSendResult.DispatchStatus.NoAuthenticatedSlot;

            long[] newlyRecorded = locationIds
                .Where(ArchipelagoClient.Progress.PendingLocationChecks.Add).ToArray();
            if (newlyRecorded.Length > 0 && GameUtility.CurrentPlayer is { } player)
                ApRunData.PublishLocalProgress(player);

            BoundApSession? bound = GetBoundSession();
            if (newlyRecorded.Length == 0 || bound == null || !IsCurrentConnectedSession(bound))
                return LocationCheckSendResult.DispatchStatus.Queued;

            _ = SendAsync(bound, newlyRecorded, replaying: false);
            return LocationCheckSendResult.DispatchStatus.Submitted;
        }

        private static void ReconcileAndSendMultiplayer()
        {
            if (!MultiplayerSupport.IsLocalOwnApSlot)
                return;

            BoundApSession? bound = GetBoundSession();
            if (bound == null || !IsCurrentConnectedSession(bound))
                return;

            HashSet<long> pending = ArchipelagoClient.Progress.PendingLocationChecks;
            pending.ExceptWith(bound.Session.Locations.AllLocationsChecked);
            if (GameUtility.CurrentPlayer is { } player)
                ApRunData.PublishLocalProgress(player);
            if (pending.Count == 0)
                return;

            var recognized = pending
                .Where(id => ArchipelagoIdCodec.GetPlayerNumber(id) == bound.Identity.PlayerNumber)
                .Where(bound.Session.Locations.AllLocations.Contains)
                .ToHashSet();
            int unrecognizedCount = pending.Count - recognized.Count;
            if (unrecognizedCount > 0)
            {
                LogUtility.Warn(
                    $"Kept {unrecognizedCount} pending multiplayer check(s) that are not "
                        + $"present in AP session {bound.Identity}"
                );
            }
            if (recognized.Count == 0)
                return;

            foreach (long locationId in recognized)
            {
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId))
                    ArchipelagoClient.CheckedLocations.Add(locationId);
            }
            LogUtility.Info($"Replaying {recognized.Count} pending multiplayer check(s)");
            _ = SendAsync(bound, recognized.ToArray(), replaying: true);
        }

        private static bool TryRecord(
            ApSessionIdentity identity, IEnumerable<long> locationIds, out long[] newlyRecorded)
        {
            newlyRecorded = Array.Empty<long>();
            try
            {
                lock (_stateLock)
                {
                    string path = GetPendingCheckPath(identity);
                    PendingCheckOutbox outbox = Load(path, identity);
                    newlyRecorded = locationIds.Where(outbox.LocationIds.Add).ToArray();
                    if (newlyRecorded.Length > 0)
                        Save(path, outbox);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to persist location checks: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Submits recorded location IDs without removing them from the outbox. A later fresh
        /// login to the same identity is required to prove that the server received them.
        /// </summary>
        private static async Task SendAsync(
            BoundApSession bound,
            long[] locationIds,
            bool replaying
        )
        {
            if (!IsCurrentConnectedSession(bound))
            {
                LogUtility.Warn(
                    $"Location check transmission cancelled because AP session {bound.Identity} "
                        + "is no longer current"
                );
                return;
            }

            try
            {
                // Keep the IDs in the durable outbox after this call. The SDK marks checks
                // locally before its socket write completes, so only a later fresh login can
                // prove that the server received them.
                await bound.Session.Locations.CompleteLocationChecksAsync(locationIds);
                LogUtility.Info(
                    replaying
                        ? $"Resubmitted {locationIds.Length} pending location check(s)"
                        : $"Submitted {locationIds.Length} location check(s)"
                );
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Location check transmission failed; {locationIds.Length} check(s) remain queued: {ex.Message}"
                );
            }
        }

        private static BoundApSession? GetBoundSession()
        {
            lock (_stateLock)
                return _boundSession;
        }

        private static bool IsCurrentConnectedSession(BoundApSession bound)
        {
            if (!ArchipelagoClient.IsConnected)
                return false;

            lock (_stateLock)
            {
                return ReferenceEquals(_boundSession, bound)
                    && ReferenceEquals(ArchipelagoClient.Session, bound.Session);
            }
        }

        private static string GetPendingCheckPath(ApSessionIdentity identity) =>
            $"{OutboxPrefix}{identity.GetFileKey()}.json";

        /// <summary>
        /// Reads and validates an outbox. Its embedded identity, not its filename, is the
        /// authority that prevents checks from crossing AP sessions.
        /// </summary>
        private static PendingCheckOutbox Load(string path, ApSessionIdentity expectedIdentity)
        {
            if (!Godot.FileAccess.FileExists(path))
                return PendingCheckOutbox.Create(expectedIdentity);

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                throw new IOException(
                    $"Could not open pending-check outbox: {Godot.FileAccess.GetOpenError()}"
                );
            }

            string json = file.GetAsText();
            PendingCheckOutbox outbox = JsonSerializer.Deserialize<PendingCheckOutbox>(json)
                ?? throw new JsonException("The pending-check outbox was empty.");
            if (outbox.SchemaVersion != PendingCheckOutbox.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported pending-check schema {outbox.SchemaVersion}."
                );
            }
            if (outbox.Identity != expectedIdentity)
            {
                throw new InvalidDataException(
                    $"Pending-check identity mismatch: expected {expectedIdentity}, "
                        + $"found {outbox.Identity}."
                );
            }

            return outbox;
        }

        /// <summary>
        /// Replaces the outbox contents, deleting only the exact identity-bound file when no
        /// checks remain after server reconciliation.
        /// </summary>
        private static void Save(string path, PendingCheckOutbox outbox)
        {
            if (outbox.LocationIds.Count == 0)
            {
                if (Godot.FileAccess.FileExists(path))
                    Godot.DirAccess.RemoveAbsolute(path);
                return;
            }

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                throw new IOException(
                    $"Could not write pending-check outbox: {Godot.FileAccess.GetOpenError()}"
                );
            }

            var persisted = new PendingCheckOutbox
            {
                SchemaVersion = outbox.SchemaVersion,
                Identity = outbox.Identity,
                LocationIds = new SortedSet<long>(outbox.LocationIds),
            };
            file.StoreString(JsonSerializer.Serialize(persisted));
        }
    }
}
