using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Makes Lasting Candy count initial Encounter card rewards instead of completed combats
/// during an Archipelago run. This includes both normal and Archipelago card rewards.
/// Note this is slightly different than base game behaviour
/// </summary>
public static class Patches_LastingCandy
{
    private static readonly FieldInfo? s_hasBeenRerolledField =
        AccessTools.Field(typeof(CardReward), "_hasBeenRerolled");

    private static readonly PropertyInfo? s_optionsProperty =
        AccessTools.Property(typeof(CardReward), "Options");

    private static readonly MethodInfo? s_doActivateVisualsMethod =
        AccessTools.Method(typeof(LastingCandy), "DoActivateVisuals");

    private static readonly MethodInfo? s_invokeDisplayAmountChangedMethod =
        AccessTools.Method(typeof(RelicModel), "InvokeDisplayAmountChanged");

    private static bool s_missingRewardStateLogged;

    private static bool CanUseRewardCadence =>
        s_hasBeenRerolledField != null
        && s_optionsProperty != null
        && s_doActivateVisualsMethod != null
        && s_invokeDisplayAmountChangedMethod != null;

    private static bool IsOwnedByCurrentArchipelagoPlayer(LastingCandy lastingCandy)
    {
        var currentPlayer = GameUtility.CurrentPlayer;
        return currentPlayer != null && ReferenceEquals(lastingCandy.Owner, currentPlayer);
    }

    private static void LogMissingRewardStateOnce()
    {
        if (s_missingRewardStateLogged)
        {
            return;
        }

        s_missingRewardStateLogged = true;
        LogUtility.Warn(
            "Could not access the game state needed for Lasting Candy compatibility; "
                + "falling back to the base game's combat cadence"
        );
    }

    private static void UpdateCounterVisuals(LastingCandy lastingCandy)
    {
        bool isTriggeringReward =
            lastingCandy.CombatsSeen > 0 && lastingCandy.CombatsSeen % 2 == 0;

        try
        {
            if (isTriggeringReward)
            {
                if (s_doActivateVisualsMethod?.Invoke(lastingCandy, null) is Task visualTask)
                {
                    // This is the same asynchronous flash/display sequence used by Lasting Candy's
                    // original AfterCombatEnd implementation.
                    TaskHelper.RunSafely(visualTask);
                }
                else
                {
                    lastingCandy.Flash();
                }
            }

            if (s_invokeDisplayAmountChangedMethod != null)
            {
                s_invokeDisplayAmountChangedMethod.Invoke(lastingCandy, null);
            }
        }
        catch (Exception ex)
        {
            // Visual compatibility is best-effort and must not prevent reward generation.
            LogUtility.Warn(
                $"Failed to update Lasting Candy's reward counter visuals: {ex.Message}"
            );
        }
    }

    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.AfterCombatEnd))]
    public static class SuppressCombatCadenceForArchipelago
    {
        [HarmonyPrefix]
        private static bool Prefix(LastingCandy __instance, ref Task __result)
        {
            if (!IsOwnedByCurrentArchipelagoPlayer(__instance))
            {
                return true;
            }

            if (!CanUseRewardCadence)
            {
                LogMissingRewardStateOnce();
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    public static class AdvanceCadenceForEncounterCardRewards
    {
        [HarmonyPrefix]
        private static void Prefix(CardReward __instance)
        {
            try
            {
                var currentPlayer = GameUtility.CurrentPlayer;
                if (currentPlayer == null || !ReferenceEquals(__instance.Player, currentPlayer))
                {
                    return;
                }

                if (!CanUseRewardCadence)
                {
                    LogMissingRewardStateOnce();
                    return;
                }

                // Already-populated rewards include AP assignments reopened from the cache or
                // restored from a save. Rerolls clear their cards before calling Populate again,
                // so they need a separate guard.
                if (__instance.IsPopulated
                    || s_hasBeenRerolledField!.GetValue(__instance) is not false
                    || s_optionsProperty!.GetValue(__instance) is not CardCreationOptions options
                    || options.Source != CardCreationSource.Encounter)
                {
                    return;
                }

                var lastingCandy = __instance.Player.Relics.OfType<LastingCandy>().FirstOrDefault();
                if (lastingCandy == null || !IsOwnedByCurrentArchipelagoPlayer(lastingCandy))
                {
                    return;
                }

                // Advance before CardReward.Populate runs so its normal creation hook sees the
                // new count and adds the Power option on every second Encounter reward.
                lastingCandy.CombatsSeen++;
                UpdateCounterVisuals(lastingCandy);

                LogUtility.Info(
                    $"Lasting Candy advanced to {lastingCandy.CombatsSeen} "
                        + "for an Encounter card reward"
                );
            }
            catch (Exception ex)
            {
                // Compatibility failure must not prevent the underlying reward from populating.
                LogUtility.Warn(
                    $"Failed to advance Lasting Candy for an Encounter card reward: {ex.Message}"
                );
            }
        }
    }
}
