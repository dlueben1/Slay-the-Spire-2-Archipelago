using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

internal static class Patches_CrossCharacterCardPools
{
    private const string GenericOptionKey = "AP_COLORFUL_PHILOSOPHERS_POOL";

    /// <summary>
    /// Gives Prismatic Gem access to every built-in character pool plus installed modded
    /// character pools selected by the owner's AP slot. The original reward pools are retained
    /// so other reward modifiers can still contribute pools of their own.
    /// </summary>
    [HarmonyPatch(typeof(PrismaticGem), nameof(PrismaticGem.ModifyCardRewardCreationOptions))]
    private static class PrismaticGemCardPools
    {
        [HarmonyPrefix]
        private static void CaptureOriginalPools(
            CardCreationOptions options,
            out IReadOnlyCollection<CardPoolModel> __state)
        {
            __state = options.CardPools.ToArray();
        }

        [HarmonyPostfix]
        // Downfall filters this result using the owner's Prismatic setting. Supply AP's
        // candidate pools first so we do not reintroduce pools that it deliberately removed.
        [HarmonyBefore("Downfall")]
        private static void ReplaceCharacterPools(
            PrismaticGem __instance,
            Player player,
            CardCreationOptions options,
            IReadOnlyCollection<CardPoolModel> __state,
            ref CardCreationOptions __result)
        {
            // Mirror the base method's guards. In particular, Colourful Philosophers uses
            // NoCardPoolModifications so Prismatic Gem cannot mix its three rarity rewards.
            if (__instance.Owner != player ||
                options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications) ||
                !options.Flags.HasFlag(CardCreationFlags.IsCardReward) ||
#if STS_PUBLIC
                options.CustomCardPool is not null ||
#endif
                __state.All(pool => pool.IsColorless) ||
                !CrossCharacterCardPoolUtility.TryGetPools(player, out var characterPools))
            {
                return;
            }

            var pools = characterPools.Concat(__state).Distinct().ToArray();
#if STS_PUBLIC
            // The public API clears the filter unless it is explicitly passed back in.
            __result = __result.WithCardPools(pools, __result.CardPoolFilter);
#else
            __result = __result.WithCardPools(pools);
#endif
            LogUtility.Debug(
                $"Prismatic Gem card pools: {string.Join(", ", __result.CardPools.Select(pool => pool.Id))}"
            );
        }
    }

    /// <summary>
    /// Lets the event spawn when AP's character-selection unlock override would otherwise
    /// make fewer than two character pools visible to the base-game eligibility check.
    /// </summary>
    [HarmonyPatch(typeof(ColorfulPhilosophers), nameof(ColorfulPhilosophers.IsAllowed))]
    private static class AllowWithCrossCharacterPools
    {
        [HarmonyPostfix]
        private static void Postfix(IRunState runState, ref bool __result)
        {
            if (__result)
                return;

            __result = runState.Players.All(player =>
                CrossCharacterCardPoolUtility.TryGetPools(player, out var pools)
                    ? pools.Any(pool => pool.Id != player.Character.CardPool.Id)
                    : player.UnlockState.CharacterCardPools.Count() > 1
            );
        }
    }

    /// <summary>
    /// Retains the event's normal three-option limit and excludes the current character's
    /// pool, while selected installed modded characters join the candidate set.
    /// </summary>
    [HarmonyPatch(typeof(ColorfulPhilosophers), "GenerateInitialOptions")]
    private static class GenerateCrossCharacterOptions
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ColorfulPhilosophers __instance,
            ref IReadOnlyList<EventOption> __result)
        {
            if (__instance.Owner is null ||
                !CrossCharacterCardPoolUtility.TryGetPools(__instance.Owner, out var pools))
            {
                return true;
            }

            try
            {
                var options = pools
                    .Where(pool => pool.Id != __instance.Owner.Character.CardPool.Id)
                    .Select(pool => CreateOption(__instance, pool))
                    .ToList();

                const int maximumOptionCount = 3;
                while (options.Count > maximumOptionCount)
                {
                    options.RemoveAt(__instance.Rng.NextInt(options.Count));
                }

                __result = options;
                LogUtility.Debug(
                    $"Colourful Philosophers card pools: {string.Join(", ", options.Select(option => option.TextKey))}"
                );
                return false;
            }
            catch (Exception ex)
            {
                LogUtility.Error(
                    $"Could not add AP character pools to Colourful Philosophers; " +
                    $"using the base-game options. {ex}"
                );
                return true;
            }
        }
    }

    private static EventOption CreateOption(
        ColorfulPhilosophers philosophers,
        CardPoolModel pool)
    {
        var nativeKey =
            $"COLORFUL_PHILOSOPHERS.pages.INITIAL.options.{pool.EnergyColorName.ToUpperInvariant()}";
        Func<Task> offerRewards = () => philosophers.OfferRewards(pool);

        // Alternate characters may reuse a vanilla energy colour without sharing its
        // card pool. In that case the vanilla option text would identify the wrong pool.
        var borrowsVanillaColor = ColorfulPhilosophers.CardPoolColorOrder.Any(nativePool =>
            nativePool.Id != pool.Id &&
            string.Equals(nativePool.EnergyColorName, pool.EnergyColorName,
                StringComparison.OrdinalIgnoreCase));
        if (!borrowsVanillaColor &&
            philosophers.GetOptionTitle(nativeKey) is not null &&
            philosophers.GetOptionDescription(nativeKey) is not null)
        {
            return new EventOption(philosophers, offerRewards, nativeKey);
        }

        var character = ModelDb.AllCharacters.FirstOrDefault(candidate =>
            candidate.CardPool.Id == pool.Id);
        var poolName = character is not null && character.Title.Exists()
            ? character.Title.GetFormattedText()
            : pool.Title;
        var title = new LocString("events", $"{GenericOptionKey}.title");
        title.Add("CardPool", poolName);
        var description = new LocString("events", $"{GenericOptionKey}.description");
        description.Add("CardPool", poolName);

        return new EventOption(
            philosophers,
            offerRewards,
            title,
            description,
            $"{GenericOptionKey}.{pool.Id.Entry}",
            Array.Empty<IHoverTip>()
        );
    }
}
