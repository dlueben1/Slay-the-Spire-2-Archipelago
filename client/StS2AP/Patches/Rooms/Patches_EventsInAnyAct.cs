using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;

namespace StS2AP.Patches;

/// <summary>
/// The Events in Any Act option. The game limits events to acts in two ways: each act shuffles
/// only its own events plus the shared ones when the run starts, and several shared events refuse
/// to appear outside certain acts in IsAllowed. With the option on, every act's event list holds
/// every event, and those act checks pass. Their other conditions (enough gold, a potion, relics
/// to trade) still apply, and the game's visited-event check keeps an event from repeating.
/// Singleplayer only.
/// </summary>
public static class Patches_EventsInAnyAct
{
    /// <summary>
    /// The act index the act-locked events see. Act 2 satisfies every act check below
    /// (== 1, > 0, &lt; 1 rejected, &lt; 2).
    /// </summary>
    private const int ActIndexThatPassesEveryGate = 1;

    private static bool IsEnabled(IRunState runState) =>
        runState.Players.Count == 1 && ArchipelagoClient.Settings?.EventsInAnyAct == true;

    /// <summary>
    /// The run manager generates every act's rooms when the run starts; saves keep the result.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateRooms))]
    private static class FillEveryActWithEveryEvent
    {
        [HarmonyPostfix]
        private static void Postfix(RunManager __instance)
        {
            RunState? runState = __instance.State;
            if (runState == null || !IsEnabled(runState))
                return;

            List<EventModel> unlocked = UnlockedEvents(runState.UnlockState).ToList();
            for (int actIndex = 0; actIndex < runState.Acts.Count; actIndex++)
            {
                ActModel act = runState.Acts[actIndex];
                List<EventModel> events = act._rooms.events;
                int before = events.Count;
                HashSet<ModelId> present = events.Select(e => e.Id).ToHashSet();
                List<EventModel> combined = events
                    .Concat(unlocked.Where(e => !present.Contains(e.Id)))
                    .ToList();

                // A separate stream leaves the game's UpFront RNG, and everything else it rolls,
                // as it would be without the option.
                Rng rng = new(runState.Rng.Seed, $"AP_EVENTS_IN_ANY_ACT_{actIndex}");
                events.Clear();
                events.AddRange(combined.UnstableShuffle(rng));
                LogUtility.Info($"Events in Any Act: act {actIndex + 1} ({act.Id.Entry}) draws from {events.Count} events instead of {before}.");
            }
        }

        /// <summary>
        /// Every act's and shared event, minus those whose timeline epoch the profile has not
        /// revealed yet, as the game filters its own act lists.
        /// </summary>
        private static IEnumerable<EventModel> UnlockedEvents(UnlockState unlockState)
        {
            IEnumerable<EventModel> events = ModelDb.AllEvents;
            if (!unlockState.IsEpochRevealed<Event1Epoch>())
                events = events.Where(e => Event1Epoch.Events.All(locked => locked.Id != e.Id));
            if (!unlockState.IsEpochRevealed<Event2Epoch>())
                events = events.Where(e => Event2Epoch.Events.All(locked => locked.Id != e.Id));
            if (!unlockState.IsEpochRevealed<Event3Epoch>())
                events = events.Where(e => Event3Epoch.Events.All(locked => locked.Id != e.Id));
            return events;
        }
    }

    /// <summary>
    /// Replaces the act index in these events' IsAllowed checks. The Legends Were True keeps its
    /// Act 1 check: its Spoils Map always marks treasure on the Act 2 map, which is useless later.
    /// </summary>
    [HarmonyPatch]
    private static class LiftActChecks
    {
        private static readonly MethodInfo CurrentActIndexGetter =
            AccessTools.PropertyGetter(typeof(IRunState), nameof(IRunState.CurrentActIndex));

        private static IEnumerable<MethodBase> TargetMethods() =>
            new[]
            {
                typeof(BrainLeech),
                typeof(CrystalSphere),
                typeof(DollRoom),
                typeof(FakeMerchant),
#if STS2_AT_LEAST_0_113_0
                typeof(MuseumOfTorture),
#endif
                typeof(PotionCourier),
                typeof(RanwidTheElder),
                typeof(RelicTrader),
                typeof(RoomFullOfCheese),
                typeof(StoneOfAllTime),
                typeof(Symbiote),
                typeof(TeaMaster),
                typeof(WelcomeToWongos),
            }.Select(type => AccessTools.Method(type, nameof(EventModel.IsAllowed), [typeof(IRunState)]));

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(CurrentActIndexGetter))
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(LiftActChecks), nameof(ActIndexForEventGate)))
                        .MoveLabelsFrom(instruction);
                    continue;
                }
                yield return instruction;
            }

            if (replaced == 0)
                LogUtility.Warn($"Events in Any Act: found no act check in {original.DeclaringType?.Name}.IsAllowed.");
        }

        private static int ActIndexForEventGate(IRunState runState) =>
            IsEnabled(runState) ? ActIndexThatPassesEveryGate : runState.CurrentActIndex;
    }
}
