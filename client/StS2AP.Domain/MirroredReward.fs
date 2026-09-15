namespace StS2AP.Domain

open System
open System.Collections.Generic

/// Structural contract failures. Native model JSON is interpreted by the C# codec.
[<RequireQualifiedAccess>]
type RewardDecodeError =
    | Invalid of reason: string
    | Materialization of MaterializationError

    member this.Match(invalid: Func<string, 'T>, materialization: Func<MaterializationError, 'T>) =
        match this with
        | RewardDecodeError.Invalid reason -> invalid.Invoke(reason)
        | RewardDecodeError.Materialization error -> materialization.Invoke(error)

/// Regular rewards may deliberately use current-act odds when no stable act mapping exists.
type CardRecipe =
    private
    | Rare
    | ActBased of int
    /// Fallback for a regular AP card reward that cannot be mapped to a stable act.
    | CurrentAct

    member this.IsRareReward = match this with Rare -> true | ActBased _ | CurrentAct -> false
    member this.ActIndex = match this with ActBased act -> Nullable act | Rare | CurrentAct -> Nullable()

    static member Decode(isRare: bool, actIndex: Nullable<int>) =
        match isRare, Option.ofNullable actIndex with
        | true, None -> Ok Rare
        | false, Some act when act >= 0 -> Ok (ActBased act)
        | false, None -> Ok CurrentAct
        | _ -> Error (RewardDecodeError.Invalid "had an invalid card recipe.")

[<RequireQualifiedAccess>]
type CardRevealState = Unrevealed | Revealed

module private RewardDecode =
    let freeze (items: seq<'T>) : IReadOnlyList<'T> = Array.AsReadOnly(Seq.toArray items)
    let invalid reason = Error (RewardDecodeError.Invalid reason)

/// Native card wrappers retain this value and replace it only on an explicit reveal transition.
type CardRewardConfiguration private
    (recipe: CardRecipe, reveal: CardRevealState, canReroll: bool, policy: RewardMaterialization) =

    member _.Recipe = recipe
    member _.Reveal = reveal
    member _.HasBeenRevealed = reveal = CardRevealState.Revealed
    member _.CanReroll = canReroll
    member _.Policy = policy

    static member Decode(isRare, actIndex, revealed, canReroll, strategy) =
        CardRecipe.Decode(isRare, actIndex)
        |> Result.bind (fun recipe ->
            RewardMaterialization.Decode(strategy)
            |> Result.mapError RewardDecodeError.Materialization
            |> Result.bind (fun policy ->
                if policy.StrategyId <> "ap_rng_replicated_card_v1" then
                    RewardDecode.invalid "had an unsupported card materialization strategy."
                else
                    Ok (CardRewardConfiguration(recipe,
                        (if revealed then CardRevealState.Revealed else CardRevealState.Unrevealed),
                        canReroll, policy))))

/// Receipt scope and presentation only. Network authentication stays in C#.
type RewardOrigin(apSlotId: int, receivedItemIndex: int, ownerNetId: uint64,
                  itemName: string, senderName: string, foundLocation: string) =
    member _.ApSlotId = apSlotId
    member _.ReceivedItemIndex = receivedItemIndex
    member _.OwnerNetId = ownerNetId
    member _.ItemName = itemName
    member _.SenderName = senderName
    member _.FoundLocation = foundLocation
    member _.ReceiptIdentity = $"{apSlotId}:{receivedItemIndex}"

type CardRewardData internal (configuration: CardRewardConfiguration, models: IReadOnlyList<string>) =
    member _.Configuration = configuration
    member _.Models = models
    /// Only menu entries may defer generation. Saved assignments always contain final cards.
    member _.IsDeferred = models.Count = 0

type PotionRewardData internal (policy: RewardMaterialization, model: string) =
    member _.Policy = policy
    member _.Model = model

/// Internal adapter vocabulary; the C# wire enum retains its existing numeric contract.
[<RequireQualifiedAccess>]
type RewardInputKind = Card | Potion | Relic | Ancient | Unavailable | Bonus

/// Flat fields exist only at the decoder boundary, mirroring the explicit C# wire DTO.
[<CLIMutable>]
type MirroredRewardInput =
    { Kind: RewardInputKind
      Origin: RewardOrigin
      IsRare: bool
      ActIndex: Nullable<int>
      Revealed: bool
      CanReroll: bool
      Strategy: string
      Models: string array
      UnavailableReason: string }

type private RewardShape =
    | Card of CardRewardData
    | Potion of PotionRewardData
    | Relic of string
    | Bonus of string
    | AncientChoice of IReadOnlyList<string>
    | Unavailable of string

/// Completed reward snapshot. All collections are copied; no DTO or engine object is retained.
type MirroredReward private (origin: RewardOrigin, shape: RewardShape) =
    member _.Origin = origin
    member _.IsRelic = match shape with Relic _ -> true | Card _ | Potion _ | AncientChoice _ | Unavailable _ | Bonus _ -> false
    member _.Match(card: Func<CardRewardData, 'T>, potion: Func<PotionRewardData, 'T>,
                   relic: Func<string, 'T>, ancient: Func<IReadOnlyList<string>, 'T>, unavailable: Func<string, 'T>, bonus: Func<string, 'T>) =
        match shape with
        | Card value -> card.Invoke(value)
        | Potion value -> potion.Invoke(value)
        | Relic value -> relic.Invoke(value)
        | Bonus value -> bonus.Invoke(value)
        | AncientChoice value -> ancient.Invoke(value)
        | Unavailable reason -> unavailable.Invoke(reason)

    static member private ValidateInput(input: MirroredRewardInput) =
        if isNull (box input) || isNull (box input.Origin) then RewardDecode.invalid "had no reward origin."
        elif isNull (box input.Kind) then RewardDecode.invalid "had no reward kind."
        elif input.Origin.ReceivedItemIndex < 0 || input.Origin.ApSlotId < 0 then RewardDecode.invalid "had an invalid receipt identity."
        elif isNull input.Models || input.Models |> Array.exists String.IsNullOrWhiteSpace then
            RewardDecode.invalid "had missing serialized models."
        else Ok ()

    static member private DecodeCardData(input: MirroredRewardInput, allowDeferred: bool) =
        // Native hooks can change option count; do not hardcode three cards.
        let deferred = input.Models.Length = 0
        if deferred && not (allowDeferred && not input.Revealed && not input.CanReroll
                            && input.Strategy = "ap_rng_replicated_card_v1") then
            RewardDecode.invalid "had invalid deferred card choices."
        elif not deferred && input.Strategy = "ap_rng_replicated_card_v1" && not input.Revealed then
            RewardDecode.invalid "had unrevealed replicated final cards."
        else
            CardRewardConfiguration.Decode(input.IsRare, input.ActIndex, input.Revealed, input.CanReroll,
                                           input.Strategy)
            |> Result.map (fun config -> CardRewardData(config, RewardDecode.freeze input.Models))

    /// Shares the completed-card validation but exposes only card data to saved-card callers.
    static member DecodeCard(input: MirroredRewardInput) =
        MirroredReward.ValidateInput(input)
        |> Result.bind (fun () ->
            match input.Kind with
            | RewardInputKind.Card -> MirroredReward.DecodeCardData(input, false)
            | RewardInputKind.Potion | RewardInputKind.Relic | RewardInputKind.Ancient | RewardInputKind.Unavailable | RewardInputKind.Bonus ->
                RewardDecode.invalid "expected a saved card assignment.")

    static member Decode(input: MirroredRewardInput, ancientChoiceCount: int) =
        MirroredReward.ValidateInput(input)
        |> Result.bind (fun () ->
            let shape =
                match input.Kind with
                | RewardInputKind.Card -> MirroredReward.DecodeCardData(input, true) |> Result.map Card
                | RewardInputKind.Potion ->
                    if input.Models.Length <> 1 || input.Strategy = "ap_rng_replicated_card_v1" then
                        RewardDecode.invalid "had an invalid potion assignment."
                    else
                        RewardMaterialization.Decode(input.Strategy)
                        |> Result.mapError RewardDecodeError.Materialization
                        |> Result.map (fun policy -> Potion (PotionRewardData(policy, input.Models[0])))
                | RewardInputKind.Relic ->
                    if input.Models.Length = 1 then Ok (Relic input.Models[0])
                    else RewardDecode.invalid "had an invalid Relic assignment."
                | RewardInputKind.Bonus ->
                    if input.Models.Length = 1 then Ok (Bonus input.Models[0])
                    else RewardDecode.invalid "had an invalid Bonus assignment."
                | RewardInputKind.Ancient ->
                    if ancientChoiceCount > 0 && input.Models.Length = ancientChoiceCount then
                        Ok (AncientChoice (RewardDecode.freeze input.Models))
                    else RewardDecode.invalid "had an invalid Ancient assignment."
                | RewardInputKind.Unavailable ->
                    if input.Models.Length = 0 && not (String.IsNullOrWhiteSpace input.UnavailableReason) then
                        Ok (Unavailable input.UnavailableReason)
                    else RewardDecode.invalid "had an invalid Unavailable assignment."
            shape |> Result.map (fun value -> MirroredReward(input.Origin, value)))
