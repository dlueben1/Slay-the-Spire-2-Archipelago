namespace StS2AP.Domain

open System

/// Unknown provenance for a completed card or potion assignment.
[<RequireQualifiedAccess>]
type MaterializationError =
    | UnknownStrategy of wireId: string

    member this.Match(unknownStrategy: Func<string, 'T>) : 'T =
        match this with
        | MaterializationError.UnknownStrategy wireId -> unknownStrategy.Invoke(wireId)

/// Provenance of a completed reward. Both cases restore final models without generation.
type RewardMaterialization =
    private
    | OwnerFinal
    | RestoredReplicaNative

    member this.StrategyId =
        match this with
        | OwnerFinal -> "ap_rng_owner_final_v1"
        | RestoredReplicaNative -> "replica_native_v1"

    member this.AllowsPersistentEffects =
        match this with
        | OwnerFinal -> true
        | RestoredReplicaNative -> false

    member this.Match(ownerFinal: Func<'T>, restoredReplicaNative: Func<'T>) : 'T =
        match this with
        | OwnerFinal -> ownerFinal.Invoke()
        | RestoredReplicaNative -> restoredReplicaNative.Invoke()

    /// Checks the received provenance. Unknown IDs (including null) are errors.
    static member Decode(strategyId: string) =
        match strategyId with
        | "ap_rng_owner_final_v1" -> Ok OwnerFinal
        | "replica_native_v1" -> Ok RestoredReplicaNative
        | unknown -> Error (MaterializationError.UnknownStrategy unknown)
