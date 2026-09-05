using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StS2AP.Utils;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Models
{
    /// <summary>
    /// One entry from the YAML's ordered `bonus_items` list. The APWorld guarantees exactly one of
    /// <see cref="Pools"/> or <see cref="Value"/> is supplied, and order is significant: the Nth
    /// received bonus item of a category unlocks the Nth definition of that category.
    /// </summary>
    public sealed class BonusItemDefinition
    {
        /// <summary>The option key used in YAML, such as `WAX_RELIC`.</summary>
        public const string WaxRelicCategory = "WAX_RELIC";

        public string Category { get; }

        /// <summary>Rarity pools the relic may be drawn from. Empty when <see cref="Value"/> is set.</summary>
        public IReadOnlyList<string> Pools { get; }

        /// <summary>A specific relic id. Null when <see cref="Pools"/> is set.</summary>
        public string? Value { get; }

        public bool HasExplicitValue => !string.IsNullOrWhiteSpace(Value);

        private BonusItemDefinition(string category, IReadOnlyList<string> pools, string? value)
        {
            Category = category;
            Pools = pools;
            Value = value;
        }

        /// <summary>Maps a received AP item to the bonus category it unlocks, or null if it is not a bonus item.</summary>
        public static string? CategoryFor(APItem item) => item switch
        {
            APItem.BonusWaxRelic => WaxRelicCategory,
            _ => null,
        };

        /// <summary>
        /// Parses the ordered `bonus_items` slot-data value, which is a list of single-key objects
        /// shaped like <c>{ "WAX_RELIC": { "Pools": [...] } }</c>. Malformed entries are skipped so a
        /// bad entry cannot prevent the slot from connecting.
        /// </summary>
        public static IReadOnlyList<BonusItemDefinition> ParseAll(object? slotDataValue)
        {
            if (slotDataValue is not JArray entries)
            {
                if (slotDataValue != null)
                    LogUtility.Warn($"Ignoring bonus_items slot data of unexpected type {slotDataValue.GetType().Name}");
                return Array.Empty<BonusItemDefinition>();
            }

            var definitions = new List<BonusItemDefinition>();
            foreach (JToken entry in entries)
            {
                if (entry is not JObject entryObject || entryObject.Count != 1)
                {
                    LogUtility.Warn($"Skipping bonus_items entry that is not a single-key object: {entry}");
                    continue;
                }

                JProperty property = entryObject.Properties().First();
                if (property.Value is not JObject selector)
                {
                    LogUtility.Warn($"Skipping bonus_items entry '{property.Name}' without a selector object");
                    continue;
                }

                var pools = selector["Pools"] is JArray poolArray
                    ? poolArray.Select(pool => pool.ToString())
                               .Where(pool => !string.IsNullOrWhiteSpace(pool))
                               .ToList()
                    : new List<string>();
                string? value = selector["Value"]?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    value = null;

                bool hasPools = pools.Count > 0;
                bool hasValue = value != null;
                if (hasPools == hasValue)
                {
                    LogUtility.Warn(
                        $"Skipping bonus_items entry '{property.Name}': exactly one of Pools or Value is required"
                    );
                    continue;
                }

                definitions.Add(new BonusItemDefinition(property.Name, pools, value));
            }

            return definitions;
        }
    }
}
