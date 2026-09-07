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

module private RewardEffectWireIds =
    [<Literal>]
    let SilkenTress = "silken_tress_used_v1"
    [<Literal>]
    let SilverCrucible = "silver_crucible_times_used_v1"

/// Absolute, validated transitions retain their identity across reopen and save/restore.
type RewardEffect =
    private
    | SilkenTressUsed
    | SilverCrucibleAdvanced of before: int

    member this.EffectId =
        match this with
        | SilkenTressUsed -> RewardEffectWireIds.SilkenTress
        | SilverCrucibleAdvanced _ -> RewardEffectWireIds.SilverCrucible

    member this.BeforeValue = match this with SilkenTressUsed -> 0 | SilverCrucibleAdvanced before -> before
    member this.AfterValue = this.BeforeValue + 1

    member this.Match(silkenTress: Func<'T>, silverCrucible: Func<int, int, 'T>) =
        match this with
        | SilkenTressUsed -> silkenTress.Invoke()
        | SilverCrucibleAdvanced before -> silverCrucible.Invoke(before, before + 1)

    /// A later Crucible transition can already subsume an earlier persisted effect.
    member this.NeedsApplication(current: int) =
        let unexpected () =
            Error (RewardDecodeError.Invalid $"effect '{this.EffectId}' expected {this.BeforeValue}, found {current}.")
        match this with
        | SilkenTressUsed ->
            if current = 1 then Ok false
            elif current = 0 then Ok true
            else unexpected ()
        | SilverCrucibleAdvanced before ->
            if current > before then Ok false
            elif current = before then Ok true
            else unexpected ()

    /// Validate the actual hook result rather than manufacturing the expected counter.
    static member ObserveSilkenTress(before: int, after: int) =
        if before = 0 && after = 1 then Ok SilkenTressUsed
        else Error (RewardDecodeError.Invalid $"had invalid effect '{RewardEffectWireIds.SilkenTress}'.")

    static member ObserveSilverCrucible(before: int, after: int) =
        if before >= 0 && before < Int32.MaxValue && after = before + 1 then
            Ok (SilverCrucibleAdvanced before)
        else Error (RewardDecodeError.Invalid $"had invalid effect '{RewardEffectWireIds.SilverCrucible}'.")

    static member Decode(effectId: string, before: int, after: int) =
        match effectId with
        | RewardEffectWireIds.SilkenTress -> RewardEffect.ObserveSilkenTress(before, after)
        | RewardEffectWireIds.SilverCrucible -> RewardEffect.ObserveSilverCrucible(before, after)
        | _ -> Error (RewardDecodeError.Invalid $"had invalid effect '{effectId}'.")

/// Untrusted adapter input only; never retained by a validated reward.
[<CLIMutable>]
type RewardEffectInput = { EffectId: string; BeforeValue: int; AfterValue: int }

module private RewardDecode =
    let freeze (items: seq<'T>) : IReadOnlyList<'T> = Array.AsReadOnly(Seq.toArray items)
    let invalid reason = Error (RewardDecodeError.Invalid reason)
    let traverse decode items =
        items |> Seq.fold (fun state item ->
            state |> Result.bind (fun values -> decode item |> Result.map (fun value -> value :: values))) (Ok [])
        |> Result.map List.rev

/// Native card wrappers retain this value and replace it only on an explicit reveal transition.
type CardRewardConfiguration private
    (recipe: CardRecipe, reveal: CardRevealState, canReroll: bool,
     policy: RewardMaterialization, effects: IReadOnlyList<RewardEffect>) =

    member _.Recipe = recipe
    member _.Reveal = reveal
    member _.HasBeenRevealed = reveal = CardRevealState.Revealed
    member _.CanReroll = canReroll
    member _.Policy = policy
    member _.Effects = effects
    member _.WithRevealed() = CardRewardConfiguration(recipe, CardRevealState.Revealed, canReroll, policy, effects)

    static member Decode(isRare, actIndex, revealed, canReroll, strategy, effects: RewardEffectInput array) =
        CardRecipe.Decode(isRare, actIndex)
        |> Result.bind (fun recipe ->
            RewardMaterialization.Decode(strategy)
            |> Result.mapError RewardDecodeError.Materialization
            |> Result.bind (fun policy ->
                if isNull effects || effects |> Array.exists (fun effect -> isNull (box effect)) then
                    RewardDecode.invalid "had missing persistent effects."
                elif (effects |> Array.distinctBy _.EffectId).Length <> effects.Length then
                    RewardDecode.invalid "repeated a persistent effect."
                elif effects.Length > 0 && not policy.AllowsPersistentEffects then
                    RewardDecode.invalid "attached persistent effects to a non-owner-final card."
                else
                    effects
                    |> RewardDecode.traverse (fun effect -> RewardEffect.Decode(effect.EffectId, effect.BeforeValue, effect.AfterValue))
                    |> Result.map (fun validated ->
                        CardRewardConfiguration(recipe,
                            (if revealed then CardRevealState.Revealed else CardRevealState.Unrevealed),
                            canReroll, policy, RewardDecode.freeze validated))))

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

type PotionRewardData internal (policy: RewardMaterialization, model: string) =
    member _.Policy = policy
    member _.Model = model

/// Internal adapter vocabulary; the C# wire enum retains its existing numeric contract.
[<RequireQualifiedAccess>]
type RewardInputKind = Card | Potion | Relic | Ancient | Unavailable

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
      Effects: RewardEffectInput array
      Models: string array
      UnavailableReason: string }

type private RewardShape =
    | Card of CardRewardData
    | Potion of PotionRewardData
    | Relic of string
    | AncientChoice of IReadOnlyList<string>
    | Unavailable of string

/// Completed reward snapshot. All collections are copied; no DTO or engine object is retained.
type MirroredReward private (origin: RewardOrigin, shape: RewardShape) =
    member _.Origin = origin
    member _.IsRelic = match shape with Relic _ -> true | Card _ | Potion _ | AncientChoice _ | Unavailable _ -> false
    member _.Effects : IReadOnlyList<RewardEffect> =
        match shape with
        | Card card -> card.Configuration.Effects
        | Potion _ | Relic _ | AncientChoice _ | Unavailable _ -> RewardDecode.freeze Seq.empty

    member _.Match(card: Func<CardRewardData, 'T>, potion: Func<PotionRewardData, 'T>,
                   relic: Func<string, 'T>, ancient: Func<IReadOnlyList<string>, 'T>, unavailable: Func<string, 'T>) =
        match shape with
        | Card value -> card.Invoke(value)
        | Potion value -> potion.Invoke(value)
        | Relic value -> relic.Invoke(value)
        | AncientChoice value -> ancient.Invoke(value)
        | Unavailable reason -> unavailable.Invoke(reason)

    static member private ValidateInput(input: MirroredRewardInput) =
        if isNull (box input) || isNull (box input.Origin) then RewardDecode.invalid "had no reward origin."
        elif isNull (box input.Kind) then RewardDecode.invalid "had no reward kind."
        elif input.Origin.ReceivedItemIndex < 0 || input.Origin.ApSlotId < 0 then RewardDecode.invalid "had an invalid receipt identity."
        elif isNull input.Models || input.Models |> Array.exists String.IsNullOrWhiteSpace then
            RewardDecode.invalid "had missing serialized models."
        elif isNull input.Effects || input.Effects |> Array.exists (fun effect -> isNull (box effect)) then
            RewardDecode.invalid "had missing persistent effects."
        else Ok ()

    static member private DecodeCardData(input: MirroredRewardInput) =
        // Native hooks can change option count; do not hardcode three cards.
        if input.Models.Length = 0 then RewardDecode.invalid "had no card choices."
        else
            CardRewardConfiguration.Decode(input.IsRare, input.ActIndex, input.Revealed, input.CanReroll,
                                           input.Strategy, input.Effects)
            |> Result.map (fun config -> CardRewardData(config, RewardDecode.freeze input.Models))

    /// Shares the completed-card validation but exposes only card data to saved-card callers.
    static member DecodeCard(input: MirroredRewardInput) =
        MirroredReward.ValidateInput(input)
        |> Result.bind (fun () ->
            match input.Kind with
            | RewardInputKind.Card -> MirroredReward.DecodeCardData(input)
            | RewardInputKind.Potion | RewardInputKind.Relic | RewardInputKind.Ancient | RewardInputKind.Unavailable ->
                RewardDecode.invalid "expected a saved card assignment.")

    static member Decode(input: MirroredRewardInput, ancientChoiceCount: int) =
        MirroredReward.ValidateInput(input)
        |> Result.bind (fun () ->
            let rejectNonCardEffects decode =
                if input.Effects.Length > 0 then
                    RewardDecode.invalid $"attached card/potion materialization data to {input.Kind}."
                else decode ()
            let shape =
                match input.Kind with
                | RewardInputKind.Card -> MirroredReward.DecodeCardData(input) |> Result.map Card
                | RewardInputKind.Potion ->
                    if input.Models.Length <> 1 || input.Effects.Length > 0 then RewardDecode.invalid "had an invalid potion assignment."
                    else
                        RewardMaterialization.Decode(input.Strategy)
                        |> Result.mapError RewardDecodeError.Materialization
                        |> Result.map (fun policy -> Potion (PotionRewardData(policy, input.Models[0])))
                | RewardInputKind.Relic -> rejectNonCardEffects (fun () ->
                    if input.Models.Length = 1 then Ok (Relic input.Models[0])
                    else RewardDecode.invalid "had an invalid Relic assignment.")
                | RewardInputKind.Ancient -> rejectNonCardEffects (fun () ->
                    if ancientChoiceCount > 0 && input.Models.Length = ancientChoiceCount then
                        Ok (AncientChoice (RewardDecode.freeze input.Models))
                    else RewardDecode.invalid "had an invalid Ancient assignment.")
                | RewardInputKind.Unavailable -> rejectNonCardEffects (fun () ->
                    if input.Models.Length = 0 && not (String.IsNullOrWhiteSpace input.UnavailableReason) then
                        Ok (Unavailable input.UnavailableReason)
                    else RewardDecode.invalid "had an invalid Unavailable assignment.")
            shape |> Result.map (fun value -> MirroredReward(input.Origin, value)))
