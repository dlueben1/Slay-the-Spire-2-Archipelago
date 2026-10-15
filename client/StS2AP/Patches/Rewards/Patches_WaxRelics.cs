using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>Owns the wax cadence for AP players with bonus wax configured, including their Toy Box relics.</summary>
internal static class Patches_WaxRelics
{
    private const int CombatsBetweenMelts = 3;

    private static bool ControlsWax(Player? player) => player != null
        && ReferenceEquals(player.RunState, RunManager.Instance.DebugOnlyGetState())
        && MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.BonusItems)
        && ApPlayerContextResolver.TryGetRewardSettings(player, out var settings)
        && settings.BonusItemsFor(BonusItemDefinition.WaxRelicCategory).Count > 0;

    private static async Task AfterCombat(Task nativeTask, IRunState runState)
    {
        await nativeTask;
        foreach (Player player in runState.Players)
        {
            if (!ControlsWax(player))
                continue;
            ApPlayerRunState? replica = null;
            if (MultiplayerSupport.IsRealMultiplayerRun)
            {
                if (runState is not RunState concrete ||
                    !ApRunData.TryGetPlayerState(concrete, player.NetId, out replica))
                    continue;
            }
            int count = replica?.CombatsSinceLastWaxMelt ?? ArchipelagoClient.Progress.CombatsSinceLastWaxMelt;
            RelicModel? wax = player.Relics.FirstOrDefault(relic => relic.IsWax && !relic.IsMelted);
            count = wax == null ? 0 : count + 1;
            if (count >= CombatsBetweenMelts && wax != null)
            {
                try
                {
                    await RelicCmd.Melt(wax);
                    count = 0;
                    LogUtility.Info($"Melted AP wax relic {wax.Id}: player={player.NetId}");
                    await Cmd.CustomScaledWait(0.5f, 0.75f);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Wax melt failed: player={player.NetId}, relic={wax.Id}, combatCount={count}. {ex}");
                }
            }
            if (replica != null)
                ApRunData.SetWaxCombatCount((RunState)runState, player.NetId, count);
            else
                ArchipelagoClient.Progress.CombatsSinceLastWaxMelt = count;
        }
    }

    [HarmonyPatch(typeof(ToyBox), nameof(ToyBox.AfterCombatEnd))]
    private static class SuppressNativeMelt
    {
        [HarmonyPrefix]
        private static bool Prefix(ToyBox __instance, ref Task __result)
        {
            if (!ControlsWax(__instance.Owner))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(ToyBox), nameof(ToyBox.ShowCounter), MethodType.Getter)]
    private static class HideManagedCounter
    {
        [HarmonyPostfix]
        private static void Postfix(ToyBox __instance, ref bool __result)
        {
            if (__instance.IsMutable && ControlsWax(__instance.Owner))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd),
        new[] { typeof(IRunState), typeof(ICombatState), typeof(CombatRoom) })]
    private static class MeltAtCombatEnd
    {
        [HarmonyPostfix]
        private static void Postfix(IRunState runState, ref Task __result) =>
            __result = AfterCombat(__result, runState);
    }
}
