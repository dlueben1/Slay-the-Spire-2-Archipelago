using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Models;
using StS2AP.Utils;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using System;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;

namespace StS2AP
{
    [ModInitializer("Initialize")]
    public class ModEntry
    {
        public const string ModId = "Archipelago";
        private static string? _modDirectory;

        public static void Initialize()
        {
            /// Register assembly resolver FIRST, before any other code runs
            /// This ensures dependencies like Archipelago.MultiClient.Net can be found
            RegisterAssemblyResolver();

            // Initialize debug console first so we can see log output
            ConsoleLogger.Initialize();

            // Register unhandled exception handler to log crashes before app closes
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            LogUtility.Info("Archipelago mod initializing...");

            /// This lets StS know that there's a property on the Death Link Curse that needs to be saved/loaded with the save system. 
            /// This is done by injecting the type into the SavedPropertiesTypeCache.
            /// 
            /// This also might change when we start using BaseLib. It probably makes this easier.
            SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(DeathLinkCurse));

            // Register with RitsuLib
            var assembly = Assembly.GetExecutingAssembly();
            ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);
            BuildModSettings();
            using (RitsuLibFramework.BeginModDataRegistration(ModId))
            {
                var store = RitsuLibFramework.GetDataStore(ModId);
                store.Register(
                    key: "apsettings",
                    fileName: "apsettings.json",
                    scope: SaveScope.Global,
                    defaultFactory: () => new ClientSettings(),
                    autoCreateIfMissing: true);
            }

            // Apply all Harmony Patches
            try
            {
                var harmony = new Harmony("archipelago.patch");

                /// VERY IMPORTANT: For `PatchAll()` to work, we need to use nested classes like we're using in the `Patches` directory.
                /// The syntax is somewhat ugly, but it's easier to maintain this way since we don't have to patch by category/individually.
                harmony.PatchAll();
                LogUtility.Success("Harmony patches applied successfully.");
                LogUtility.Info("Archipelago mod initialized.");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to apply Harmony patches: {ex.Message}");
            }

            // Register cleanup when the application exits
            AppDomain.CurrentDomain.ProcessExit += (s, e) => ConsoleLogger.Shutdown();
        }

        /// <summary>
        /// Registers a custom assembly resolver to find DLLs in the mod's directory.
        /// This is necessary because Godot's runtime doesn't automatically search the mod folder.
        /// </summary>
        private static void RegisterAssemblyResolver()
        {
            // Get the directory where this mod's DLL is located
            var assembly = Assembly.GetExecutingAssembly();
            _modDirectory = Path.GetDirectoryName(assembly.Location);

            // Register the resolver for the default AssemblyLoadContext
            AssemblyLoadContext.Default.Resolving += OnAssemblyResolve;
        }

        /// <summary>
        /// Called when the runtime can't find an assembly. We check the mod directory.
        /// </summary>
        private static Assembly? OnAssemblyResolve(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(_modDirectory) || string.IsNullOrEmpty(assemblyName.Name))
                return null;

            // Try to find the assembly in the mod directory
            var assemblyPath = Path.Combine(_modDirectory, $"{assemblyName.Name}.dll");
            
            if (File.Exists(assemblyPath))
            {
                try
                {
                    return context.LoadFromAssemblyPath(assemblyPath);
                }
                catch
                {
                    // If loading fails, return null to let other resolvers try
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Handles unhandled exceptions by logging them before the application terminates.
        /// </summary>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogUtility.Error("=== UNHANDLED EXCEPTION ===");
                    LogUtility.Error($"Exception Type: {ex.GetType().FullName}");
                    LogUtility.Error($"Message: {ex.Message}");
                    LogUtility.Error($"Stack Trace:\n{ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        LogUtility.Error($"Inner Exception: {ex.InnerException.GetType().FullName}");
                        LogUtility.Error($"Inner Message: {ex.InnerException.Message}");
                        LogUtility.Error($"Inner Stack Trace:\n{ex.InnerException.StackTrace}");
                    }
                    
                    LogUtility.Error($"Is Terminating: {e.IsTerminating}");
                    LogUtility.Error("=== END UNHANDLED EXCEPTION ===");
                }
                else
                {
                    LogUtility.Error($"Unhandled exception (non-Exception type): {e.ExceptionObject}");
                }

                // Flush the console output to ensure everything is written
                Console.Out.Flush();
                Console.Error.Flush();
            }
            catch
            {
                // If logging fails, at least try to write something to standard output
                Console.Error.WriteLine($"CRITICAL: Failed to log unhandled exception: {e.ExceptionObject}");
            }
        }

        #region Mod Settings

        /// <summary>
        /// NOTE: We may want to move this to a global function that can be used for other parts of the codebase to get 
        /// whether death link overriding is enabled or not, but for now, it's just for this mod settings option.
        /// </summary>
        private static bool IsDeathLinkOverriden()
        {
            var store = RitsuLibFramework.GetDataStore(ModId);
            var settings = store.Get<ClientSettings>("apsettings");
            return settings.OverrideDeathLinkOptions;
        }

        /// <summary>
        /// Builds the Settings Page for our Archipelago mod
        /// </summary>
        private static void BuildModSettings()
        {
            RitsuLibFramework.RegisterModSettings(ModId, page => page
            .WithTitle(ModSettingsText.Literal("AP Settings"))
            .WithModDisplayName(ModSettingsText.Literal("Archipelago"))
            .AddSection("notifications", section => section
                .WithTitle(ModSettingsText.Literal("Notifications"))
                .AddChoice("reward_notifications", ModSettingsText.Literal("Reward Notifications"), 
                    new ModSettingsValueBinding<ClientSettings, string>(
                        ModId, "apsettings", SaveScope.Global, s => s.RewardNotificationPref, (s, value) => s.RewardNotificationPref = value),
                    new STS2RitsuLib.Settings.ModSettingsChoiceOption<string>[]
                    {
                        new("All", ModSettingsText.Literal("All")),
                        new("My Checks & Items", ModSettingsText.Literal("My Checks & Items")),
                        new("Only My Checks", ModSettingsText.Literal("Only My Checks"))
                    }))
            .AddSection("deathlink", section => section
                .WithTitle(ModSettingsText.Literal("Death Link"))
                .AddToggle("override_deathlink", ModSettingsText.Literal("Use Custom Death Link Settings"), 
                    new ModSettingsValueBinding<ClientSettings, bool>(
                        ModId, "apsettings", SaveScope.Global, s => s.OverrideDeathLinkOptions, (s, value) => s.OverrideDeathLinkOptions = value),
                    ModSettingsText.Literal("If enabled, Death Link settings will be controlled by this mod's configuration rather than the Server's Slot Data (i.e. your YAML's settings)."))
                .AddToggle(
                    "enable_deathlink", 
                    ModSettingsText.Literal("Enable Death Link"), 
                    new ModSettingsValueBinding<ClientSettings, bool>(
                        ModId, "apsettings", SaveScope.Global, s => s.EnableDeathLink, (s, value) => s.EnableDeathLink = value),
                    ModSettingsText.Literal("Opts in/out of Death Link")
                    ).WithEntryEnabledWhen("enable_deathlink", IsDeathLinkOverriden)
                .AddToggle(
                    "enable_death_fragments", 
                    ModSettingsText.Literal("Enable Death Fragments"), 
                    new ModSettingsValueBinding<ClientSettings, bool>(
                        ModId, "apsettings", SaveScope.Global, s => s.EnableDeathFragments, (s, value) => s.EnableDeathFragments = value),
                    ModSettingsText.Literal("If enabled, you will receive a special curse when a death link is received.")
                    ).WithEntryEnabledWhen("enable_death_fragments", IsDeathLinkOverriden)
                .AddIntSlider(
                    "deathlink_damage", 
                    ModSettingsText.Literal("Death Link % Damage"), 
                    new ModSettingsValueBinding<ClientSettings, int>(
                        ModId, "apsettings", SaveScope.Global, s => s.DeathLinkPercentDamage, (s, value) => s.DeathLinkPercentDamage = value),
                    0, 100,
                    description: ModSettingsText.Literal("The percentage of your max health that will be lost when a death link is received.")
                    ).WithEntryEnabledWhen("deathlink_damage", IsDeathLinkOverriden)));
        }

        #endregion
    }
}
