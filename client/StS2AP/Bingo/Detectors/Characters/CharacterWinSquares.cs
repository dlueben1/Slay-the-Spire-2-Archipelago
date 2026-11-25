using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>
/// "Win as X" squares (C-IR-H2, C-SI-H1, C-DE-H2, C-RE-H3, C-NE-H4, C-FO-H2): a run victory
/// while this client's player is playing that character.
/// </summary>
internal static class CharacterWinSquares
{
    /// <summary>The "Win as X" square for a character, or null for characters without one.</summary>
    private static string? WinSquareFor(CharacterModel character) => character switch
    {
        Ironclad => "C-IR-H2",
        Silent => "C-SI-H1",
        Defect => "C-DE-H2",
        Regent => "C-RE-H3",
        Necrobinder => "C-NE-H4",
#if STS2_AT_LEAST_0_114_0
        Fool => "C-FO-H2",
#endif
        _ => null,
    };

    // RunManager.OnEnded(isVictory: true) is the run win, the same call the character goal's
    // victory patch watches (Patches_Victory). Every multiplayer client ends the shared run, so
    // each one credits its own player.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), new[] { typeof(bool) })]
    private static class RunEnded
    {
        [HarmonyPostfix]
        private static void Postfix(RunManager __instance, bool isVictory)
        {
            if (!isVictory || !BingoSquares.IsActive)
                return;

            CharacterSquares.Guard($"{nameof(CharacterWinSquares)}.{nameof(RunEnded)}", () =>
            {
                if (__instance.State is not { } runState)
                    return;

                Player? me = LocalContext.GetMe(runState.Players);
                string? key = me == null ? null : WinSquareFor(me.Character);
                if (key != null && BingoSquares.IsOpen(key))
                    BingoSquares.Complete(key, me);
            });
        }
    }
}
