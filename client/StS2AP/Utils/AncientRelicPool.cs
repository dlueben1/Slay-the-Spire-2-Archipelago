using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
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

        /// <summary>Relics that must never appear in AP-built Ancient choice pools.</summary>
        private static readonly Type[] BlacklistedRelicTypes =
        [
            typeof(GoldenCompass),
            typeof(Driftwood),
        ];

        private static bool IsBlacklisted(RelicModel relic) =>
            BlacklistedRelicTypes.Any(type => type.IsInstanceOfType(relic));

        /// <summary>
        /// Selects a stable set of three relics for a reward key without consuming the game's RNG.
        /// </summary>
        public static IReadOnlyList<RelicModel> CreateChoices(
            Player player,
            string choiceKey,
            IReadOnlyCollection<ModelId>? reservedRelicIds = null,
            int? ancientActIndex = null,
            AncientEventModel? specificAncient = null)
        {
            var ownedRelicIds = player.Relics.Select(relic => relic.Id).ToHashSet();
            if (reservedRelicIds != null)
                ownedRelicIds.UnionWith(reservedRelicIds);
            var eligibleAncients = GetEligibleAncients(ancientActIndex, specificAncient);
            var candidatesById = CollectCandidateRelics(eligibleAncients, ownedRelicIds, logFailures: true);

            if (candidatesById.Count < ChoiceCount)
            {
                LogUtility.Error($"Ancient relic pool only contained {candidatesById.Count} eligible relic(s); {ChoiceCount} are required");
                return Array.Empty<RelicModel>();
            }

            var runSeed = ResolveRunSeed(player);
            var choices = new List<RelicModel>(ChoiceCount);
            foreach (var candidate in candidatesById.Values
                                                    .OrderBy(relic => StableChoiceKey(runSeed, choiceKey, relic.Id)))
            {
                var preparedRelic = PrepareForPlayer(candidate, player, choiceKey);
                if (preparedRelic != null)
                    choices.Add(preparedRelic);

                if (choices.Count == ChoiceCount)
                    break;
            }

            if (choices.Count < ChoiceCount)
            {
                LogUtility.Error(
                    $"Ancient relic pool only contained {choices.Count} player-compatible relic(s) after setup; " +
                    $"{ChoiceCount} are required"
                );
                return Array.Empty<RelicModel>();
            }

            LogUtility.Info(
                $"Assigned Ancient relic choices for '{choiceKey}' from " +
                $"{DescribePool(ancientActIndex, specificAncient)} pool of {candidatesById.Count}: " +
                string.Join(", ", choices.Select(relic => relic.Id.ToString()))
            );

            return choices;
        }

        /// <summary>
        /// Creates the mutable, player-specific form of an Ancient relic. The base game performs
        /// these setup calls while generating the natural Ancient's options; AP choices are built
        /// from AllPossibleOptions instead, so they must mirror that setup explicitly.
        /// </summary>
        private static RelicModel? PrepareForPlayer(RelicModel relicModel, Player player, string choiceKey)
        {
            try
            {
                var relic = relicModel.ToMutable();
                switch (relic)
                {
                    case SeaGlass seaGlass:
                        // Orobas normally selects an unlocked character other than the owner.
                        // Use the AP reward key to make that character stable without consuming
                        // the game's RNG, then set the saved property before the tooltip is built.
                        var targetCharacter = player.UnlockState.Characters
                            .Where(character => character.Id != player.Character.Id)
                            .OrderBy(character => StableChoiceKey(
                                ResolveRunSeed(player),
                                $"{choiceKey}|sea-glass",
                                character.Id
                            ))
                            .FirstOrDefault() ?? player.Character;
                        seaGlass.CharacterId = targetCharacter.Id;
                        break;
                    case DustyTome dustyTome:
                        dustyTome.SetupForPlayer(player);
                        break;
                    case ArchaicTooth archaicTooth when !archaicTooth.SetupForPlayer(player):
                    case TouchOfOrobas touchOfOrobas when !touchOfOrobas.SetupForPlayer(player):
                        LogUtility.Warn($"Skipping Ancient relic '{relic.Id}' because it is not compatible with the current player");
                        return null;
                }

                return relic;
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Skipping Ancient relic '{relicModel.Id}' because player setup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves the Ancient rolled for the requested act. If it is unavailable or cannot
        /// provide three eligible relics, selects a stable fallback Ancient from that act.
        /// </summary>
        public static AncientEventModel? ResolveSpecificAncient(
            Player player,
            int ancientActIndex,
            string choiceKey,
            IReadOnlyCollection<ModelId>? reservedRelicIds = null)
        {
            var excludedRelicIds = player.Relics.Select(relic => relic.Id).ToHashSet();
            if (reservedRelicIds != null)
                excludedRelicIds.UnionWith(reservedRelicIds);

            var rolledAncient = TryGetRolledAncient(player, ancientActIndex);
            if (rolledAncient != null &&
                CollectCandidateRelics(new[] { rolledAncient }, excludedRelicIds, logFailures: false).Count >= ChoiceCount)
            {
                LogUtility.Info($"Using rolled Act {ancientActIndex + 1} Ancient '{rolledAncient.Id}' for '{choiceKey}'");
                return rolledAncient;
            }

            if (rolledAncient != null)
            {
                LogUtility.Warn(
                    $"Rolled Act {ancientActIndex + 1} Ancient '{rolledAncient.Id}' could not provide " +
                    $"{ChoiceCount} eligible relics for '{choiceKey}'; selecting a stable fallback"
                );
            }

            var runSeed = ResolveRunSeed(player);
            var fallback = GetFallbackAncients(player, ancientActIndex)
                .Where(ancient => CollectCandidateRelics(new[] { ancient }, excludedRelicIds, logFailures: false).Count >= ChoiceCount)
                .OrderBy(ancient => StableChoiceKey(runSeed, $"{choiceKey}|ancient", ancient.Id))
                .FirstOrDefault();

            if (fallback == null)
            {
                LogUtility.Error($"No Act {ancientActIndex + 1} Ancient could provide {ChoiceCount} eligible relics for '{choiceKey}'");
                return null;
            }

            LogUtility.Info($"Using stable fallback Act {ancientActIndex + 1} Ancient '{fallback.Id}' for '{choiceKey}'");
            return fallback;
        }

        /// <summary>Reads the Ancient already rolled and stored for an act in this run.</summary>
        private static AncientEventModel? TryGetRolledAncient(Player player, int ancientActIndex)
        {
            try
            {
                return player.RunState.Acts.FirstOrDefault(act => act.Index == ancientActIndex)?.Ancient;
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not read the rolled Act {ancientActIndex + 1} Ancient: {ex.Message}");
                return null;
            }
        }

        /// <summary>Builds the unlocked same-act Ancient list, used by Balanced's seeded fallback.</summary>
        private static IReadOnlyList<AncientEventModel> GetFallbackAncients(Player player, int ancientActIndex)
        {
            try
            {
                var runAct = player.RunState.Acts.FirstOrDefault(act => act.Index == ancientActIndex);
                if (runAct != null)
                {
                    return runAct.GetUnlockedAncients(player.RunState.UnlockState)
                        .Concat(player.RunState.UnlockState.SharedAncients)
                        .GroupBy(ancient => ancient.Id)
                        .Select(group => group.First())
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not inspect unlocked Act {ancientActIndex + 1} Ancients: {ex.Message}");
            }

            return GetEligibleAncients(ancientActIndex, specificAncient: null);
        }

        /// <summary>Builds the Ancient source for a specific, act-scoped, or combined relic pool.</summary>
        private static IReadOnlyList<AncientEventModel> GetEligibleAncients(
            int? ancientActIndex,
            AncientEventModel? specificAncient)
        {
            if (specificAncient != null)
                return new[] { specificAncient };

            var actAncients = ModelDb.Acts
                .Where(act => ancientActIndex.HasValue ? act.Index == ancientActIndex.Value : act.Index is 1 or 2)
                .SelectMany(act => act.AllAncients);

            // Shared Ancients can be rolled into either Act 2 or Act 3 and therefore belong in
            // each act-scoped pool as well as the combined pool.
            return actAncients
                .Concat(ModelDb.AllSharedAncients)
                .GroupBy(ancient => ancient.Id)
                .Select(group => group.First())
                .ToList();
        }

        /// <summary>Extracts unique, eligible relic models from the supplied Ancients.</summary>
        private static Dictionary<ModelId, RelicModel> CollectCandidateRelics(
            IEnumerable<AncientEventModel> ancients,
            IReadOnlySet<ModelId> excludedRelicIds,
            bool logFailures)
        {
            var candidatesById = new Dictionary<ModelId, RelicModel>();
            foreach (var ancient in ancients)
            {
                try
                {
                    var extractedForAncient = 0;
                    foreach (var relic in ancient.AllPossibleOptions
                                                  .Select(option => option.Relic?.CanonicalInstance)
                                                  .OfType<RelicModel>())
                    {
                        extractedForAncient++;
                        // TODO: do model selection in a better way than this
                        if (relic.Id == ModelId.none ||
                            excludedRelicIds.Contains(relic.Id) ||
                            IsBlacklisted(relic))
                        {
                            continue;
                        }

                        candidatesById.TryAdd(relic.Id, relic);
                    }

                    if (logFailures && extractedForAncient == 0)
                        LogUtility.Warn($"Ancient '{ancient.Id}' exposed no relic models through AllPossibleOptions");
                }
                catch (Exception ex)
                {
                    if (logFailures)
                        LogUtility.Warn($"Failed to inspect Ancient '{ancient.Id}' for relic choices: {ex.Message}");
                }
            }

            return candidatesById;
        }

        /// <summary>Formats the selected pool for assignment diagnostics.</summary>
        private static string DescribePool(int? ancientActIndex, AncientEventModel? specificAncient)
        {
            if (specificAncient != null)
                return $"Ancient {specificAncient.Id}";
            return ancientActIndex.HasValue ? $"Act {ancientActIndex.Value + 1}" : "Act 2/3";
        }

        /// <summary>Returns the base-game run seed used for deterministic choice ordering.</summary>
        private static string ResolveRunSeed(Player player)
        {
            return player.RunState.Rng.StringSeed;
        }

        /// <summary>Hashes run, character, reward, and model identity into a stable sort key.</summary>
        private static string StableChoiceKey(string runSeed, string choiceKey, ModelId modelId)
        {
            var material = $"{ChoiceSeedDomain}|{runSeed}|{GameUtility.CurrentCharacterID}|{choiceKey}|{modelId}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }
    }
}
