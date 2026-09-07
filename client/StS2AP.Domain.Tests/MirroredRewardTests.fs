namespace StS2AP.Domain.Tests

open System
open System.Collections.Generic
open FsCheck
open FsCheck.Xunit
open StS2AP.Domain
open global.Xunit

module MirroredRewardTests =
    let private input kind =
        { Kind = kind
          Origin = RewardOrigin(4, 17, 42UL, "Reward", "Sender", "Location")
          IsRare = false; ActIndex = Nullable 1; Revealed = false; CanReroll = false
          Strategy = "ap_rng_owner_final_v1"
          Effects = [||]; Models = [| "model" |]; UnavailableReason = "" }

    let private require = function Ok value -> value | Error error -> failwithf "%A" error
    let private decode value = MirroredReward.Decode(value, 3) |> require
    let private card (reward: MirroredReward) =
        reward.Match(Func<_, _>(id), Func<_, _>(fun _ -> failwith "potion"),
                     Func<_, _>(fun _ -> failwith "relic"), Func<_, _>(fun _ -> failwith "ancient"), Func<_, _>(fun _ -> failwith "unavailable"))
    let private effect name before after = { EffectId = name; BeforeValue = before; AfterValue = after }

    let validRewardShapes =
        [| RewardInputKind.Card, 1; RewardInputKind.Potion, 1; RewardInputKind.Relic, 1
           RewardInputKind.Ancient, 3; RewardInputKind.Unavailable, 0 |]
        |> Array.map (fun (kind, count) -> [| box kind; box count |])

    [<Theory>]
    [<MemberData(nameof validRewardShapes)>]
    let ``every reward dispatches exactly its own payload`` kind count =
        let reward = decode { input kind with Models = Array.create count "model"; UnavailableReason = "No choices" }
        let selected = reward.Match(Func<_, _>(fun _ -> RewardInputKind.Card), Func<_, _>(fun _ -> RewardInputKind.Potion),
                                    Func<_, _>(fun _ -> RewardInputKind.Relic), Func<_, _>(fun _ -> RewardInputKind.Ancient), Func<_, _>(fun _ -> RewardInputKind.Unavailable))
        Assert.Equal(kind, selected)

    let invalidRewardShapes =
        [| RewardInputKind.Card, 0; RewardInputKind.Potion, 0; RewardInputKind.Potion, 2
           RewardInputKind.Relic, 0; RewardInputKind.Relic, 2; RewardInputKind.Ancient, 2
           RewardInputKind.Ancient, 4; RewardInputKind.Unavailable, 1 |]
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
    let ``restored native assignment never replays generation`` kind =
        let reward = decode { input kind with Strategy = "replica_native_v1" }
        let identify (policy: RewardMaterialization) =
            policy.Match(Func<_>(fun () -> "owner"), Func<_>(fun () -> "restored"))
        Assert.Equal("restored", reward.Match(Func<_, _>(fun card -> identify card.Configuration.Policy), Func<_, _>(fun potion -> identify potion.Policy),
                                             Func<_, _>(fun _ -> "relic"), Func<_, _>(fun _ -> "ancient"), Func<_, _>(fun _ -> "unavailable")))

    [<Fact>]
    let ``snapshot copies models and effect inputs and exposes no mutable collections`` () =
        let effects = [| effect "silver_crucible_times_used_v1" 4 5 |]
        let models = [| "original" |]
        let original = input RewardInputKind.Card
        let decoded = decode { original with Models = models; Effects = effects } |> card
        models[0] <- "changed"
        effects[0] <- effect "silken_tress_used_v1" 0 1
        Assert.Equal("original", decoded.Models[0])
        Assert.Equal(4, decoded.Configuration.Effects[0].BeforeValue)
        Assert.Throws<NotSupportedException>(fun () -> (decoded.Models :?> IList<string>)[0] <- "mutation") |> ignore

    [<Fact>]
    let ``reveal transition retains recipe policy reroll and persisted effects`` () =
        let config = decode { input RewardInputKind.Card with CanReroll = true; Effects = [| effect "silken_tress_used_v1" 0 1 |] } |> card |> _.Configuration
        let revealed = config.WithRevealed()
        Assert.False(config.HasBeenRevealed)
        Assert.True(revealed.HasBeenRevealed)
        Assert.True(revealed.WithRevealed().HasBeenRevealed)
        Assert.Same(config.Recipe, revealed.Recipe)
        Assert.Same(config.Policy, revealed.Policy)
        Assert.Same(config.Effects, revealed.Effects)
        Assert.True(revealed.CanReroll)

    [<Fact>]
    let ``effects require a unique owner final card transition`` () =
        let effects = [| effect "silken_tress_used_v1" 0 1 |]
        for value in [ { input RewardInputKind.Card with Effects = Array.append effects effects }
                       { input RewardInputKind.Card with Effects = effects; Strategy = "replica_native_v1" }
                       { input RewardInputKind.Potion with Effects = effects }
                       { input RewardInputKind.Relic with Effects = effects } ] do
            Assert.True(MirroredReward.Decode(value, 3) |> Result.isError)
        Assert.Equal(1, (decode { input RewardInputKind.Card with Effects = effects }).Effects.Count)

    [<Fact>]
    let ``malformed nullable boundary values are rejected`` () =
        for value in [ { input RewardInputKind.Card with Models = null }
                       { input RewardInputKind.Card with Models = [| null |] }
                       { input RewardInputKind.Card with Effects = null }
                       { input RewardInputKind.Card with Effects = [| Unchecked.defaultof<_> |] }
                       { input RewardInputKind.Card with Origin = RewardOrigin(1, -1, 0UL, "", "", "") }
                       { input RewardInputKind.Card with Strategy = null }
                       { input RewardInputKind.Card with Kind = Unchecked.defaultof<_> } ] do
            Assert.True(MirroredReward.Decode(value, 3) |> Result.isError)
            Assert.True(MirroredReward.DecodeCard(value) |> Result.isError)

    [<Fact>]
    let ``card decoder rejects other reward kinds and empty choices`` () =
        for kind in [ RewardInputKind.Potion; RewardInputKind.Relic; RewardInputKind.Ancient; RewardInputKind.Unavailable ] do
            Assert.True(MirroredReward.DecodeCard(input kind) |> Result.isError)
        Assert.True(MirroredReward.DecodeCard({ input RewardInputKind.Card with Models = [||] }) |> Result.isError)

    [<Fact>]
    let ``effect overflow and unknown effect identities are rejected`` () =
        Assert.True(RewardEffect.Decode("silver_crucible_times_used_v1", Int32.MaxValue, Int32.MinValue) |> Result.isError)
        Assert.True(RewardEffect.Decode("silver_crucible_times_used_v1", -1, 0) |> Result.isError)
        Assert.True(RewardEffect.Decode("silken_tress_used_v1", 1, 2) |> Result.isError)
        Assert.True(RewardEffect.Decode("unknown", 0, 1) |> Result.isError)

    [<Property(MaxTest = 500)>]
    let ``crucible application is idempotent and later progress subsumes earlier effects`` (NonNegativeInt value) =
        let before = value % Int32.MaxValue
        let decoded = RewardEffect.ObserveSilverCrucible(before, before + 1) |> require
        RewardEffect.Decode(decoded.EffectId, decoded.BeforeValue, decoded.AfterValue) = Ok decoded
        && decoded.NeedsApplication(before) = Ok true
        && decoded.NeedsApplication(before + 1) = Ok false
        && decoded.NeedsApplication(Int32.MaxValue) = Ok false
        && (decoded.NeedsApplication(-1) |> Result.isError)

    [<Fact>]
    let ``silken tress applies once and rejects unrelated state`` () =
        let decoded = RewardEffect.ObserveSilkenTress(0, 1) |> require
        Assert.Equal(Ok decoded, RewardEffect.Decode(decoded.EffectId, decoded.BeforeValue, decoded.AfterValue))
        Assert.Equal(Ok true, decoded.NeedsApplication(0))
        Assert.Equal(Ok false, decoded.NeedsApplication(1))
        Assert.True(decoded.NeedsApplication(2) |> Result.isError)

    [<Theory>]
    [<InlineData(-1, 0)>]
    [<InlineData(0, 0)>]
    [<InlineData(0, 2)>]
    [<InlineData(1, 0)>]
    [<InlineData(Int32.MaxValue, Int32.MinValue)>]
    let ``observed effects reject unchanged reversed skipped and overflowing transitions`` before after =
        Assert.True(RewardEffect.ObserveSilkenTress(before, after) |> Result.isError)
        Assert.True(RewardEffect.ObserveSilverCrucible(before, after) |> Result.isError)

    [<Fact>]
    let ``silken tress rejects an otherwise valid crucible transition`` () =
        Assert.True(RewardEffect.ObserveSilkenTress(1, 2) |> Result.isError)
        Assert.True(RewardEffect.ObserveSilverCrucible(1, 2) |> Result.isOk)
