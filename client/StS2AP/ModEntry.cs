using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using StS2AP.Utils;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Utils.Persistence;

namespace StS2AP
{
    [ModInitializer("Initialize")]
    public class ModEntry
    {
        public const string ModId = "Archipelago";

        public static void Initialize()
        {
            // Bootstrap runs before this method and already tells .NET where to find
            // our dependency DLLs. It loads them alongside the game and this mod.

            // Register unhandled exception handler to log crashes before app closes
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            LogUtility.Info("Archipelago mod initializing...");

            // Register with RitsuLib
            var assembly = typeof(ModEntry).Assembly;
            ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);
            using (RitsuLibFramework.BeginModDataRegistration(ModId))
            {
                var store = RitsuLibFramework.GetDataStore(ModId);
                store.Register(
                    key: "apsettings",
                    fileName: "apsettings.json",
                    scope: SaveScope.Global,
                    defaultFactory: () => new ClientSettings(),
                    autoCreateIfMissing: true
                );
                ArchipelagoReward.Initialize();
                ApRunData.Initialize();
            }
            ModSettingsRegistration.Register();
            ApTelemetry.Initialize();
            ApGameplayTelemetry.Initialize();

            // Initialize Utilities
            ApMirroredRewardDispatcher.Initialize();
            RelicReceiptMultiplayer.Initialize();
            ProgressiveStarterMultiplayer.Initialize();
            AscensionMultiplayer.Initialize();
            DeathLinkUtility.Initialize();
            BuffUtility.Initialize();

            // Apply all Harmony Patches
            try
            {
                var harmony = new Harmony("archipelago.patch");

                /// VERY IMPORTANT: For `PatchAll()` to work, we need to use nested classes like we're using in the `Patches` directory.
                /// The syntax is somewhat ugly, but it's easier to maintain this way since we don't have to patch by category/individually.
                harmony.PatchAll(assembly);
                ApTelemetry.RecordPatchResult();
                LogUtility.Success("Harmony patches applied successfully.");
                LogUtility.Info("Archipelago mod initialized.");
            }
            catch (Exception ex)
            {
                ApTelemetry.RecordPatchResult(ex);
                LogUtility.Error($"Failed to apply Harmony patches: {ex.Message}");
            }
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
                        LogUtility.Error(
                            $"Inner Exception: {ex.InnerException.GetType().FullName}"
                        );
                        LogUtility.Error($"Inner Message: {ex.InnerException.Message}");
                        LogUtility.Error($"Inner Stack Trace:\n{ex.InnerException.StackTrace}");
                    }

                    LogUtility.Error($"Is Terminating: {e.IsTerminating}");
                    LogUtility.Error("=== END UNHANDLED EXCEPTION ===");
                }
                else
                {
                    LogUtility.Error(
                        $"Unhandled exception (non-Exception type): {e.ExceptionObject}"
                    );
                }

                // Flush the console output to ensure everything is written
                Console.Out.Flush();
                Console.Error.Flush();
            }
            catch
            {
                // If logging fails, at least try to write something to standard output
                Console.Error.WriteLine(
                    $"CRITICAL: Failed to log unhandled exception: {e.ExceptionObject}"
                );
            }
        }
    }
}
