# Mirrored reward domain migration

Implemented 2026-09-06 on `multiplayer-squashed`. The initial strategy-only F# guard has
been replaced by a completed reward snapshot that the dispatcher actually consumes.
The unused new-native generation path was subsequently removed at the user's request.
This is a domain and integration refactor, not a replacement of the reward ledger.

## Ownership and control flow

`client/StS2AP.Domain/MirroredReward.fs` owns reward shapes, recipes, reveal state,
assignment provenance, structural validation, and persistent-effect decisions. It has no
MegaCrit, Godot, Harmony, RitsuLib, or JSON dependency.

`client/StS2AP/DomainAdapters/MirroredRewardAdapter.cs` owns explicit DTO conversion,
JSON object syntax checks, error conversion with receipt context, effect encoding,
and the saved-card decoder. Exact serialized model strings are retained, including
their order, for deterministic comparison. MegaCrit still interprets their contents;
a syntactically valid JSON object is not proof of a valid game model.

The execution chain in `client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs` is:

1. Owner: `BuildOwnerMenuSpec` / `BuildAssignedSpec` use native factories and mutable
   construction DTOs. Card factories consume a validated `CardRewardConfiguration`;
   after native hooks complete, their observed effects are decoded into the final
   configuration. The completed menu enters `DecodeRewards` once before sending or
   displaying it, including in singleplayer and when the owner is the host.
2. Receiver: `HandleMenuSpec` authenticates the sender and schedules the main loop.
   `CompleteRemoteMenu` resolves the active owner, calls `DecodeRewards` once, then
   passes those same domain values to the host's live receipt/reservation checks,
   `ApplyOwnerFinalEffects`, and `BuildRewardsSet`.
3. `BuildNativeReward` matches the F# reward shape and passes only its payload and
   immutable origin metadata to the native wrapper. Receiving peers always restore
   the final models. There is no replica-generation branch or strategy interface.
4. `ApNativeCardReward` retains `CardRewardConfiguration`. Selecting the picker
   replaces it with `WithRevealed()`. Save properties remain projections consumed by
   `ArchipelagoProgress.SerializableAP`; load goes through
   `RestorePersistedCardAssignment` -> `MirroredRewardAdapter.DecodeSavedCardAssignment`
   -> the same typed card restoration. The existing reset and replica-cache clearing
   paths still own native objects; no new run-scoped cache was added.

The menu envelope, gold row, reservations, commands, claim/skip handling, and
save/network codecs stay in C#. Network authentication and live receipt checks
remain separate from domain validation.

Removed with the unused path: new-native F# cases, the overlapping `RewardGeneration`
type, before/after fingerprint fields and capture/comparison, card/potion strategy
interfaces and diagnostic switch, replica-generation factories, acknowledgement and
decision messages/subscriptions, agreement digests, timeout tasks, and related run
state. `RewardMaterialization` now has exactly two provenance cases, both describing
completed assignments. The normal menu synchronization and native selection
synchronizer remain in use.

## Domain contract

| Value | Invariant / behavior |
| --- | --- |
| Card | Nonempty ordered final models and a card configuration with provenance; hooks may change choice count, so three is not required. |
| Card recipe | Rare without an act, regular with a nonnegative act index, or the existing regular current-act fallback. Rare-plus-act and negative acts are invalid. |
| Card reveal | Explicit unrevealed/revealed state. Revealing preserves recipe, reroll setting, policy, and effects. |
| Potion | Exactly one final model and its provenance; no card effects. |
| Relic | Exactly one final model. |
| Ancient choice | Exactly the existing `AncientRelicPool.ChoiceCount` models, supplied by C# rather than duplicated as a game constant in F#. |
| Unavailable | A reason and no model payload. Surplus Ancient rewards still produce the existing disabled row. |
| Owner-final | Restore the owner's final models; never roll again. |
| Restored native | Retain `replica_native_v1` provenance while restoring models without RNG replay. |
| Silken Tress | Only `0 -> 1`; apply at zero, do nothing at one, reject unrelated state. |
| Silver Crucible | Only nonnegative `n -> n + 1`, without integer overflow. Apply at `n`; later counter values already subsume the persisted effect. |

Effects must be unique within a reward and belong to an owner-final card. Mutable
input arrays, lists, and effect objects are copied into immutable domain values.
Collection projections wrap private copies, not the original mutable collections.

The reopened-card builder previously called `Configure` with the default unrevealed
flag before reading the cached card's actual reveal state. It now copies that state
first. Cached native cards also receive the decoded configuration before display.
This preserves the existing reveal contract across reopen and restore.

Completed reward shape validation now runs on the owner and every receiving peer,
rather than only checking strategy/effect flags on the host. Invalid model cardinality,
malformed model JSON, missing collections, and invalid
effect transitions fail before completed-menu execution. Native models still need
normal game deserialization. Unknown strategy messages retain their wording; structural
errors include the receipt identity. The old wire `RequiresNativeMaterialization`
flag is retained only as a rejection guard: true fails before payload decoding with
`AP reward <slot>:<index> requested removed replica-native generation.` It is never
passed to F# and cannot enable generation or silent restoration.

## Compatibility and scope

The unused acknowledgement/decision message keys and fingerprint DTO properties were
removed. The normal reward-menu message key, enum ordinals, schema versions, save keys,
serialized model formats, item IDs, APWorld files, and project versions are unchanged.
Old completed menus with unused fingerprint fields can still decode; old requests
asking replicas to generate are explicitly rejected. The saved-card loader
keeps its existing empty-strategy default; the live wire decoder remains strict.
No default F# union serialization enters a protocol or save.

Stable assignment keys, owner AP RNG algorithm, hook ordering, native
grant commands, consumption boundaries, and progress publication remain in their
existing owners. Card skips and full potion slots remain non-consumption paths.
Gold planning, relic receipt ledgers, progress revisions, progressive starters, and
broader lifecycle machines were deliberately left outside this migration.

## Validation

The subsequent typing cleanup replaces the adapter's intermediate kind strings with
`RewardInputKind`, makes `RewardEffect.NeedsApplication` exhaustive by effect case,
and shares `ObserveSilkenTress` / `ObserveSilverCrucible` validation between locally
observed hooks and wire decoding. The owner captures both counters at the existing
hook boundary and validates them after temporary-card cleanup, then encodes the
typed effects with the unchanged IDs. Card effect eligibility uses the exhaustive
`RewardMaterialization.AllowsPersistentEffects` property.

`DecodeSavedCardAssignment` now returns `(RewardOrigin Origin, CardRewardData Card)`.
It shares JSON, receipt, collection, and card configuration validation with completed
menus through `ToInput` and `MirroredReward.DecodeCard`; restoration no longer needs
non-card handlers or an Ancient choice count. The existing empty-strategy save default
is preserved. No union is serialized into the save or wire contract.

Cleanup validation: 44 F# cases and 118 C# cases passed, including the beta intermediate
manifest check; the packaged-loader case was skipped because no fresh bundle was built.
Beta compile-only passed with the existing CS8785 and CS0436 Godot generator warnings.
Full packaging was not rerun by the agent for this cleanup. On 2026-09-06, following
the planned brief multiplayer smoke test, the user reported that the in-game test run
worked perfectly. This is a user-reported smoke-test pass for the expanded refactor
and typing cleanup; individual scenarios, logs, and tested binary hashes were not
supplied. The build and packaging rows below record the earlier migration build,
not a package validation of the latest source.

| Layer | Result for this migration |
| --- | --- |
| F# xUnit/FsCheck | PASS after removal: 38 cases, including five properties with 500 generated examples each. |
| C# regression suite | PASS after removal: 117 cases with artifact paths set, zero skips; includes five obsolete replay-request rejection cases and existing receipt/construction regressions. |
| Beta compile-only | PASS against `C:/Users/terai/sts2dll/v01110`. Existing CS8785 missing `GodotProjectDir` and CS0436 generated `Main` conflict warnings. |
| Full client build | PASS after removal: public `0.107.1`, beta `0.111.0`, Godot export, loader, domain/Core dependencies; zero warnings/errors in this full build. |
| Packaged interop | PASS after removal: both variants invoke the complete decoder via the staged loader and preserve native provenance. Embedded manifests and beta intermediate manifests pass. |
| Base-game source evidence | Inspected `CardReward`, `SilkenTress`, and `SilverCrucible` from preserved public/beta assemblies using ilspycmd. No decompiled files added to the repository. This is static evidence only. |
| In-game runtime | USER-REPORTED PASS on 2026-09-06 after the expanded refactor and typing cleanup, following a planned brief multiplayer smoke test. Individual matrix cases and logs were not supplied. |
| APWorld framework/package build | NOT RUN; no Python changes. Any preexisting APWorld copied by the client build is not a fresh APWorld validation. |

The staged bundle is local at
`artifacts/mirrored-reward-migration-game/mods/Archipelago`; the build transcript is
`artifacts/mirrored-reward-full-build.log`. The installed game mod was not replaced.
The first sandboxed full-build attempt could not read the user NuGet configuration;
the subsequent build with normal local access passed. These paths and deployment
notes describe that earlier agent-run build. No release or version change was made.

Reproduction from the repository root:

```powershell
dotnet test client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj -c Release --no-restore
dotnet build client/StS2AP/StS2AP.csproj -c Debug -t:Compile --no-restore
# Full build: choose and verify a workspace staging directory before running.
dotnet build client/StS2AP/StS2AP.csproj -c Debug -p:STS2GamePath=C:/Users/terai/Projects/Slay-the-Spire-2-Archipelago/artifacts/mirrored-reward-migration-game
$env:STS2AP_TEST_BUNDLE = (Resolve-Path 'artifacts/mirrored-reward-migration-game/mods/Archipelago').Path
$env:STS2AP_TEST_ASSEMBLY = (Resolve-Path 'client/StS2AP/obj/0.111.0/Debug/net9.0/Archipelago.dll').Path
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release --no-restore
Remove-Item Env:STS2AP_TEST_BUNDLE, Env:STS2AP_TEST_ASSEMBLY
```

## Further in-game coverage

The brief smoke test passed as reported above. Coverage of each scenario below
remains unconfirmed; this matrix is for broader regression testing.

Use the complete staged bundle on both peers, beta 0.111.0, and matching RitsuLib.
The joining participant must own an AP slot; an empty vanilla-guest menu does not
exercise these reward paths. Also repeat affected singleplayer behavior on public.

| Scenario | Expected result |
| --- | --- |
| Owner/host and non-host AP menus with cards, potions, relics, and Ancient choices | Matching ordered menus and exactly one grant per successful claim; missing Ancient choices remain a disabled row. |
| Open card picker, skip, reopen | Same card choices and revealed state; receipt remains claimable. |
| Full potion slots, then make space and claim | First attempt remains claimable; successful attempt consumes once. |
| Save/rejoin with unclaimed cards and potions | Same final models; restored native provenance does not request another roll. |
| Pending owner-final rewards with Silken Tress or Silver Crucible effects, including multiple pending receipts | Hooks apply at most once per transition; reopening does not advance counters again. Use a suitable development fixture for relics excluded from ordinary multiplayer pools. |
| New run after leaving an open/pending menu | Existing reset paths clear native replica caches and stale menu work. |

Useful existing diagnostics remain `Materialized AP card reward <slot>:<index> with
<strategy>`, `Materialized AP potion reward <slot>:<index>`, and `Native AP reward menu
<menu-id> completed`. Structural decoder failures include `AP reward <slot>:<index>`.
There is no new success-log flood; compare actual choices, grants, saved reveal state,
and relic counters on both peers, not just the absence of errors.
