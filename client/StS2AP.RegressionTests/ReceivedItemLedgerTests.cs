using System.Text.Json;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ReceivedItemLedgerTests
{
    [Fact]
    public void RepeatedDeliveryDoesNotDuplicateOrReopenAConsumedReceipt()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterReceived(Receipt(1));
        ledger.RegisterReceived(Receipt(1));
        Assert.Single(ledger.Received);
        Assert.Empty(ledger.UsedIndexes);

        ledger.MarkUsed(1);
        long revision = ledger.Revision;
        ledger.MarkUsed(1);
        ledger.RegisterReceived(Receipt(1));
        Assert.Single(ledger.Received);
        Assert.Equal(new[] { 1 }, ledger.UsedIndexes);
        Assert.Equal(revision, ledger.Revision);
    }

    [Fact]
    public void TwoCopiesOfAnItemAreSeparateReceipts()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterReceived(Receipt(1));
        ledger.RegisterReceived(Receipt(2));
        ledger.MarkUsed(1);

        Assert.Equal(2, ledger.Received.Count);
        Assert.True(ledger.IsUsed(1));
        Assert.False(ledger.IsUsed(2));
    }

    [Fact]
    public void RestoredConsumptionSurvivesPartialHistoryAndLaterDelivery()
    {
        // Exercise the unchanged JSON list boundary, including duplicate legacy entries.
        var save = new ApRunProgressState { UsedItems = new() { 2, 2, 9 } };
        var restored = JsonSerializer.Deserialize<ApRunProgressState>(JsonSerializer.Serialize(save))!;
        var ledger = ApReceivedItemLedger.FromUsedIndexes(restored.UsedItems);
        Assert.Empty(ledger.Received);
        Assert.Equal(new[] { 2, 9 }, ledger.UsedIndexes);

        ledger.RegisterReceived(Receipt(2));
        ledger.ReplaceReceivedItems(new[] { Receipt(1) });
        Assert.Equal(new[] { 1 }, ledger.Received.Select(receipt => receipt.Index));
        Assert.Equal(new[] { 2, 9 }, ledger.UsedIndexes);

        ledger.RegisterReceived(Receipt(9));
        ledger.RegisterReceived(Receipt(2));
        Assert.Equal(new[] { 1, 2, 9 }, ledger.Received.Select(receipt => receipt.Index));
        Assert.Equal(new[] { 2, 9 }, ledger.UsedIndexes);
        Assert.Equal(new[] { 2, 9 }, JsonSerializer.Deserialize<ApRunProgressState>(
            JsonSerializer.Serialize(new ApRunProgressState { UsedItems = ledger.UsedIndexes.ToList() }))!.UsedItems);
    }

    [Fact]
    public void NewRunClearsConsumptionAndKeepsOnlyKnownReceipts()
    {
        var ledger = ApReceivedItemLedger.FromUsedIndexes(new[] { 1, 9 });
        ledger.RegisterReceived(Receipt(1));
        ledger.RegisterReceived(Receipt(2));
        ledger.StartNewRun();

        Assert.Equal(new[] { 1, 2 }, ledger.Received.Select(receipt => receipt.Index));
        Assert.Empty(ledger.UsedIndexes);
        Assert.Throws<InvalidOperationException>(() => ledger.MarkUsed(9));
        ledger.RegisterReceived(Receipt(9));
        Assert.False(ledger.IsUsed(9));
    }

    [Fact]
    public void ImmediateConsumptionRegistersTheReceiptAndCannotBeUndoneByReplay()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterConsumed(Receipt(1));
        ledger.RegisterReceived(Receipt(1));
        Assert.Single(ledger.Received);
        Assert.Equal(new[] { 1 }, ledger.UsedIndexes);
    }

    [Fact]
    public void NormalConsumptionRequiresAReceiptAndCannotFabricateHistory()
    {
        var ledger = new ApReceivedItemLedger();
        Assert.Throws<InvalidOperationException>(() => ledger.MarkUsed(1));
        Assert.Empty(ledger.Received);
        Assert.Empty(ledger.UsedIndexes);
    }

    [Theory]
    [InlineData(10002, 50)]
    [InlineData(10001, 51)]
    public void ConflictingDeliveryCannotReplaceOrConsumeTheOriginal(long itemId, long locationId)
    {
        var ledger = new ApReceivedItemLedger();
        var original = Receipt(1);
        ledger.RegisterReceived(original);
        Assert.Throws<InvalidOperationException>(() =>
            ledger.RegisterConsumed(Receipt(1, itemId, locationId)));
        Assert.Same(original, Assert.Single(ledger.Received));
        Assert.Empty(ledger.UsedIndexes);
    }

    [Fact]
    public void ConflictingOrInterruptedReplacementLeavesTheEntirePreviousStateIntact()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterConsumed(Receipt(1));
        var previous = ledger.Received;
        long revision = ledger.Revision;

        // The conflict is between two incoming entries, not only against the old catalogue.
        Assert.Throws<InvalidOperationException>(() => ledger.ReplaceReceivedItems(
            new[] { Receipt(2), Receipt(2, itemId: 99999) }));
        Assert.Throws<InvalidOperationException>(() => ledger.ReplaceReceivedItems(InterruptedHistory()));
        Assert.Same(previous, ledger.Received);
        Assert.Equal(revision, ledger.Revision);
        Assert.Equal(new[] { 1 }, ledger.UsedIndexes);
    }

    [Fact]
    public void ReadViewsCannotMutateTheLedgerAndRemainStableDuringConsumption()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterReceived(Receipt(1));
        var received = ledger.Received;
        var used = ledger.UsedIndexes;
        Assert.Throws<NotSupportedException>(() => ((IList<IndexedItemInfo>)received).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)used).Add(1));
        ledger.MarkUsed(1);
        Assert.Empty(used);
        Assert.Single(ledger.UsedIndexes);
    }

    [Fact]
    public void SameSizeCatalogueReplacementInvalidatesDerivedCounts()
    {
        var ledger = new ApReceivedItemLedger();
        ledger.RegisterReceived(Receipt(1));
        long revision = ledger.Revision;
        ledger.ReplaceReceivedItems(new[] { Receipt(2) });
        Assert.Single(ledger.Received);
        Assert.True(ledger.Revision > revision);
        Assert.Equal(2, ledger.Received[0].Index);
    }

    [Fact]
    public void PlayersSharingAnApReceiptHaveIndependentRunConsumption()
    {
        var first = new ApReceivedItemLedger();
        var second = new ApReceivedItemLedger();
        first.RegisterReceived(Receipt(1));
        second.RegisterReceived(Receipt(1));
        first.MarkUsed(1);
        Assert.True(first.IsUsed(1));
        Assert.False(second.IsUsed(1));
    }

    private static IEnumerable<IndexedItemInfo> InterruptedHistory()
    {
        yield return Receipt(2);
        throw new InvalidOperationException("History was interrupted.");
    }

    private static IndexedItemInfo Receipt(int index, long itemId = 10001, long locationId = 50) =>
        new(new ItemInfo(
            new NetworkItem { Item = itemId, Location = locationId, Player = 1 },
            "Slay the Spire 2", "Slay the Spire 2", null!, new PlayerInfo()), index);
}
