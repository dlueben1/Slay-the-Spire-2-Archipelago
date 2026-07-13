using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using STS2RitsuLib;
using STS2RitsuLib.RuntimeInput;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace StS2AP;

/// <summary>
/// Registers the player-facing settings UI for the Archipelago client.
/// </summary>
public static class ModSettingsRegistration
{
    #region Settings Keys

    // Keybindings
    private const string KeyBinds_APMenuId = "keybind_ap_menu";

    // Notifications
    private const string Notif_AnnouncerId = "notif_announcer";

    // Death Link
    private const string DeathLink_OverrideId = "override_deathlink";
    private const string DeathLink_EnableId = "enable_deathlink";
    private const string DeathLink_FragmentsOnId = "enable_death_fragments";
    private const string DeathLink_DamageId = "deathlink_damage";

    #endregion

    #region Handle Hotkeys

    /// <summary>
    /// The Handle for the current registered hotkey for opening the Archipelago Loot Menu.
    /// </summary>
    private static IRuntimeHotkeyHandle? ApLootHotkeyHandle;

    /// <summary>
    /// Registers runtime hotkeys after the game is ready.
    /// Currently we don't have more than one hotkey, but if we ever end up with a lot more
    /// we probably want to refactor this into a separate hotkey registration system.
    /// </summary>
    public static void RegisterHotkeys()
    {
        // Pull the settings from the data store and normalize the hotkey binding
        var store = RitsuLibFramework.GetDataStore(ModEntry.ModId);
        var settings = store.Get<ClientSettings>("apsettings");

        var normalizedBinding = RuntimeHotkeyService.NormalizeOrDefault(
            settings.OpenArchLootHotKey,
            "P"
        );

        // Map the normalized binding
        settings.OpenArchLootHotKey = normalizedBinding;

        // And register it
        ApLootHotkeyHandle?.Dispose();
        ApLootHotkeyHandle = RuntimeHotkeyService.Register(
            normalizedBinding,
            () =>
            {
                // Ignore if we're not in a run
                if (!GameUtility.IsInRun)
                    return;

                // Toggle the Archipelago Reward UI
                if (!ArchipelagoRewardUI.IsOpen)
                {
                    ArchipelagoRewardUI.ShowRewards();
                }
                else
                {
                    ArchipelagoRewardUI.Hide();
                }
            },
            new RuntimeHotkeyOptions
            {
                Id = $"{ModEntry.ModId}.{KeyBinds_APMenuId}",
                DisplayName = RuntimeHotkeyText.Literal("Open AP Loot Menu"),
                Description = RuntimeHotkeyText.Literal("Opens the Archipelago Loot menu."),
                Category = RuntimeHotkeyText.Literal("Archipelago"),
                MarkInputHandled = true,
            }
        );
    }

    #endregion

    #region Settings Screen Composition

    /// <summary>
    /// Registers the Archipelago settings page with RitsuLib.
    /// </summary>
    public static void Register()
    {
        RitsuLibFramework.RegisterModSettings(
            ModEntry.ModId,
            page =>
                page.WithTitle(ModSettingsText.Literal("Archipelago Settings"))
                    .WithModDisplayName(ModSettingsText.Literal("Archipelago"))
                    .WithMenuCapabilities(ModSettingsMenuCapabilities.None)
                    .AddSection("keybinds", ConfigureKeybindsSection)
                    .AddSection("notifications", ConfigureNotificationsSection)
                    .AddSection("deathlink", ConfigureDeathLinkSection)
        );
        RegisterHotkeys();
    }

    /// <summary>
    /// Composes the Keybinds settings section
    /// </summary>
    private static void ConfigureKeybindsSection(ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Keyboard Controls"))
            .WithDescription(
                ModSettingsText.Literal("Configure keybinds for Archipelago functionality.")
            )
            .AddKeyBinding(
                KeyBinds_APMenuId,
                ModSettingsText.Literal("Open AP Loot Menu"),
                CreateBinding(
                    static settings => settings.OpenArchLootHotKey,
                    static (settings, value) =>
                    {
                        // Normalize the input and save it
                        var normalizedBinding = RuntimeHotkeyService.NormalizeOrDefault(value, "P");
                        settings.OpenArchLootHotKey = normalizedBinding;

                        // Attempt to rebind it (it should have been initially bound during startup)
                        if (ApLootHotkeyHandle is not null)
                        {
                            ApLootHotkeyHandle.TryRebind(normalizedBinding, out var error);

                            if (error is not null)
                            {
                                RitsuLibFramework.Logger.Warn(
                                    $"Unable to rebind AP menu hotkey: {error}"
                                );
                            }
                        }
                    }
                )
            )
            .ConfigureEntryMenu(
                KeyBinds_APMenuId,
                ModSettingsMenuCapabilities.Copy | ModSettingsMenuCapabilities.Paste
            );
    }

    /// <summary>
    /// Composes the Notifications settings section
    /// </summary>
    private static void ConfigureNotificationsSection(ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Notifications"))
            .WithDescription(
                ModSettingsText.Literal("Configure how Archipelago notifications are displayed.")
            )
            .WithMenuCapabilities(ModSettingsMenuCapabilities.None)
            .AddChoice(
                Notif_AnnouncerId,
                ModSettingsText.Literal("Announcer"),
                CreateBinding(
                    static settings => settings.Announcer,
                    static (settings, value) =>
                    {
                        settings.Announcer = value;

                        // Update the speaker icon immediately if the UI is already injected
                        ArchipelagoNotificationUI.UpdateSpeakerIcon();
                    }
                ),
                options: new[]
                {
                    new ModSettingsChoiceOption<string>("neow", ModSettingsText.Literal("Neow")),
                    new ModSettingsChoiceOption<string>("pael", ModSettingsText.Literal("Pael")),
                    new ModSettingsChoiceOption<string>(
                        "orobas",
                        ModSettingsText.Literal("Orobas")
                    ),
                    new ModSettingsChoiceOption<string>(
                        "tezcatara",
                        ModSettingsText.Literal("Tezcatara")
                    ),
                    new ModSettingsChoiceOption<string>("darv", ModSettingsText.Literal("Darv")),
                    new ModSettingsChoiceOption<string>("vakuu", ModSettingsText.Literal("Vakuu")),
                    new ModSettingsChoiceOption<string>("tanx", ModSettingsText.Literal("Tanx")),
                    new ModSettingsChoiceOption<string>(
                        "nonupeipe",
                        ModSettingsText.Literal("Nonupeipe")
                    ),
                },
                description: ModSettingsText.Literal(
                    "Select which Ancient announces notifications"
                ),
                presentation: ModSettingsChoicePresentation.Dropdown
            )
            .ConfigureEntryMenu(Notif_AnnouncerId, ModSettingsMenuCapabilities.None);
    }

    /// <summary>
    /// Composes the Death Link settings section
    /// </summary>
    private static void ConfigureDeathLinkSection(ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Death Link"))
            .WithDescription(
                ModSettingsText.Literal("Configure how received Death Links affect this game.")
            )
            .WithMenuCapabilities(ModSettingsMenuCapabilities.None)
            .AddToggle(
                DeathLink_OverrideId,
                ModSettingsText.Literal("Use Custom Death Link Settings"),
                CreateBinding(
                    static settings => settings.OverrideDeathLinkOptions,
                    static (settings, value) => settings.OverrideDeathLinkOptions = value
                ),
                ModSettingsText.Literal(
                    "Override the Death Link options supplied by the Archipelago slot data."
                )
            )
            .ConfigureEntryMenu(DeathLink_OverrideId, ModSettingsMenuCapabilities.None)
            .AddToggle(
                DeathLink_EnableId,
                ModSettingsText.Literal("Enable Death Link"),
                CreateBinding(
                    static settings => settings.EnableDeathLink,
                    static (settings, value) => settings.EnableDeathLink = value
                ),
                ModSettingsText.Literal("Opt in to or out of Death Link.")
            )
            .ConfigureEntryMenu(DeathLink_EnableId, ModSettingsMenuCapabilities.None)
            .WithEntryEnabledWhen(DeathLink_EnableId, IsDeathLinkOverriden)
            .AddToggle(
                DeathLink_FragmentsOnId,
                ModSettingsText.Literal("Enable Death Fragments"),
                CreateBinding(
                    static settings => settings.EnableDeathFragments,
                    static (settings, value) => settings.EnableDeathFragments = value
                ),
                ModSettingsText.Literal("Receive a special curse when a Death Link is received.")
            )
            .ConfigureEntryMenu(DeathLink_FragmentsOnId, ModSettingsMenuCapabilities.None)
            .WithEntryEnabledWhen(DeathLink_FragmentsOnId, IsDeathLinkOverriden)
            .AddIntSlider(
                DeathLink_DamageId,
                ModSettingsText.Literal("Death Link Damage"),
                CreateBinding(
                    static settings => settings.DeathLinkPercentDamage,
                    static (settings, value) => settings.DeathLinkPercentDamage = value
                ),
                minValue: 0,
                maxValue: 100,
                step: 5,
                valueFormatter: static value => $"{value}%",
                description: ModSettingsText.Literal(
                    "The percentage of maximum health lost when a Death Link is received."
                )
            )
            .ConfigureEntryMenu(DeathLink_DamageId, ModSettingsMenuCapabilities.None)
            .WithEntryEnabledWhen(DeathLink_DamageId, IsDeathLinkOverriden);
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
    private static ModSettingsValueBinding<ClientSettings, TValue> CreateBinding<TValue>(
        Func<ClientSettings, TValue> getter,
        Action<ClientSettings, TValue> setter
    )
    {
        return new ModSettingsValueBinding<ClientSettings, TValue>(
            ModEntry.ModId,
            "apsettings",
            SaveScope.Global,
            getter,
            setter
        );
    }

    #endregion
}
