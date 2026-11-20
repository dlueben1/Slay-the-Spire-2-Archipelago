using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils
{
    /// <summary>
    /// Maps a configured bonus entry to the relic it grants. A <see cref="BonusItemDefinition.Value"/>
    /// entry resolves a specific relic by id; a <see cref="BonusItemDefinition.Pools"/> entry builds a
    /// candidate set and the caller picks one deterministically at run start. This never pulls from
    /// RelicFactory, so a bonus relic stays available in the normal relic pool. Only wax relics
    /// reject pickup effects: the effect would outlast the melting, so the wax would mean nothing.
    /// </summary>
    public static class BonusRelicResolver
    {
        /// <summary>
        /// Resolves the relic for an explicit <see cref="BonusItemDefinition.Value"/> entry, or null
        /// when the id is unknown. Pickup-effect relics are rejected only when requested by the bonus
        /// category that owns the entry.
        /// </summary>
        public static RelicModel? ResolveValue(string valueId, bool rejectPickupEffectRelics = false)
        {
            RelicModel? relic = FindRelicByJsonId(valueId);
            if (relic == null)
            {
                LogUtility.Error($"Bonus relic value '{valueId}' did not match any relic in the game");
                return null;
            }

            if (rejectPickupEffectRelics && HasPickupEffect(relic))
            {
                LogUtility.Error(
                    $"Bonus relic value '{valueId}' has an upon-pickup effect and cannot be granted"
                );
                return null;
            }

            return relic;
        }

        /// <summary>
        /// Builds the deduplicated candidate set for a pool-based entry, without the relics a pool
        /// never rolls. The caller orders these deterministically and picks one at
        /// run start. When requested by the owning category, relics with upon-pickup effects are
        /// filtered as well. Returns an empty list when no pool produced an eligible relic.
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

            foreach (string excluded in BonusRelicCatalog.NeverRandom)
                candidates.Remove(excluded);

            if (rejectPickupEffectRelics)
            {
                List<RelicModel> pickupRelics = candidates.Values.Where(HasPickupEffect).ToList();
                foreach (RelicModel relic in pickupRelics)
                    candidates.Remove(BonusRelicIds.Normalize(relic.Id.Entry));
                // One line, not one per relic: most of the Ancient pool is pickup effects.
                if (pickupRelics.Count > 0)
                    LogUtility.Info($"Excluded {pickupRelics.Count} bonus relic(s) with an upon-pickup effect");
            }

            if (candidates.Count == 0)
                LogUtility.Error(
                    $"Bonus relic pools [{string.Join(", ", pools)}] produced no eligible relics"
                );

            return candidates.Values.ToList();
        }

        /// <summary>
        /// Gives a relic the per-player state the game sets while it builds an Ancient's options,
        /// before the relic is offered. Without it Dusty Tome adds no card and Sea Glass shows
        /// Ironclad's cards. AP's Ancient rewards do the same in <see cref="AncientRelicPool"/>.
        /// The picks use the bonus's own selection key, not the game's RNG, so every replica of a
        /// multiplayer run agrees. Returns false when the relic cannot work for this player.
        /// </summary>
        public static bool TrySetUpForPlayer(RelicModel relic, Player player, Func<string, string> selectionKey)
        {
            switch (relic)
            {
                case DustyTome dustyTome:
                    // The same cards the game's own setup chooses from.
                    List<CardModel> transcendenceCards = ArchaicTooth.TranscendenceCards;
                    CardModel? ancientCard = player.Character.CardPool
                        .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                        .Where(card => card.Rarity == CardRarity.Ancient && !transcendenceCards.Contains(card))
                        .OrderBy(card => selectionKey(card.Id.ToString()), StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (ancientCard == null)
                        return false;
                    dustyTome.AncientCard = ancientCard.Id;
                    return true;
                case SeaGlass seaGlass:
                    // Orobas offers another unlocked character's cards.
                    CharacterModel? otherCharacter = player.UnlockState.Characters
                        .Where(character => character.Id != player.Character.Id)
                        .OrderBy(character => selectionKey(character.Id.ToString()), StringComparer.Ordinal)
                        .FirstOrDefault();
                    seaGlass.CharacterId = (otherCharacter ?? player.Character).Id;
                    return true;
                case ArchaicTooth archaicTooth:
                    return archaicTooth.SetupForPlayer(player);
                case TouchOfOrobas touchOfOrobas:
                    return touchOfOrobas.SetupForPlayer(player);
                default:
                    return true;
            }
        }

        private static bool HasPickupEffect(RelicModel relic) =>
            relic.HasUponPickupEffect
            || BonusRelicCatalog.AncientPickupEffects.Contains(BonusRelicIds.Normalize(relic.Id.Entry));

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
