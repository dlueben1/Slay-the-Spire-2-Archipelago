using MegaCrit.Sts2.Core.Models;
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

    // Controller.joystickPress was renamed to Controller.lStickPress in 0.108. Keeping both
    // action strings lets the multiplayer branch retain the cross-version runtime binding.
    private const string LegacyControllerStickPress = "controller_joystick_press";
    private const string CurrentControllerStickPress = "controller_l_stick_press";

    // Notifications
    private const string Notif_AnnouncerId = "notif_announcer";

    // Death Link
    private const string DeathLink_OverrideId = "override_deathlink";
    private const string DeathLink_EnableId = "enable_deathlink";
    private const string DeathLink_FragmentsOnId = "enable_death_fragments";
    private const string DeathLink_DamageId = "deathlink_damage";

    // Relic rewards
    private const string RelicRewards_OverrideId = "override_relic_rewards_available_anytime";
    private const string RelicRewards_AvailableAnytimeId = "relic_rewards_available_anytime";

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
            GetApLootBindings(normalizedBinding),
            () =>
            {
                // Ignore if we're not in a run
                if (!GameUtility.IsInRun)
                    return;

                ArchipelagoRewardUI.Toggle();
            },
            new RuntimeHotkeyOptions
            {
                Id = $"{ModEntry.ModId}.{KeyBinds_APMenuId}",
                DisplayName = RuntimeHotkeyText.Literal("Open AP Loot Menu"),
                Description = RuntimeHotkeyText.Literal(
                    "Opens the Archipelago Loot menu with the configured keyboard shortcut or L3."
                ),
                Category = RuntimeHotkeyText.Literal("Archipelago"),
                MarkInputHandled = true,
            }
        );
    }

    private static string[] GetApLootBindings(string keyboardBinding)
    {
        return
        [
            keyboardBinding,
            RuntimeHotkeyService.ActionBinding(LegacyControllerStickPress),
            RuntimeHotkeyService.ActionBinding(CurrentControllerStickPress),
        ];
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
                    .AddSection("charnames", ConfigureModdedCharactersSection)
                    .AddSection("keybinds", ConfigureKeybindsSection)
                    .AddSection("notifications", ConfigureNotificationsSection)
                    .AddSection("multiplayer", ConfigureMultiplayerSection)
                    .AddSection("relic_rewards", ConfigureRelicRewardsSection)
                    .AddSection("ancient_rewards", ConfigureAncientRewardsSection)
                    .AddSection("deathlink", ConfigureDeathLinkSection)
                    .AddSection("bug_reports", ConfigureBugReportsSection)
        );
        RegisterHotkeys();
    }

    private static void ConfigureModdedCharactersSection(ModSettingsSectionBuilder section)
    {
        section.WithTitle(ModSettingsText.Literal("Installed Characters"))
                .WithDescription(ModSettingsText.Literal("Internal Names of Installed Modded Characters"))
                .AddInfoCard("ap-modded-chars", ModSettingsText.Literal("Character Names"), ModSettingsText.Dynamic(GetModdedNames));

    }

    private static void ConfigureBugReportsSection(ModSettingsSectionBuilder section)
    {
        section.WithTitle(ModSettingsText.Literal("Bug Reports"))
            .AddButton("export_bug_report", ModSettingsText.Literal("Multiplayer diagnostics"),
                ModSettingsText.Literal("Export Bug Report"), host =>
                {
                    ApBugReport.TryStart(out _, host.RequestRefresh);
                    host.RequestRefresh();
                }, description: ModSettingsText.Literal(
                    "Packages the newest divergence report and available game logs, then opens the ZIP's folder. You can also type ap report in the console."))
            .AddInfoCard("bug_report_status", ModSettingsText.Literal("Export status"),
                ModSettingsText.Dynamic(() => ApBugReport.Status));
    }

    private static string GetModdedNames()
    {
        StringWriter sw =  new StringWriter();
        var moddedChars = ModelDb.AllCharacters.Where(c => {
            if(!c.IsPlayable)
            {
                return false;
            }
            switch(c.Id.Entry)
            {
                case "IRONCLAD":
                case "SILENT":
                case "REGENT":
                case "DEFECT":
                case "NECROBINDER":
                    return false;
            }
            return true;
        });
        foreach(var c in moddedChars)
        {
            sw.WriteLine(c.Id.Entry);
        }
        return sw.ToString();
    }

    /// <summary>
    /// Composes the Keybinds settings section
    /// </summary>
    private static void ConfigureKeybindsSection(ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Controls"))
            .WithDescription(
                ModSettingsText.Literal(
                    "Configure Archipelago controls. The AP Loot Menu also opens with L3."
                )
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
                            if (!ApLootHotkeyHandle.TryRebind(
                                    GetApLootBindings(normalizedBinding),
                                    out _
                                ))
                            {
                                RitsuLibFramework.Logger.Warn(
                                    $"Unable to rebind AP menu keyboard hotkey to '{normalizedBinding}'."
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

    private static void ConfigureAncientRewardsSection(ModSettingsSectionBuilder section)
    {
        section.WithTitle(ModSettingsText.Literal("Ancient Rewards"))
            .WithDescription(ModSettingsText.Literal(
                "Applies only to new runs, including multiplayer. Continuing a run keeps its saved mode and pool."))
            .WithMenuCapabilities(ModSettingsMenuCapabilities.None)
            .AddChoice(
                "ancient_mode", ModSettingsText.Literal("Ancient Mode"),
                CreateBinding(
                    static settings => settings.AncientRelicLocationOverride is { } mode ? (int)mode : -1,
                    static (settings, value) => settings.AncientRelicLocationOverride =
                        value == -1 ? null : (AncientRelicLocation)value),
                options: new[]
                {
                    new ModSettingsChoiceOption<int>(-1, ModSettingsText.Literal("Use AP Slot Setting")),
                    new ModSettingsChoiceOption<int>(0, ModSettingsText.Literal("Start of Act")),
                    new ModSettingsChoiceOption<int>(1, ModSettingsText.Literal("Anytime")),
                },
                description: ModSettingsText.Literal(
                    "Anytime is recommended for multiplayer. Start of Act rewards missed before a checkpoint cannot be claimed later in that run."))
            .AddChoice(
                "ancient_pool", ModSettingsText.Literal("Ancient Pool"),
                CreateBinding(
                    static settings => settings.AncientRelicPoolOverride is { } pool ? (int)pool : -1,
                    static (settings, value) => settings.AncientRelicPoolOverride =
                        value == -1 ? null : (AncientRelicPoolMode)value),
                options: new[]
                {
                    new ModSettingsChoiceOption<int>(-1, ModSettingsText.Literal("Use AP Slot Setting")),
                    new ModSettingsChoiceOption<int>(0, ModSettingsText.Literal("Balanced")),
                    new ModSettingsChoiceOption<int>(1, ModSettingsText.Literal("Chaos")),
                    new ModSettingsChoiceOption<int>(2, ModSettingsText.Literal("True Chaos")),
                },
                description: ModSettingsText.Literal(
                    "Balanced uses the run's Ancient; Chaos uses the act's pool; True Chaos combines Acts 2 and 3. Neow remains Neow-only."));
    }

    private static void ConfigureMultiplayerSection(ModSettingsSectionBuilder section)
    {
        const string key = "multiplayer_player_number";
        section.WithTitle(ModSettingsText.Literal("Multiplayer Settings"))
            .WithDescription(ModSettingsText.Literal(
                "Select the player whose items and checks you own in a shared AP slot. "
                + "Choose a number within the YAML's player_count. People choosing the same number share its AP items and checks. "
                + "Set this before connecting to Archipelago."))
            .AddIntSlider(key, ModSettingsText.Literal("Player Number"),
                CreateBinding(static settings => settings.MultiplayerPlayerNumber,
                    static (settings, value) =>
                    {
                        if (CanChangePlayerNumber())
                        {
                            settings.MultiplayerPlayerNumber = value;
                            LogUtility.Info($"[AP Settings] Player Number set to {value}");
                        }
                    }),
                minValue: 1, maxValue: 4, step: 1,
                valueFormatter: static value => $"Player {value}",
                description: ModSettingsText.Dynamic(() => GetPlayerNumberLockReason()
                    ?? "Choose your player number, then connect to Archipelago."))
            .ConfigureEntryMenu(key, ModSettingsMenuCapabilities.None)
            .WithEntryEnabledWhen(key, CanChangePlayerNumber);
    }

    private static bool CanChangePlayerNumber() => GetPlayerNumberLockReason() == null;

    private static string? GetPlayerNumberLockReason()
    {
        if (GameUtility.IsInRun || MultiplayerSupport.IsMultiplayerScope)
            return "Locked while in a run or multiplayer menu/lobby. Return to the main menu first.";
        if (ArchipelagoClient.HasSlotConnection)
            return "Locked to the selected AP slot. Use Disconnect from Archipelago (or Cancel Connection/Reconnect) "
                + "on the main menu, then reopen these settings.";
        return null;
    }

    private static void ConfigureRelicRewardsSection(ModSettingsSectionBuilder section)
    {
        section
            .WithTitle(ModSettingsText.Literal("Relic Rewards"))
            .WithDescription(
                ModSettingsText.Literal("Configure Relic availability for future runs.")
            )
            .WithMenuCapabilities(ModSettingsMenuCapabilities.None)
            .AddToggle(
                RelicRewards_OverrideId,
                ModSettingsText.Literal("Override AP Relic Availability"),
                CreateBinding(
                    static settings => settings.OverrideRelicRewardsAvailableAnytime,
                    static (settings, value) =>
                    {
                        // Start an override from the slot's value instead of a stale local value.
                        if (value && !settings.OverrideRelicRewardsAvailableAnytime)
                        {
                            settings.RelicRewardsAvailableAnytime =
                                ArchipelagoClient.Settings?.RelicRewardsAvailableAnytime
                                ?? settings.RelicRewardsAvailableAnytime;
                        }

                        settings.OverrideRelicRewardsAvailableAnytime = value;
                    }
                ),
                ModSettingsText.Literal(
                    "Use a local value instead of the one supplied by the Archipelago slot."
                )
            )
            .ConfigureEntryMenu(
                RelicRewards_OverrideId,
                ModSettingsMenuCapabilities.None
            )
            .AddIntSlider(
                RelicRewards_AvailableAnytimeId,
                ModSettingsText.Literal("Relics Available Anytime"),
                CreateBinding(
                    static settings => settings.OverrideRelicRewardsAvailableAnytime
                        ? settings.RelicRewardsAvailableAnytime
                        : ArchipelagoClient.Settings?.RelicRewardsAvailableAnytime
                            ?? settings.RelicRewardsAvailableAnytime,
                    static (settings, value) => settings.RelicRewardsAvailableAnytime = value
                ),
                minValue: 0,
                maxValue: 10,
                step: 1,
                description: ModSettingsText.Literal(
                    "Overrides the AP slot setting for new runs. Does not affect the current run."
                )
            )
            .ConfigureEntryMenu(
                RelicRewards_AvailableAnytimeId,
                ModSettingsMenuCapabilities.None
            )
            .WithEntryEnabledWhen(
                RelicRewards_AvailableAnytimeId,
                IsRelicRewardsOverrideEnabled
            );
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

    private static bool IsRelicRewardsOverrideEnabled()
    {
        var store = RitsuLibFramework.GetDataStore(ModEntry.ModId);
        return store.Get<ClientSettings>("apsettings").OverrideRelicRewardsAvailableAnytime;
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
