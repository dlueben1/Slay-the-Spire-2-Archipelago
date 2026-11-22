using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;

namespace StS2AP.Utils;

/// <summary>
/// The Relic Sight bonus: relic rewards offer <see cref="RelicSight.ChoiceCount"/> relics and the
/// player keeps one. Each extra relic is taken from the player's relic pool like a normal reward,
/// and every relic left behind goes back to the end of the pool for its rarity, so a run spends
/// one relic per reward just as without the bonus and does not run out sooner. Singleplayer only.
/// </summary>
internal static class RelicSightChoices
{
    private static readonly HashSet<RelicRarity> PoolRarities =
    [
        RelicRarity.Common,
        RelicRarity.Uncommon,
        RelicRarity.Rare,
        RelicRarity.Shop,
    ];

    /// <summary>How many relics a new AP relic assignment should hold.</summary>
    internal static int AssignmentChoiceCount => RelicSight.HasChoiceBonus ? RelicSight.ChoiceCount : 1;

    /// <summary>
    /// Turns a native relic reward (an Elite or Black Star relic paired with an AP Relic) into a
    /// choice between its relic and extras of the same rarity. Leaves the reward alone when the
    /// bonus is off or the pool has nothing else to offer.
    /// </summary>
    internal static void OfferNativeChoice(List<Reward> rewards, RelicReward native, Player player)
    {
        if (!RelicSight.HasChoiceBonus)
            return;
        int index = rewards.IndexOf(native);
        if (index < 0)
            return;

        // Populate now rather than when the screen opens, so the extras can match its rarity.
        native.Populate();
        if (native.Relic is not { } baseRelic)
            return;

        var relics = new List<RelicModel> { baseRelic };
        RelicRarity rarity = PoolRarities.Contains(baseRelic.Rarity) ? baseRelic.Rarity : RelicRarity.Common;
        while (relics.Count < RelicSight.ChoiceCount)
        {
            RelicModel extra = RelicFactory.PullNextRelicFromFront(
                player,
                rarity,
                candidate => relics.All(chosen => chosen.Id != candidate.Id));
            // An empty pool gives the fallback relic, which is no choice at all.
            if (extra.Id == RelicFactory.FallbackRelic.Id)
                break;
            relics.Add(extra.ToMutable());
        }
        if (relics.Count < 2)
            return;

        List<Reward> children = relics.Select(relic => (Reward)new ChoiceRelicReward(relic, player)).ToList();
        rewards[index] = ApLinkedRewardSets.CreateChooseOne(children, player);
        LogUtility.Info($"Relic Sight offers a choice of {string.Join(", ", relics.Select(relic => relic.Id))}");
    }

    /// <summary>
    /// The relics an AP Relic's saved assignment holds besides <paramref name="first"/>. The AP
    /// menu entry carries only the first; the bonus's other choices stay in the assignment.
    /// </summary>
    internal static IReadOnlyList<RelicModel> OtherAssignedChoices(int itemIndex, RelicModel first)
    {
        if (MultiplayerSupport.IsRealMultiplayerRun
            || !ArchipelagoClient.Progress.RelicChoiceAssignments.TryGetValue(itemIndex, out var assigned))
        {
            return [];
        }
        return assigned.Where(relic => relic.Id != first.Id).ToList();
    }

    /// <summary>
    /// Returns the relics an AP Relic's assignment offered but the player did not take. Call it
    /// before the claim removes the assignment.
    /// </summary>
    internal static void ReturnUnpickedAssignment(Player player, int itemIndex, RelicModel picked)
    {
        foreach (RelicModel relic in OtherAssignedChoices(itemIndex, picked))
            ReturnToPool(player, relic);
    }

    /// <summary>
    /// Puts a relic back at the end of its rarity in the player's pool and the run's shared pool,
    /// where taking it as a reward removed it from. Owned relics and the fallback relic stay out.
    /// </summary>
    internal static void ReturnToPool(Player player, RelicModel relic)
    {
        RelicModel canonical = relic.IsMutable ? ModelDb.GetById<RelicModel>(relic.Id) : relic;
        if (canonical.Id == RelicFactory.FallbackRelic.Id
            || !PoolRarities.Contains(canonical.Rarity)
            || player.Relics.Any(owned => owned.Id == canonical.Id))
        {
            return;
        }
        AddToBack(player.RelicGrabBag, canonical);
        AddToBack(player.RunState.SharedRelicGrabBag, canonical);
        LogUtility.Info($"Relic Sight returned {canonical.Id} to the relic pool");
    }

    private static void AddToBack(RelicGrabBag bag, RelicModel canonical)
    {
        // An unpopulated bag (the shared bag outside multiplayer) has nothing to return to.
        if (!bag.IsPopulated || bag._deques.Values.Any(deque => deque.Any(relic => relic.Id == canonical.Id)))
            return;
        if (!bag._deques.TryGetValue(canonical.Rarity, out List<RelicModel>? deque))
        {
            deque = new List<RelicModel>();
            bag._deques[canonical.Rarity] = deque;
        }
        deque.Add(canonical);
    }

    /// <summary>
    /// A native relic reward inside a Relic Sight choice. When it is not taken, because the player
    /// took another relic or skipped the whole choice, its relic goes back to the pool. A save and
    /// reload restores these as plain relic rewards, so the return is lost after a reload.
    /// </summary>
    private sealed class ChoiceRelicReward(RelicModel relic, Player player) : RelicReward(relic, player)
    {
        public override void OnSkipped()
        {
            base.OnSkipped();
            if (!_wasTaken && Relic != null)
                ReturnToPool(Player, Relic);
        }
    }
}
