#if STS2_AT_LEAST_0_113_0
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Keeps AP Dread runs out of the game's own Dread win streaks. The game counts a streak per
/// character in the player's profile and resets it on any Dread loss, then plays the result the
/// next time the Dread screen opens. AP runs have checkpoints and Ascension Down items, and an
/// abandoned or lost AP run would otherwise wipe a streak the player earned in the base game.
/// Wins, losses and playtime are still recorded, as for every AP run.
/// </summary>
internal static class Patches_DreadStreak
{
    private sealed record StreakSnapshot(
        CharacterStats Stats,
        int Current,
        int BestThisVersion,
        string? BestThisVersionTag,
        int BestOldVersion,
        string? BestOldVersionTag,
        PendingDreadWinStreakResult? PendingResult);

    [HarmonyPatch(typeof(ProgressSaveManager), nameof(ProgressSaveManager.UpdateWithRunData))]
    private static class KeepApRunsOutOfDreadStreaks
    {
        [HarmonyPrefix]
        private static void Prefix(
            ProgressSaveManager __instance,
            SerializableRun serializableRun,
            out StreakSnapshot? __state)
        {
            __state = null;
            // AP singleplayer runs never set the rotating character, so the streak the game is
            // about to change belongs to the run's own character.
            if (serializableRun.GameMode != GameMode.Dread
                || serializableRun.Players.Count != 1
                || !ApSingleplayerSaves.IsHandlingSingleplayerRun)
            {
                return;
            }

            if (serializableRun.Players[0].CharacterId is not { } characterId)
                return;

            CharacterStats stats = __instance.Progress.GetOrCreateCharacterStats(characterId);
            __state = new StreakSnapshot(
                stats,
                stats.CurrentSingleplayerDreadWinStreak,
                stats.BestSingleplayerDreadWinStreakThisVersion,
                stats.BestSingleplayerDreadWinStreakThisVersionTag,
                stats.BestSingleplayerDreadWinStreakOldVersion,
                stats.BestSingleplayerDreadWinStreakOldVersionTag,
                __instance.Progress.PendingSingleplayerDreadWinStreakResult
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ProgressSaveManager __instance, StreakSnapshot? __state)
        {
            if (__state == null)
                return;

            CharacterStats stats = __state.Stats;
            stats.CurrentSingleplayerDreadWinStreak = __state.Current;
            stats.BestSingleplayerDreadWinStreakThisVersion = __state.BestThisVersion;
            stats.BestSingleplayerDreadWinStreakThisVersionTag = __state.BestThisVersionTag;
            stats.BestSingleplayerDreadWinStreakOldVersion = __state.BestOldVersion;
            stats.BestSingleplayerDreadWinStreakOldVersionTag = __state.BestOldVersionTag;
            __instance.Progress.PendingSingleplayerDreadWinStreakResult = __state.PendingResult;
            // The method has already written the profile once with the changed streak.
            __instance.SaveProgress();
            LogUtility.Info("Left the base game's Dread win streak unchanged after an AP Dread run.");
        }
    }
}
#endif
