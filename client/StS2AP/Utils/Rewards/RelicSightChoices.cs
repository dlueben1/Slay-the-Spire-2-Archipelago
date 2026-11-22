using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;

namespace StS2AP.Utils;

/// <summary>
/// The Relic Sight bonus: relic rewards offer <see cref="RelicSight.ChoiceCount"/> relics and the
/// player keeps one. Each extra relic is taken from the player's relic pool like a normal reward,
/// and every relic left behind goes back to the end of the pool for its rarity, so a run spends
/// one relic per reward just as without the bonus and does not run out sooner. Singleplayer only.
/// Covers relics paired with an AP Relic: Elite and Black Star rewards, treasure chests and the AP
/// menu.
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

    /// <summary>The chest whose extra relics are waiting for the player's pick.</summary>
    private static PendingChest? s_pendingChest;

    private sealed record PendingChest(TreasureRoomRelicSynchronizer Picker, Player Player, List<RelicModel> Offered);

    /// <summary>
    /// Adds extras of the same rarity to a singleplayer chest that holds one relic paired with an AP
    /// Relic. Call it when the chest opens, before the chest screen lays out its relics. The game's
    /// chest screen already lays out several relics (multiplayer chests have one per player), and
    /// the relics left unpicked end the vote unawarded.
    /// </summary>
    internal static void OfferChestChoice(TreasureRoomRelicSynchronizer picker, Player player)
    {
        s_pendingChest = null;
        if (!RelicSight.HasChoiceBonus || picker._currentRelics is not { Count: 1 } relics)
            return;
#if STS2_AT_LEAST_0_113_0
        // Blitz chests already hand out a second relic over a second round.
        if (player.RunState.GameMode == GameMode.Blitz)
            return;
#endif

        RelicModel baseRelic = relics[0];
        RelicRarity rarity = PoolRarities.Contains(baseRelic.Rarity) ? baseRelic.Rarity : RelicRarity.Common;
        while (relics.Count < RelicSight.ChoiceCount)
        {
            // Chests draw from the run's shared pool, so the extras do too.
            RelicModel? extra = picker._sharedGrabBag.PullFromFront(
                rarity,
                candidate => relics.All(offered => offered.Id != candidate.Id),
                player.RunState);
            if (extra == null)
                break;
            relics.Add(extra);
        }
        if (relics.Count < 2)
            return;

        s_pendingChest = new PendingChest(picker, player, relics.ToList());
        LogUtility.Info($"Relic Sight offers a chest choice of {string.Join(", ", relics.Select(relic => relic.Id))}");
    }

    /// <summary>Returns the chest relics the player passed over once they pick one.</summary>
    internal static void OnChestPicked(TreasureRoomRelicSynchronizer picker, int? index)
    {
        if (s_pendingChest is not { } chest || chest.Picker != picker || !index.HasValue)
            return;
        if (picker._currentRelics is not { } relics || index.Value < 0 || index.Value >= relics.Count)
            return;
        s_pendingChest = null;
        ModelId picked = relics[index.Value].Id;
        foreach (RelicModel relic in chest.Offered.Where(relic => relic.Id != picked))
            ReturnToPool(chest.Player, relic);
    }

    /// <summary>
    /// Returns every chest relic when the player leaves without picking. Skipping a singleplayer
    /// chest leaves the room, so this is where a skip ends.
    /// </summary>
    internal static void OnChestRoomExited(TreasureRoomRelicSynchronizer picker)
    {
        if (s_pendingChest is not { } chest || chest.Picker != picker)
            return;
        s_pendingChest = null;
        foreach (RelicModel relic in chest.Offered)
            ReturnToPool(chest.Player, relic);
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
        // An unpopulated bag has no rarities to return to.
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
