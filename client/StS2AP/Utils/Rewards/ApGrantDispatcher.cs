using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Data;
using static StS2AP.Data.ItemTable;
using StS2AP.Utils.RunGraph;

namespace StS2AP.Utils;

// EXPLAIN: this entire file to me and why is the path for gold so different than singleplayer

/// <summary>
/// Routes AP-owned causes to their concrete game effects. The first supported route is
/// aggregate gold, which deliberately uses a cumulative raw cursor instead of discrete
/// receipt IDs and delegates replication to MegaCrit's RewardSynchronizer.
/// </summary>
public static class ApGrantDispatcher
{
    public const int UniversalBuffGoldValue = UniversalBuffGold.ValuePerBuff;

    /// <summary>Rebuilds the raw per-character bank from authoritative AP history.</summary>
    public static void RebuildGoldBank(IReadOnlyList<ItemInfo> receivedItems)
    {
        ArchipelagoSettings settings = RequireSettings("rebuild the AP gold bank");
        long[] characterOffsets = settings.Characters.Values
            .Select(config => (long)config.CharOffset)
            .ToArray();
        var rebuilt = new Dictionary<long, int>();
        var gainItemEnds = new Dictionary<long, List<int>>();
        int buffCount = 0;
        foreach (ItemInfo item in receivedItems)
        {
            if (ArchipelagoIdCodec.IsArchipelagoReservedId(item.ItemId))
                continue;
            if (ArchipelagoIdCodec.IsUniversalItemId(item.ItemId))
            {
                // Add buff gold in receipt order, as live receipts do, so the gain item positions
                // in GoldGainItemEnds match either way.
                if (ItemTable.IsMultiplayerBuffGoldFallback(item.ItemId) || BonusRewardUtility.ConvertToGold(item.ItemId))
                {
                    UniversalBuffGold.AddToBank(rebuilt, characterOffsets, buffCount, addedBuffCount: 1);
                    buffCount++;
                }
                continue;
            }

            AddGoldItem(rebuilt, gainItemEnds, item.GetAPCharacterNumber(), item.GetCharacterItemType());
        }

        // Gives every configured character a bank entry, even one with no gold yet.
        UniversalBuffGold.AddToBank(rebuilt, characterOffsets, buffCount, addedBuffCount: 0);
        ArchipelagoClient.Progress.GoldReceived = rebuilt;
        ArchipelagoClient.Progress.GoldGainItemEnds = gainItemEnds;
        ArchipelagoClient.Progress.UniversalBuffsConvertedToGold = buffCount;
        LogUtility.Info(
            $"Rebuilt AP gold bank from {receivedItems.Count} receipt(s): "
                + string.Join(",", rebuilt.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={pair.Value}"))
        );
    }

    /// <summary>Adds one live character gold receipt to the bank, as the history rebuild does.</summary>
    public static bool AddGoldItem(long characterOffset, APItem itemId)
    {
        var progress = ArchipelagoClient.Progress;
        return AddGoldItem(progress.GoldReceived, progress.GoldGainItemEnds, characterOffset, itemId);
    }

    private static bool AddGoldItem(
        Dictionary<long, int> bank,
        Dictionary<long, List<int>> gainItemEnds,
        long characterOffset,
        APItem itemId)
    {
        if (!ItemTable.GoldItemAmounts.TryGetValue(itemId, out int amount))
            return false;

        bank.TryGetValue(characterOffset, out int previous);
        int total = previous + amount;
        bank[characterOffset] = total;
        if (itemId is APItem.CombatGold or APItem.EliteGold or APItem.BossGold)
            GoldGainCount.RecordItemEnd(gainItemEnds, characterOffset, total);
        return true;
    }

    /// <summary>
    /// Divides cumulative universal buff gold across the characters configured by this slot.
    /// Only the increase in each character's whole-gold share is added for this receipt.
    /// The resulting bank uses the same cursor and Poverty handling as all other AP gold.
    /// </summary>
    public static int AddUniversalBuffGold()
    {
        ArchipelagoSettings settings = RequireSettings("convert a universal buff into AP gold");
        var progress = ArchipelagoClient.Progress;
        int amount = UniversalBuffGold.AddToBank(
            progress.GoldReceived,
            settings.Characters.Values.Select(config => (long)config.CharOffset),
            progress.UniversalBuffsConvertedToGold,
            addedBuffCount: 1
        );
        progress.UniversalBuffsConvertedToGold++;
        return amount;
    }

    private static ArchipelagoSettings RequireSettings(string operation)
    {
        ArchipelagoSettings? settings = ArchipelagoClient.Settings;
        if (settings != null)
            return settings;

        string message = $"Cannot {operation} because AP slot settings are unavailable.";
        LogUtility.Error(message);
        throw new InvalidOperationException(message);
    }

    /// <summary>Binds the local player's aggregate gold cursor to the launched STS run.</summary>
    public static bool BeginRun(long characterOffset, out string reason)
    {
        reason = string.Empty;
        if (MultiplayerSupport.PreparedApRoomSeed is not { } roomSeed
            || MultiplayerSupport.PreparedApTeamId is not { } apTeamId
            || MultiplayerSupport.PreparedApSlotId is not { } apSlotId)
        {
            reason = "The AP owner identity was not prepared before the run launched.";
            return false;
        }

        int redeemedRaw = ArchipelagoClient.Progress.GoldRedeemed;
        ArchipelagoClient.Progress.GoldReceived.TryGetValue(characterOffset, out int receivedRaw);
        if (redeemedRaw < 0 || redeemedRaw > receivedRaw)
        {
            LogUtility.Warn(
                $"Clamping invalid persisted AP gold cursor {redeemedRaw} to raw bank {receivedRaw}"
            );
            redeemedRaw = Math.Clamp(redeemedRaw, 0, receivedRaw);
        }

        ArchipelagoClient.Progress.GoldRedeemed = redeemedRaw;
        LogUtility.Info(
            $"Bound aggregate AP gold cursor: room={roomSeed}, team={apTeamId}, slot={apSlotId}, "
                + $"character={characterOffset}, "
                + $"redeemedRaw={redeemedRaw}, receivedRaw={receivedRaw}"
        );
        return true;
    }

    /// <summary>Materializes one immutable aggregate claim for the current reward-menu row.</summary>
    public static ApGoldClaim? MaterializeGoldClaim()
    {
        ArchipelagoGoldOffer offer = ArchipelagoClient.Progress.PrepareGoldOffer();
        if (offer.SourceAmount <= 0 || offer.GrantedAmount <= 0)
            return null;

        var progress = ArchipelagoClient.Progress;
        int redeemedBefore = progress.GoldRedeemed;
        int redeemedAfter = redeemedBefore + offer.SourceAmount;
        int gainCount = 0;
        if (GameUtility.CurrentConfig is { } config
            && progress.GoldGainItemEnds.TryGetValue(config.CharOffset, out List<int>? itemEnds))
        {
            gainCount = GoldGainCount.CountWithin(itemEnds, redeemedBefore, redeemedAfter);
        }

        return new ApGoldClaim(offer.SourceAmount, offer.GrantedAmount, redeemedAfter, gainCount);
    }

    /// <summary>
    /// Advances the AP source cursor after a native GoldReward has already applied its concrete
    /// wallet mutation. This is owner-only bookkeeping; remote replicas must never call it.
    /// </summary>
    public static bool CommitGoldClaim(ApGoldClaim claim)
    {
        int expectedBefore = claim.RedeemedRawAfter - claim.SourceAmount;
        if (claim.SourceAmount <= 0
            || claim.GrantedAmount <= 0
            || ArchipelagoClient.Progress.GoldRedeemed != expectedBefore
            || ArchipelagoClient.Progress.GoldRemaining < claim.SourceAmount)
        {
            LogUtility.Error(
                $"Could not commit native AP gold claim: source={claim.SourceAmount}, "
                    + $"granted={claim.GrantedAmount}, expectedCursor={expectedBefore}, "
                    + $"actualCursor={ArchipelagoClient.Progress.GoldRedeemed}, "
                    + $"remaining={ArchipelagoClient.Progress.GoldRemaining}"
            );
            MultiplayerSupport.InvalidateRunClaims(
                "a native AP gold reward was applied after its source cursor became invalid"
            );
            return false;
        }

        ArchipelagoClient.Progress.GoldRedeemed = claim.RedeemedRawAfter;
        ApRunGraphRecorder.RecordGoldClaim(claim.GrantedAmount);
        Player? player = GameUtility.CurrentPlayer;
        if (player != null && ApRunData.PublishLocalProgress(player))
            return true;

        MultiplayerSupport.InvalidateRunClaims(
            "the aggregate AP gold cursor could not be published after applying gold"
        );
        return false;
    }
}
