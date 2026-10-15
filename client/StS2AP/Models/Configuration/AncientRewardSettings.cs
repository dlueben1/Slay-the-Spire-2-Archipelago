namespace StS2AP.Models
{
    // Where you can receive Ancient Relics
    public enum AncientRelicLocation
    {
        StartOfAct = 0,
        Anytime = 1,
    }

    // Balanced = Relics from the run's Ancient for each Progressive Ancient reward.
    // Chaos = Any Ancient relic from the reward's act.
    // TrueChaos = Any Act 2 or Act 3 Ancient relic; Neow's reward remains Neow-only.
    public enum AncientRelicPoolMode
    {
        Balanced = 0,
        Chaos = 1,
        TrueChaos = 2,
    }

    /// <summary>Immutable Ancient policy captured for one run.</summary>
    public sealed record AncientRewardSettings(
        AncientRelicLocation Location,
        AncientRelicPoolMode Pool)
    {
        public AncientRewardSettings WithOverrides(ClientSettings local) => new(
            local.AncientRelicLocationOverride ?? Location,
            local.AncientRelicPoolOverride ?? Pool);
    }
}
