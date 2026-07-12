using StS2AP.Models;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace StS2AP;

/// <summary>
/// Registers the player-facing settings UI for the Archipelago client.
/// </summary>
public static class ModSettingsRegistration
{
    #region Death Link Settings Keys

    private const string DeathLink_OverrideId = "override_deathlink";
    private const string DeathLink_EnableId = "enable_deathlink";
    private const string DeathLink_FragmentsOnId = "enable_death_fragments";
    private const string DeathLink_DamageId = "deathlink_damage";

    #endregion

    #region Settings Screen Composition

    /// <summary>
    /// Registers the Archipelago settings page with RitsuLib.
    /// </summary>
    public static void Register()
    {
        RitsuLibFramework.RegisterModSettings(
            ModEntry.ModId,
            page => page
                .WithTitle(ModSettingsText.Literal("Archipelago Settings"))
                .WithModDisplayName(ModSettingsText.Literal("Archipelago"))
                .AddSection("deathlink", ConfigureDeathLinkSection));
    }

    /// <summary>
    /// Composes the Death Link settings section
    /// </summary>
    private static void ConfigureDeathLinkSection(
        ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Death Link"))
            .WithDescription(ModSettingsText.Literal(
                "Configure how received Death Links affect this game."))
            .AddToggle(
                DeathLink_OverrideId,
                ModSettingsText.Literal("Use Custom Death Link Settings"),
                CreateBinding(
                    static settings => settings.OverrideDeathLinkOptions,
                    static (settings, value) => settings.OverrideDeathLinkOptions = value),
                ModSettingsText.Literal(
                    "Override the Death Link options supplied by the " +
                    "Archipelago slot data."))
            .AddToggle(
                DeathLink_EnableId,
                ModSettingsText.Literal("Enable Death Link"),
                CreateBinding(
                    static settings => settings.EnableDeathLink,
                    static (settings, value) => settings.EnableDeathLink = value),
                ModSettingsText.Literal(
                    "Opt in to or out of Death Link."))
            .WithEntryEnabledWhen(
                DeathLink_EnableId,
                IsDeathLinkOverriden)
            .AddToggle(
                DeathLink_FragmentsOnId,
                ModSettingsText.Literal("Enable Death Fragments"),
                CreateBinding(
                    static settings => settings.EnableDeathFragments,
                    static (settings, value) => settings.EnableDeathFragments = value),
                ModSettingsText.Literal(
                    "Receive a special curse when a Death Link is received."))
            .WithEntryEnabledWhen(
                DeathLink_FragmentsOnId,
                IsDeathLinkOverriden)
            .AddIntSlider(
                DeathLink_DamageId,
                ModSettingsText.Literal("Death Link Damage"),
                CreateBinding(
                    static settings => settings.DeathLinkPercentDamage,
                    static (settings, value) => settings.DeathLinkPercentDamage = value),
                minValue: 0,
                maxValue: 100,
                step: 5,
                valueFormatter: static value => $"{value}%",
                description: ModSettingsText.Literal(
                    "The percentage of maximum health lost when a " +
                    "Death Link is received."))
            .WithEntryEnabledWhen(
                DeathLink_DamageId,
                IsDeathLinkOverriden);
    }

    #endregion

    #region Helper Functions

    /// <summary>
    /// Local Check to use in-settings only for determining if Death Link overrides are enabled or not.
    /// Do NOT use this outside of this class - if you want to check if Death Link is overridden, use
    /// <see cref="ArchipelagoClient.LocalSettings"/>
    /// </summary>
    private static bool IsDeathLinkOverriden()
    {
        var store = RitsuLibFramework.GetDataStore(ModEntry.ModId);
        var settings = store.Get<ClientSettings>("apsettings");
        return settings.OverrideDeathLinkOptions;
    }

    /// <summary>
    /// Factory Pattern for Settings Binding 
    /// </summary>
    private static ModSettingsValueBinding<ClientSettings, TValue>
        CreateBinding<TValue>(
            Func<ClientSettings, TValue> getter,
            Action<ClientSettings, TValue> setter)
    {
        return new ModSettingsValueBinding<ClientSettings, TValue>(
            ModEntry.ModId,
            "apsettings",
            SaveScope.Global,
            getter,
            setter);
    }

    #endregion
}