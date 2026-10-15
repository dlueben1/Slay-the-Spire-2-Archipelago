using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StS2AP.Utils;

namespace StS2AP.Data
{
    /// <summary>
    /// Loads developer-maintained bonus relic JSON shipped as .data files in the mod's data subdirectory
    /// to avoid the game's recursive JSON manifest discovery. Builds copy the shared JSON sources
    /// without requiring C# recompilation. Each catalog is cached on first use; installed-file edits
    /// require a game restart once loaded. "Pool" here
    /// means a selectable bonus source (a rarity bucket or a custom whitelist), never the game's
    /// character/shared RelicPoolModel.
    /// </summary>
    public static class BonusRelicCatalog
    {
        private const string CustomPoolsFileName = "relic_custom_pools.data";
        private const string BlacklistFileName = "bonus_relic_blacklist.data";

        private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> CustomPools =
            new(LoadCustomPools);
        private static readonly Lazy<IReadOnlySet<string>> _blacklist = new(LoadBlacklist);

        /// <summary>
        /// Normalized (PascalCase) ids of relics that must never become bonus wax relics, regardless
        /// of which pool selected them or whether they were named explicitly.
        /// </summary>
        public static IReadOnlySet<string> Blacklist => _blacklist.Value;

        /// <summary>
        /// Returns the normalized ids whitelisted for one custom pool by name (e.g. "Fake",
        /// "Classic"), or an empty list when the name is unknown or the file is missing.
        /// </summary>
        public static IReadOnlyList<string> CustomPool(string name) =>
            CustomPools.Value.TryGetValue(name, out IReadOnlyList<string>? ids)
                ? ids
                : Array.Empty<string>();

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadCustomPools()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            if (!TryReadJsonFile(CustomPoolsFileName, out JToken? root) || root is not JObject pools)
                return result;

            foreach (JProperty property in pools.Properties())
            {
                if (property.Value is not JArray idArray)
                    continue;
                var ids = new List<string>();
                foreach (JToken token in idArray)
                {
                    string normalized = BonusRelicIds.Normalize(token.ToString());
                    if (normalized.Length > 0)
                        ids.Add(normalized);
                }
                result[property.Name] = ids;
            }

            return result;
        }

        private static IReadOnlySet<string> LoadBlacklist()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryReadJsonFile(BlacklistFileName, out JToken? root) || root is not JArray idArray)
                return result;

            foreach (JToken token in idArray)
            {
                string normalized = BonusRelicIds.Normalize(token.ToString());
                if (normalized.Length > 0)
                    result.Add(normalized);
            }

            return result;
        }

        /// <summary>
        /// Reads one JSON file from data/ beneath the folder containing the mod DLL.
        /// The mod root is reserved for the manifest and other loader-facing files.
        /// </summary>
        private static bool TryReadJsonFile(string fileName, out JToken? root)
        {
            root = null;
            try
            {
                string? modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(modDirectory))
                {
                    LogUtility.Warn($"Bonus relic catalog could not resolve the mod directory for {fileName}");
                    return false;
                }

                string path = Path.Combine(modDirectory, "data", fileName);
                // The compatibility loader places the client assembly under lib/<version>.
                if (!File.Exists(path) && Path.GetFileName(Path.GetDirectoryName(modDirectory)) == "lib")
                    path = Path.GetFullPath(Path.Combine(modDirectory, "..", "..", "data", fileName));
                if (!File.Exists(path))
                {
                    LogUtility.Warn($"Bonus relic catalog file not found in the mod data directory: {path}");
                    return false;
                }

                root = JToken.Parse(File.ReadAllText(path));
                return true;
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not load bonus relic catalog '{fileName}': {ex.Message}");
                return false;
            }
        }
    }
}
