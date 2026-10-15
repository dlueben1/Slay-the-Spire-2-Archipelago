namespace StS2AP.Domain

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
type ParticipantKind =
    | VanillaGuest
    | OwnApSlot

    member this.WireValue = match this with VanillaGuest -> 0 | OwnApSlot -> 1
    static member Decode(value: int) =
        match value with 0 -> Ok VanillaGuest | 1 -> Ok OwnApSlot | unknown -> Error unknown

[<RequireQualifiedAccess>]
type SlotIdentityError = Incomplete | Invalid

/// Game-independent room/team/slot/player value. Server-qualified ownership stays in the C# session type.
type ParticipantSlot =
    private | Slot of roomSeed: string * teamId: int * slotId: int * playerNumber: int

    member this.RoomSeed = let (Slot(seed, _, _, _)) = this in seed
    member this.ApTeamId = let (Slot(_, team, _, _)) = this in team
    member this.ApSlotId = let (Slot(_, _, slot, _)) = this in slot
    member this.PlayerNumber = let (Slot(_, _, _, number)) = this in number
    override this.ToString() =
        $"{this.RoomSeed}/ap-team-{this.ApTeamId}/ap-slot-{this.ApSlotId}"
        + (if this.PlayerNumber = 1 then "" else $"/player-{this.PlayerNumber}")

    static member Decode(seed: string, team: Nullable<int>, slot: Nullable<int>, playerNumber: int) =
        if isNull seed || not team.HasValue || not slot.HasValue then Error SlotIdentityError.Incomplete
        elif String.IsNullOrWhiteSpace(seed) || team.Value < 0 || slot.Value < 0 || playerNumber < 1 || playerNumber > 4 then Error SlotIdentityError.Invalid
        else Ok (Slot(seed, team.Value, slot.Value, playerNumber))

    static member Decode(seed: string, team: Nullable<int>, slot: Nullable<int>) =
        ParticipantSlot.Decode(seed, team, slot, 1)

/// Untrusted save/lobby fields. A missing contribution is represented by missing input, not a guest.
[<CLIMutable>]
type ParticipationInput =
    { Kind: int; RoomSeed: string; ApTeamId: Nullable<int>; ApSlotId: Nullable<int>; PlayerNumber: int }

type ParticipantIdentity =
    private | GuestIdentity | OwnSlotIdentity of ParticipantSlot

    member this.Kind = match this with GuestIdentity -> ParticipantKind.VanillaGuest | OwnSlotIdentity _ -> ParticipantKind.OwnApSlot
    member this.Match(guest: Func<'T>, ownSlot: Func<ParticipantSlot, 'T>) =
        match this with GuestIdentity -> guest.Invoke() | OwnSlotIdentity slot -> ownSlot.Invoke(slot)

[<RequireQualifiedAccess>]
type PreparationBlocker =
    | MissingContribution | MissingIdentity | MissingSettings | MissingHistory

    member this.Code =
        match this with
        | MissingContribution -> "missing-ap-contribution"
        | MissingIdentity -> "incomplete-ap-identity"
        | MissingSettings -> "ap-settings-incomplete"
        | MissingHistory -> "ap-history-incomplete"

[<RequireQualifiedAccess>]
type ContributionError =
    | UnsupportedSchema of int
    | UnsupportedParticipation of int
    | InvalidIdentity
    | InvalidCoopPlayerNumber of playerCount: int * playerNumber: int
    | InvalidReceipts of string

    member this.Code =
        match this with
        | UnsupportedSchema version -> $"unsupported-ap-run-schema-{version}"
        | UnsupportedParticipation _ -> "unsupported-ap-participation"
        | InvalidIdentity -> "invalid-ap-identity"
        | InvalidCoopPlayerNumber _ -> "invalid-coop-player-number"
        | InvalidReceipts _ -> "invalid-ap-history"

    member this.Description =
        match this with
        | UnsupportedSchema version -> $"unsupported AP run schema {version}"
        | UnsupportedParticipation kind -> $"unsupported AP participation {kind}"
        | InvalidIdentity -> "invalid AP room/team/slot/player identity"
        | InvalidCoopPlayerNumber(count, number) -> $"Player {number}, player_count={count}"
        | InvalidReceipts reason -> reason

/// Borrowed input for synchronous validation. Collections must stay stable during Evaluate.
[<CLIMutable>]
type ParticipantContributionInput =
    { SchemaVersion: int
      Participation: ParticipationInput
      PlayerCount: int
      HasSettings: bool
      ReceiptSourceReady: bool
      RelicReceipts: IEnumerable<KeyValuePair<int64, IReadOnlyList<int>>>
      ProgressiveAncients: IReadOnlyDictionary<int64, int> }

/// A decision about the current input, not a retained contribution or a launch authorization.
type ContributionReadiness =
    private | Ready of ParticipantIdentity | Waiting of PreparationBlocker | Rejected of ContributionError

    member this.Match(ready: Func<ParticipantIdentity, 'T>, waiting: Func<PreparationBlocker, 'T>, rejected: Func<ContributionError, 'T>) =
        match this with
        | Ready participant -> ready.Invoke(participant)
        | Waiting blocker -> waiting.Invoke(blocker)
        | Rejected error -> rejected.Invoke(error)

    static member private ValidateReceipts(input: ParticipantContributionInput) =
        if isNull input.RelicReceipts || isNull input.ProgressiveAncients then
            Error (ContributionError.InvalidReceipts "Missing initial receipt collections.")
        else
            let indexes = HashSet<int>()
            let mutable valid = true
            for KeyValue(character, receipts) in input.RelicReceipts do
                if character < 1L || isNull receipts then valid <- false
                else
                    for index in receipts do
                        if index < 0 || not (indexes.Add(index)) then valid <- false
            for KeyValue(character, count) in input.ProgressiveAncients do
                if character < 1L || count < 0 then valid <- false
            if not valid then Error (ContributionError.InvalidReceipts "Invalid character, receipt index, duplicate receipt, or Ancient count.")
            else Ok ()

    /// Evaluates one current contribution, not the whole roster and not the player's Ready click.
    static member Evaluate(expectedSchema: int, input: ParticipantContributionInput) =
        if obj.ReferenceEquals(input, null) then Waiting PreparationBlocker.MissingContribution
        elif input.SchemaVersion <> expectedSchema then Rejected (ContributionError.UnsupportedSchema input.SchemaVersion)
        elif obj.ReferenceEquals(input.Participation, null) then Rejected ContributionError.InvalidIdentity
        else
            match ParticipantKind.Decode(input.Participation.Kind) with
            | Error kind -> Rejected (ContributionError.UnsupportedParticipation kind)
            | Ok ParticipantKind.VanillaGuest -> Ready GuestIdentity
            | Ok ParticipantKind.OwnApSlot ->
                match ParticipantSlot.Decode(input.Participation.RoomSeed, input.Participation.ApTeamId, input.Participation.ApSlotId, input.Participation.PlayerNumber) with
                | Error SlotIdentityError.Incomplete -> Waiting PreparationBlocker.MissingIdentity
                | Error SlotIdentityError.Invalid -> Rejected ContributionError.InvalidIdentity
                | Ok slot ->
                    if not input.HasSettings then Waiting PreparationBlocker.MissingSettings
                    elif input.PlayerCount < 1 || input.PlayerCount > 4 || slot.PlayerNumber > input.PlayerCount then
                        Rejected (ContributionError.InvalidCoopPlayerNumber(input.PlayerCount, slot.PlayerNumber))
                    elif not input.ReceiptSourceReady then Waiting PreparationBlocker.MissingHistory
                    else
                        match ContributionReadiness.ValidateReceipts(input) with
                        | Error error -> Rejected error
                        | Ok () -> Ready (OwnSlotIdentity slot)

[<RequireQualifiedAccess>]
type ParticipantResumeError =
    | UnsupportedSchema of int
    | UnsupportedSavedParticipation of int
    | UnsupportedCurrentParticipation of int
    | ParticipationMismatch of saved: ParticipantKind * current: ParticipantKind
    | InvalidSavedIdentity of SlotIdentityError
    | InvalidCurrentIdentity of SlotIdentityError
    | SlotMismatch of saved: ParticipantSlot * current: ParticipantSlot

    member this.Description =
        match this with
        | UnsupportedSchema _ -> "This multiplayer save uses an unsupported schema. Start a new campaign."
        | UnsupportedSavedParticipation kind -> $"unsupported saved participation {kind}"
        | UnsupportedCurrentParticipation kind -> $"unsupported current participation {kind}"
        | ParticipationMismatch(saved, current) -> $"saved participation is {saved}, but this process entered as {current}"
        | InvalidSavedIdentity _ -> "the saved AP slot identity is incomplete or invalid"
        | InvalidCurrentIdentity _ -> "the currently prepared AP slot identity is incomplete or invalid"
        | SlotMismatch(saved, current) -> $"saved={saved}, prepared={current}"

[<AbstractClass; Sealed>]
type ParticipantResume private () =
    /// Deliberately independent of connection status, settings readiness, and live receipt history.
    static member Match(expectedSchema: int, savedSchema: int, saved: ParticipationInput, current: ParticipationInput) =
        if savedSchema <> expectedSchema then Error (ParticipantResumeError.UnsupportedSchema savedSchema)
        elif obj.ReferenceEquals(saved, null) then Error (ParticipantResumeError.InvalidSavedIdentity SlotIdentityError.Incomplete)
        elif obj.ReferenceEquals(current, null) then Error (ParticipantResumeError.InvalidCurrentIdentity SlotIdentityError.Incomplete)
        else
            match ParticipantKind.Decode(saved.Kind), ParticipantKind.Decode(current.Kind) with
            | Error kind, _ -> Error (ParticipantResumeError.UnsupportedSavedParticipation kind)
            | _, Error kind -> Error (ParticipantResumeError.UnsupportedCurrentParticipation kind)
            | Ok ParticipantKind.VanillaGuest, Ok ParticipantKind.OwnApSlot ->
                Error (ParticipantResumeError.ParticipationMismatch(ParticipantKind.VanillaGuest, ParticipantKind.OwnApSlot))
            | Ok ParticipantKind.OwnApSlot, Ok ParticipantKind.VanillaGuest ->
                Error (ParticipantResumeError.ParticipationMismatch(ParticipantKind.OwnApSlot, ParticipantKind.VanillaGuest))
            | Ok ParticipantKind.VanillaGuest, Ok ParticipantKind.VanillaGuest -> Ok GuestIdentity
            | Ok ParticipantKind.OwnApSlot, Ok ParticipantKind.OwnApSlot ->
                match ParticipantSlot.Decode(saved.RoomSeed, saved.ApTeamId, saved.ApSlotId, saved.PlayerNumber),
                      ParticipantSlot.Decode(current.RoomSeed, current.ApTeamId, current.ApSlotId, current.PlayerNumber) with
                | Error error, _ -> Error (ParticipantResumeError.InvalidSavedIdentity error)
                | _, Error error -> Error (ParticipantResumeError.InvalidCurrentIdentity error)
                | Ok savedSlot, Ok currentSlot when savedSlot <> currentSlot -> Error (ParticipantResumeError.SlotMismatch(savedSlot, currentSlot))
                | Ok savedSlot, Ok _ -> Ok (OwnSlotIdentity savedSlot)
