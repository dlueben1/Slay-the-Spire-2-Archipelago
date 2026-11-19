using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Starts the next new run on the seed set with the <c>ap_seed</c> console command.
/// </summary>
internal static class Patches_RunSeedOverride
{
    /// <summary>
    /// Every new run, singleplayer or hosted, picks its seed in this method, and the game reads
    /// its own debug seed override there first. So the pending seed is lent to that override for
    /// the length of the call: the lobby then uses it for everything it derives from the seed
    /// (the act list, the message to guests, the run itself), and no other run inherits it.
    /// </summary>
    [HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.BeginRunForAllPlayersIfAllReady))]
    private static class UsePendingSeed
    {
        [HarmonyPrefix]
        private static void Prefix(StartRunLobby __instance, out string? __state)
        {
            // The game's own override before the call, to put back afterwards; null when this
            // patch did nothing.
            __state = null;
            if (RunSeedOverride.Pending == null
                || NGame.Instance is not { } game
                // A guest takes the seed from the host's message.
                || __instance.NetService.Type == NetGameType.Client
                || !__instance.IsAboutToBeginGame()
                || RunSeedOverride.BeginLaunch() is not { } seed)
            {
                return;
            }

            __state = game.DebugSeedOverride ?? "";
            game.DebugSeedOverride = seed;
        }

        [HarmonyPostfix]
        private static void Postfix(string? __state, bool __runOriginal)
        {
            if (__state == null || NGame.Instance is not { } game)
                return;

            game.DebugSeedOverride = __state.Length == 0 ? null : __state;
            // Another prefix can still refuse the launch, and then the seed waits for the next try.
            if (__runOriginal)
                LogUtility.Info($"Starting the run on the seed set with ap_seed: {RunSeedOverride.Pending}");
            RunSeedOverride.FinishLaunch(launched: __runOriginal);
        }
    }
}
