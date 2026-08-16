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

        private static string SanitizeFileNamePart(string value)
        {
            return string.Join("_", value.Split(System.IO.Path.GetInvalidFileNameChars()));
        }

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
