using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Extensions;

namespace StS2AP.Utils;

/// <summary>
/// Selects standard AP relic rewards without consuming MegaCrit's reward RNG. The owner persists
/// and publishes the concrete model in the mirrored reward recipe; every replica then reserves
/// that exact model from the native grab bags before the synchronized RewardsSet begins.
/// </summary>
public static class StandardRelicPool
{
    private const string ChoiceSeedDomain = "sts2ap-standard-relic-choice-v1";

    /// <summary>
    /// Creates stable standard-relic choices from the player's current native grab bag without
    /// mutating the bag or advancing PlayerRng.Rewards.
    /// </summary>
    public static IReadOnlyList<RelicModel> CreateChoices(
        Player player,
        string choiceKey,
        int choiceCount,
        IReadOnlyCollection<ModelId>? reservedRelicIds = null)
    {
        if (choiceCount <= 0)
            return Array.Empty<RelicModel>();

        var excludedIds = player.Relics.Select(relic => relic.Id).ToHashSet();
        if (reservedRelicIds != null)
            excludedIds.UnionWith(reservedRelicIds);

        var candidatesByRarity = CollectCandidates(player, excludedIds);
        var selectedIds = new HashSet<ModelId>();
        var choices = new List<RelicModel>(choiceCount);
        string runSeed = player.RunState.Rng.StringSeed;
        long? characterOffset = player.GetAPCharacterNumber();

        for (int ordinal = 0; ordinal < choiceCount; ordinal++)
        {
            string ordinalKey = $"{choiceKey}|{ordinal}";
            RelicRarity rolledRarity = RollStableRarity(runSeed, characterOffset, ordinalKey);
            RelicModel? selected = SelectCandidate(
                candidatesByRarity,
                selectedIds,
                rolledRarity,
                runSeed,
                characterOffset,
                ordinalKey
            );

            selected ??= RelicFactory.FallbackRelic;
            selectedIds.Add(selected.Id);
            choices.Add(selected.ToMutable());
        }

        LogUtility.Info(
            $"Assigned standard AP relic choices for '{choiceKey}' without native reward RNG: "
                + string.Join(", ", choices.Select(relic => relic.Id.ToString()))
        );
        return choices;
    }

    /// <summary>
    /// Applies the base factory's exact-ID bag side effect on each replica. Removal is idempotent,
    /// so reopening a persisted reward or rebuilding a mirrored menu is safe.
    /// </summary>
    public static void ReserveChoice(Player player, RelicModel relic)
    {
        player.RelicGrabBag.Remove(relic);
        player.RunState.SharedRelicGrabBag.Remove(relic);
    }

    /// <summary>
    /// Reapplies persisted reservations when a multiplayer run is bound. Native bag removal is
    /// idempotent, so this also heals saves created before every replica reserved AP assignments.
    /// </summary>
    public static void BindRun(RunState runState)
    {
        foreach (Player player in runState.Players)
        {
            if (ApRunData.TryGetPlayerState(
                    runState,
                    player.NetId,
                    out ApPlayerRunState state
                ) && state.Progress.Initialized)
            {
                ReserveAssignedChoices(player, state.Progress);
            }
        }
    }

    /// <summary>
    /// Reserves the exact standard relic models carried by host-confirmed AP progress. This uses
    /// the existing progress transport rather than introducing a relic-specific network message.
    /// </summary>
    public static void ReserveAssignedChoices(Player player, ApRunProgressState progress)
    {
        foreach ((int itemIndex, List<string> assignments) in progress.RelicChoiceAssignments)
        {
            foreach (string assignment in assignments)
            {
                try
                {
                    SerializableRelic serialized = JsonSerializer.Deserialize<SerializableRelic>(
                        assignment,
                        SerializationUtility.CombinedOptions
                    ) ?? throw new InvalidOperationException("The serialized relic was empty.");
                    ReserveChoice(player, RelicModel.FromSerializable(serialized));
                }
                catch (Exception ex)
                {
                    LogUtility.Warn(
                        $"Could not reserve standard AP relic assignment {itemIndex} for player "
                            + $"{player.NetId}: {ex.GetBaseException().Message}"
                    );
                }
            }
        }
    }

    private static Dictionary<RelicRarity, List<RelicModel>> CollectCandidates(
        Player player,
        IReadOnlySet<ModelId> excludedIds)
    {
        var candidates = new Dictionary<RelicRarity, List<RelicModel>>();
        var serializedBag = player.RelicGrabBag.ToSerializable();
        foreach (RelicRarity rarity in StandardRarities)
        {
            if (!serializedBag.RelicIdLists.TryGetValue(rarity, out List<ModelId>? ids))
                continue;

            foreach (ModelId id in ids)
            {
                if (excludedIds.Contains(id)
                    || ModelDb.GetByIdOrNull<RelicModel>(id) is not RelicModel relic)
                {
                    continue;
                }

                try
                {
                    if (relic.IsAllowed(player.RunState))
                    {
                        if (!candidates.TryGetValue(rarity, out List<RelicModel>? rarityPool))
                        {
                            rarityPool = new List<RelicModel>();
                            candidates[rarity] = rarityPool;
                        }
                        rarityPool.Add(relic);
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Warn(
                        $"Skipping standard AP relic candidate '{id}' because its availability "
                            + $"check failed: {ex.GetBaseException().Message}"
                    );
                }
            }
        }
        return candidates;
    }

    private static RelicModel? SelectCandidate(
        IReadOnlyDictionary<RelicRarity, List<RelicModel>> candidatesByRarity,
        IReadOnlySet<ModelId> selectedIds,
        RelicRarity rolledRarity,
        string runSeed,
        long? characterOffset,
        string choiceKey)
    {
        foreach (RelicRarity rarity in GetRaritySearchOrder(rolledRarity))
        {
            if (!candidatesByRarity.TryGetValue(rarity, out List<RelicModel>? candidates))
                continue;

            RelicModel? selected = candidates
                .Where(relic => !selectedIds.Contains(relic.Id))
                .OrderBy(relic => StableChoiceKey(runSeed, characterOffset, choiceKey, relic.Id))
                .FirstOrDefault();
            if (selected != null)
                return selected;
        }
        return null;
    }

    private static readonly RelicRarity[] StandardRarities =
    [
        RelicRarity.Common,
        RelicRarity.Uncommon,
        RelicRarity.Rare,
    ];

    private static IEnumerable<RelicRarity> GetRaritySearchOrder(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Common =>
        [
            RelicRarity.Common,
            RelicRarity.Uncommon,
            RelicRarity.Rare,
        ],
        RelicRarity.Uncommon =>
        [
            RelicRarity.Uncommon,
            RelicRarity.Rare,
        ],
        _ => [RelicRarity.Rare],
    };

    private static RelicRarity RollStableRarity(
        string runSeed,
        long? characterOffset,
        string choiceKey)
    {
        byte[] hash = StableHash(runSeed, characterOffset, $"{choiceKey}|rarity", ModelId.none);
        double roll = BinaryPrimitives.ReadUInt32BigEndian(hash) / 4294967296d;
        return roll < 0.5d
            ? RelicRarity.Common
            : roll < 0.83d
                ? RelicRarity.Uncommon
                : RelicRarity.Rare;
    }

    private static string StableChoiceKey(
        string runSeed,
        long? characterOffset,
        string choiceKey,
        ModelId modelId) => Convert.ToHexString(
            StableHash(runSeed, characterOffset, choiceKey, modelId)
        );

    private static byte[] StableHash(
        string runSeed,
        long? characterOffset,
        string choiceKey,
        ModelId modelId)
    {
        string material =
            $"{ChoiceSeedDomain}|{runSeed}|{characterOffset}|{choiceKey}|{modelId}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }
}
