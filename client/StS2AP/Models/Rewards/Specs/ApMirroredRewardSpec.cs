namespace StS2AP.Models;

/// <summary>
/// Concrete owner-authored specification sent through RitsuLib Sidecar before a native
/// reward flow starts. Model payloads are MegaCrit save-schema JSON strings so mutable
/// card/relic/potion state can be reconstructed rather than reduced to a model ID.
/// </summary>
public sealed class ApMirroredRewardSpec
{
    public int SchemaVersion { get; set; } = 5;

    public int ApSlotId { get; set; }
    public int ReceivedItemIndex { get; set; }

    /// <summary>
    /// Run-scoped owner binding. MegaCrit preserves this Net ID across active-run rejoin; the
    /// durable receipt identity remains <see cref="GrantId"/> rather than this transport identity.
    /// </summary>
    public ulong OwnerNetId { get; set; }

    public ApMirroredRewardKind Kind { get; set; }

    /// <summary>Display text used only when no concrete native reward type exists.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>AP sender and location retained as presentation metadata on native rewards.</summary>
    public string SenderName { get; set; } = string.Empty;
    public string FoundLocation { get; set; } = string.Empty;

    /// <summary>Owner-facing reason that this receipt cannot currently be claimed.</summary>
    public string UnavailableReason { get; set; } = string.Empty;

    // maybe the card state can be its own data structure, maybe thats too much though
    public bool IsRareCardReward { get; set; }

    public int? CardRewardActIndex { get; set; }

    // CONFIRM: future reroll support as I don't think driftwood works for AP card rewards
    public bool CardCanReroll { get; set; }

    /// <summary>
    /// True once the owning player has opened this card picker and learned its choices. Relics
    /// obtained afterward must not retroactively modify those already-known cards.
    /// </summary>
    public bool CardHasBeenRevealed { get; set; }

    /// <summary>
    /// Versioned generation contract used for a newly assigned card or potion. Existing stable
    /// assignments still carry this value so every replica knows whether owner-final models or
    /// replica-native generation produced them.
    /// </summary>
    public string MaterializationStrategyId { get; set; } = string.Empty;

    /// <summary>
    /// Legacy wire guard only. True is rejected: replica-native generation has been removed.
    /// Current menus always carry final models and leave this false.
    /// </summary>
    public bool RequiresNativeMaterialization { get; set; }

    /// <summary>
    /// Idempotent native state transitions produced by reviewed owner-final card hooks. These are
    /// persisted with the stable assignment and replayed on replicas which did not run the hooks.
    /// </summary>
    public List<ApRewardEffectSpec> AppliedEffects { get; set; } = new();

    // CONFIRM: the datatype of serialized models, why string?
    public List<string> SerializedModels { get; set; } = new();

    public ApGrantId GrantId => new(ApSlotId, ReceivedItemIndex);
}
