using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>
/// Ordered field-level mutation for <see cref="ApRunProgressState"/>. Assignment dictionaries
/// carry only changed keys and removals, so claiming one reward never resends every saved model.
/// </summary>
public sealed class ApProgressDelta
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AncientRewardSettings? AncientSettingsForRun { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CardRewardsAttempted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RareCardRewardsAttempted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RelicRewardsAttempted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BankedRelicRewards { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RelicRewardsAvailableAnytimeForRun { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<long, List<int>>? RelicReceiptIndexesByCharacter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GoldRewardsAttempted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PotionRewardsAttempted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BossRewardsDistributed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GoldRedeemed { get; set; }
    public HashSet<int> MultiplayerBossCompensatedActsAdded { get; set; } = new();
    public HashSet<int> MultiplayerBossCompensatedActsRemoved { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApProgressiveStarterState? StarterCard { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApProgressiveStarterState? StarterRelic { get; set; }

    public Dictionary<int, List<string>> RelicChoiceAssignmentUpserts { get; set; } = new();
    public List<int> RelicChoiceAssignmentRemovals { get; set; } = new();
    public Dictionary<int, List<string>> AncientRelicChoiceAssignmentUpserts { get; set; } = new();
    public List<int> AncientRelicChoiceAssignmentRemovals { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<long, int>? ProgressiveAncients { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<long, int>? ProgressiveRests { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<long, int>? ProgressiveSmiths { get; set; }
    public HashSet<long> CheckedCampfireLocationIdsAdded { get; set; } = new();
    public HashSet<long> CheckedCampfireLocationIdsRemoved { get; set; } = new();
    public Dictionary<int, ApCardAssignmentState> CardAssignmentUpserts { get; set; } = new();
    public List<int> CardAssignmentRemovals { get; set; } = new();
    public Dictionary<int, string> PotionAssignmentUpserts { get; set; } = new();
    public List<int> PotionAssignmentRemovals { get; set; } = new();
    public List<int> UsedItemsAdded { get; set; } = new();
    public List<int> UsedItemsRemoved { get; set; } = new();
    public HashSet<long> PendingLocationChecksAdded { get; set; } = new();
    public HashSet<long> PendingLocationChecksRemoved { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? Ascensions { get; set; }

    [JsonIgnore]
    public bool HasChanges =>
        AncientSettingsForRun != null
        || CardRewardsAttempted.HasValue
        || RareCardRewardsAttempted.HasValue
        || RelicRewardsAttempted.HasValue
        || BankedRelicRewards.HasValue
        || RelicRewardsAvailableAnytimeForRun.HasValue
        || RelicReceiptIndexesByCharacter != null
        || GoldRewardsAttempted.HasValue
        || PotionRewardsAttempted.HasValue
        || BossRewardsDistributed.HasValue
        || GoldRedeemed.HasValue
        || MultiplayerBossCompensatedActsAdded.Count > 0
        || MultiplayerBossCompensatedActsRemoved.Count > 0
        || StarterCard != null
        || StarterRelic != null
        || RelicChoiceAssignmentUpserts.Count > 0
        || RelicChoiceAssignmentRemovals.Count > 0
        || AncientRelicChoiceAssignmentUpserts.Count > 0
        || AncientRelicChoiceAssignmentRemovals.Count > 0
        || ProgressiveAncients != null
        || ProgressiveRests != null
        || ProgressiveSmiths != null
        || CheckedCampfireLocationIdsAdded.Count > 0
        || CheckedCampfireLocationIdsRemoved.Count > 0
        || CardAssignmentUpserts.Count > 0
        || CardAssignmentRemovals.Count > 0
        || PotionAssignmentUpserts.Count > 0
        || PotionAssignmentRemovals.Count > 0
        || UsedItemsAdded.Count > 0
        || UsedItemsRemoved.Count > 0
        || PendingLocationChecksAdded.Count > 0
        || PendingLocationChecksRemoved.Count > 0
        || Ascensions != null;

    public static ApProgressDelta Between(ApRunProgressState before, ApRunProgressState after)
    {
        var delta = new ApProgressDelta
        {
            AncientSettingsForRun = before.AncientSettingsForRun == after.AncientSettingsForRun
                ? null : after.AncientSettingsForRun,
            CardRewardsAttempted = Changed(before.CardRewardsAttempted, after.CardRewardsAttempted),
            RareCardRewardsAttempted = Changed(before.RareCardRewardsAttempted, after.RareCardRewardsAttempted),
            RelicRewardsAttempted = Changed(before.RelicRewardsAttempted, after.RelicRewardsAttempted),
            BankedRelicRewards = Changed(before.BankedRelicRewards, after.BankedRelicRewards),
            RelicRewardsAvailableAnytimeForRun = Changed(
                before.RelicRewardsAvailableAnytimeForRun,
                after.RelicRewardsAvailableAnytimeForRun
            ),
            RelicReceiptIndexesByCharacter = RelicReceiptMapsEqual(
                before.RelicReceiptIndexesByCharacter,
                after.RelicReceiptIndexesByCharacter
            )
                ? null
                : CloneRelicReceiptMap(after.RelicReceiptIndexesByCharacter),
            GoldRewardsAttempted = Changed(before.GoldRewardsAttempted, after.GoldRewardsAttempted),
            PotionRewardsAttempted = Changed(before.PotionRewardsAttempted, after.PotionRewardsAttempted),
            BossRewardsDistributed = Changed(before.BossRewardsDistributed, after.BossRewardsDistributed),
            GoldRedeemed = Changed(before.GoldRedeemed, after.GoldRedeemed),
            ProgressiveAncients = CountMapsEqual(
                before.ProgressiveAncients,
                after.ProgressiveAncients
            )
                ? null
                : new Dictionary<long, int>(after.ProgressiveAncients),
            ProgressiveRests = CountMapsEqual(before.ProgressiveRests, after.ProgressiveRests)
                ? null
                : new Dictionary<long, int>(after.ProgressiveRests),
            ProgressiveSmiths = CountMapsEqual(before.ProgressiveSmiths, after.ProgressiveSmiths)
                ? null
                : new Dictionary<long, int>(after.ProgressiveSmiths),
            StarterCard = StarterChanged(
                before.ProgressiveStarterCardBaseId,
                before.ProgressiveStarterCardUpgradedId,
                before.ProgressiveStarterCardTier,
                after.ProgressiveStarterCardBaseId,
                after.ProgressiveStarterCardUpgradedId,
                after.ProgressiveStarterCardTier
            ),
            StarterRelic = StarterChanged(
                before.ProgressiveStarterRelicBaseId,
                before.ProgressiveStarterRelicUpgradedId,
                before.ProgressiveStarterRelicTier,
                after.ProgressiveStarterRelicBaseId,
                after.ProgressiveStarterRelicUpgradedId,
                after.ProgressiveStarterRelicTier
            ),
        };

        DiffDictionary(
            before.RelicChoiceAssignments,
            after.RelicChoiceAssignments,
            static (left, right) => left.SequenceEqual(right),
            static value => value.ToList(),
            delta.RelicChoiceAssignmentUpserts,
            delta.RelicChoiceAssignmentRemovals
        );
        DiffDictionary(
            before.AncientRelicChoiceAssignments,
            after.AncientRelicChoiceAssignments,
            static (left, right) => left.SequenceEqual(right),
            static value => value.ToList(),
            delta.AncientRelicChoiceAssignmentUpserts,
            delta.AncientRelicChoiceAssignmentRemovals
        );
        DiffDictionary(
            before.CardAssignments,
            after.CardAssignments,
            CardAssignmentEquals,
            CloneCardAssignment,
            delta.CardAssignmentUpserts,
            delta.CardAssignmentRemovals
        );
        DiffDictionary(
            before.PotionAssignments,
            after.PotionAssignments,
            static (left, right) => left == right,
            static value => value,
            delta.PotionAssignmentUpserts,
            delta.PotionAssignmentRemovals
        );

        delta.UsedItemsAdded = after.UsedItems.Except(before.UsedItems).ToList();
        delta.UsedItemsRemoved = before.UsedItems.Except(after.UsedItems).ToList();
        delta.PendingLocationChecksAdded = after.PendingLocationChecks
            .Except(before.PendingLocationChecks).ToHashSet();
        delta.PendingLocationChecksRemoved = before.PendingLocationChecks
            .Except(after.PendingLocationChecks).ToHashSet();
        delta.MultiplayerBossCompensatedActsAdded = after.MultiplayerBossCompensatedActs
            .Except(before.MultiplayerBossCompensatedActs).ToHashSet();
        delta.MultiplayerBossCompensatedActsRemoved = before.MultiplayerBossCompensatedActs
            .Except(after.MultiplayerBossCompensatedActs).ToHashSet();
        delta.CheckedCampfireLocationIdsAdded = after.CheckedCampfireLocationIds
            .Except(before.CheckedCampfireLocationIds).ToHashSet();
        delta.CheckedCampfireLocationIdsRemoved = before.CheckedCampfireLocationIds
            .Except(after.CheckedCampfireLocationIds).ToHashSet();
        if (!before.Ascensions.ToHashSet().SetEquals(after.Ascensions))
            delta.Ascensions = after.Ascensions.Distinct().OrderBy(level => level).ToList();
        return delta;
    }

    public ApRunProgressState ApplyToCopy(ApRunProgressState source)
    {
        ApRunProgressState result = Clone(source);
        if (AncientSettingsForRun != null)
            result.AncientSettingsForRun = AncientSettingsForRun;
        Apply(CardRewardsAttempted, value => result.CardRewardsAttempted = value);
        Apply(RareCardRewardsAttempted, value => result.RareCardRewardsAttempted = value);
        Apply(RelicRewardsAttempted, value => result.RelicRewardsAttempted = value);
        Apply(BankedRelicRewards, value => result.BankedRelicRewards = value);
        Apply(RelicRewardsAvailableAnytimeForRun, value => result.RelicRewardsAvailableAnytimeForRun = value);
        if (RelicReceiptIndexesByCharacter != null)
            result.RelicReceiptIndexesByCharacter = CloneRelicReceiptMap(RelicReceiptIndexesByCharacter);
        Apply(GoldRewardsAttempted, value => result.GoldRewardsAttempted = value);
        Apply(PotionRewardsAttempted, value => result.PotionRewardsAttempted = value);
        Apply(BossRewardsDistributed, value => result.BossRewardsDistributed = value);
        Apply(GoldRedeemed, value => result.GoldRedeemed = value);
        if (StarterCard != null)
        {
            result.ProgressiveStarterCardBaseId = StarterCard.BaseId;
            result.ProgressiveStarterCardUpgradedId = StarterCard.UpgradedId;
            result.ProgressiveStarterCardTier = StarterCard.Tier;
        }
        if (StarterRelic != null)
        {
            result.ProgressiveStarterRelicBaseId = StarterRelic.BaseId;
            result.ProgressiveStarterRelicUpgradedId = StarterRelic.UpgradedId;
            result.ProgressiveStarterRelicTier = StarterRelic.Tier;
        }

        ApplyDictionary(result.RelicChoiceAssignments, RelicChoiceAssignmentUpserts, RelicChoiceAssignmentRemovals);
        ApplyDictionary(result.AncientRelicChoiceAssignments, AncientRelicChoiceAssignmentUpserts, AncientRelicChoiceAssignmentRemovals);
        if (ProgressiveAncients != null)
            result.ProgressiveAncients = new Dictionary<long, int>(ProgressiveAncients);
        if (ProgressiveRests != null)
            result.ProgressiveRests = new Dictionary<long, int>(ProgressiveRests);
        if (ProgressiveSmiths != null)
            result.ProgressiveSmiths = new Dictionary<long, int>(ProgressiveSmiths);
        result.CheckedCampfireLocationIds.ExceptWith(CheckedCampfireLocationIdsRemoved);
        result.CheckedCampfireLocationIds.UnionWith(CheckedCampfireLocationIdsAdded);
        ApplyDictionary(result.CardAssignments, CardAssignmentUpserts, CardAssignmentRemovals);
        ApplyDictionary(result.PotionAssignments, PotionAssignmentUpserts, PotionAssignmentRemovals);
        result.UsedItems.RemoveAll(UsedItemsRemoved.Contains);
        foreach (int item in UsedItemsAdded)
            if (!result.UsedItems.Contains(item))
                result.UsedItems.Add(item);
        result.PendingLocationChecks.ExceptWith(PendingLocationChecksRemoved);
        result.PendingLocationChecks.UnionWith(PendingLocationChecksAdded);
        result.MultiplayerBossCompensatedActs.ExceptWith(
            MultiplayerBossCompensatedActsRemoved
        );
        result.MultiplayerBossCompensatedActs.UnionWith(
            MultiplayerBossCompensatedActsAdded
        );
        if (Ascensions != null)
            result.Ascensions = Ascensions.Distinct().OrderBy(level => level).ToList();
        return result;
    }

    private static ApRunProgressState Clone(ApRunProgressState source) => new()
    {
        Initialized = source.Initialized,
        CardRewardsAttempted = source.CardRewardsAttempted,
        RareCardRewardsAttempted = source.RareCardRewardsAttempted,
        RelicRewardsAttempted = source.RelicRewardsAttempted,
        BankedRelicRewards = source.BankedRelicRewards,
        AncientSettingsForRun = source.AncientSettingsForRun,
        RelicRewardsAvailableAnytimeForRun = source.RelicRewardsAvailableAnytimeForRun,
        RelicReceiptIndexesByCharacter = CloneRelicReceiptMap(source.RelicReceiptIndexesByCharacter),
        GoldRewardsAttempted = source.GoldRewardsAttempted,
        PotionRewardsAttempted = source.PotionRewardsAttempted,
        BossRewardsDistributed = source.BossRewardsDistributed,
        RelicChoiceAssignments = source.RelicChoiceAssignments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList()
        ),
        AncientRelicChoiceAssignments = source.AncientRelicChoiceAssignments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList()
        ),
        ProgressiveAncients = new Dictionary<long, int>(source.ProgressiveAncients),
        ProgressiveRests = new Dictionary<long, int>(source.ProgressiveRests),
        ProgressiveSmiths = new Dictionary<long, int>(source.ProgressiveSmiths),
        CheckedCampfireLocationIds = new HashSet<long>(source.CheckedCampfireLocationIds),
        CardAssignments = source.CardAssignments.ToDictionary(
            pair => pair.Key,
            pair => CloneCardAssignment(pair.Value)
        ),
        PotionAssignments = new Dictionary<int, string>(source.PotionAssignments),
        UsedItems = source.UsedItems.ToList(),
        GoldRedeemed = source.GoldRedeemed,
        ProgressiveStarterCardBaseId = source.ProgressiveStarterCardBaseId,
        ProgressiveStarterCardUpgradedId = source.ProgressiveStarterCardUpgradedId,
        ProgressiveStarterCardTier = source.ProgressiveStarterCardTier,
        ProgressiveStarterRelicBaseId = source.ProgressiveStarterRelicBaseId,
        ProgressiveStarterRelicUpgradedId = source.ProgressiveStarterRelicUpgradedId,
        ProgressiveStarterRelicTier = source.ProgressiveStarterRelicTier,
        PendingLocationChecks = new HashSet<long>(source.PendingLocationChecks),
        MultiplayerBossCompensatedActs = new HashSet<int>(
            source.MultiplayerBossCompensatedActs
        ),
        Ascensions = source.Ascensions.ToList(),
    };

    private static int? Changed(int before, int after) => before == after ? null : after;

    private static bool RelicReceiptMapsEqual(
        IReadOnlyDictionary<long, List<int>> left,
        IReadOnlyDictionary<long, List<int>> right) =>
        left.Count == right.Count
        && left.All(pair =>
            right.TryGetValue(pair.Key, out List<int>? values)
            && pair.Value.SequenceEqual(values));

    private static bool CountMapsEqual(
        IReadOnlyDictionary<long, int> left,
        IReadOnlyDictionary<long, int> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out int value) && value == pair.Value);

    private static Dictionary<long, List<int>> CloneRelicReceiptMap(
        IReadOnlyDictionary<long, List<int>> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());

    private static ApProgressiveStarterState? StarterChanged(
        string? beforeBaseId,
        string? beforeUpgradedId,
        ProgressiveStarterTier beforeTier,
        string? afterBaseId,
        string? afterUpgradedId,
        ProgressiveStarterTier afterTier)
    {
        if (string.Equals(beforeBaseId, afterBaseId, StringComparison.Ordinal)
            && string.Equals(beforeUpgradedId, afterUpgradedId, StringComparison.Ordinal)
            && beforeTier == afterTier)
        {
            return null;
        }

        return new ApProgressiveStarterState
        {
            BaseId = afterBaseId,
            UpgradedId = afterUpgradedId,
            Tier = afterTier,
        };
    }

    private static bool CardAssignmentEquals(
        ApCardAssignmentState left,
        ApCardAssignmentState right) =>
        left.CanReroll == right.CanReroll
        && left.IsRare == right.IsRare
        && left.RewardActIndex == right.RewardActIndex
        && left.HasBeenRevealed == right.HasBeenRevealed
        && left.MaterializationStrategyId == right.MaterializationStrategyId
        && left.AppliedEffects.Select(EffectKey).SequenceEqual(
            right.AppliedEffects.Select(EffectKey)
        )
        && left.SerializedCards.SequenceEqual(right.SerializedCards);

    private static ApCardAssignmentState CloneCardAssignment(ApCardAssignmentState value) => new()
    {
        CanReroll = value.CanReroll,
        IsRare = value.IsRare,
        RewardActIndex = value.RewardActIndex,
        HasBeenRevealed = value.HasBeenRevealed,
        MaterializationStrategyId = value.MaterializationStrategyId,
        AppliedEffects = value.AppliedEffects.Select(CloneEffect).ToList(),
        SerializedCards = value.SerializedCards.ToList(),
    };

    private static string EffectKey(ApRewardEffectSpec effect) =>
        $"{effect.EffectId}\n{effect.BeforeValue}\n{effect.AfterValue}";

    private static ApRewardEffectSpec CloneEffect(ApRewardEffectSpec effect) => new()
    {
        EffectId = effect.EffectId,
        BeforeValue = effect.BeforeValue,
        AfterValue = effect.AfterValue,
    };

    private static void DiffDictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> before,
        IReadOnlyDictionary<TKey, TValue> after,
        Func<TValue, TValue, bool> equal,
        Func<TValue, TValue> clone,
        IDictionary<TKey, TValue> upserts,
        ICollection<TKey> removals)
        where TKey : notnull
    {
        foreach ((TKey key, TValue value) in after)
        {
            if (!before.TryGetValue(key, out TValue? oldValue) || !equal(oldValue, value))
                upserts[key] = clone(value);
        }
        foreach (TKey key in before.Keys)
            if (!after.ContainsKey(key))
                removals.Add(key);
    }

    private static void ApplyDictionary<TKey, TValue>(
        IDictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> upserts,
        IEnumerable<TKey> removals)
        where TKey : notnull
    {
        foreach (TKey key in removals)
            target.Remove(key);
        foreach ((TKey key, TValue value) in upserts)
            target[key] = value;
    }

    private static void Apply<T>(T? value, Action<T> setter) where T : struct
    {
        if (value.HasValue)
            setter(value.Value);
    }
}
