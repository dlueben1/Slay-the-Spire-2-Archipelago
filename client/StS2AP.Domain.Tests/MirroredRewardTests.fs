namespace StS2AP.Domain.Tests

open System
open System.Collections.Generic
open StS2AP.Domain
open global.Xunit

module MirroredRewardTests =
    let private input (kind: RewardInputKind) : MirroredRewardInput =
        { Kind = kind
          Origin = RewardOrigin(4, 17, 42UL, "Reward", "Sender", "Location")
          IsRare = false; ActIndex = Nullable 1; Revealed = kind = RewardInputKind.Card; CanReroll = false
          Strategy = if kind = RewardInputKind.Card then "ap_rng_replicated_card_v1" else "ap_rng_owner_final_v1"
          Models = [| "model" |]; UnavailableReason = "" }

    let private require = function Ok value -> value | Error error -> failwithf "%A" error
    let private decode (value: MirroredRewardInput) : MirroredReward = MirroredReward.Decode(value, 3) |> require
    let private card (reward: MirroredReward) : CardRewardData =
        reward.Match(
            Func<CardRewardData, CardRewardData>(id),
            Func<PotionRewardData, CardRewardData>(fun _ -> failwith "potion"),
            Func<string, CardRewardData>(fun _ -> failwith "relic"),
            Func<IReadOnlyList<string>, CardRewardData>(fun _ -> failwith "ancient"),
            Func<string, CardRewardData>(fun _ -> failwith "unavailable"),
            Func<string, CardRewardData>(fun _ -> failwith "bonus"))

    let validRewardShapes =
        [| RewardInputKind.Card, 1; RewardInputKind.Potion, 1; RewardInputKind.Relic, 1
           RewardInputKind.Ancient, 3; RewardInputKind.Unavailable, 0; RewardInputKind.Bonus, 1 |]
        |> Array.map (fun (kind, count) -> [| box kind; box count |])

    [<Theory>]
    [<MemberData(nameof validRewardShapes)>]
    let ``every reward dispatches exactly its own payload`` kind count =
        let reward = decode { input kind with Models = Array.create count "model"; UnavailableReason = "No choices" }
        let selected = reward.Match(
            Func<CardRewardData, RewardInputKind>(fun _ -> RewardInputKind.Card),
            Func<PotionRewardData, RewardInputKind>(fun _ -> RewardInputKind.Potion),
            Func<string, RewardInputKind>(fun _ -> RewardInputKind.Relic),
            Func<IReadOnlyList<string>, RewardInputKind>(fun _ -> RewardInputKind.Ancient),
            Func<string, RewardInputKind>(fun _ -> RewardInputKind.Unavailable),
            Func<string, RewardInputKind>(fun _ -> RewardInputKind.Bonus))
        Assert.Equal(kind, selected)

    let invalidRewardShapes =
        [| RewardInputKind.Potion, 0; RewardInputKind.Potion, 2
           RewardInputKind.Relic, 0; RewardInputKind.Relic, 2; RewardInputKind.Ancient, 2
           RewardInputKind.Ancient, 4; RewardInputKind.Unavailable, 1
           RewardInputKind.Bonus, 0; RewardInputKind.Bonus, 2 |]
        |> Array.map (fun (kind, count) -> [| box kind; box count |])

    [<Theory>]
    [<MemberData(nameof invalidRewardShapes)>]
    let ``invalid reward shape is rejected before execution`` kind count =
        Assert.True(MirroredReward.Decode({ input kind with Models = Array.create count "model"; UnavailableReason = "No choices" }, 3) |> Result.isError)

    [<Fact>]
    let ``hook expanded card choices retain their exact order`` () =
        let models = [| "c4"; "c1"; "c3"; "c2" |]
        let decoded = decode { input RewardInputKind.Card with Models = models } |> card
        Assert.Equal<string>(models, decoded.Models)

    [<Fact>]
    let ``recipe distinguishes rare mapped act and current act fallback`` () =
        let rare = CardRecipe.Decode(true, Nullable()) |> require
        let mapped = CardRecipe.Decode(false, Nullable 2) |> require
        let current = CardRecipe.Decode(false, Nullable()) |> require
        Assert.True(rare.IsRareReward)
        Assert.False(current.IsRareReward)
        Assert.Equal(Nullable 2, mapped.ActIndex)
        Assert.False(current.ActIndex.HasValue)
        Assert.True(CardRecipe.Decode(true, Nullable 0) |> Result.isError)
        Assert.True(CardRecipe.Decode(false, Nullable -1) |> Result.isError)

    let provenanceRewardKinds =
        [| [| box RewardInputKind.Card |]; [| box RewardInputKind.Potion |] |]

    [<Theory>]
    [<MemberData(nameof provenanceRewardKinds)>]
    let ``removed native provenance is rejected for cards and potions`` kind =
        match MirroredReward.Decode({ input kind with Strategy = "replica_native_v1" }, 3) with
        | Error (RewardDecodeError.Materialization (MaterializationError.UnknownStrategy original)) ->
            Assert.Equal("replica_native_v1", original)
        | actual -> failwithf "Expected UnknownStrategy, got %A" actual

    [<Fact>]
    let ``snapshot copies models and exposes no mutable collections`` () =
        let models = [| "original" |]
        let original = input RewardInputKind.Card
        let decoded = decode { original with Models = models } |> card
        models[0] <- "changed"
        Assert.Equal("original", decoded.Models[0])
        Assert.Throws<NotSupportedException>(fun () -> (decoded.Models :?> IList<string>)[0] <- "mutation") |> ignore

    [<Fact>]
    let ``malformed nullable boundary values are rejected`` () =
        for value in [ { input RewardInputKind.Card with Models = null }
                       { input RewardInputKind.Card with Models = [| null |] }
                       { input RewardInputKind.Card with Origin = RewardOrigin(1, -1, 0UL, "", "", "") }
                       { input RewardInputKind.Card with Strategy = null }
                       { input RewardInputKind.Card with Kind = Unchecked.defaultof<_> } ] do
            Assert.True(MirroredReward.Decode(value, 3) |> Result.isError)
            Assert.True(MirroredReward.DecodeCard(value) |> Result.isError)

    [<Fact>]
    let ``card decoder rejects other reward kinds and empty choices`` () =
        for kind in [ RewardInputKind.Potion; RewardInputKind.Relic; RewardInputKind.Ancient; RewardInputKind.Unavailable; RewardInputKind.Bonus ] do
            Assert.True(MirroredReward.DecodeCard(input kind) |> Result.isError)
        Assert.True(MirroredReward.DecodeCard({ input RewardInputKind.Card with Models = [||] }) |> Result.isError)

    [<Fact>]
    let ``unopened menu cards carry only a recipe and cannot be saved as assignments`` () =
        let pending = { input RewardInputKind.Card with Models = [||]; Revealed = false }
        let decoded = decode pending |> card
        Assert.True(decoded.IsDeferred)
        Assert.False(decoded.Configuration.HasBeenRevealed)
        Assert.True(MirroredReward.DecodeCard(pending) |> Result.isError)

    [<Fact>]
    let ``deferred cards cannot claim rerolls revealed state or legacy generation`` () =
        let pending = { input RewardInputKind.Card with Models = [||]; Revealed = false }
        for invalid in [ { pending with Revealed = true }
                         { pending with CanReroll = true }
                         { pending with Strategy = "replica_native_v1" }
                         { pending with Strategy = "ap_rng_owner_final_v1" } ] do
            Assert.True(MirroredReward.Decode(invalid, 3) |> Result.isError)

    [<Fact>]
    let ``replicated strategy requires revealed final cards and cannot describe potions`` () =
        let replicated = { input RewardInputKind.Card with Revealed = false }
        Assert.True(MirroredReward.Decode(replicated, 3) |> Result.isError)
        Assert.True(MirroredReward.Decode({ replicated with Revealed = true }, 3) |> Result.isOk)
        Assert.True(MirroredReward.Decode({ replicated with Kind = RewardInputKind.Potion }, 3) |> Result.isError)

    [<Fact>]
    let ``owner final card assignments are rejected before execution`` () =
        let old = { input RewardInputKind.Card with Strategy = "ap_rng_owner_final_v1" }
        Assert.True(MirroredReward.Decode(old, 3) |> Result.isError)
        Assert.True(MirroredReward.DecodeCard(old) |> Result.isError)
