namespace StS2AP.Domain

open System

[<RequireQualifiedAccess>]
type StarterError =
    | InvalidState of detail: string
    | InvalidTransition of detail: string
    | RecipeChanged

    member this.Description =
        match this with
        | StarterError.InvalidState detail -> detail
        | StarterError.InvalidTransition detail -> detail
        | StarterError.RecipeChanged -> "The initialized starter recipe changed."

[<RequireQualifiedAccess>]
type StarterKind =
    | Card
    | Relic

    member this.Match(card: Func<'T>, relic: Func<'T>) =
        match this with
        | StarterKind.Card -> card.Invoke()
        | StarterKind.Relic -> relic.Invoke()

/// Unsupported is a state, never an applied tier of a supported starter.
[<RequireQualifiedAccess>]
type StarterTier =
    | None
    | Basic
    | Upgraded

    member this.WireValue =
        match this with
        | StarterTier.None -> 0
        | StarterTier.Basic -> 1
        | StarterTier.Upgraded -> 2

    static member Decode(value: int) =
        match value with
        | 0 -> Ok StarterTier.None
        | 1 -> Ok StarterTier.Basic
        | 2 -> Ok StarterTier.Upgraded
        | _ -> Error (StarterError.InvalidState "Invalid supported starter tier.")

    static member FromReceivedCount(count: int) =
        if count <= 0 then StarterTier.None
        elif count = 1 then StarterTier.Basic
        else StarterTier.Upgraded

[<RequireQualifiedAccess>]
type StarterContext =
    | Initialization
    | Reconciliation
    | LiveReceipt

/// Validated identities. These describe a mapping, not proof that a game model exists.
type StarterMapping =
    private
    | CardMapping of baseId: string * upgradedId: string
    | RelicMapping of baseId: string * upgradedId: string

    member this.BaseId =
        match this with
        | CardMapping (baseId, _) | RelicMapping (baseId, _) -> baseId

    member this.UpgradedId =
        match this with
        | CardMapping (_, upgradedId) | RelicMapping (_, upgradedId) -> upgradedId

    member this.Kind =
        match this with
        | CardMapping _ -> StarterKind.Card
        | RelicMapping _ -> StarterKind.Relic

    static member Decode(kind: StarterKind, baseId: string, upgradedId: string) =
        if String.IsNullOrWhiteSpace baseId || String.IsNullOrWhiteSpace upgradedId then
            Error (StarterError.InvalidState "A supported starter requires both model IDs.")
        else
            match kind with
            | StarterKind.Card -> Ok (CardMapping (baseId, upgradedId))
            | StarterKind.Relic -> Ok (RelicMapping (baseId, upgradedId))

/// Captured card and relic recipes are different alternatives. Exact JSON is preserved;
/// the C# engine boundary validates its schema and verifies the Orobas mapping.
type CapturedStarterRecipe =
    private
    | CardRecipe of mapping: StarterMapping * baseCardJson: string * toothJson: string
    | RelicRecipe of mapping: StarterMapping * baseRelicJson: string * touchJson: string

    member this.Mapping =
        match this with
        | CardRecipe (mapping, _, _) | RelicRecipe (mapping, _, _) -> mapping

    member this.SerializedBaseModel =
        match this with
        | CardRecipe (_, json, _) | RelicRecipe (_, json, _) -> json

    member this.SerializedUpgradeRelic =
        match this with
        | CardRecipe (_, _, json) | RelicRecipe (_, _, json) -> json

    static member Decode(mapping: StarterMapping, baseJson: string, upgradeJson: string) =
        if String.IsNullOrWhiteSpace baseJson || String.IsNullOrWhiteSpace upgradeJson then
            Error (StarterError.InvalidState "A captured starter requires both serialized models.")
        else
            match mapping.Kind with
            | StarterKind.Card -> Ok (CardRecipe (mapping, baseJson, upgradeJson))
            | StarterKind.Relic -> Ok (RelicRecipe (mapping, baseJson, upgradeJson))

/// Shared state machine: singleplayer uses a mapping; multiplayer uses a captured recipe.
type StarterState<'Recipe when 'Recipe: equality> =
    private
    | Uninitialized
    | Unsupported
    | Supported of recipe: 'Recipe * tier: StarterTier

    member this.Match(uninitialized: Func<'T>, unsupported: Func<'T>, supported: Func<'Recipe, StarterTier, 'T>) =
        match this with
        | Uninitialized -> uninitialized.Invoke()
        | Unsupported -> unsupported.Invoke()
        | Supported (recipe, tier) -> supported.Invoke(recipe, tier)

    member this.AppliedWireValue =
        match this with
        | Uninitialized | Unsupported -> -1
        | Supported (_, tier) -> tier.WireValue

/// Effects describe the next command only. Planning never marks a command as applied.
[<RequireQualifiedAccess>]
type StarterOperation =
    | RemoveBase
    | RestoreBase
    | GrantUpgrade

    member this.ResultTier =
        match this with
        | StarterOperation.RemoveBase -> StarterTier.None
        | StarterOperation.RestoreBase -> StarterTier.Basic
        | StarterOperation.GrantUpgrade -> StarterTier.Upgraded

    member this.Match(removeBase: Func<'T>, restoreBase: Func<'T>, grantUpgrade: Func<'T>) =
        match this with
        | StarterOperation.RemoveBase -> removeBase.Invoke()
        | StarterOperation.RestoreBase -> restoreBase.Invoke()
        | StarterOperation.GrantUpgrade -> grantUpgrade.Invoke()

type StarterPlan<'Recipe when 'Recipe: equality> internal
    (state: StarterState<'Recipe>, operations: StarterOperation list) =
    member _.State = state
    member _.Operations = operations

/// Pure decoding and transition policy; no game objects, networking, or mutable save DTOs.
type StarterProgression =
    static member DecodeSingleplayer(kind: StarterKind, baseId: string, upgradedId: string, tier: int) =
        if tier = -1 && isNull baseId && isNull upgradedId then
            Ok (Unsupported: StarterState<StarterMapping>)
        else
            match StarterMapping.Decode(kind, baseId, upgradedId), StarterTier.Decode tier with
            | Ok mapping, Ok applied -> Ok (Supported (mapping, applied))
            | Error error, _ | _, Error error -> Error error

    static member DecodeMultiplayer(kind: StarterKind, initialized: bool, supported: bool,
                                    baseId: string, upgradedId: string, baseJson: string,
                                    upgradeJson: string, tier: int) =
        let empty = isNull baseId && isNull upgradedId && isNull baseJson && isNull upgradeJson && tier = -1
        match initialized, supported with
        | false, false when empty -> Ok (Uninitialized: StarterState<CapturedStarterRecipe>)
        | true, false when empty -> Ok Unsupported
        | false, _ | true, false -> Error (StarterError.InvalidState "Inconsistent starter initialization/support fields.")
        | true, true ->
            match StarterMapping.Decode(kind, baseId, upgradedId), StarterTier.Decode tier with
            | Ok mapping, Ok applied ->
                CapturedStarterRecipe.Decode(mapping, baseJson, upgradeJson)
                |> Result.map (fun recipe -> Supported (recipe, applied))
            | Error error, _ | _, Error error -> Error error

    static member private Transitions(context: StarterContext, current: StarterTier, target: StarterTier) =
        match context, target with
        | StarterContext.LiveReceipt, StarterTier.None ->
            Error (StarterError.InvalidTransition "A live receipt cannot target tier None.")
        | (StarterContext.Initialization | StarterContext.Reconciliation | StarterContext.LiveReceipt),
          (StarterTier.None | StarterTier.Basic | StarterTier.Upgraded) ->
            match current, target with
            | StarterTier.None, StarterTier.None
            | StarterTier.Basic, StarterTier.Basic
            | StarterTier.Upgraded, StarterTier.Upgraded -> Ok []
            | StarterTier.None, StarterTier.Basic -> Ok [StarterOperation.RestoreBase]
            | StarterTier.None, StarterTier.Upgraded -> Ok [StarterOperation.RestoreBase; StarterOperation.GrantUpgrade]
            | StarterTier.Basic, StarterTier.Upgraded -> Ok [StarterOperation.GrantUpgrade]
            | StarterTier.Basic, StarterTier.None ->
                match context with
                | StarterContext.Initialization -> Ok [StarterOperation.RemoveBase]
                | StarterContext.Reconciliation | StarterContext.LiveReceipt ->
                    Error (StarterError.InvalidTransition "Only initialization may remove the vanilla starter.")
            | StarterTier.Upgraded, (StarterTier.None | StarterTier.Basic) ->
                Error (StarterError.InvalidTransition "An upgraded starter cannot be downgraded.")

    /// An incoming recipe can initialize state, but cannot replace a recipe already bound to a player.
    /// Its applied tier is an observation at authoring time; current per-player state remains authoritative.
    static member Plan(current: StarterState<'Recipe>, specification: StarterState<'Recipe>,
                       context: StarterContext, targetWireValue: int) =
        let adopted =
            match current, specification with
            | _, Uninitialized -> Error (StarterError.InvalidState "An action requires an initialized starter specification.")
            | Uninitialized, (Unsupported | Supported _) -> Ok specification
            | Unsupported, Unsupported -> Ok current
            | Supported (recipe, _), Supported (incoming, _) when recipe = incoming -> Ok current
            | Unsupported, Supported _ | Supported _, Unsupported | Supported _, Supported _ ->
                Error StarterError.RecipeChanged
        adopted |> Result.bind (fun state ->
            match state with
            | Uninitialized -> Error (StarterError.InvalidState "Cannot plan an uninitialized starter.")
            | Unsupported ->
                if targetWireValue = -1 then Ok (StarterPlan(state, []))
                else Error (StarterError.InvalidTransition "An unsupported starter cannot receive a supported tier.")
            | Supported (_, applied) ->
                StarterTier.Decode targetWireValue
                |> Result.bind (fun target -> StarterProgression.Transitions(context, applied, target))
                |> Result.map (fun operations -> StarterPlan(state, operations)))

    /// Called by C# only after the corresponding game command has completed successfully.
    static member AfterApplied(state: StarterState<'Recipe>, operation: StarterOperation) =
        match state with
        | Uninitialized | Unsupported -> Error (StarterError.InvalidState "Cannot apply a command to an unsupported/uninitialized starter.")
        | Supported (recipe, tier) ->
            match tier, operation with
            | StarterTier.Basic, StarterOperation.RemoveBase
            | StarterTier.None, StarterOperation.RestoreBase
            | StarterTier.Basic, StarterOperation.GrantUpgrade -> Ok (Supported (recipe, operation.ResultTier))
            | StarterTier.None, (StarterOperation.RemoveBase | StarterOperation.GrantUpgrade)
            | StarterTier.Basic, StarterOperation.RestoreBase
            | StarterTier.Upgraded, (StarterOperation.RemoveBase | StarterOperation.RestoreBase | StarterOperation.GrantUpgrade) ->
                Error (StarterError.InvalidTransition "Starter commands completed out of order.")
