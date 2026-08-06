using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
    public static class Patches_AncientRelics
    {
        [HarmonyPostfix]
        public static void ReplaceAncientOptions(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
        {
            var player = GameUtility.CurrentPlayer;
            if (player == null)
                return;

            var currentAct = player.RunState.CurrentActIndex + 1;
            var maxAct = ArchipelagoClient.Progress.MaxAncientUnlock(player?.Character.GetCharacterOffset() ?? -1);
            
            // use Anytime and balanced as our defaults
            var location = ArchipelagoClient.Settings?.AncientRelicLocation ?? AncientRelicLocation.Anytime;
            var poolMode = ArchipelagoClient.Settings?.AncientRelicPool ?? AncientRelicPoolMode.Balanced;
            var useProceedOnly = maxAct < currentAct ||
                                 (location == AncientRelicLocation.Anytime && currentAct is 2 or 3);
            if (useProceedOnly)
            {
                LogUtility.Info($"Replacing Ancient choices with Proceed; location {location} maxAct {maxAct} current act {currentAct}");
                __result = new List<EventOption> { CreateFakeOption(__instance) };
                return;
            }

            if (location != AncientRelicLocation.StartOfAct ||
                poolMode == AncientRelicPoolMode.Balanced ||
                currentAct is not (2 or 3))
            {
                return;
            }

            // Chaos uses the current act's Ancient pool. True Chaos uses the combined
            // Act 2 and Act 3 pool for both Progressive Ancient rewards.
            int? poolActIndex = (poolMode == AncientRelicPoolMode.TrueChaos) ? null : currentAct - 1;

            // This key is part of the stable SHA-256 ordering, not user-facing text. It gives
            // each start-of-act reward a repeatable choice set without consuming game RNG.
            var choiceKey = $"start-act-{currentAct}";
            var choices = AncientRelicPool.CreateChoices(
                player,
                choiceKey,
                ancientActIndex: poolActIndex
            );
            if (choices.Count != AncientRelicPool.ChoiceCount)
            {
                LogUtility.Error(
                    $"Could not build {poolMode} choices for the Act {currentAct} Ancient; " +
                    "leaving the native Ancient options in place"
                );
                return;
            }

            try
            {
                var replacementOptions = choices.Select(relic => CreateRelicOption(__instance, relic)).ToList();
                __result = replacementOptions;
                LogUtility.Info($"Replaced the Act {currentAct} Ancient options with {poolMode} relic choices");
            }
            catch (Exception ex)
            {
                LogUtility.Error(
                    $"Failed to construct {poolMode} options for the Act {currentAct} Ancient; " +
                    $"leaving the native options in place: {ex.Message}"
                );
            }
        }

        [HarmonyPrefix]
        public static void SendAncientUnlockCheck()
        {

            var player = GameUtility.CurrentPlayer;
            if(player != null)
            {
                var currentAct = player.RunState.CurrentActIndex + 1;
                if(currentAct == 1 && !ArchipelagoClient.Settings.NeowSanity)
                {
                    return;
                }
                GameUtility.SendCheck($"{player.Character.APName()} Ancient Act {currentAct}");
            }
        }

        private static EventOption CreateFakeOption(AncientEventModel ancient)
        {
            return new EventOption(ancient,
                NEventRoom.Proceed,
                new MegaCrit.Sts2.Core.Localization.LocString("events", "AP_PROCEED.title"),
                new MegaCrit.Sts2.Core.Localization.LocString("events", "AP_PROCEED.description"),
                "AP_PROCEED", new List<IHoverTip>());
        }

        private static EventOption CreateRelicOption(AncientEventModel ancient, RelicModel relicModel)
        {
            var relic = relicModel.IsMutable ? relicModel : relicModel.ToMutable();
            var owner = ancient.Owner ?? throw new InvalidOperationException(
                $"Cannot construct Ancient relic option '{relic.Id}': the event has no owner"
            );

            // Mirrors the base game's EventModel.RelicOption helper. Binding the mutable relic
            // to the event owner initializes owner-dependent descriptions/hover tips and ensures
            // the same player is passed to RelicCmd.Obtain when the option is chosen. 
            // Something something megacrit multiplayer thing, idk the base game had it
            relic.Owner = owner;

            var textKey = $"{StringHelper.Slugify(ancient.GetType().Name)}.pages.INITIAL.options.{relic.Id.Entry}";
            return EventOption.FromRelic(relic, ancient, async () =>
            {
                try
                {
                    await RelicCmd.Obtain(relic, owner);
                    LogUtility.Success($"Granted start-of-act Ancient relic '{relic.Id}' from {ancient.Id}");
                }
                catch (Exception ex)
                {
                    // The Progressive Ancient item is authoritative and was already received.
                    // Treat an obtain failure as catastrophic diagnostics, not a retry path.
                    LogUtility.Error($"Failed to grant start-of-act Ancient relic '{relic.Id}': {ex.Message}");
                }
                finally
                {
                    ancient.StartPreFinished();
                }
            }, textKey);
        }
    }
}
