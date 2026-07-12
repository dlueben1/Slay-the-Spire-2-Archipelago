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
            ModSettingsRegistration.Register();
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

            // Initialize Utilities
            DeathLinkUtility.Initialize();

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
    }
}
