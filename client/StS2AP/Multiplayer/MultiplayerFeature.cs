namespace StS2AP.Multiplayer;

/// <summary>Capabilities reviewed independently for real multiplayer runs.</summary>
public enum MultiplayerFeature
{
    CharacterUnlocks,
    PressStartCheck,
    GoldRewards,
    CardRewards,
    RelicRewards,
    PotionRewards,
    AncientRewardChoices,
    CombatRewardLocations,
    FloorChecks,
    Shops,

    // Canonical AP progress plus MegaCrit's native RestSiteSynchronizer.
    RestSites,
    Ancients,
    VictoryChecks,
    ProgressiveStarters,
    AscensionEffects,
    CombatEffects,
    DeathLink,

    // AP_MP: permanent deck and combat-pile mutation remains disabled independently of damage.
    DeathFragments,
    SaveAndReconnect,
    UnknownReceivedItems,
}
