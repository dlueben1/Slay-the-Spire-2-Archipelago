using System.Text.Json;
using StS2AP.Persistence;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class StrictCardChainTests
{
    private static string Seed(
        int revealOrdinal = 2,
        bool isRare = false,
        int? actIndex = 1,
        params string[] history) =>
        StrictCardChain.SeedMaterial("RUNSEED", 7, 0, 3, revealOrdinal, isRare, actIndex, history);

    [Fact]
    public void RepeatingThePicksRepeatsTheSeed()
    {
        Assert.Equal(Seed(history: ["CARD.A@0", "none"]), Seed(history: ["CARD.A@0", "none"]));
    }

    [Fact]
    public void ADifferentEarlierPickChangesTheSeed()
    {
        string tookA = Seed(history: ["CARD.A@0"]);
        Assert.NotEqual(tookA, Seed(history: ["CARD.B@0"]));
        Assert.NotEqual(tookA, Seed(history: ["CARD.A@1"]));
        Assert.NotEqual(tookA, Seed(history: [StrictCardChain.NoCardTaken]));
    }

    [Fact]
    public void RevealOrderAndRecipeSeparateRewardsWithTheSameHistory()
    {
        string seed = Seed();
        Assert.NotEqual(seed, Seed(revealOrdinal: 3));
        Assert.NotEqual(seed, Seed(isRare: true));
        Assert.NotEqual(seed, Seed(actIndex: 2));
    }

    [Fact]
    public void PickEntryRecordsTheTakenCardsAndTheRestOfTheOffer()
    {
        Assert.Equal("CARD.A@1<CARD.B@0,CARD.C@0",
            StrictCardChain.PickEntry([("CARD.A", 1)], [("CARD.B", 0), ("CARD.C", 0)]));
        Assert.Equal("CARD.A@0+CARD.B@1<CARD.C@0",
            StrictCardChain.PickEntry([("CARD.A", 0), ("CARD.B", 1)], [("CARD.C", 0)]));
        Assert.Equal("none<CARD.A@0,CARD.B@0,CARD.C@0",
            StrictCardChain.PickEntry([], [("CARD.A", 0), ("CARD.B", 0), ("CARD.C", 0)]));
    }

    [Fact]
    public void TheSamePickFromDifferentOffersIsADifferentEntry()
    {
        Assert.NotEqual(
            StrictCardChain.PickEntry([("CARD.INFLAME", 0)], [("CARD.ANGER", 0), ("CARD.CLASH", 1)]),
            StrictCardChain.PickEntry([("CARD.INFLAME", 0)], [("CARD.ANGER", 0), ("CARD.HAVOC", 0)]));
    }

    [Fact]
    public void ChainRoundTripsWithRunProgressAndDefaultsForOlderSaves()
    {
        var progress = new ApRunProgressState
        {
            StrictCardRevealCount = 3,
            StrictCardPickHistory = ["CARD.A@0", "none"],
        };
        ApRunProgressState restored = JsonSerializer.Deserialize<ApRunProgressState>(
            JsonSerializer.Serialize(progress))!;
        Assert.Equal(3, restored.StrictCardRevealCount);
        Assert.Equal(["CARD.A@0", "none"], restored.StrictCardPickHistory);

        ApRunProgressState older = JsonSerializer.Deserialize<ApRunProgressState>("""{"initialized":true}""")!;
        Assert.Equal(0, older.StrictCardRevealCount);
        Assert.Empty(older.StrictCardPickHistory);
    }
}
