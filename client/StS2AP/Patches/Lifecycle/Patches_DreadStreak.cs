#if STS2_AT_LEAST_0_113_0
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Keeps AP Dread runs out of the game's own Dread win streaks. The game counts a streak per
/// character in the player's profile, one for singleplayer and one for multiplayer, and resets it
/// on any Dread loss, then plays the result the next time the Dread screen opens. AP runs have
/// checkpoints, and an abandoned or lost AP run would otherwise wipe a streak the player earned
/// in the base game. Wins, losses and playtime are still recorded, as for every AP run.
/// </summary>
internal static class Patches_DreadStreak
{
    private sealed record CharacterStreaks(
        CharacterStats Stats,
        int SingleplayerCurrent,
        int SingleplayerBestThisVersion,
        string? SingleplayerBestThisVersionTag,
        int SingleplayerBestOldVersion,
        string? SingleplayerBestOldVersionTag,
        int MultiplayerCurrent,
        int MultiplayerBestThisVersion,
        string? MultiplayerBestThisVersionTag,
        int MultiplayerBestOldVersion,
        string? MultiplayerBestOldVersionTag)
    {
        public static CharacterStreaks Of(CharacterStats stats) => new(
            stats,
            stats.CurrentSingleplayerDreadWinStreak,
            stats.BestSingleplayerDreadWinStreakThisVersion,
            stats.BestSingleplayerDreadWinStreakThisVersionTag,
            stats.BestSingleplayerDreadWinStreakOldVersion,
            stats.BestSingleplayerDreadWinStreakOldVersionTag,
            stats.CurrentMultiplayerDreadWinStreak,
            stats.BestMultiplayerDreadWinStreakThisVersion,
            stats.BestMultiplayerDreadWinStreakThisVersionTag,
            stats.BestMultiplayerDreadWinStreakOldVersion,
            stats.BestMultiplayerDreadWinStreakOldVersionTag);

        public void Restore()
        {
            Stats.CurrentSingleplayerDreadWinStreak = SingleplayerCurrent;
            Stats.BestSingleplayerDreadWinStreakThisVersion = SingleplayerBestThisVersion;
            Stats.BestSingleplayerDreadWinStreakThisVersionTag = SingleplayerBestThisVersionTag;
            Stats.BestSingleplayerDreadWinStreakOldVersion = SingleplayerBestOldVersion;
            Stats.BestSingleplayerDreadWinStreakOldVersionTag = SingleplayerBestOldVersionTag;
            Stats.CurrentMultiplayerDreadWinStreak = MultiplayerCurrent;
            Stats.BestMultiplayerDreadWinStreakThisVersion = MultiplayerBestThisVersion;
            Stats.BestMultiplayerDreadWinStreakThisVersionTag = MultiplayerBestThisVersionTag;
            Stats.BestMultiplayerDreadWinStreakOldVersion = MultiplayerBestOldVersion;
            Stats.BestMultiplayerDreadWinStreakOldVersionTag = MultiplayerBestOldVersionTag;
        }
    }

    private sealed record StreakSnapshot(
        List<CharacterStreaks> Characters,
        PendingDreadWinStreakResult? SingleplayerPendingResult,
        PendingDreadWinStreakResult? MultiplayerPendingResult);

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
            if (serializableRun.GameMode != GameMode.Dread)
                return;
            bool isApRun = serializableRun.Players.Count == 1
                ? ApSingleplayerSaves.IsHandlingSingleplayerRun
                : MultiplayerSupport.IsRealMultiplayerRun;
            if (!isApRun)
                return;

            // AP runs never set the rotating character, so the streak the game is about to change
            // belongs to one of the run's own characters.
            List<CharacterStreaks> characters = serializableRun.Players
                .Select(player => player.CharacterId)
                .OfType<ModelId>()
                .Distinct()
                .Select(id => CharacterStreaks.Of(__instance.Progress.GetOrCreateCharacterStats(id)))
                .ToList();
            __state = new StreakSnapshot(
                characters,
                __instance.Progress.PendingSingleplayerDreadWinStreakResult,
                __instance.Progress.PendingMultiplayerDreadWinStreakResult
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ProgressSaveManager __instance, StreakSnapshot? __state)
        {
            if (__state == null)
                return;

            foreach (CharacterStreaks character in __state.Characters)
                character.Restore();
            __instance.Progress.PendingSingleplayerDreadWinStreakResult = __state.SingleplayerPendingResult;
            __instance.Progress.PendingMultiplayerDreadWinStreakResult = __state.MultiplayerPendingResult;
            // The method has already written the profile once with the changed streak.
            __instance.SaveProgress();
            LogUtility.Info("Left the base game's Dread win streak unchanged after an AP Dread run.");
        }
    }
}
#endif
