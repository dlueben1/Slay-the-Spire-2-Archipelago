using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Native reward messages start independent tasks. Keep AP selections for one player ordered
/// through their awaited callbacks, so a following card reveal sees the preceding relic grant.
/// Nested native rewards remain native and can finish while their parent AP action awaits them.
/// </summary>
[HarmonyPatch(typeof(Reward), nameof(Reward.SelectUnsynchronized))]
internal static class Patches_APRewardSelectionOrder
{
    private static readonly ConditionalWeakTable<Player, ApRewardSelectionQueue> Queues = new();
    [ThreadStatic] private static Reward? s_entering;

    internal static void Reset() => Queues.Clear();

    internal static Task WhenIdle(Player player) =>
        Queues.TryGetValue(player, out var queue) ? queue.WhenIdle() : Task.CompletedTask;

    [HarmonyPrefix]
    private static bool Prefix(Reward __instance, ref Task<bool> __result)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || __instance is not ApMirroredRewardDispatcher.IApNativeReward
            || ReferenceEquals(s_entering, __instance))
            return true;

        var reward = __instance;
        var run = reward.Player.RunState;
        __result = ObserveSelection(Queues.GetValue(reward.Player, _ => new ApRewardSelectionQueue()).Run(() =>
        {
            if (RunManager.Instance.DebugOnlyGetState() != run || MultiplayerSupport.ClaimsInvalidated)
                throw new OperationCanceledException("AP reward selection belongs to an inactive run.");
            Reward? previous = s_entering;
            s_entering = reward;
            try
            {
                // Bypass only this immediate invocation, never the async lifetime of the action.
                return reward.SelectUnsynchronized();
            }
            finally
            {
                s_entering = previous;
            }
        }), reward);
        return false;
    }

    private static async Task<bool> ObserveSelection(Task<bool> selection, Reward reward)
    {
        try
        {
            return await selection;
        }
        catch (Exception ex)
        {
            if (RunManager.Instance.DebugOnlyGetState() == reward.Player.RunState)
                MultiplayerSupport.InvalidateRunClaims("An AP reward selection failed; reload the campaign.");
            LogUtility.Error($"Ordered AP reward selection failed for player {reward.Player.NetId}: {ex}");
            throw;
        }
    }
}
