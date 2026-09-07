# Progressive starter domain

`client/StS2AP.Domain/ProgressiveStarter.fs` owns supported/unsupported state and tier
transitions for both singleplayer and multiplayer. `StarterState<StarterMapping>` holds
the singleplayer identity mapping. `StarterState<CapturedStarterRecipe>` additionally
holds the exact owner-authored model JSON for multiplayer. Card and relic mappings and
captured recipes are distinct discriminated-union alternatives with validated construction.

Supported states always have a recipe and a tier of `None`, `Basic`, or `Upgraded`.
Multiplayer's uninitialized and unsupported states cannot carry a recipe. The singleplayer
save's absent mapping decodes as unsupported. Missing/blank supported fields and invalid
tiers are rejected; invalid JSON is rejected by the C# adapter. MegaCrit remains responsible
for interpreting model schemas and checking that an Orobas recipe matches actual model IDs.

## Decisions and execution

`StarterProgression.Plan` binds the incoming recipe to existing state and returns immutable
operations. An incoming specification's tier may be stale; the current player's applied tier
remains authoritative. Once initialized, a recipe cannot change, including its exact JSON.
Receipt counts remain separate from applied tiers and saturate at the upgraded target.

| Current | Target | Operations |
| --- | --- | --- |
| None | None | None |
| None | Basic | Restore base |
| None | Upgraded | Restore base, then grant upgrade relic |
| Basic | None | Remove base, permitted only during initialization |
| Basic | Basic | None |
| Basic | Upgraded | Grant upgrade relic |
| Upgraded | Upgraded | None |
| Upgraded | None or Basic | Reject downgrade |

Live receipts cannot target tier None. Continue-run singleplayer reconciliation cannot remove
the vanilla starter. Unsupported characters remain unchanged. `AfterApplied` advances state
after each successful command and rejects commands acknowledged out of order. Planning alone
does not advance state; an unsuccessful upgrade cannot mark the upgraded tier as applied.

Singleplayer flow:

1. `Patches_HookRunStart.OnStartingRelicsFinalized` calls
   `ProgressiveStarterUtility.InitializeForRun`, which captures vanilla mappings and explicitly
   selects initialization context.
2. `Patches_ItemProcessor.HandleProgressiveStarterReceipt` queues main-thread reconciliation
   for singleplayer receipts/history. It uses reconciliation context, not initialization.
3. `ProgressiveStarterAdapter.DecodeSingleplayer` creates the immutable state, and the F#
   planner chooses operations.
4. `ApplyCardOperation`/`ApplyRelicOperation` use native card/relic commands. Missing native
   models retain the existing fail-open behavior. Applied tiers are written after success.

Multiplayer flow:

1. `ProgressiveStarterMultiplayer.BeginRun`/`ReceiveLiveReceipt` obtain an immutable captured
   recipe from per-player saved state or the run/player/kind pending cache. Owner-only card
   capture retains its setup-preview cleanup. `EndRun` clears the pending cache.
2. The owner encodes a message and requests the existing managed noncombat action.
3. `TryValidate` checks run/owner/slot/character identity and unique targets. The adapter
   decodes each state, `ValidateRecipe` checks actual MegaCrit models, and F# plans every
   target before any target executes.
4. `ApplyTarget` consumes immutable validated plans. It restores captured models, uses native
   Orobas relic commands, and records each successful intermediate state before moving on.
5. `ApRunData.SetProgressiveStarterState` persists the per-player state. Command failures
   retain the existing claims-invalidation path; no retry, rollback, or receipt replay was added.

## Persistence and validation

The existing C# save/message DTOs remain boundary data, not trusted domain values. This
extraction did not need a format change and adds no legacy aliases or compatibility decoder.
Singleplayer reset and `ArchipelagoProgress`/`SerializableAP` mapping continue to store IDs
and applied tiers. Multiplayer continues to persist recipes under each player's
`ApPlayerRunState.ProgressiveStarters`; slot receipt counts do not replace those applied tiers.
No game IDs, feature settings, message keys, or project versions change.

`ProgressiveStarterTests` exercises the shared rules through C# interop and the production
DTO adapter. It covers the transition table, stale authored tiers, repeated reconciliation,
changed recipes, malformed states/JSON, exact save round trips, and successful intermediate
state. These are policy and adapter tests, not tests of Godot or native command execution.

## In-game checks still required

Use beta 0.111.0 with matching RitsuLib for multiplayer. Check both starter card and relic
settings; repeat affected singleplayer behavior on the supported game variants.

| Scenario | Expected result |
| --- | --- |
| New run with 0 / 1 / 2 received tiers | Starter absent / basic / upgraded; tier 2 restores before upgrading if needed |
| Receive first and second tiers during a run | Basic starter restored once, then upgraded once; all multiplayer replicas agree |
| Save and continue at each tier; reconnect in multiplayer | Exact applied state retained, no duplicate starter/upgrade grants |
| Unsupported modded character | Starter left unchanged with the existing unsupported-mapping warning |
| End run and start another | Fresh mapping/recipe captured for the new run; no pending recipe reused |

Expected existing logs include `Progressive Starter Card applied tier Basic` in singleplayer
and `Managed Progressive Starter Card applied tier Upgraded` in multiplayer, with analogous
Relic messages. There should be no `invalid managed Progressive Starter action` or
`managed Progressive Starter ... failed` logs in these valid scenarios.
