using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StS2AP.Patches
{
    /// <summary>
    /// Removes the vanilla Orobas upgrade relics that would bypass progressive starter tiers.
    /// Orobas normally takes one option from each of three pools. If its pool-three upgrade relics
    /// are blocked or unavailable, the third choice comes from the remaining first two pools.
    /// A future advanced-Ancient pool implementation must apply the same exclusions to its pool.
    /// </summary>
    [HarmonyPatch(typeof(Orobas), "GenerateInitialOptions")]
    internal static class Patches_OrobasProgressiveStarters
    {
        [HarmonyPrefix]
        private static bool Prefix(Orobas __instance, ref IReadOnlyList<EventOption> __result)
        {
            if (__instance.Owner is null || !ShouldFilterProgressiveStarters())
                return true;

            try
            {
                var currentCharacter = __instance.Owner.Character;
                var seaGlassCharacter = __instance.Rng.NextItem(
                    __instance.Owner.UnlockState.Characters.Where(character =>
                        character.Id != currentCharacter.Id)
                ) ?? currentCharacter;

                // Materialize Orobas's pools in the same order as the base method, without
                // modifying the event model's property results.
                var pool1 = GetPrivateProperty<IEnumerable<EventOption>>(__instance, "OptionPool1").ToList();

                EventOption dynamicPool1Option;
                if (__instance.Rng.NextFloat() < 0.3333333f)
                {
                    dynamicPool1Option = GetPrivateProperty<EventOption>(
                        __instance,
                        "PrismaticGemOption"
                    );
                }
                else
                {
                    dynamicPool1Option = GetPrivateProperty<IEnumerable<EventOption>>(
                        __instance,
                        "SeaGlassOptions"
                    ).FirstOrDefault(option =>
                        option.Relic is SeaGlass seaGlass &&
                        seaGlass.CharacterId == seaGlassCharacter.Id
                    ) ?? throw new InvalidOperationException(
                        $"Orobas has no Sea Glass option for {seaGlassCharacter.Id}."
                    );
                }

                pool1.Add(dynamicPool1Option);
                pool1.RemoveAll(IsBlocked);
                var firstOption = PickRequired(
                    __instance,
                    pool1,
                    "Orobas option pool 1 contains no valid options."
                );

                var pool2 = GetPrivateProperty<IEnumerable<EventOption>>(__instance, "OptionPool2").ToList();
                pool2.RemoveAll(IsBlocked);
                var secondOption = PickRequired(
                    __instance,
                    pool2,
                    "Orobas option pool 2 contains no valid options."
                );

                var pool3 = GetPrivateProperty<IEnumerable<EventOption>>(__instance, "OptionPool3").ToList();
                pool3.RemoveAll(option => IsBlocked(option) || option.Relic is null);
                var thirdOptionPool = pool3.Count > 0
                    ? pool3
                    : BuildFallbackThirdPool(pool1, pool2, firstOption, secondOption);
                var thirdOption = PickRequired(
                    __instance,
                    thirdOptionPool,
                    "Orobas contains no valid option for its third reward."
                );

                __result = new[] { firstOption, secondOption, thirdOption };
                return false;
            }
            catch (Exception ex)
            {
                LogUtility.Error(
                    $"Could not filter progressive starter relics from Orobas; " +
                    $"falling back to the base-game options. {ex}"
                );
                return true;
            }
        }

        private static bool IsBlocked(EventOption option) =>
            ProgressiveStarterUtility.ShouldExcludeAncientRelic(option.Relic);

        private static bool ShouldFilterProgressiveStarters() =>
            ProgressiveStarterUtility.ShouldExcludeAncientRelic(ModelDb.Relic<ArchaicTooth>()) ||
            ProgressiveStarterUtility.ShouldExcludeAncientRelic(ModelDb.Relic<TouchOfOrobas>());

        private static List<EventOption> BuildFallbackThirdPool(
            IEnumerable<EventOption> pool1,
            IEnumerable<EventOption> pool2,
            EventOption firstOption,
            EventOption secondOption)
        {
            var selectedRelicIds = new HashSet<string>();
            if (firstOption.Relic is not null)
                selectedRelicIds.Add(firstOption.Relic.Id.ToString());
            if (secondOption.Relic is not null)
                selectedRelicIds.Add(secondOption.Relic.Id.ToString());

            return pool1
                .Concat(pool2)
                .Where(option =>
                    option.Relic is null || !selectedRelicIds.Contains(option.Relic.Id.ToString()))
                .GroupBy(GetOptionIdentity)
                .Select(group => group.First())
                .ToList();
        }

        private static string GetOptionIdentity(EventOption option) =>
            option.Relic?.Id.ToString() ?? $"EVENT_OPTION:{option.TextKey}";

        private static EventOption PickRequired(
            Orobas instance,
            IReadOnlyList<EventOption> pool,
            string errorMessage)
        {
            if (pool.Count == 0)
                throw new InvalidOperationException(errorMessage);

            return instance.Rng.NextItem(pool)
                ?? throw new InvalidOperationException(errorMessage);
        }

        private static T GetPrivateProperty<T>(Orobas instance, string propertyName)
            where T : class
        {
            var property = AccessTools.Property(instance.GetType(), propertyName)
                ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);

            return property.GetValue(instance) as T
                ?? throw new InvalidOperationException(
                    $"{instance.GetType().Name}.{propertyName} did not contain a {typeof(T).FullName}."
                );
        }
    }

    [HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
    public static class Patches_AncientRelics
    {
        [HarmonyPostfix]
        public static void ReplaceAncientOptions(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
        {
            var player = GameUtility.CurrentPlayer;
            if (player == null)
                return;
            if(ArchipelagoClient.Settings.APWorldVersion <= Constants.VERSION_0_5_3)
            {
                // Version is before Ancient Relics could be replaced, so we get out.
                return;
            }

            var currentAct = player.RunState.CurrentActIndex + 1;
            var maxAct = ArchipelagoClient.Progress.MaxProgressiveAncientLevel(
                player.Character.GetCharacterOffset() ?? -1
            );
            
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
        public static void SendAncientCheck()
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
