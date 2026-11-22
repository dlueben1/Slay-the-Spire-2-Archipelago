namespace StS2AP.Domain.Tests

open StS2AP.Domain
open global.Xunit

module RewardMaterializationTests =
    let private decode strategy =
        match RewardMaterialization.Decode(strategy) with
        | Ok policy -> policy
        | Error error -> failwithf "Expected valid assignment provenance, got %A" error

    [<Fact>]
    let ``owner final and replicated card retain distinct provenance`` () =
        let owner = decode "ap_rng_owner_final_v1"
        let replicated = decode "ap_rng_replicated_card_v1"
        Assert.Equal("ap_rng_owner_final_v1", owner.StrategyId)
        Assert.Equal("ap_rng_replicated_card_v1", replicated.StrategyId)
        Assert.NotEqual<RewardMaterialization>(owner, replicated)

    [<Theory>]
    [<InlineData(null)>]
    [<InlineData("")>]
    [<InlineData(" ")>]
    [<InlineData("REPLICA_NATIVE_V1")>]
    [<InlineData("replica_native_v1")>]
    [<InlineData("replica_native_v1 ")>]
    [<InlineData("replica_native_v2")>]
    let ``malformed strategy is rejected without normalization or fallback`` (strategy: string) =
        match RewardMaterialization.Decode(strategy) with
        | Error (MaterializationError.UnknownStrategy original) -> Assert.Equal<string>(strategy, original)
        | actual -> failwithf "Expected UnknownStrategy, got %A" actual
