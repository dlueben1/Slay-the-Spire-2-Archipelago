namespace StS2AP.Utils;

public static class AncientSettingsUtility
{
    public static AncientRewardSettings ForNewRun =>
        ArchipelagoClient.AncientSlotDefaults.WithOverrides(ArchipelagoClient.LocalSettings.Value);

    public static AncientRewardSettings Current
    {
        get
        {
            // Every AP participant keeps their own saved policy, including non-host clients.
            if (MultiplayerSupport.IsRealMultiplayerRun
                && GameUtility.CurrentPlayer is { } player
                && ApPlayerContextResolver.TryGetRewardSettings(player, out var settings))
                return new(settings.AncientRelicLocation, settings.AncientRelicPool);

            return ArchipelagoClient.Progress.AncientSettingsForRun
                ?? ArchipelagoClient.AncientSlotDefaults;
        }
    }
}
