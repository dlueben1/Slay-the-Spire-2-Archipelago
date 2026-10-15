using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Collection of Patches that apply when a run is won
    /// </summary>
    public static class Patches_Victory
    {
        /// <summary>
        /// Runs when a run is won. Used to update Goal Progress and release any remaining checks for a given character.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), new Type[] {typeof(bool)})]
        public static class OnEnded
        {
            [HarmonyPostfix]
            public static void Postfix(bool isVictory)
            {
                if (isVictory
                    && RunManager.Instance.NetService.Type
                        == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
                {
                    ApMultiplayerCampaignStore.MarkCurrentCampaignCompleted();
                }

                if (!isVictory
                    || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.VictoryChecks))
                    return;

                Player? player = GameUtility.CurrentPlayer;
                if (player == null)
                {
                    LogUtility.Warn("Skipping AP victory handling because there is no active player");
                    return;
                }

                // Every multiplayer replica ends the shared run. Only this process's own AP
                // player may write victory progress to this process's AP connection.
                if (MultiplayerSupport.IsRealMultiplayerRun
                    && (!MultiplayerSupport.IsLocalOwnApSlot
                        || !MultiplayerLocationChecks.IsLocalProgressOwner(player)))
                {
                    return;
                }

                // Capture the player before deferring. Returning to the menu clears
                // GameUtility.CurrentPlayer, but the victory work still belongs to this player.
                Callable.From(() =>
                {
                    _ = GameUtility.TrySetGoalAchieved(player);
                }).CallDeferred();
            }
        }
    }
}
