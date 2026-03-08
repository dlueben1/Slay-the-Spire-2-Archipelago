using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using StS2AP.Utils;
using System;

namespace StS2AP
{
    [ModInitializer("Initialize")]
    public class ModEntry
    {
        public static void Initialize()
        {
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
    }
}
