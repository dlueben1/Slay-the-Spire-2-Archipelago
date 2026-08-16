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
        private static readonly object _fileLock = new();

        /// <summary>
        /// Adds a newly earned location to the durable outbox, then attempts to send it
        /// immediately when the Archipelago socket still reports itself as connected.
        /// Checks already present in the outbox are not submitted a second time here; the
        /// reconnect reconciliation path is responsible for retrying them.
        /// </summary>
        /// <param name="locationId">The Archipelago location ID earned by the player.</param>
        public static void RecordAndSend(long locationId)
        {
            if (!TryRecord(locationId))
            {
                return;
            }

            if (!ArchipelagoClient.IsConnected)
            {
                LogUtility.Warn(
                    $"Queued location check {locationId} until Archipelago reconnects"
                );
                return;
            }

            _ = SendAsync(ArchipelagoClient.Session, new[] { locationId }, replaying: false);
        }

        /// <summary>
        /// Reconciles the durable outbox against authoritative state from a fresh login,
        /// then resends only checks that the server still reports as missing.
        /// </summary>
        /// <remarks>
        /// This must run before any new checks are submitted through the new session. At that
        /// point, <c>AllLocationsChecked</c> contains only server-confirmed locations and can
        /// safely distinguish acknowledged checks from checks that need to be replayed.
        /// </remarks>
        public static void ReconcileAndSend()
        {
            if (!ArchipelagoClient.IsConnected)
            {
                return;
            }

            ArchipelagoSession session = ArchipelagoClient.Session;
            HashSet<long> pending;
            try
            {
                lock (_fileLock)
                {
                    var path = GetPendingCheckPath();
                    if (path == null)
                    {
                        return;
                    }

                    pending = Load(path);
                    pending.ExceptWith(session.Locations.AllLocationsChecked);
                    Save(path, pending);
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to reconcile pending location checks: {ex}");
                return;
            }

            if (pending.Count == 0)
            {
                return;
            }

            foreach (var locationId in pending)
            {
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId))
                {
                    ArchipelagoClient.CheckedLocations.Add(locationId);
                }
            }

            LogUtility.Info(
                $"Replaying {pending.Count} pending location check(s) after reconnecting"
            );
            _ = SendAsync(session, pending.ToArray(), replaying: true);
        }

        /// <summary>
        /// Attempts to add a location to the slot-and-seed-specific outbox on disk.
        /// </summary>
        /// <param name="locationId">The location ID to persist.</param>
        /// <returns>
        /// <see langword="true"/> when the caller should attempt an immediate network send.
        /// This means either the location was newly persisted or persistence was unavailable
        /// and sending is still preferable to silently dropping the check.
        /// <see langword="false"/> means the location was already present in the outbox, so
        /// this call should not create another same-session send attempt.
        /// </returns>
        private static bool TryRecord(long locationId)
        {
            try
            {
                lock (_fileLock)
                {
                    var path = GetPendingCheckPath();
                    if (path == null)
                    {
                        LogUtility.Error(
                            $"Could not persist location check {locationId}: slot or seed was unavailable"
                        );
                        return true;
                    }

                    var pending = Load(path);
                    if (!pending.Add(locationId))
                    {
                        return false;
                    }

                    Save(path, pending);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to persist location check {locationId}: {ex}");
                // Preserve the previous behavior if local persistence is unavailable: make
                // the immediate send attempt instead of silently suppressing the check.
                return true;
            }
        }

        /// <summary>
        /// Submits one or more recorded location IDs without removing them from the outbox.
        /// A later fresh login is required to prove that the server received the submission.
        /// </summary>
        /// <param name="session">The session that should transmit the locations.</param>
        /// <param name="locationIds">The exact location IDs to submit.</param>
        /// <param name="replaying">
        /// <see langword="true"/> when these IDs came from reconnect reconciliation;
        /// <see langword="false"/> for the first send attempt when a check is earned.
        /// </param>
        private static async Task SendAsync(
            ArchipelagoSession session,
            long[] locationIds,
            bool replaying
        )
        {
            try
            {
                // Keep the IDs in the durable outbox after this call. The SDK marks checks
                // locally before its socket write completes, so only a later fresh login can
                // prove that the server received them.
                await session.Locations.CompleteLocationChecksAsync(locationIds);
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
        /// Builds the Godot user-data path for the current slot's pending-check outbox.
        /// </summary>
        /// <returns>
        /// A slot-and-seed-specific <c>user://</c> path, or <see langword="null"/> before the
        /// client has enough authenticated session information to identify the multiworld.
        /// </returns>
        private static string? GetPendingCheckPath()
        {
            if (
                string.IsNullOrWhiteSpace(ArchipelagoClient.PlayerName)
                || string.IsNullOrWhiteSpace(ArchipelagoClient.Seed)
            )
            {
                return null;
            }

            var safeName = SanitizeFileNamePart(ArchipelagoClient.PlayerName);
            var safeSeed = SanitizeFileNamePart(ArchipelagoClient.Seed);
            return $"user://sts_ap_pending_checks_{safeName}_{safeSeed}.json";
        }

        /// <summary>
        /// Replaces characters that cannot safely appear in a local filename.
        /// </summary>
        /// <param name="value">The slot name or room seed to sanitize.</param>
        /// <returns>A filesystem-safe filename component.</returns>
        private static string SanitizeFileNamePart(string value)
        {
            return string.Join("_", value.Split(System.IO.Path.GetInvalidFileNameChars()));
        }

        /// <summary>
        /// Reads all pending location IDs from an outbox file.
        /// </summary>
        /// <param name="path">The Godot user-data path to read.</param>
        /// <returns>
        /// The persisted set of location IDs, or an empty set when no outbox exists yet.
        /// </returns>
        /// <exception cref="IOException">The existing outbox could not be opened.</exception>
        /// <exception cref="JsonException">The existing outbox contains invalid JSON.</exception>
        private static HashSet<long> Load(string path)
        {
            if (!Godot.FileAccess.FileExists(path))
            {
                return new HashSet<long>();
            }

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                throw new IOException(
                    $"Could not open pending-check outbox: {Godot.FileAccess.GetOpenError()}"
                );
            }

            var json = file.GetAsText();
            return JsonSerializer.Deserialize<HashSet<long>>(json) ?? new HashSet<long>();
        }

        /// <summary>
        /// Replaces the outbox contents with the supplied pending IDs, deleting the outbox
        /// when no checks remain after server reconciliation.
        /// </summary>
        /// <param name="path">The Godot user-data path to update.</param>
        /// <param name="pending">The complete set of IDs that still require confirmation.</param>
        /// <exception cref="IOException">The outbox could not be written.</exception>
        private static void Save(string path, HashSet<long> pending)
        {
            if (pending.Count == 0)
            {
                if (Godot.FileAccess.FileExists(path))
                {
                    Godot.DirAccess.RemoveAbsolute(path);
                }
                return;
            }

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                throw new IOException(
                    $"Could not write pending-check outbox: {Godot.FileAccess.GetOpenError()}"
                );
            }

            file.StoreString(JsonSerializer.Serialize(pending.OrderBy(id => id)));
        }
    }
}
