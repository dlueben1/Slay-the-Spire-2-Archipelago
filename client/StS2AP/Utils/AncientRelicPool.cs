using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace StS2AP.Utils
{
    /// <summary>
    /// Builds deterministic three-relic choices from the Ancients assigned to Acts 2 and 3.
    /// The pool is derived from each Ancient's possible options so Neow relics are excluded by
    /// construction and conventionally registered custom Ancients are included automatically.
    /// </summary>
    public static class AncientRelicPool
    {
        public const int ChoiceCount = 3;

        private const string ChoiceSeedDomain = "sts2ap-ancient-choice-v1";
        private const string GoldenCompassId = "GOLDEN_COMPASS";

        /// <summary>
        /// Selects a stable set of three relics for an AP item index without consuming the game's RNG.
        /// </summary>
        public static IReadOnlyList<RelicModel> CreateChoices(
            Player player,
            int itemIndex,
            IReadOnlyCollection<ModelId>? reservedRelicIds = null,
            int? ancientActIndex = null)
        {
            var ownedRelicIds = player.Relics.Select(relic => relic.Id).ToHashSet();
            if (reservedRelicIds != null)
                ownedRelicIds.UnionWith(reservedRelicIds);
            var candidatesById = new Dictionary<ModelId, RelicModel>();

            var eligibleActs = ModelDb.Acts.Where(act => ancientActIndex.HasValue
                ? act.Index == ancientActIndex.Value
                : act.Index is 1 or 2);
            foreach (var act in eligibleActs)
            {
                foreach (var ancient in act.AllAncients)
                {
                    try
                    {
                        var extractedForAncient = 0;
                        foreach (var relic in ancient.AllPossibleOptions
                                                      .Select(option => option.Relic?.CanonicalInstance)
                                                      .OfType<RelicModel>())
                        {
                            extractedForAncient++;
                            if (relic.Id == ModelId.none ||
                                ownedRelicIds.Contains(relic.Id) ||
                                string.Equals(relic.Id.Entry, GoldenCompassId, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            candidatesById.TryAdd(relic.Id, relic);
                        }

                        if (extractedForAncient == 0)
                            LogUtility.Warn($"Ancient '{ancient.Id}' exposed no relic models through AllPossibleOptions");
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Warn($"Failed to inspect Ancient '{ancient.Id}' for relic choices: {ex.Message}");
                    }
                }
            }

            if (candidatesById.Count < ChoiceCount)
            {
                LogUtility.Error($"Ancient relic pool only contained {candidatesById.Count} eligible relic(s); {ChoiceCount} are required");
                return Array.Empty<RelicModel>();
            }

            var runSeed = ResolveRunSeed(player);
            var choices = candidatesById.Values
                .OrderBy(relic => StableChoiceKey(runSeed, itemIndex, relic.Id))
                .Take(ChoiceCount)
                .ToList();

            LogUtility.Info(
                $"Assigned Ancient relic choices for item index {itemIndex} from " +
                $"{(ancientActIndex.HasValue ? $"Act {ancientActIndex.Value + 1}" : "Act 2/3")} pool of {candidatesById.Count}: " +
                string.Join(", ", choices.Select(relic => relic.Id.ToString()))
            );

            return choices;
        }

        private static string ResolveRunSeed(Player player)
        {
            return player.RunState.Rng.StringSeed;
        }

        private static string StableChoiceKey(string runSeed, int itemIndex, ModelId relicId)
        {
            var material = $"{ChoiceSeedDomain}|{runSeed}|{GameUtility.CurrentCharacterID}|{itemIndex}|{relicId}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }
    }
}
