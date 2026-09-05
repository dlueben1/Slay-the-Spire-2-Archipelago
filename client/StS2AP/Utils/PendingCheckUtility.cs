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
        private static readonly HashSet<string> _reportedLegacyPaths = new(StringComparer.Ordinal);
        private static BoundApSession? _boundSession;

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
                session.ConnectionInfo.Slot
            );

            lock (_stateLock)
            {
                _boundSession = new BoundApSession(session, identity);
                ReportLegacyOutboxIfPresent(roomSeed);
            }

            LogUtility.Debug($"Bound pending-check outbox to AP session {identity}");
        }

        /// <summary>
        /// Adds a newly earned location to the durable outbox, then attempts to send it
        /// immediately when the same authenticated Archipelago session is still connected.
        /// Checks already present in the outbox are not submitted a second time here; the
        /// reconnect reconciliation path is responsible for retrying them.
        /// </summary>
        /// <param name="locationId">The Archipelago location ID earned by the player.</param>
        public static void RecordAndSend(long locationId)
        {
            BoundApSession? bound = GetBoundSession();
            if (bound == null)
            {
                LogUtility.Error(
                    $"Could not persist location check {locationId}: no authenticated AP identity is bound"
                );
                TrySendWithoutPersistence(locationId);
                return;
            }

            if (!TryRecord(bound.Identity, locationId))
                return;

            if (!IsCurrentConnectedSession(bound))
            {
                LogUtility.Warn(
                    $"Queued location check {locationId} until its Archipelago session reconnects"
                );
                return;
            }

            _ = SendAsync(bound, new[] { locationId }, replaying: false);
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

        /// <summary>
        /// Attempts to add a location to the identity-specific outbox on disk.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the caller should attempt an immediate network send.
        /// <see langword="false"/> when the location was already present or could not be
        /// associated with the supplied authenticated identity.
        /// </returns>
        private static bool TryRecord(ApSessionIdentity identity, long locationId)
        {
            try
            {
                lock (_stateLock)
                {
                    string path = GetPendingCheckPath(identity);
                    PendingCheckOutbox outbox = Load(path, identity);
                    if (!outbox.LocationIds.Add(locationId))
                        return false;

                    Save(path, outbox);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to persist location check {locationId}: {ex}");
                // The currently authenticated session is still the correct destination. Preserve
                // the previous best-effort behavior rather than silently dropping the check.
                return true;
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
                        : $"Submitted location check: {locationIds[0]}"
                );
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Location check transmission failed; {locationIds.Length} check(s) remain queued: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Preserves the old immediate-send behavior if identity binding failed. Nothing is
        /// persisted or replayed because ownership could not be proven.
        /// </summary>
        private static void TrySendWithoutPersistence(long locationId)
        {
            if (!ArchipelagoClient.IsConnected)
                return;

            ArchipelagoSession session = ArchipelagoClient.Session;
            _ = SendWithoutPersistenceAsync(session, locationId);
        }

        private static async Task SendWithoutPersistenceAsync(
            ArchipelagoSession session,
            long locationId
        )
        {
            if (
                !ArchipelagoClient.IsConnected
                || !ReferenceEquals(ArchipelagoClient.Session, session)
            )
                return;

            try
            {
                await session.Locations.CompleteLocationChecksAsync(locationId);
                LogUtility.Warn(
                    $"Submitted location check {locationId} without durable outbox protection"
                );
            }
            catch (Exception ex)
            {
                LogUtility.Error(
                    $"Location check {locationId} could not be persisted or submitted: {ex.Message}"
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

        /// <summary>
        /// Legacy files contain only location IDs and cannot prove their AP destination. Leave
        /// them untouched and report them once instead of guessing and replaying them.
        /// </summary>
        private static void ReportLegacyOutboxIfPresent(string roomSeed)
        {
            if (string.IsNullOrWhiteSpace(ArchipelagoClient.PlayerName))
                return;

            string safeName = SanitizeLegacyFileNamePart(ArchipelagoClient.PlayerName);
            string safeSeed = SanitizeLegacyFileNamePart(roomSeed);
            string path = $"user://sts_ap_pending_checks_{safeName}_{safeSeed}.json";
            if (!Godot.FileAccess.FileExists(path) || !_reportedLegacyPaths.Add(path))
                return;

            LogUtility.Warn(
                $"Ignored legacy pending-check outbox '{path}' because it has no authenticated "
                    + "AP identity. The file was left untouched."
            );
        }

        private static string SanitizeLegacyFileNamePart(string value) =>
            string.Join("_", value.Split(System.IO.Path.GetInvalidFileNameChars()));
    }
}
