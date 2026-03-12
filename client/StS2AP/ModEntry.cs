using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using StS2AP.Utils;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace StS2AP
{
    [ModInitializer("Initialize")]
    public class ModEntry
    {
        private static string? _modDirectory;

        public static void Initialize()
        {
            // Register assembly resolver FIRST, before any other code runs
            // This ensures dependencies like Archipelago.MultiClient.Net can be found
            RegisterAssemblyResolver();

            // Initialize debug console first so we can see log output
            ConsoleLogger.Initialize();

            LogUtility.Info("Archipelago mod initializing...");

            try
            {
                var harmony = new Harmony("archipelago.patch");
                harmony.PatchAll();
                LogUtility.Success("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to apply Harmony patches: {ex.Message}");
            }

            LogUtility.Info("Archipelago mod initialized.");

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
    }
}
