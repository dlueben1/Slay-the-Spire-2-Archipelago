namespace StS2AP.Domain.Tests

open System
open FsCheck
open FsCheck.Xunit
open StS2AP.Domain
open global.Xunit

module RewardMaterializationTests =
    let private decode strategy =
        match RewardMaterialization.Decode(strategy) with
        | Ok policy -> policy
        | Error error -> failwithf "Expected valid assignment provenance, got %A" error

    let private validInputs = [| "ap_rng_owner_final_v1"; "replica_native_v1" |]

    [<Fact>]
    let ``owner final and restored native retain distinct provenance`` () =
        let owner = decode "ap_rng_owner_final_v1"
        let restored = decode "replica_native_v1"
        Assert.Equal("ap_rng_owner_final_v1", owner.StrategyId)
        Assert.Equal("replica_native_v1", restored.StrategyId)
        Assert.NotEqual<RewardMaterialization>(owner, restored)

    [<Theory>]
    [<InlineData(null)>]
    [<InlineData("")>]
    [<InlineData(" ")>]
    [<InlineData("REPLICA_NATIVE_V1")>]
    [<InlineData("replica_native_v1 ")>]
    [<InlineData("replica_native_v2")>]
    let ``malformed strategy is rejected without normalization or fallback`` (strategy: string) =
        match RewardMaterialization.Decode(strategy) with
        | Error (MaterializationError.UnknownStrategy original) -> Assert.Equal<string>(strategy, original)
        | actual -> failwithf "Expected UnknownStrategy, got %A" actual

    [<Theory>]
    [<InlineData(0)>]
    [<InlineData(1)>]
    let ``provenance eliminator invokes only its selected delegate`` (selected: int) =
        let policy = decode validInputs[selected]
        let calls = ResizeArray<int>()
        let handler index = Func<int>(fun () ->
            calls.Add(index)
            if index <> selected then failwith "An unselected branch was evaluated"
            index)
        let actual = policy.Match(handler 0, handler 1)
        Assert.Equal(selected, actual)
        Assert.Equal<int list>([ selected ], List.ofSeq calls)

    [<Property(MaxTest = 500)>]
    let ``valid provenance survives a wire round trip`` (NonNegativeInt selection) =
        let policy = decode validInputs[selection % validInputs.Length]
        RewardMaterialization.Decode(policy.StrategyId) = Ok policy

    [<Property(MaxTest = 500)>]
    let ``repeated decoding has no hidden history`` (strategy: string) =
        let first = RewardMaterialization.Decode(strategy)
        let unrelated = decode "replica_native_v1"
        unrelated.StrategyId = "replica_native_v1" && first = RewardMaterialization.Decode(strategy)

    [<Property(MaxTest = 500)>]
    let ``unknown wire data is retained exactly`` (suffix: string) =
        let strategy = "unsupported:" + suffix
        match RewardMaterialization.Decode(strategy) with
        | Error (MaterializationError.UnknownStrategy original) -> original = strategy
        | _ -> false

    [<Property(MaxTest = 500)>]
    let ``both provenances remain distinct after round trips`` (NonNegativeInt start) =
        let policies =
            [ for offset in 0 .. validInputs.Length - 1 do
                  let policy = decode validInputs[(start % validInputs.Length + offset) % validInputs.Length]
                  yield decode policy.StrategyId ]
        Set.ofList policies |> Set.count = validInputs.Length
