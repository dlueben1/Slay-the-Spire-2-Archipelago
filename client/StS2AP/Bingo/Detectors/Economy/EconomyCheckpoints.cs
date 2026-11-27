using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>
/// Broad moments at which every economy state square is checked again. Specific hooks catch
/// most changes as they happen; these catch the rest a little later (a card removed at an event,
/// Max HP lost after a damage animation) and judge a run that was just loaded: loading a run
/// re-enters its room, which fires the room-entered hook with the saved deck, relics and gold.
/// </summary>
internal static class EconomyCheckpoints
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
    private static class AfterRoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, AbstractRoom room) =>
            EconomyDetector.Run(nameof(AfterRoomEntered), (runState, room), static args =>
            {
                (IRunState runState, AbstractRoom room) = args;
                if (EconomyDetector.LocalPlayer(runState) is not { } me)
                    return;
                EconomyDetector.CheckAll(me);
                GoldDetectors.CheckReachedAct2BossWithoutSpending(me, room);
            });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRewardTaken))]
    private static class AfterRewardTaken
    {
        [HarmonyPrefix]
        private static void Prefix(Player player) =>
            EconomyDetector.Run(nameof(AfterRewardTaken), player, static player => EconomyDetector.CheckAll(player));
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    private static class AfterCombatEnd
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState) =>
            EconomyDetector.Run(nameof(AfterCombatEnd), runState, static runState =>
            {
                if (EconomyDetector.LocalPlayer(runState) is { } me)
                    EconomyDetector.CheckAll(me);
            });
    }
}
