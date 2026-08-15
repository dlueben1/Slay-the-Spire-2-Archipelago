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

    // DeclaredMethod is intentional: beta inherits AbstractModel.AfterCombatEnd but no longer
    // overrides it. Treating that inherited no-op as the public hook would patch every model.
    private static readonly MethodInfo? s_afterCombatEndMethod =
        AccessTools.DeclaredMethod(typeof(LastingCandy), nameof(LastingCandy.AfterCombatEnd));

    private static readonly MethodInfo? s_beforeCombatRewardOfferedMethod =
        AccessTools.DeclaredMethod(typeof(LastingCandy), "BeforeCombatRewardOffered");

    private static readonly PropertyInfo? s_rewardCountProperty =
        AccessTools.Property(typeof(LastingCandy), nameof(LastingCandy.CombatsSeen))
        ?? AccessTools.Property(typeof(LastingCandy), "CombatRewardsSeen");

    private static readonly MethodInfo? s_invokeDisplayAmountChangedMethod =
        AccessTools.Method(typeof(RelicModel), "InvokeDisplayAmountChanged");

    private static bool s_missingRewardStateLogged;

    private static bool UsesLegacyCombatCadence =>
        s_afterCombatEndMethod != null;

    private static bool CanUseRewardCadence =>
        s_hasBeenRerolledField != null
        && s_optionsProperty != null
        && s_doActivateVisualsMethod != null
        && s_rewardCountProperty != null
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

    private static void AdvanceRewardCadence(LastingCandy lastingCandy)
    {
        int rewardsSeen = (int)s_rewardCountProperty!.GetValue(lastingCandy)! + 1;
        s_rewardCountProperty.SetValue(lastingCandy, rewardsSeen);

        bool isTriggeringReward = rewardsSeen > 0 && rewardsSeen % 2 == 0;

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

    [HarmonyPatch]
    public static class SuppressCombatCadenceForArchipelago
    {
        // Beta replaced AfterCombatEnd with BeforeCombatRewardOffered. Patch whichever native
        // cadence hook exists so AP card-reward population remains the single counting boundary.
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() =>
            s_afterCombatEndMethod
            ?? s_beforeCombatRewardOfferedMethod
            ?? throw new MissingMethodException(
                typeof(LastingCandy).FullName,
                "AfterCombatEnd or BeforeCombatRewardOffered"
            );

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
        private static void Prefix(CardReward __instance, out LastingCandy? __state)
        {
            __state = null;

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

                if (!UsesLegacyCombatCadence)
                {
                    // Beta checks CombatRewardsSeen before its former native increment. Advance in
                    // the postfix so every second reward still sees the triggering odd value here.
                    __state = lastingCandy;
                    return;
                }

                // Public checks CombatsSeen during Populate, so advance before its creation hook.
                AdvanceRewardCadence(lastingCandy);

                LogUtility.Info(
                    $"Lasting Candy advanced to {s_rewardCountProperty!.GetValue(lastingCandy)} "
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

        [HarmonyPostfix]
        private static void Postfix(LastingCandy? __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                AdvanceRewardCadence(__state);
                LogUtility.Info(
                    $"Lasting Candy advanced to {s_rewardCountProperty!.GetValue(__state)} "
                        + "for an Encounter card reward"
                );
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Failed to advance Lasting Candy for an Encounter card reward: {ex.Message}"
                );
            }
        }
    }
}
