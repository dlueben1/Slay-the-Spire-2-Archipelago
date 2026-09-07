# Multiplayer code learning handoff

## Resume here

Continue a source-guided walkthrough of **`ApRelicReceiptState.ApproveMenu`** in
[ApRelicReceiptState.cs](../../client/StS2AP/Persistence/ApRelicReceiptState.cs).
Explain receipt eligibility, the anytime allowance, banked relic opportunities, and outstanding
menu reservations. Relic reconciliation was just introduced; do not assume the learner has
mastered the complete chest/menu reservation flow.

Suggested opening: “We have distinguished reserving an AP receipt from reserving a concrete
relic model. Let us follow how `ApproveMenu` decides which receipts can enter the menu.”
Use a small concrete example and walk through actual expressions and their state changes.

## Objective and working style

The user understands the high-level multiplayer decisions and wants detailed code understanding
to explain a large multiplayer PR against upstream/main to its maintainer.

- Keep this learning session **read-only**. The user will do editing work in another session.
- Explain small, connected execution paths with the source open and clickable file/symbol references.
- Emphasize ownership, which process executes a method, state mutations, callback registration,
  and the reason for each layer. Explain unfamiliar terminology directly.
- Questions and teach-back are optional aids, not mandatory checkpoints. The user declined a
  repetitive ascension question after saying they understood; move on when requested.
- Track “explained” separately from independently demonstrated understanding. Do not automatically
  mark entire chapters mastered from a walkthrough.
- Check current source before relying on this handoff, old comments, or historical design documents.
- Do not infer permission to edit, commit, or push from a learning question.

## Files and revision boundaries

- [Complete historical learning map](multiplayer-diff-map.md): 17 chapters, every one of the original
  234 Git change entries assigned exactly once, with entry points and cross-cutting file guidance.
- [Machine-readable historical inventory](multiplayer-diff-inventory.json): optional coverage aid.
- Original comparison base: `7a1535c7cfd6f4de972eb72cf7e18d5073c78ba4` (then-local upstream/main).
- Original comparison head: `0650885b5b30d6fe0167b6c76091409817a5d051` (multiplayer-squashed).
- Code head when this handoff was prepared: `cbf37df06dcd21ef6f01b67349ef8a3390c0ccfa`.

The original map is intentionally historical. Its counts do not include the newer Ancient
settings commit or these learning documents. Source links target the current checkout and may
therefore show newer behavior than the pinned map describes. Recompute the full diff before
claiming current complete review coverage.

## What has been discussed

### Identity and ascensions

The user explained that native `NetId` distinguishes STS players, while same-slot players receive
one AP slot's receipt history but consume rewards independently. The local harness sets `-clientId`;
the Steam ID mapping was not independently verified during this session.

The user said they understand the central multiplayer ascension execution path:
host-authorized request -> native ordered action -> shared ascension state -> process-local
projection -> retrospective effects on existing game state. Full initialization/rejoin paths
were not exhaustively studied.

Details explained:

- A “live receipt” is an `IndexedItemInfo` arriving through live item processing, not a distinct
  multiplayer item type. `RequestLiveReceipt` requests a game action, not another AP item.
- `RegisterReceived` occurs before the host/character checks. A different-character Ascension Down
  is retained but does not change the host character's active shared ascensions.
- `TryApplyAscensionDown` updates canonical state and handled receipt indexes.
- `SyncLocalProjection(RunState)` calls the overload taking `ApRunSharedState`; it does not apply
  the receipt a second time. `ReplaceLevels` refreshes the local AP model/presentation.
- `ApplyRetrospectiveEffect` repairs already-created effects: curse, potion capacity, gold refund,
  or second boss. It is skipped for already-handled receipts and already-absent levels.
- Failures after the canonical update do not roll back that update; the action invalidates claims
  and rethrows. This was source inspection, not a runtime test.

### Gold and progress transport

Explained using GoldRedeemed 40 -> 100:

- Native reward synchronization applies gold to replicas of the reward owner, not to every
  player's wallet. `ApNativeGoldReward.OnSelect` awaits native selection, then only the local
  owner calls `CommitGoldClaim`.
- Gold uses aggregate entitlement accounting, not discrete receipt consumption. Native wallet
  balance cannot reconstruct AP gold claimed, since it includes spending and other sources.
- The host needs AP progress for saving; replicas also use each player's GoldRedeemed for Poverty
  refunds. A different design could transmit explicit effects, but current code replicates inputs.
- `PublishLocalProgress` captures a snapshot and computes changed fields against the prior
  publication. GoldRedeemed is a replacement value, not an arithmetic increment.
- A non-host sends to the host, then advances its own record/baseline without waiting for host
  acceptance. Initial publication uses a snapshot; subsequent publications can use deltas.
- The host authenticates ownership, resolves run/player, checks revisions, applies progress, and
  rebroadcasts. Mismatched delta baselines are rejected; the handler does not repair them.
- `ApRunData.Initialize` registers `OnProgressSnapshotReceived` and `OnProgressDeltaReceived` via
  `Subscribe` with the same descriptors used for sending. Both host and clients use those handlers.
- Host-local publication supplies both message forms to `BroadcastHostConfirmedProgress`, which
  prefers the snapshot. Accepted non-host deltas are rebroadcast as deltas.
- Publication updates memory. Durability requires a subsequent host checkpoint.

### Relic reconciliation and reservations: current topic

Explained, but not yet fully worked through:

- `Claims` is keyed by player NetId -> receipt index -> Claim. Same-slot players retain separate
  receipt claims. Destination can be menu or chest; reservation precedes consumption.
- `ApRelicReceiptState.ReconcileProgress` preserves settled chest attempts, consumed receipt state,
  exact pending menu assignments, and recalculates banked opportunities when installing progress.
- `StandardRelicPool.ReserveAssignedChoices` is different: it removes assigned concrete models
  from native player/shared relic bags, preventing future generation from using those models.
  It neither grants the relic nor consumes the AP receipt.
- NEXT: inspect `ApproveMenu` with a numerical example, then trace its callers into actual host
  reservation requests and replies as needed. Do not jump straight to a whole-system summary.

### Ancient confirmation

Explained `ConfirmedByOwner` versus encounter-frozen `FrozenByOwner`:

- `ConfirmProgress` observes already host-accepted progress and copies ProgressiveAncients counts.
  It is not itself the general progress-acceptance operation and sends no new message.
- `BeginEncounter` freezes those inputs for option construction; later confirmation does not
  change the current frozen encounter.
- At the studied revision, this runs in both modes; the option patch reads frozen context before
  applying the anytime-mode Proceed behavior. Do not describe it as strictly start-of-act-only.
- Architectural observation, not an approved edit: a generic confirmation coordinator belongs in
  shared coordination if more consumers are added. Ancient-specific projection remains in
  AncientMultiplayer. `ObserveHostConfirmedProgress` was suggested as a clearer possible name.

## Open issue: missed start-of-act Ancient opportunities

Source inspection supported this scenario: the host is eligible for an Ancient, a joining
player is not, that player proceeds without a relic, and a later host-eligible checkpoint
replaces the rollback point before the late Progressive Ancient arrives. The item is retained,
but its missed encounter reward may be unavailable for the remainder of that campaign.

Checking that all players currently have enough receipts is not necessarily sufficient: an item
can arrive after encounter inputs freeze but before a subsequent checkpoint. An actual fix would
need a policy for unresolved encounter opportunities or another delivery mechanism.

Anytime mode was suggested as a practical recommendation, not implemented or documented by this
learning session. No exact in-game reproduction was performed. Reassess with the newer per-run
Ancient settings below; do not assume that adding an override fixes start-of-act checkpoint logic.

## Newer code to account for

Commit `cbf37df` (“add: client side ancients toggle for new runs”) arrived after the map was created.
It adds nullable local location/pool overrides, immutable `AncientRewardSettings`, per-run capture,
`AncientSettingsUtility`, persistence/delta integration, and regression tests. The utility resolves
a multiplayer participant's own saved settings; overrides are for new runs. Checkpoint eligibility
now reads `AncientSettingsUtility.Current.Location` rather than the direct settings field.

This commit also contains the earlier terminology cleanup (obsolete AP Guest/receipt-relay helper,
misleading host-owned labels) and other edits. Review its diff rather than assuming all 23 changed
paths are solely the toggle. It has not been comprehensively taught or validated in this session.

Changed paths since the original map:

- [client/StS2AP.RegressionTests/AncientSettingsTests.cs](../../client/StS2AP.RegressionTests/AncientSettingsTests.cs)
- [client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj](../../client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj)
- [client/StS2AP/ArchipelagoClient.cs](../../client/StS2AP/ArchipelagoClient.cs)
- [client/StS2AP/Data/ItemTable.cs](../../client/StS2AP/Data/ItemTable.cs)
- [client/StS2AP/ModSettingsRegistration.cs](../../client/StS2AP/ModSettingsRegistration.cs)
- [client/StS2AP/Models/ArchipelagoProgress.cs](../../client/StS2AP/Models/ArchipelagoProgress.cs)
- [client/StS2AP/Models/Configuration/AncientRewardSettings.cs](../../client/StS2AP/Models/Configuration/AncientRewardSettings.cs)
- [client/StS2AP/Models/Configuration/ArchipelagoSettings.cs](../../client/StS2AP/Models/Configuration/ArchipelagoSettings.cs)
- [client/StS2AP/Models/Configuration/ClientSettings.cs](../../client/StS2AP/Models/Configuration/ClientSettings.cs)
- [client/StS2AP/Multiplayer/ApRunData.cs](../../client/StS2AP/Multiplayer/ApRunData.cs)
- [client/StS2AP/Multiplayer/MultiplayerSupport.cs](../../client/StS2AP/Multiplayer/MultiplayerSupport.cs)
- [client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs](../../client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs)
- [client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs](../../client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs)
- [client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs](../../client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs)
- [client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs](../../client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs)
- [client/StS2AP/Persistence/ApProgressDelta.cs](../../client/StS2AP/Persistence/ApProgressDelta.cs)
- [client/StS2AP/Persistence/ApRunProgressState.cs](../../client/StS2AP/Persistence/ApRunProgressState.cs)
- [client/StS2AP/Utils/Connection/PendingCheckUtility.cs](../../client/StS2AP/Utils/Connection/PendingCheckUtility.cs)
- [client/StS2AP/Utils/Progression/AncientMultiplayer.cs](../../client/StS2AP/Utils/Progression/AncientMultiplayer.cs)
- [client/StS2AP/Utils/Progression/AncientSettingsUtility.cs](../../client/StS2AP/Utils/Progression/AncientSettingsUtility.cs)
- [client/StS2AP/Utils/Progression/AscensionMultiplayer.cs](../../client/StS2AP/Utils/Progression/AscensionMultiplayer.cs)
- [client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs](../../client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs)
- [client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs](../../client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs)

## Validation and exclusions

The historical map was mechanically checked: 234 unique entries, matching +24,960/-5,088 text
counts, and existing current-file links. Original terminology cleanup passed `git diff --check`.
The two API compilation attempts during that cleanup were blocked by NuGet access (`NU1301`).
No in-game proof, complete multiplayer test pass, or validation of cbf37df is claimed here.

No decompiled source, local settings, saved credentials, binaries, or runtime logs are included.
Untracked `.qoder/`, `PR_230_REVIEW.md`, and `Spire2-Beta-Decompiled 2/` are unrelated local files.

## Starter prompt on the other machine

> Read docs/learning/README.md and use docs/learning/multiplayer-diff-map.md as our syllabus.
> Keep this session read-only. Continue teaching from ApRelicReceiptState.ApproveMenu using a
> concrete example, source links, and small code sections. Explain expressions and ownership;
> avoid compulsory quizzes. Check current source and account for changes since the pinned map.
