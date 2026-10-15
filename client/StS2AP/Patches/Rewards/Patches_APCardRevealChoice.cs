using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace StS2AP.Patches;

/// <summary>
/// Bind the first native picker choice to the ID reserved before an AP reveal awaits network data.
/// Otherwise a later reward click could reserve IDs ahead of the delayed picker's first choice.
/// </summary>
internal static class Patches_APCardRevealChoice
{
    private sealed class Reservation(PlayerChoiceSynchronizer synchronizer, Player player, uint id)
    {
        internal readonly PlayerChoiceSynchronizer Synchronizer = synchronizer;
        internal readonly Player Player = player;
        internal readonly uint Id = id;
        internal bool Used;
    }

    [ThreadStatic]
    private static Reservation? s_reservation;

    internal static Task<bool> Select(
        PlayerChoiceSynchronizer synchronizer, Player player, uint id, Func<Task<bool>> select)
    {
        var previous = s_reservation;
        var reservation = new Reservation(synchronizer, player, id);
        s_reservation = reservation;
        try
        {
            // Native OnSelect reserves its first choice synchronously, before its first await.
            // End the scope as soon as it returns the Task; subsequent choices reserve normally.
            Task<bool> selection = select();
            if (!reservation.Used)
                throw new InvalidOperationException("Native card picker did not use its reserved AP choice.");
            return selection;
        }
        finally
        {
            s_reservation = previous;
        }
    }

    [HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.ReserveChoiceId))]
    private static class UseReservedFirstChoice
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerChoiceSynchronizer __instance, Player player, ref uint __result)
        {
            Reservation? reservation = s_reservation;
            if (reservation == null || reservation.Used
                || reservation.Synchronizer != __instance || reservation.Player != player)
                return true;
            reservation.Used = true;
            __result = reservation.Id;
            return false;
        }
    }
}
