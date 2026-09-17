using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils
{
    /// <summary>
    /// Maps a configured bonus entry to the relic it grants. A <see cref="BonusItemDefinition.Value"/>
    /// entry resolves a specific relic by id; a <see cref="BonusItemDefinition.Pools"/> entry builds a
    /// candidate set and the caller picks one deterministically at run start. This never pulls from
    /// RelicFactory, so a bonus relic stays available in the normal relic pool. Pickup-effect filtering
    /// is opt-in because it is currently a restriction for bonus wax relics only.
    /// </summary>
    public static class BonusRelicResolver
    {
        /// <summary>
        /// Resolves the relic for an explicit <see cref="BonusItemDefinition.Value"/> entry, or null
        /// when the id is unknown or blacklisted. Pickup-effect relics are rejected only when requested
        /// by the bonus category that owns the entry.
        /// </summary>
        public static RelicModel? ResolveValue(string valueId, bool rejectPickupEffectRelics = false)
        {
            RelicModel? relic = FindRelicByJsonId(valueId);
            if (relic == null)
            {
                LogUtility.Error($"Bonus relic value '{valueId}' did not match any relic in the game");
                return null;
            }

            if (BonusRelicCatalog.Blacklist.Contains(BonusRelicIds.Normalize(valueId)))
            {
                LogUtility.Error(
                    $"Bonus relic value '{valueId}' is on the bonus blacklist and cannot be granted"
                );
                return null;
            }

            if (rejectPickupEffectRelics && relic.HasUponPickupEffect)
            {
                LogUtility.Error(
                    $"Bonus relic value '{valueId}' has an upon-pickup effect and cannot be granted"
                );
                return null;
            }

            return relic;
        }

        /// <summary>
        /// Builds the deduplicated, blacklist-filtered candidate set for a pool-based entry. The
        /// caller orders these deterministically and picks one at run start. When requested by the
        /// owning category, relics with upon-pickup effects are filtered as well. Returns an empty
        /// list when no pool produced an eligible relic.
        /// </summary>
        public static IReadOnlyList<RelicModel> BuildPoolCandidates(
            IReadOnlyList<string> pools,
            bool rejectPickupEffectRelics = false)
        {
            var candidates = new Dictionary<string, RelicModel>(StringComparer.OrdinalIgnoreCase);
            foreach (string poolName in pools)
            {
                if (Enum.TryParse(poolName, ignoreCase: true, out RelicRarity rarity))
                {
                    AddRarityBucket(candidates, rarity, poolName);
                    continue;
                }

                AddCustomPool(candidates, poolName);
            }

            foreach (string blacklisted in BonusRelicCatalog.Blacklist)
                candidates.Remove(blacklisted);

            if (rejectPickupEffectRelics)
            {
                foreach (RelicModel relic in candidates.Values
                    .Where(relic => relic.HasUponPickupEffect)
                    .ToList())
                {
                    candidates.Remove(BonusRelicIds.Normalize(relic.Id.Entry));
                    LogUtility.Info(
                        $"Excluded bonus relic '{relic.Id.Entry}' because it has an upon-pickup effect"
                    );
                }
            }

            if (candidates.Count == 0)
                LogUtility.Error(
                    $"Bonus relic pools [{string.Join(", ", pools)}] produced no eligible relics"
                );

            return candidates.Values.ToList();
        }

        private static void AddRarityBucket(
            Dictionary<string, RelicModel> candidates,
            RelicRarity rarity,
            string poolName)
        {
            int added = 0;
            foreach (RelicModel relic in ModelDb.AllRelics)
            {
                if (relic.Rarity != rarity)
                    continue;
                if (candidates.TryAdd(BonusRelicIds.Normalize(relic.Id.Entry), relic))
                    added++;
            }

            LogUtility.Info($"Bonus relic rarity pool '{poolName}' contributed {added} relic(s)");
        }

        private static void AddCustomPool(Dictionary<string, RelicModel> candidates, string poolName)
        {
            IReadOnlyList<string> ids = BonusRelicCatalog.CustomPool(poolName);
            if (ids.Count == 0)
            {
                LogUtility.Warn(
                    $"Bonus relic pool '{poolName}' is neither a known rarity nor a configured custom pool"
                );
                return;
            }

            int added = 0;
            foreach (string id in ids)
            {
                RelicModel? relic = FindRelicByJsonId(id);
                if (relic == null)
                {
                    LogUtility.Warn($"Custom bonus pool '{poolName}' references unknown relic '{id}'");
                    continue;
                }

                if (candidates.TryAdd(BonusRelicIds.Normalize(relic.Id.Entry), relic))
                    added++;
            }

            LogUtility.Info($"Bonus relic custom pool '{poolName}' contributed {added} relic(s)");
        }

        /// <summary>Finds a relic model from a shared-JSON id, comparing the normalized id case-insensitively.</summary>
        public static RelicModel? FindRelicByJsonId(string? jsonId)
        {
            string normalized = BonusRelicIds.Normalize(jsonId);
            if (normalized.Length == 0)
                return null;

            return ModelDb.AllRelics.FirstOrDefault(relic =>
                string.Equals(relic.Id.Entry, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relic.GetType().Name, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }
}
