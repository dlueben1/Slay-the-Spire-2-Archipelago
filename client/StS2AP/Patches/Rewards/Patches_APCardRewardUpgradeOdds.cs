using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Routes replicated AP card generation through receipt-local RNG while keeping native
/// card-pool, upgrade, modifier, and alternative hooks on every machine.
/// </summary>
public static class Patches_APCardRewardUpgradeOdds
{
    [ThreadStatic]
    private static int? s_rewardActIndex;

    [ThreadStatic]
    private static bool s_deferOptionHooks;

    [ThreadStatic]
    private static Rng? s_apRewardRng;

    private sealed class ApRewardRngScope(Rng? previous) : IDisposable
    {
        public void Dispose() => s_apRewardRng = previous;
    }

    private sealed class RewardActScope(int? previous) : IDisposable
    {
        public void Dispose() => s_rewardActIndex = previous;
    }

    /// <summary>
    /// Provides the receipt-specific RNG for native generation and the scoped RNG patches.
    /// The scope is thread-local because native card generation is synchronous on the Godot thread.
    /// </summary>
    internal static IDisposable EnterApRewardRng(Rng rng)
    {
        Rng? previous = s_apRewardRng;
        s_apRewardRng = rng;
        return new ApRewardRngScope(previous);
    }

    internal static IDisposable EnterRewardAct(int? actIndex)
    {
        int? previous = s_rewardActIndex;
        s_rewardActIndex = actIndex;
        return new RewardActScope(previous);
    }

    /// <summary>
    /// Populates one new AP reward under a temporary act override. CardReward.Populate is
    /// synchronous, so the override is always cleared before control returns to the UI.
    /// </summary>
    public static void PopulateForAct(CardReward reward, int actIndex)
    {
        var previousActIndex = s_rewardActIndex;
        try
        {
            s_rewardActIndex = actIndex;
            reward.Populate();
        }
        finally
        {
            s_rewardActIndex = previousActIndex;
        }
    }

    internal static T RunDeferringOptionHooks<T>(Func<T> materialize)
    {
        bool previousDeferOptionHooks = s_deferOptionHooks;
        try
        {
            s_deferOptionHooks = true;
            return materialize();
        }
        finally
        {
            s_deferOptionHooks = previousDeferOptionHooks;
        }
    }

    /// <summary>
    /// CardFactory normally launches post-generation modifier callbacks without awaiting them.
    /// AP materialization suppresses that one final hook call and invokes it explicitly after the
    /// native base roll, so native callbacks finish before the offer is verified and published.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.TryModifyCardRewardOptions))]
    private static class DeferReplicaMaterializationOptionHooks
    {
        [HarmonyPrefix]
        private static bool Prefix(
            IRunState runState,
            Player player,
            List<CardCreationResult> cardRewardOptions,
            CardCreationOptions creationOptions,
            ref bool __result,
            ref List<AbstractModel> modifiers)
        {
            if (s_deferOptionHooks)
            {
                modifiers = new List<AbstractModel>();
                __result = false;
                return false;
            }

            return true;
        }
    }

    // Keep the receipt RNG attached after native pool hooks, including nested generation.
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardRewardCreationOptions))]
    private static class PreserveApRewardRng
    {
        [HarmonyPostfix]
        private static void Postfix(ref CardCreationOptions __result)
        {
            if (s_apRewardRng != null)
                __result = __result.WithRngOverride(s_apRewardRng);
        }
    }

    /// <summary>Propagates the AP RNG into nested generation such as Lasting Candy.</summary>
    [HarmonyPatch(
        typeof(CardFactory),
        nameof(CardFactory.CreateForReward),
        [typeof(Player), typeof(int), typeof(CardCreationOptions)]
    )]
    private static class PropagateApRewardRng
    {
        [HarmonyPrefix]
        private static void Prefix(CardCreationOptions options)
        {
            if (s_apRewardRng != null)
                options.WithRngOverride(s_apRewardRng);
        }
    }

    /// <summary>
    /// AP regular rewards use fixed independent 57/37/6 rarity rolls. Scarcity halves the
    /// Rare chance to use a 60/37/3 table, and rare AP rewards remain guaranteed Rare.
    /// </summary>
    [HarmonyPatch(
        typeof(CardFactory),
        "RollForRarity",
        [
            typeof(Player),
            typeof(CardRarityOddsType),
            typeof(CardCreationSource),
            typeof(HashSet<CardRarity>),
            typeof(bool),
        ]
    )]
    private static class RollApCardRarity
    {
        [HarmonyPrefix]
        private static bool Prefix(
            CardRarityOddsType rollMethod,
            HashSet<CardRarity> allowedRarities,
            ref CardRarity __result)
        {
            if (s_apRewardRng == null)
                return true;
            CardRarity rarity;
            if (rollMethod == CardRarityOddsType.BossEncounter)
            {
                rarity = CardRarity.Rare;
            }
            else
            {
                bool scarcity = AscensionMultiplayer.TryHasLevel(
                    AscensionLevel.Scarcity,
                    out bool multiplayerScarcity
                )
                    ? multiplayerScarcity
                    : ArchipelagoClient.Progress.Ascensions.HasLevel(AscensionLevel.Scarcity);
                float rareChance = scarcity ? 0.03f : 0.06f;
                float uncommonChance = 0.37f;
                float roll = s_apRewardRng.NextFloat();
                rarity = roll < rareChance
                    ? CardRarity.Rare
                    : roll < rareChance + uncommonChance
                        ? CardRarity.Uncommon
                        : CardRarity.Common;
            }
            var attempted = new HashSet<CardRarity>();
            while (!allowedRarities.Contains(rarity) && rarity != CardRarity.None)
            {
                if (!attempted.Add(rarity))
                {
                    rarity = CardRarity.None;
                    break;
                }
                rarity = rarity.GetNextHighestRarityWithWrapping();
            }
            __result = rarity;
            return false;
        }
    }

    /// <summary>Wing Charm's native Niche RNG choice becomes reward-local under AP generation.</summary>
    [HarmonyPatch(typeof(WingCharm), nameof(WingCharm.TryModifyCardRewardOptionsLate))]
    private static class RouteWingCharmThroughApRng
    {
        [HarmonyPrefix]
        private static bool Prefix(
            WingCharm __instance,
            Player player,
            List<CardCreationResult> cardRewards,
            ref bool __result)
        {
            if (s_apRewardRng == null)
                return true;
            if (player != __instance.Owner)
            {
                __result = false;
                return false;
            }

            Swift swift = ModelDb.Enchantment<Swift>();
            List<CardCreationResult> valid = cardRewards
                .Where(result => swift.CanEnchant(result.Card))
                .ToList();
            CardCreationResult? selected = s_apRewardRng.NextItem(valid);
            if (selected == null)
            {
                __result = false;
                return false;
            }

            CardModel card = __instance.Owner.RunState.CloneCard(selected.Card);
            CardCmd.Enchant<Swift>(
                card,
                __instance.DynamicVars["SwiftAmount"].BaseValue
            );
            selected.ModifyCard(card, __instance);
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// CardFactory calculates the current act's base odds before calling this public hook.
    /// Replace those incoming odds with the AP reward's assigned act, then let the original
    /// hook apply every normal relic and model modifier.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardRewardUpgradeOdds))]
    private static class OverrideAssignedActUpgradeOdds
    {
        [HarmonyPrefix]
        private static bool Prefix(
            CardModel card,
            ref decimal originalOdds)
        {
            if (!s_rewardActIndex.HasValue || card.Rarity == CardRarity.Rare)
                return true;

            bool scarcity = AscensionMultiplayer.TryHasLevel(
                AscensionLevel.Scarcity,
                out bool multiplayerScarcity
            )
                ? multiplayerScarcity
                : ArchipelagoClient.Progress.Ascensions.HasLevel(AscensionLevel.Scarcity);
            var scaling = scarcity
                ? 0.125m
                : 0.25m;
            originalOdds = s_rewardActIndex.Value * scaling;
            return true;
        }
    }

}
