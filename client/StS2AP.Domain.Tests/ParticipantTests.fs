namespace StS2AP.Domain.Tests

open System
open System.Collections.Generic
open FsCheck
open FsCheck.Xunit
open StS2AP.Domain
open global.Xunit

module ParticipantTests =
    let private identity kind seed team slot : ParticipationInput =
        { Kind = kind; RoomSeed = seed; ApTeamId = Nullable team; ApSlotId = Nullable slot }

    let private input () : ParticipantContributionInput =
        { SchemaVersion = 9
          Participation = identity 1 "seed" 0 1
          HasSettings = true
          ReceiptSourceReady = true
          RelicReceipts = Dictionary<int64, IReadOnlyList<int>>()
          ProgressiveAncients = Dictionary<int64, int>() }

    let private outcome (result: ContributionReadiness) =
        result.Match(Func<_, _>(fun _ -> "ready"), Func<_, _>(fun waiting -> waiting.Code), Func<_, _>(fun error -> error.Code))
    let private evaluate value = ContributionReadiness.Evaluate(9, value)
    let private ready value =
        (evaluate value).Match(Func<_, _>(id), Func<_, _>(fun waiting -> failwith waiting.Code), Func<_, _>(fun error -> failwith error.Description))

    [<Fact>]
    let ``missing contribution is waiting while explicit vanilla needs no AP payload`` () =
        Assert.Equal("missing-ap-contribution", evaluate Unchecked.defaultof<_> |> outcome)
        let vanilla =
            { input () with Participation = identity 0 null -1 -1; HasSettings = false
                            ReceiptSourceReady = false; RelicReceipts = null; ProgressiveAncients = null }
        Assert.Equal(ParticipantKind.VanillaGuest, (ready vanilla).Kind)

    [<Fact>]
    let ``all combinations of independently arriving prerequisites preserve blocker priority`` () =
        for hasIdentity in [false; true] do
            for hasSettings in [false; true] do
                for hasHistory in [false; true] do
                    let candidate =
                        { input () with
                            Participation = identity 1 (if hasIdentity then "seed" else null) 0 1
                            HasSettings = hasSettings
                            ReceiptSourceReady = hasHistory }
                    let expected =
                        if not hasIdentity then "incomplete-ap-identity"
                        elif not hasSettings then "ap-settings-incomplete"
                        elif not hasHistory then "ap-history-incomplete"
                        else "ready"
                    Assert.Equal(expected, evaluate candidate |> outcome)

    [<Fact>]
    let ``empty history is prepared but a previously ready contribution cannot certify newer input`` () =
        let original = input ()
        Assert.Equal(ParticipantKind.OwnApSlot, (ready original).Kind)
        Assert.Equal("ap-history-incomplete", evaluate { original with ReceiptSourceReady = false } |> outcome)

    [<Theory>]
    [<InlineData(0)>]
    [<InlineData(1)>]
    let ``schema is checked even for a vanilla guest`` kind =
        Assert.Equal("unsupported-ap-run-schema-8",
            evaluate { input () with SchemaVersion = 8; Participation = identity kind "seed" 0 1 } |> outcome)

    [<Theory>]
    [<InlineData(-1)>]
    [<InlineData(2)>]
    [<InlineData(99)>]
    let ``unknown participation cannot become a guest`` kind =
        Assert.Equal("unsupported-ap-participation", evaluate { input () with Participation = identity kind "seed" 0 1 } |> outcome)

    [<Theory>]
    [<InlineData("", 0, 1)>]
    [<InlineData(" ", 0, 1)>]
    [<InlineData("seed", -1, 1)>]
    [<InlineData("seed", 0, -1)>]
    let ``present but invalid identity is rejected rather than reported as preparation`` seed team slot =
        Assert.Equal("invalid-ap-identity", evaluate { input () with Participation = identity 1 seed team slot } |> outcome)

    [<Fact>]
    let ``receipt validation leaves input unchanged and reevaluates later mutations`` () =
        let indexes = ResizeArray<int>([1; 4])
        let relics = Dictionary<int64, IReadOnlyList<int>>()
        relics.Add(1L, indexes)
        let candidate = { input () with RelicReceipts = relics }
        Assert.Equal("ready", evaluate candidate |> outcome)
        Assert.Same(indexes, relics[1L])
        Assert.Equal<int>([1; 4], indexes)
        indexes[1] <- 1
        Assert.Equal("invalid-ap-history", evaluate candidate |> outcome)

    [<Fact>]
    let ``receipt indexes cannot be assigned to two characters`` () =
        let receipts = Dictionary<int64, IReadOnlyList<int>>()
        receipts.Add(1L, [| 7 |])
        receipts.Add(2L, [| 7 |])
        Assert.Equal("invalid-ap-history", evaluate { input () with RelicReceipts = receipts } |> outcome)

    [<Fact>]
    let ``invalid receipt maps are rejected only once history is prepared`` () =
        let candidate = { input () with RelicReceipts = null; ProgressiveAncients = null }
        Assert.Equal("ap-history-incomplete", evaluate { candidate with ReceiptSourceReady = false } |> outcome)
        Assert.Equal("invalid-ap-history", evaluate candidate |> outcome)
        let invalidCounts = Dictionary<int64, int>()
        invalidCounts.Add(0L, -1)
        Assert.Equal("invalid-ap-history", evaluate { input () with ProgressiveAncients = invalidCounts } |> outcome)

    [<Fact>]
    let ``all saved and current participation combinations are explicit`` () =
        for savedKind in [0; 1] do
            for currentKind in [0; 1] do
                let result = ParticipantResume.Match(9, 9, identity savedKind "seed" 0 1, identity currentKind "seed" 0 1)
                Assert.Equal((savedKind = currentKind), Result.isOk result)
                match result with
                | Ok matched -> Assert.Equal(savedKind, matched.Kind.WireValue)
                | Error (ParticipantResumeError.ParticipationMismatch _) -> ()
                | Error other -> failwithf "%A" other

    [<Fact>]
    let ``resume identity does not require fresh lobby settings or history`` () =
        let candidate = { input () with HasSettings = false; ReceiptSourceReady = false }
        Assert.Equal("ap-settings-incomplete", evaluate candidate |> outcome)
        Assert.True(ParticipantResume.Match(9, 9, candidate.Participation, candidate.Participation) |> Result.isOk)
        let guest = identity 0 null -1 -1
        Assert.True(ParticipantResume.Match(9, 9, guest, guest) |> Result.isOk)

    [<Fact>]
    let ``resume rejects unknown schemas kinds and missing or malformed identities`` () =
        let valid = identity 1 "seed" 0 1
        Assert.True(ParticipantResume.Match(9, 8, valid, valid) |> Result.isError)
        for invalid in [ identity -1 "seed" 0 1; identity 2 "seed" 0 1
                         identity 1 null 0 1; identity 1 " " 0 1; identity 1 "seed" -1 1
                         { valid with ApSlotId = Nullable() } ] do
            Assert.True(ParticipantResume.Match(9, 9, invalid, valid) |> Result.isError)
            Assert.True(ParticipantResume.Match(9, 9, valid, invalid) |> Result.isError)

    [<Property(MaxTest = 100)>]
    let ``own-slot resume matches exactly room team and slot``
        (NonNegativeInt teamA) (NonNegativeInt slotA) (NonNegativeInt teamB) (NonNegativeInt slotB) (sameSeed: bool) =
        let seedB = if sameSeed then "seed" else "SEED"
        let result = ParticipantResume.Match(9, 9, identity 1 "seed" teamA slotA, identity 1 seedB teamB slotB)
        Result.isOk result = (sameSeed && teamA = teamB && slotA = slotB)
