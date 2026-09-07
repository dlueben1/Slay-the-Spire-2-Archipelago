# Multiplayer diff learning map

> Historical map pinned to 0650885. Start with [the learning handoff](README.md) for study progress and subsequent code changes.

Base: `7a1535c7cfd6f4de972eb72cf7e18d5073c78ba4` (locally stored `upstream/main`).
Head: `0650885b5b30d6fe0167b6c76091409817a5d051` (`multiplayer-squashed`).

The base equals the merge-base. This inventory is the pinned committed PR diff, not a fetched comparison or a working-tree diff. All 234 Git change entries are assigned exactly once below; a rename is one entry. Text totals: +24,960 / -5,088, plus one deleted binary. Untracked local files and decompiled sources are excluded.

**Coverage means every changed path is inventoried, not that every hunk has been audited or every behavior verified.** Chapter explanations come from current source entry-point inspection and the diff. Detailed execution-path study, native API checks, and test execution remain separate. Tests listed here have been located, not run as part of mapping.

## How to use this map

Skim chapters 14, 15, and 17 to orient yourself. Study 01–03 first. Then use 04 as background for synchronized actions and proceed through the feature chapters. Read feature tests alongside the implementation; use 16 to reconcile the APWorld contract. One chapter may require several short learning exchanges.

For each chapter: trace one scenario with source open, predict an edge case, explain it back, then write reviewer notes. Mark understanding separately from inventory coverage.

The current executable participation kinds are **OwnApSlot** and **VanillaGuest**. Older AP Guest/receipt-relay descriptions are not the current model. Host-authored shared state still exists; that does not make every player's progress or outgoing checks host-owned.

## Chapter index

| Chapter | Topic | Entries | Text + / - | Prerequisites |
| --- | --- | ---: | ---: | --- |
| 01 | [Identity, participation, and character configuration](#chapter-01) | 18 | +1053 / -153 | Start here |
| 02 | [Connection, lobby readiness, run binding, and feature gates](#chapter-02) | 12 | +3119 / -918 | 01 |
| 03 | [Receipt ledger, canonical progress, and replica construction](#chapter-03) | 13 | +2117 / -110 | 01, 02 |
| 04 | [Native action admission and deferred requests](#chapter-04) | 4 | +298 / -0 | 02 |
| 05 | [Native reward menus, assigned models, and typed reward domain](#chapter-05) | 31 | +3487 / -756 | 01, 02, 03 |
| 06 | [Aggregate gold and unsupported combat buffs](#chapter-06) | 8 | +567 / -50 | 03, 05 |
| 07 | [Relic receipts, reservations, pools, and treasure agreement](#chapter-07) | 8 | +1591 / -239 | 03, 05 |
| 08 | [Location checks, durable delivery, floors, and victory](#chapter-08) | 10 | +1114 / -395 | 01, 03 |
| 09 | [Campaign selection, checkpoint/recovery saves, and continuation](#chapter-09) | 10 | +1706 / -148 | 01, 02, 03 |
| 10 | [Ascension, progressive starters, and Ancient progression](#chapter-10) | 28 | +3121 / -743 | 01, 03, 04, 05, 07 |
| 11 | [Rest sites and shop behavior](#chapter-11) | 12 | +864 / -553 | 01, 03, 08 |
| 12 | [DeathLink routing, damage, and deduplication](#chapter-12) | 10 | +1299 / -16 | 01, 02, 04 |
| 13 | [UI support and diagnostics](#chapter-13) | 18 | +1624 / -116 | 02, 05, 09, 11 |
| 14 | [API variants, loader, build, packaging, and release](#chapter-14) | 23 | +2570 / -651 | Orientation only; revisit after feature chapters |
| 15 | [Test infrastructure and validation boundaries](#chapter-15) | 7 | +324 / -0 | Read alongside every chapter |
| 16 | [APWorld contract and version changes](#chapter-16) | 5 | +40 / -10 | 01, 08, 10 |
| 17 | [Source organization and repository housekeeping](#chapter-17) | 17 | +66 / -230 | Skim first; revisit during final coverage check |

## Cross-cutting files: revisit by responsibility

| File | Primary chapter | Additional reading obligations |
| --- | --- | --- |
| [client/StS2AP/ArchipelagoClient.cs](<../../client/StS2AP/ArchipelagoClient.cs>) | 02 | Character validation (01), receipt preparation (03/06), reconnect/check updates (08), DeathLink callback lifecycle (12) |
| [client/StS2AP/Multiplayer/MultiplayerSupport.cs](<../../client/StS2AP/Multiplayer/MultiplayerSupport.cs>) | 02 | Ownership (01), history preparation (03/06), save identity (09), claim gates (05), enabled/disabled feature policy |
| [client/StS2AP/Multiplayer/ApRunData.cs](<../../client/StS2AP/Multiplayer/ApRunData.cs>) | 03 | Lobby staging (02), receipt reconciliation (07), save capture (09), Ancient confirmation (10) |
| [client/StS2AP/Models/ArchipelagoProgress.cs](<../../client/StS2AP/Models/ArchipelagoProgress.cs>) | 03 | Reward assignments (05), aggregate gold (06), relic bank (07), pending checks (08), serialization/reset (09), starter state (10) |
| [client/StS2AP/Utils/GameUtility.cs](<../../client/StS2AP/Utils/GameUtility.cs>) | 08 | Strict character handling and per-player resolution (01), slot reset (02), server save helpers (09) |
| [client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs](<../../client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs>) | 06 | Main-thread queue and receipt ledger (03), feature deferral (02), progression dispatch (10), singleplayer paths |

## Complete file inventory

Git A/D describe paths, not whether all their logic is new/deleted. The same-named successor notes prevent rewritten/moved files being mistaken for removed features. Similarity scores are Git heuristics. Import-only classification compares text after removing using-directives, blank lines, and a BOM; it is a navigation aid, not a compilation result.

<a id="chapter-01"></a>

### 01. Identity, participation, and character configuration

**Question:** Who is this STS player, which AP slot do they use, and whose settings apply?

Start with the two participation kinds and the C# adapter to the F# participant decisions. Separate a server-qualified connection destination from room/team/slot identity, native Player.NetId, and AP character numbering. The resolver reads the concrete player's frozen SlotSettings in multiplayer. Several players may connect to one AP slot; each still has its own per-player progress. Character loading is stricter than upstream: missing configured characters reject connection rather than receiving mirrored checks.

**Entry points:**
- [client/StS2AP/Multiplayer/ApParticipationKind.cs](<../../client/StS2AP/Multiplayer/ApParticipationKind.cs>)
- [client/StS2AP.Domain/Participant.fs](<../../client/StS2AP.Domain/Participant.fs>)
- [client/StS2AP/DomainAdapters/ParticipantAdapter.cs](<../../client/StS2AP/DomainAdapters/ParticipantAdapter.cs>)
- [client/StS2AP/Multiplayer/ApPlayerContextResolver.cs](<../../client/StS2AP/Multiplayer/ApPlayerContextResolver.cs>)
- [client/StS2AP/Utils/Connection/ApSessionIdentity.cs](<../../client/StS2AP/Utils/Connection/ApSessionIdentity.cs>)
- [client/StS2AP/Utils/Connection/ApSlotIdentity.cs](<../../client/StS2AP/Utils/Connection/ApSlotIdentity.cs>)

**Teach-back target:** Explain two players sharing an AP slot without conflating their native identity, receipt index, or consumption. Trace missing character validation through ArchipelagoClient and the APWorld documentation.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.Domain.Tests/ParticipantTests.fs](<../../client/StS2AP.Domain.Tests/ParticipantTests.fs>) | Added path | Test | 143 / 0 |
| A | [client/StS2AP.Domain/Participant.fs](<../../client/StS2AP.Domain/Participant.fs>) | Added path | Source | 175 / 0 |
| A | [client/StS2AP.RegressionTests/ApSessionIdentityTests.cs](<../../client/StS2AP.RegressionTests/ApSessionIdentityTests.cs>) | Added path | Test | 139 / 0 |
| A | [client/StS2AP.RegressionTests/ParticipantInteropTests.cs](<../../client/StS2AP.RegressionTests/ParticipantInteropTests.cs>) | Added path | Test | 65 / 0 |
| A | [client/StS2AP/Data/ArchipelagoIdCodec.cs](<../../client/StS2AP/Data/ArchipelagoIdCodec.cs>) | Added path | Source | 56 / 0 |
| D | `client/StS2AP/Data/CharTable.cs` (base only) | Deleted path | Source | 0 / 20 |
| A | [client/StS2AP/DomainAdapters/ParticipantAdapter.cs](<../../client/StS2AP/DomainAdapters/ParticipantAdapter.cs>) | Added path | Source | 44 / 0 |
| M | [client/StS2AP/Extensions/CharacterModelExtensions.cs](<../../client/StS2AP/Extensions/CharacterModelExtensions.cs>) | Modified | Source | 10 / 22 |
| M | [client/StS2AP/Extensions/ItemInfoExtensions.cs](<../../client/StS2AP/Extensions/ItemInfoExtensions.cs>) | Modified | Source | 26 / 32 |
| M | [client/StS2AP/Extensions/PlayerExtensions.cs](<../../client/StS2AP/Extensions/PlayerExtensions.cs>) | Modified | Source | 28 / 15 |
| R083 | [client/StS2AP/Models/Configuration/ArchipelagoSettings.cs](<../../client/StS2AP/Models/Configuration/ArchipelagoSettings.cs>)<br>From `client/StS2AP/Models/ArchipelagoSettings.cs` | Move + edits | Source | 24 / 14 |
| R091 | [client/StS2AP/Models/Configuration/CharacterConfig.cs](<../../client/StS2AP/Models/Configuration/CharacterConfig.cs>)<br>From `client/StS2AP/Models/CharacterConfig.cs` | Move + edits | Source | 5 / 5 |
| A | [client/StS2AP/Multiplayer/ApParticipationKind.cs](<../../client/StS2AP/Multiplayer/ApParticipationKind.cs>) | Added path | Source | 8 / 0 |
| A | [client/StS2AP/Multiplayer/ApPlayerContextResolver.cs](<../../client/StS2AP/Multiplayer/ApPlayerContextResolver.cs>) | Added path | Source | 167 / 0 |
| R086 | [client/StS2AP/Patches/Progression/Patches_UnlockCharacters.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockCharacters.cs>)<br>From `client/StS2AP/Patches/Patches_UnlockCharacters.cs` | Move + edits | Source | 50 / 22 |
| R060 | [client/StS2AP/Utils/Connection/ApSessionIdentity.cs](<../../client/StS2AP/Utils/Connection/ApSessionIdentity.cs>)<br>From `client/StS2AP/Utils/ApSessionIdentity.cs` | Move + edits | Source | 18 / 23 |
| A | [client/StS2AP/Utils/Connection/ApSessionIdentityJsonConverter.cs](<../../client/StS2AP/Utils/Connection/ApSessionIdentityJsonConverter.cs>) | Added path | Source | 55 / 0 |
| A | [client/StS2AP/Utils/Connection/ApSlotIdentity.cs](<../../client/StS2AP/Utils/Connection/ApSlotIdentity.cs>) | Added path | Source | 40 / 0 |

<a id="chapter-02"></a>

### 02. Connection, lobby readiness, run binding, and feature gates

**Question:** How does a connection become a participant in a launched run?

Trace the main-menu destination selection, AP connection/history preparation, lobby contribution, final readiness check, and binding at RunManager.Launch. MultiplayerSupport is the switchboard for pending/active participation, identity checks, unsupported-item deferral, and claim invalidation. The host requires a prepared AP slot; joining permits VanillaGuest. Inspect separate paths for new runs, invite/fast launch, returning players, and leaving a slot. ArchipelagoClient also contains callback/session isolation and strict character validation; revisit its sections rather than reading it once end-to-end.

**Entry points:**
- [client/StS2AP/ArchipelagoClient.cs](<../../client/StS2AP/ArchipelagoClient.cs>)
- [client/StS2AP/Multiplayer/MultiplayerSupport.cs](<../../client/StS2AP/Multiplayer/MultiplayerSupport.cs>)
- [client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs>)
- [client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs>)
- [client/StS2AP/Utils/Connection/ApFastMpLaunchController.cs](<../../client/StS2AP/Utils/Connection/ApFastMpLaunchController.cs>)

**Teach-back target:** Trace a delayed initial item history through the last player becoming ready. Explain why an explicitly empty prepared history differs from an unprepared history.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| M | [client/StS2AP/ArchipelagoClient.cs](<../../client/StS2AP/ArchipelagoClient.cs>) | Modified | Source | 459 / 270 |
| A | [client/StS2AP/Multiplayer/ApPlayDestination.cs](<../../client/StS2AP/Multiplayer/ApPlayDestination.cs>) | Added path | Source | 8 / 0 |
| A | [client/StS2AP/Multiplayer/MultiplayerFeature.cs](<../../client/StS2AP/Multiplayer/MultiplayerFeature.cs>) | Added path | Source | 30 / 0 |
| A | [client/StS2AP/Multiplayer/MultiplayerSupport.cs](<../../client/StS2AP/Multiplayer/MultiplayerSupport.cs>) | Added path | Source | 1059 / 0 |
| A | [client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs>) | Added path | Source | 337 / 0 |
| A | [client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs>) | Added path | Source | 945 / 0 |
| D | `client/StS2AP/Patches/Patches_HookRunStart.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_HookRunStart.cs>) | Deleted path | Source | 0 / 146 |
| D | `client/StS2AP/Patches/Patches_MainMenuBehavior.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_MainMenuBehavior.cs>) | Deleted path | Source | 0 / 469 |
| M | [client/StS2AP/UI/ArchipelagoConnectionUI.cs](<../../client/StS2AP/UI/ArchipelagoConnectionUI.cs>) | Modified | Source | 39 / 29 |
| A | [client/StS2AP/Utils/Connection/ApFastMpLaunchController.cs](<../../client/StS2AP/Utils/Connection/ApFastMpLaunchController.cs>) | Added path | Source | 237 / 0 |
| R094 | [client/StS2AP/Utils/Connection/ApReconnectController.cs](<../../client/StS2AP/Utils/Connection/ApReconnectController.cs>)<br>From `client/StS2AP/Utils/ApReconnectController.cs` | Move + edits | Source | 3 / 4 |
| M | [client/StS2AP/godot/Archipelago/localization/eng/main_menu_ui.json](<../../client/StS2AP/godot/Archipelago/localization/eng/main_menu_ui.json>) | Modified | Configuration/asset | 2 / 0 |

<a id="chapter-03"></a>

### 03. Receipt ledger, canonical progress, and replica construction

**Question:** Which state records receipts, consumption, and replicated construction?

ApReceivedItemLedger consolidates receipt catalogue and per-run used indexes. ArchipelagoProgress projects the local model to ApRunProgressState. ApRunData registers shared/per-player RitsuLib run data, stages lobby facts, restores progress, and publishes snapshots/deltas. The first publication establishes a baseline; later deltas carry base/revision checks. Hosts authenticate the sending owner and rebroadcast confirmed progress. ApReplicaConstructionState is a separate cursor for constructing rewards on each replica; do not treat it as owner consumption. Persistence DTOs are used by both live synchronization and saves.

**Entry points:**
- [client/StS2AP/Models/ApReceivedItemLedger.cs](<../../client/StS2AP/Models/ApReceivedItemLedger.cs>)
- [client/StS2AP/Models/ArchipelagoProgress.cs](<../../client/StS2AP/Models/ArchipelagoProgress.cs>)
- [client/StS2AP/Persistence/ApRunProgressState.cs](<../../client/StS2AP/Persistence/ApRunProgressState.cs>)
- [client/StS2AP/Persistence/ApPlayerRunState.cs](<../../client/StS2AP/Persistence/ApPlayerRunState.cs>)
- [client/StS2AP/Persistence/ApRunSharedState.cs](<../../client/StS2AP/Persistence/ApRunSharedState.cs>)
- [client/StS2AP/Persistence/ApReplicaConstructionState.cs](<../../client/StS2AP/Persistence/ApReplicaConstructionState.cs>)
- [client/StS2AP/Persistence/ApProgressDelta.cs](<../../client/StS2AP/Persistence/ApProgressDelta.cs>)
- [client/StS2AP/Multiplayer/ApRunData.cs](<../../client/StS2AP/Multiplayer/ApRunData.cs>)

**Teach-back target:** Distinguish received, assigned, constructed, consumed, and published. Predict duplicate and mismatched-revision handling and explain new-run reset versus continue restoration.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/ReceivedItemLedgerTests.cs](<../../client/StS2AP.RegressionTests/ReceivedItemLedgerTests.cs>) | Added path | Test | 178 / 0 |
| A | [client/StS2AP.RegressionTests/ReplicaConstructionTests.cs](<../../client/StS2AP.RegressionTests/ReplicaConstructionTests.cs>) | Added path | Test | 47 / 0 |
| A | [client/StS2AP/Models/ApReceivedItemLedger.cs](<../../client/StS2AP/Models/ApReceivedItemLedger.cs>) | Added path | Source | 138 / 0 |
| M | [client/StS2AP/Models/ArchipelagoProgress.cs](<../../client/StS2AP/Models/ArchipelagoProgress.cs>) | Modified | Source | 261 / 101 |
| M | [client/StS2AP/Models/IndexedItemInfo.cs](<../../client/StS2AP/Models/IndexedItemInfo.cs>) | Modified | Source | 7 / 9 |
| A | [client/StS2AP/Multiplayer/ApRunData.cs](<../../client/StS2AP/Multiplayer/ApRunData.cs>) | Added path | Source | 881 / 0 |
| A | [client/StS2AP/Multiplayer/Messages/ApProgressDeltaMessage.cs](<../../client/StS2AP/Multiplayer/Messages/ApProgressDeltaMessage.cs>) | Added path | Source | 11 / 0 |
| A | [client/StS2AP/Multiplayer/Messages/ApProgressSnapshotMessage.cs](<../../client/StS2AP/Multiplayer/Messages/ApProgressSnapshotMessage.cs>) | Added path | Source | 10 / 0 |
| A | [client/StS2AP/Persistence/ApPlayerRunState.cs](<../../client/StS2AP/Persistence/ApPlayerRunState.cs>) | Added path | Source | 23 / 0 |
| A | [client/StS2AP/Persistence/ApProgressDelta.cs](<../../client/StS2AP/Persistence/ApProgressDelta.cs>) | Added path | Source | 414 / 0 |
| A | [client/StS2AP/Persistence/ApReplicaConstructionState.cs](<../../client/StS2AP/Persistence/ApReplicaConstructionState.cs>) | Added path | Source | 62 / 0 |
| A | [client/StS2AP/Persistence/ApRunProgressState.cs](<../../client/StS2AP/Persistence/ApRunProgressState.cs>) | Added path | Source | 70 / 0 |
| A | [client/StS2AP/Persistence/ApRunSharedState.cs](<../../client/StS2AP/Persistence/ApRunSharedState.cs>) | Added path | Source | 15 / 0 |

<a id="chapter-04"></a>

### 04. Native action admission and deferred requests

**Question:** When is it safe to ask the native synchronizer to execute an action?

NonCombatActionAdmissionState enumerates loading, combat transition, executor, and queue conditions. NonCombatActionAdmission captures the actual runtime state; ManagedActionRequestScheduler defers requests and clears them at run end. Feature owners still validate identities and receipts. This chapter supplies vocabulary for progression and DeathLink; it is not the whole network transport.

**Entry points:**
- [client/StS2AP/Utils/Actions/NonCombatActionAdmissionState.cs](<../../client/StS2AP/Utils/Actions/NonCombatActionAdmissionState.cs>)
- [client/StS2AP/Utils/Actions/NonCombatActionAdmission.cs](<../../client/StS2AP/Utils/Actions/NonCombatActionAdmission.cs>)
- [client/StS2AP/Utils/Actions/ManagedActionRequestScheduler.cs](<../../client/StS2AP/Utils/Actions/ManagedActionRequestScheduler.cs>)

**Teach-back target:** Explain why being outside an active combat is insufficient for admission. Trace an enqueued action across travel and run end.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/NonCombatActionAdmissionTests.cs](<../../client/StS2AP.RegressionTests/NonCombatActionAdmissionTests.cs>) | Added path | Test | 50 / 0 |
| A | [client/StS2AP/Utils/Actions/ManagedActionRequestScheduler.cs](<../../client/StS2AP/Utils/Actions/ManagedActionRequestScheduler.cs>) | Added path | Source | 151 / 0 |
| A | [client/StS2AP/Utils/Actions/NonCombatActionAdmission.cs](<../../client/StS2AP/Utils/Actions/NonCombatActionAdmission.cs>) | Added path | Source | 64 / 0 |
| A | [client/StS2AP/Utils/Actions/NonCombatActionAdmissionState.cs](<../../client/StS2AP/Utils/Actions/NonCombatActionAdmissionState.cs>) | Added path | Source | 33 / 0 |

<a id="chapter-05"></a>

### 05. Native reward menus, assigned models, and typed reward domain

**Question:** How does one AP reward become matching native menus on peers?

The old ApNativeRewardMenu is replaced by ApMirroredRewardDispatcher. Follow OpenMenu, BuildOwnerMenuSpec/BuildAssignedSpec, typed DecodeRewards, completed model transport, CompleteRemoteMenu, and native wrappers. F# owns validated reward shapes and effect decisions; C# owns DTO/JSON conversion, native model construction, sender validation, and game execution. Completed assignments are restored on peers instead of rerolled. Study reveal state, saved assignments, card-hook effects, successful grant consumption, skips/full potion slots, and stale menu work during travel separately. Existing presentation models are also reorganized.

**Entry points:**
- [client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs](<../../client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs>)
- [client/StS2AP.Domain/MirroredReward.fs](<../../client/StS2AP.Domain/MirroredReward.fs>)
- [client/StS2AP.Domain/RewardMaterialization.fs](<../../client/StS2AP.Domain/RewardMaterialization.fs>)
- [client/StS2AP/DomainAdapters/MirroredRewardAdapter.cs](<../../client/StS2AP/DomainAdapters/MirroredRewardAdapter.cs>)
- [client/StS2AP/Models/Rewards/Specs/ApRewardMenuSpec.cs](<../../client/StS2AP/Models/Rewards/Specs/ApRewardMenuSpec.cs>)
- [client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs](<../../client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs>)
- [client/StS2AP/UI/ArchipelagoRewardUI.cs](<../../client/StS2AP/UI/ArchipelagoRewardUI.cs>)

**Teach-back target:** Trace a non-host card reward through skip, reopen, save, and rejoin. Identify where final models are chosen and where the receipt becomes used.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.Domain.Tests/MirroredRewardTests.fs](<../../client/StS2AP.Domain.Tests/MirroredRewardTests.fs>) | Added path | Test | 169 / 0 |
| A | [client/StS2AP.Domain.Tests/RewardMaterializationTests.fs](<../../client/StS2AP.Domain.Tests/RewardMaterializationTests.fs>) | Added path | Test | 75 / 0 |
| A | [client/StS2AP.Domain/MirroredReward.fs](<../../client/StS2AP.Domain/MirroredReward.fs>) | Added path | Source | 253 / 0 |
| A | [client/StS2AP.Domain/RewardMaterialization.fs](<../../client/StS2AP.Domain/RewardMaterialization.fs>) | Added path | Source | 40 / 0 |
| A | [client/StS2AP.RegressionTests/MirroredRewardAdapterTests.cs](<../../client/StS2AP.RegressionTests/MirroredRewardAdapterTests.cs>) | Added path | Test | 241 / 0 |
| A | [client/StS2AP.RegressionTests/RewardMaterializationInteropTests.cs](<../../client/StS2AP.RegressionTests/RewardMaterializationInteropTests.cs>) | Added path | Test | 36 / 0 |
| A | [client/StS2AP.RegressionTests/RewardTravelTests.cs](<../../client/StS2AP.RegressionTests/RewardTravelTests.cs>) | Added path | Test | 69 / 0 |
| A | [client/StS2AP/DomainAdapters/MirroredRewardAdapter.cs](<../../client/StS2AP/DomainAdapters/MirroredRewardAdapter.cs>) | Added path | Source | 135 / 0 |
| D | `client/StS2AP/Models/ArchipelagoReward.cs` (base only)<br>Same-named successor: [client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs](<../../client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs>) | Deleted path | Source | 0 / 147 |
| A | [client/StS2AP/Models/Rewards/Grants/ApGrantId.cs](<../../client/StS2AP/Models/Rewards/Grants/ApGrantId.cs>) | Added path | Source | 10 / 0 |
| R099 | [client/StS2AP/Models/Rewards/Presentation/ApItemCardModel.cs](<../../client/StS2AP/Models/Rewards/Presentation/ApItemCardModel.cs>)<br>From `client/StS2AP/Models/ApItemCardModel.cs` | Move + imports/whitespace only | Source | 0 / 1 |
| R098 | [client/StS2AP/Models/Rewards/Presentation/ApItemPotionModel.cs](<../../client/StS2AP/Models/Rewards/Presentation/ApItemPotionModel.cs>)<br>From `client/StS2AP/Models/ApItemPotionModel.cs` | Move + imports/whitespace only | Source | 0 / 1 |
| R098 | [client/StS2AP/Models/Rewards/Presentation/ApItemRelicModel.cs](<../../client/StS2AP/Models/Rewards/Presentation/ApItemRelicModel.cs>)<br>From `client/StS2AP/Models/ApItemRelicModel.cs` | Move + imports/whitespace only | Source | 0 / 1 |
| A | [client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs](<../../client/StS2AP/Models/Rewards/Presentation/ArchipelagoReward.cs>) | Added path | Source | 129 / 0 |
| A | [client/StS2AP/Models/Rewards/Specs/ApMenuGoldSpec.cs](<../../client/StS2AP/Models/Rewards/Specs/ApMenuGoldSpec.cs>) | Added path | Source | 11 / 0 |
| A | [client/StS2AP/Models/Rewards/Specs/ApMirroredRewardKind.cs](<../../client/StS2AP/Models/Rewards/Specs/ApMirroredRewardKind.cs>) | Added path | Source | 13 / 0 |
| A | [client/StS2AP/Models/Rewards/Specs/ApMirroredRewardSpec.cs](<../../client/StS2AP/Models/Rewards/Specs/ApMirroredRewardSpec.cs>) | Added path | Source | 70 / 0 |
| A | [client/StS2AP/Models/Rewards/Specs/ApRewardEffectSpec.cs](<../../client/StS2AP/Models/Rewards/Specs/ApRewardEffectSpec.cs>) | Added path | Source | 12 / 0 |
| A | [client/StS2AP/Models/Rewards/Specs/ApRewardMenuSpec.cs](<../../client/StS2AP/Models/Rewards/Specs/ApRewardMenuSpec.cs>) | Added path | Source | 16 / 0 |
| D | `client/StS2AP/Patches/Patches_APCardRewardUpgradeOdds.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Rewards/Patches_APCardRewardUpgradeOdds.cs](<../../client/StS2AP/Patches/Rewards/Patches_APCardRewardUpgradeOdds.cs>) | Deleted path | Source | 0 / 57 |
| A | [client/StS2AP/Patches/Rewards/Patches_APCardRewardUpgradeOdds.cs](<../../client/StS2AP/Patches/Rewards/Patches_APCardRewardUpgradeOdds.cs>) | Added path | Source | 423 / 0 |
| R088 | [client/StS2AP/Patches/Rewards/Patches_APRewardScreen.cs](<../../client/StS2AP/Patches/Rewards/Patches_APRewardScreen.cs>)<br>From `client/StS2AP/Patches/Patches_APRewardScreen.cs` | Move + edits | Source | 32 / 18 |
| A | [client/StS2AP/Patches/Rewards/Patches_APRewardTravel.cs](<../../client/StS2AP/Patches/Rewards/Patches_APRewardTravel.cs>) | Added path | Source | 37 / 0 |
| R100 | [client/StS2AP/Patches/Rewards/Patches_LastingCandy.cs](<../../client/StS2AP/Patches/Rewards/Patches_LastingCandy.cs>)<br>From `client/StS2AP/Patches/Patches_LastingCandy.cs` | Exact move | Source | 0 / 0 |
| A | [client/StS2AP/Patches/Rewards/Patches_WingCharmMultiplayer.cs](<../../client/StS2AP/Patches/Rewards/Patches_WingCharmMultiplayer.cs>) | Added path | Source | 21 / 0 |
| A | [client/StS2AP/Persistence/ApCardAssignmentState.cs](<../../client/StS2AP/Persistence/ApCardAssignmentState.cs>) | Added path | Source | 27 / 0 |
| M | [client/StS2AP/UI/ArchipelagoRewardUI.cs](<../../client/StS2AP/UI/ArchipelagoRewardUI.cs>) | Modified | Source | 213 / 26 |
| D | `client/StS2AP/Utils/ApNativeRewardMenu.cs` (base only) | Deleted path | Source | 0 / 505 |
| A | [client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs](<../../client/StS2AP/Utils/Rewards/ApMirroredRewardDispatcher.cs>) | Added path | Source | 1238 / 0 |
| A | [client/StS2AP/Utils/Rewards/ApRewardTravelGuard.cs](<../../client/StS2AP/Utils/Rewards/ApRewardTravelGuard.cs>) | Added path | Source | 34 / 0 |
| A | [docs/design/mirrored-reward-domain.md](<../../docs/design/mirrored-reward-domain.md>) | Added path | Documentation | 183 / 0 |

<a id="chapter-06"></a>

### 06. Aggregate gold and unsupported combat buffs

**Question:** Why does gold use an aggregate cursor instead of a discrete reward receipt?

ApGrantDispatcher rebuilds per-character raw gold banks from AP history, materializes a claim, and advances the source cursor after the native grant. UniversalBuffGold divides cumulative conversion across configured characters. The multiplayer item-processing path converts universal combat buffs into gold; ordinary combat effects and DeathFragments are not in the enabled multiplayer feature set. Compare live receipt handling with history preparation and the singleplayer path. Gold shares menu transport with chapter 05 but has distinct accounting.

**Entry points:**
- [client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs](<../../client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs>)
- [client/StS2AP/Utils/Rewards/UniversalBuffGold.cs](<../../client/StS2AP/Utils/Rewards/UniversalBuffGold.cs>)
- [client/StS2AP/Models/Rewards/Grants/ApGoldClaim.cs](<../../client/StS2AP/Models/Rewards/Grants/ApGoldClaim.cs>)
- [client/StS2AP/Models/Rewards/Grants/ArchipelagoGoldOffer.cs](<../../client/StS2AP/Models/Rewards/Grants/ArchipelagoGoldOffer.cs>)
- [client/StS2AP/Utils/Rewards/BuffUtility.cs](<../../client/StS2AP/Utils/Rewards/BuffUtility.cs>)

**Teach-back target:** Explain SourceAmount, GrantedAmount, and redeemed cursor, including Poverty and fractional shares. Prove which paths must agree when rebuilding history.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/UniversalBuffGoldTests.cs](<../../client/StS2AP.RegressionTests/UniversalBuffGoldTests.cs>) | Added path | Test | 112 / 0 |
| M | [client/StS2AP/Data/ItemTable.cs](<../../client/StS2AP/Data/ItemTable.cs>) | Modified | Source | 15 / 8 |
| A | [client/StS2AP/Models/Rewards/Grants/ApGoldClaim.cs](<../../client/StS2AP/Models/Rewards/Grants/ApGoldClaim.cs>) | Added path | Source | 11 / 0 |
| R100 | [client/StS2AP/Models/Rewards/Grants/ArchipelagoGoldOffer.cs](<../../client/StS2AP/Models/Rewards/Grants/ArchipelagoGoldOffer.cs>)<br>From `client/StS2AP/Models/ArchipelagoGoldOffer.cs` | Exact move | Source | 0 / 0 |
| R065 | [client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs](<../../client/StS2AP/Patches/Rewards/Patches_ItemProcessor.cs>)<br>From `client/StS2AP/Patches/Patches_ItemProcessor.cs` | Move + edits | Source | 167 / 30 |
| A | [client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs](<../../client/StS2AP/Utils/Rewards/ApGrantDispatcher.cs>) | Added path | Source | 176 / 0 |
| R091 | [client/StS2AP/Utils/Rewards/BuffUtility.cs](<../../client/StS2AP/Utils/Rewards/BuffUtility.cs>)<br>From `client/StS2AP/Utils/BuffUtility.cs` | Move + edits | Source | 51 / 12 |
| A | [client/StS2AP/Utils/Rewards/UniversalBuffGold.cs](<../../client/StS2AP/Utils/Rewards/UniversalBuffGold.cs>) | Added path | Source | 35 / 0 |

<a id="chapter-07"></a>

### 07. Relic receipts, reservations, pools, and treasure agreement

**Question:** How do chest opportunities and menu claims avoid spending the same receipt?

ApRelicReceiptState and RelicReceiptMultiplayer coordinate receipt ownership, menu reservations, chest freezing, assignments, and consumption. Treasure patches connect these decisions to native chest construction and readiness. StandardRelicPool and RelicRewardUtility manage candidate availability and saved choices. These mechanisms interact with both progress publication and native menus, so this chapter follows them rather than being folded into generic reward handling.

**Entry points:**
- [client/StS2AP/Persistence/ApRelicReceiptState.cs](<../../client/StS2AP/Persistence/ApRelicReceiptState.cs>)
- [client/StS2AP/Multiplayer/RelicReceiptMultiplayer.cs](<../../client/StS2AP/Multiplayer/RelicReceiptMultiplayer.cs>)
- [client/StS2AP/Utils/Rewards/RelicRewardUtility.cs](<../../client/StS2AP/Utils/Rewards/RelicRewardUtility.cs>)
- [client/StS2AP/Utils/Rewards/StandardRelicPool.cs](<../../client/StS2AP/Utils/Rewards/StandardRelicPool.cs>)
- [client/StS2AP/Patches/Rooms/Patches_TreasureReceiptAgreement.cs](<../../client/StS2AP/Patches/Rooms/Patches_TreasureReceiptAgreement.cs>)
- [client/StS2AP/Patches/Rooms/Patches_TreasureRoomRelicScarcity.cs](<../../client/StS2AP/Patches/Rooms/Patches_TreasureRoomRelicScarcity.cs>)

**Teach-back target:** Trace a relic arriving near chest opening while an AP menu is pending. Locate the arbitration state and explain how save/rejoin preserves it.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/RelicReceiptTests.cs](<../../client/StS2AP.RegressionTests/RelicReceiptTests.cs>) | Added path | Test | 193 / 0 |
| A | [client/StS2AP/Multiplayer/RelicReceiptMultiplayer.cs](<../../client/StS2AP/Multiplayer/RelicReceiptMultiplayer.cs>) | Added path | Source | 465 / 0 |
| A | [client/StS2AP/Patches/Rooms/Patches_TreasureReceiptAgreement.cs](<../../client/StS2AP/Patches/Rooms/Patches_TreasureReceiptAgreement.cs>) | Added path | Source | 65 / 0 |
| A | [client/StS2AP/Patches/Rooms/Patches_TreasureRoomRelicScarcity.cs](<../../client/StS2AP/Patches/Rooms/Patches_TreasureRoomRelicScarcity.cs>) | Added path | Source | 61 / 0 |
| A | [client/StS2AP/Persistence/ApRelicReceiptState.cs](<../../client/StS2AP/Persistence/ApRelicReceiptState.cs>) | Added path | Source | 159 / 0 |
| D | `client/StS2AP/Utils/RelicRewardUtility.cs` (base only)<br>Same-named successor: [client/StS2AP/Utils/Rewards/RelicRewardUtility.cs](<../../client/StS2AP/Utils/Rewards/RelicRewardUtility.cs>) | Deleted path | Source | 0 / 239 |
| A | [client/StS2AP/Utils/Rewards/RelicRewardUtility.cs](<../../client/StS2AP/Utils/Rewards/RelicRewardUtility.cs>) | Added path | Source | 397 / 0 |
| A | [client/StS2AP/Utils/Rewards/StandardRelicPool.cs](<../../client/StS2AP/Utils/Rewards/StandardRelicPool.cs>) | Added path | Source | 251 / 0 |

<a id="chapter-08"></a>

### 08. Location checks, durable delivery, floors, and victory

**Question:** Who may send checks, and how are multiplayer opportunity counts reconciled?

MultiplayerLocationChecks separates replicated construction from the direct AP connection authorized to write a check. PendingCheckUtility handles per-player multiplayer pending checks and reconnect reconciliation; PendingCheckOutbox is an unchanged move. GameUtility has related ownership, goal, release, and slot-reset changes. Floorsanity normalizes multiplayer floors and compensates missing opportunities at boss boundaries while the APWorld retains its singleplayer location counts. Reward injection and victory are additional entry points.

**Entry points:**
- [client/StS2AP/Multiplayer/MultiplayerLocationChecks.cs](<../../client/StS2AP/Multiplayer/MultiplayerLocationChecks.cs>)
- [client/StS2AP/Utils/Connection/PendingCheckUtility.cs](<../../client/StS2AP/Utils/Connection/PendingCheckUtility.cs>)
- [client/StS2AP/Utils/GameUtility.cs](<../../client/StS2AP/Utils/GameUtility.cs>)
- [client/StS2AP/Patches/Progression/Patches_Floorsanity.cs](<../../client/StS2AP/Patches/Progression/Patches_Floorsanity.cs>)
- [client/StS2AP/Patches/Rewards/Patches_InjectAPRewards.cs](<../../client/StS2AP/Patches/Rewards/Patches_InjectAPRewards.cs>)
- [client/StS2AP/Patches/Progression/Patches_Victory.cs](<../../client/StS2AP/Patches/Progression/Patches_Victory.cs>)
- [client/StS2AP/Data/LocationData.cs](<../../client/StS2AP/Data/LocationData.cs>)

**Teach-back target:** Trace an offline check to confirmation after reconnect. Explain boss compensation without confusing an outgoing relic location with permission to consume an incoming relic.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| M | [client/StS2AP/Data/LocationData.cs](<../../client/StS2AP/Data/LocationData.cs>) | Modified | Source | 90 / 31 |
| A | [client/StS2AP/Multiplayer/MultiplayerLocationChecks.cs](<../../client/StS2AP/Multiplayer/MultiplayerLocationChecks.cs>) | Added path | Source | 329 / 0 |
| D | `client/StS2AP/Patches/Patches_Floorsanity.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Progression/Patches_Floorsanity.cs](<../../client/StS2AP/Patches/Progression/Patches_Floorsanity.cs>) | Deleted path | Source | 0 / 128 |
| D | `client/StS2AP/Patches/Patches_Victory.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Progression/Patches_Victory.cs](<../../client/StS2AP/Patches/Progression/Patches_Victory.cs>) | Deleted path | Source | 0 / 39 |
| A | [client/StS2AP/Patches/Progression/Patches_Floorsanity.cs](<../../client/StS2AP/Patches/Progression/Patches_Floorsanity.cs>) | Added path | Source | 174 / 0 |
| A | [client/StS2AP/Patches/Progression/Patches_Victory.cs](<../../client/StS2AP/Patches/Progression/Patches_Victory.cs>) | Added path | Source | 59 / 0 |
| R057 | [client/StS2AP/Patches/Rewards/Patches_InjectAPRewards.cs](<../../client/StS2AP/Patches/Rewards/Patches_InjectAPRewards.cs>)<br>From `client/StS2AP/Patches/Patches_InjectAPRewards.cs` | Move + edits | Source | 176 / 50 |
| R100 | [client/StS2AP/Utils/Connection/PendingCheckOutbox.cs](<../../client/StS2AP/Utils/Connection/PendingCheckOutbox.cs>)<br>From `client/StS2AP/Utils/PendingCheckOutbox.cs` | Exact move | Source | 0 / 0 |
| R076 | [client/StS2AP/Utils/Connection/PendingCheckUtility.cs](<../../client/StS2AP/Utils/Connection/PendingCheckUtility.cs>)<br>From `client/StS2AP/Utils/PendingCheckUtility.cs` | Move + edits | Source | 111 / 1 |
| M | [client/StS2AP/Utils/GameUtility.cs](<../../client/StS2AP/Utils/GameUtility.cs>) | Modified | Source | 175 / 146 |

<a id="chapter-09"></a>

### 09. Campaign selection, checkpoint/recovery saves, and continuation

**Question:** What exactly gets saved, selected, and restored?

ApMultiplayerCampaignStore/Flow and the picker add a campaign layer around native multiplayer saves: metadata, roster/identity validation, snapshots, checkpoint/recovery selection, and filesystem integrity. Patches_SaveManagement integrates saving and continuation. SerializableAP is moved and substantially reduced around the shared progress DTO. Trace the multiplayer native snapshot and the singleplayer envelope separately. Data schemas belong with chapter 03; this chapter owns the disk/UI/lifecycle flow.

**Entry points:**
- [client/StS2AP/Multiplayer/ApMultiplayerCampaignFlow.cs](<../../client/StS2AP/Multiplayer/ApMultiplayerCampaignFlow.cs>)
- [client/StS2AP/Multiplayer/ApMultiplayerCampaignStore.cs](<../../client/StS2AP/Multiplayer/ApMultiplayerCampaignStore.cs>)
- [client/StS2AP/Persistence/CampaignSaveFiles.cs](<../../client/StS2AP/Persistence/CampaignSaveFiles.cs>)
- [client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs>)
- [client/StS2AP/Persistence/SerializableAP.cs](<../../client/StS2AP/Persistence/SerializableAP.cs>)
- [client/StS2AP/UI/ApMultiplayerCampaignPicker.cs](<../../client/StS2AP/UI/ApMultiplayerCampaignPicker.cs>)

**Teach-back target:** Explain checkpoint versus recovery selection and which player identities can resume. Locate the actual payload and its integrity check.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/CampaignSaveTests.cs](<../../client/StS2AP.RegressionTests/CampaignSaveTests.cs>) | Added path | Test | 90 / 0 |
| D | `client/StS2AP/Models/SerializableAP.cs` (base only)<br>Same-named successor: [client/StS2AP/Persistence/SerializableAP.cs](<../../client/StS2AP/Persistence/SerializableAP.cs>) | Deleted path | Source | 0 / 101 |
| A | [client/StS2AP/Multiplayer/ApMultiplayerCampaignFlow.cs](<../../client/StS2AP/Multiplayer/ApMultiplayerCampaignFlow.cs>) | Added path | Source | 130 / 0 |
| A | [client/StS2AP/Multiplayer/ApMultiplayerCampaignStore.cs](<../../client/StS2AP/Multiplayer/ApMultiplayerCampaignStore.cs>) | Added path | Source | 788 / 0 |
| R068 | [client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_SaveManagement.cs>)<br>From `client/StS2AP/Patches/Patches_SaveManagement.cs` | Move + edits | Source | 138 / 47 |
| A | [client/StS2AP/Persistence/ApSerializationContext.cs](<../../client/StS2AP/Persistence/ApSerializationContext.cs>) | Added path | Source | 7 / 0 |
| A | [client/StS2AP/Persistence/CampaignSaveFiles.cs](<../../client/StS2AP/Persistence/CampaignSaveFiles.cs>) | Added path | Source | 51 / 0 |
| A | [client/StS2AP/Persistence/SerializableAP.cs](<../../client/StS2AP/Persistence/SerializableAP.cs>) | Added path | Source | 14 / 0 |
| A | [client/StS2AP/Persistence/SerializationUtility.cs](<../../client/StS2AP/Persistence/SerializationUtility.cs>) | Added path | Source | 24 / 0 |
| A | [client/StS2AP/UI/ApMultiplayerCampaignPicker.cs](<../../client/StS2AP/UI/ApMultiplayerCampaignPicker.cs>) | Added path | Source | 464 / 0 |

<a id="chapter-10"></a>

### 10. Ascension, progressive starters, and Ancient progression

**Question:** How do received progression items produce synchronized game mutations?

AscensionMultiplayer owns shared ascension changes. ProgressiveStarterMultiplayer and the shared F# starter domain handle validated tiers/recipes and synchronized applications; startup hooks also reconcile after native starting relic effects. AncientMultiplayer and Ancient reward patches connect progress confirmation, options, and stable choices. Character unlock/card-pool patches are a related per-player adaptation. Learn each as a separate execution path after the common identity, state, and action chapters.

**Entry points:**
- [client/StS2AP/Utils/Progression/AscensionMultiplayer.cs](<../../client/StS2AP/Utils/Progression/AscensionMultiplayer.cs>)
- [client/StS2AP.Domain/ProgressiveStarter.fs](<../../client/StS2AP.Domain/ProgressiveStarter.fs>)
- [client/StS2AP/DomainAdapters/ProgressiveStarterAdapter.cs](<../../client/StS2AP/DomainAdapters/ProgressiveStarterAdapter.cs>)
- [client/StS2AP/Utils/Progression/ProgressiveStarterMultiplayer.cs](<../../client/StS2AP/Utils/Progression/ProgressiveStarterMultiplayer.cs>)
- [client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs](<../../client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs>)
- [client/StS2AP/Utils/Progression/AncientMultiplayer.cs](<../../client/StS2AP/Utils/Progression/AncientMultiplayer.cs>)
- [client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs](<../../client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs>)

**Teach-back target:** Trace one starter upgrade during a run, then at new-run initialization. Explain the host ascension set and Ancient progress confirmation.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.Domain/ProgressiveStarter.fs](<../../client/StS2AP.Domain/ProgressiveStarter.fs>) | Added path | Source | 235 / 0 |
| A | [client/StS2AP.RegressionTests/ProgressiveStarterTests.cs](<../../client/StS2AP.RegressionTests/ProgressiveStarterTests.cs>) | Added path | Test | 227 / 0 |
| A | [client/StS2AP/DomainAdapters/ProgressiveStarterAdapter.cs](<../../client/StS2AP/DomainAdapters/ProgressiveStarterAdapter.cs>) | Added path | Source | 86 / 0 |
| A | [client/StS2AP/Models/ApAscensionDownActionMessage.cs](<../../client/StS2AP/Models/ApAscensionDownActionMessage.cs>) | Added path | Source | 14 / 0 |
| A | [client/StS2AP/Models/ApProgressiveStarterActionMessage.cs](<../../client/StS2AP/Models/ApProgressiveStarterActionMessage.cs>) | Added path | Source | 40 / 0 |
| D | `client/StS2AP/Patches/Patches_UnlockAllCards.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Progression/Patches_UnlockAllCards.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllCards.cs>) | Deleted path | Source | 0 / 64 |
| D | `client/StS2AP/Patches/Patches_UnlockAllPotions.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Progression/Patches_UnlockAllPotions.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllPotions.cs>) | Deleted path | Source | 0 / 64 |
| D | `client/StS2AP/Patches/Patches_UnlockAllRelics.cs` (base only)<br>Same-named successor: [client/StS2AP/Patches/Progression/Patches_UnlockAllRelics.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllRelics.cs>) | Deleted path | Source | 0 / 62 |
| R086 | [client/StS2AP/Patches/Progression/Patches_APProgressOnCharSelect.cs](<../../client/StS2AP/Patches/Progression/Patches_APProgressOnCharSelect.cs>)<br>From `client/StS2AP/Patches/Patches_APProgressOnCharSelect.cs` | Move + edits | Source | 30 / 19 |
| R072 | [client/StS2AP/Patches/Progression/Patches_AncientsUnlock.cs](<../../client/StS2AP/Patches/Progression/Patches_AncientsUnlock.cs>)<br>From `client/StS2AP/Patches/Patches_AncientsUnlock.cs` | Move + edits | Source | 23 / 5 |
| R069 | [client/StS2AP/Patches/Progression/Patches_AscensionOverride.cs](<../../client/StS2AP/Patches/Progression/Patches_AscensionOverride.cs>)<br>From `client/StS2AP/Patches/Patches_AscensionOverride.cs` | Move + edits | Source | 63 / 21 |
| R091 | [client/StS2AP/Patches/Progression/Patches_PreventEpochTriggers.cs](<../../client/StS2AP/Patches/Progression/Patches_PreventEpochTriggers.cs>)<br>From `client/StS2AP/Patches/Patches_PreventEpochTriggers.cs` | Move + imports/whitespace only | Source | 0 / 5 |
| A | [client/StS2AP/Patches/Progression/Patches_UnlockAllCards.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllCards.cs>) | Added path | Source | 35 / 0 |
| A | [client/StS2AP/Patches/Progression/Patches_UnlockAllPotions.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllPotions.cs>) | Added path | Source | 66 / 0 |
| A | [client/StS2AP/Patches/Progression/Patches_UnlockAllRelics.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockAllRelics.cs>) | Added path | Source | 66 / 0 |
| R100 | [client/StS2AP/Patches/Progression/Patches_UnlockUnderdocks.cs](<../../client/StS2AP/Patches/Progression/Patches_UnlockUnderdocks.cs>)<br>From `client/StS2AP/Patches/Patches_UnlockUnderdocks.cs` | Exact move | Source | 0 / 0 |
| R058 | [client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs](<../../client/StS2AP/Patches/Rewards/Patches_AncientRelics.cs>)<br>From `client/StS2AP/Patches/Patches_AncientRelics.cs` | Move + edits | Source | 170 / 63 |
| A | [client/StS2AP/Persistence/ApProgressiveStarterKindState.cs](<../../client/StS2AP/Persistence/ApProgressiveStarterKindState.cs>) | Added path | Source | 18 / 0 |
| A | [client/StS2AP/Persistence/ApProgressiveStarterPlayerState.cs](<../../client/StS2AP/Persistence/ApProgressiveStarterPlayerState.cs>) | Added path | Source | 8 / 0 |
| A | [client/StS2AP/Persistence/ApProgressiveStarterState.cs](<../../client/StS2AP/Persistence/ApProgressiveStarterState.cs>) | Added path | Source | 9 / 0 |
| A | [client/StS2AP/Utils/Progression/AncientMultiplayer.cs](<../../client/StS2AP/Utils/Progression/AncientMultiplayer.cs>) | Added path | Source | 161 / 0 |
| R082 | [client/StS2AP/Utils/Progression/AscensionManager.cs](<../../client/StS2AP/Utils/Progression/AscensionManager.cs>)<br>From `client/StS2AP/Utils/AscensionManager.cs` | Move + edits | Source | 45 / 6 |
| A | [client/StS2AP/Utils/Progression/AscensionMultiplayer.cs](<../../client/StS2AP/Utils/Progression/AscensionMultiplayer.cs>) | Added path | Source | 634 / 0 |
| A | [client/StS2AP/Utils/Progression/ProgressiveStarterMultiplayer.cs](<../../client/StS2AP/Utils/Progression/ProgressiveStarterMultiplayer.cs>) | Added path | Source | 691 / 0 |
| A | [client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs](<../../client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs>) | Added path | Source | 351 / 0 |
| D | `client/StS2AP/Utils/ProgressiveStarterUtility.cs` (base only)<br>Same-named successor: [client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs](<../../client/StS2AP/Utils/Progression/ProgressiveStarterUtility.cs>) | Deleted path | Source | 0 / 422 |
| R084 | [client/StS2AP/Utils/Rewards/AncientRelicPool.cs](<../../client/StS2AP/Utils/Rewards/AncientRelicPool.cs>)<br>From `client/StS2AP/Utils/AncientRelicPool.cs` | Move + edits | Source | 55 / 12 |
| A | [docs/design/progressive-starter-domain.md](<../../docs/design/progressive-starter-domain.md>) | Added path | Documentation | 94 / 0 |

<a id="chapter-11"></a>

### 11. Rest sites and shop behavior

**Question:** Which room operations are replicated, and which belong to the local owner?

The old CampfireSanity patch and extension are replaced by a RestSiteModel, explicit options, policy, and room hook. These resolve per-player settings/progress and use native rest-site synchronization. Shop code constructs a separate AP inventory/page from rolled native entries and authorizes local AP purchases; local shop presentation is not identical to replicated reward construction. Preserve this distinction when explaining RNG, slot unlocks, and check ownership.

**Entry points:**
- [client/StS2AP/Models/Singleton/ApRestSiteModel.cs](<../../client/StS2AP/Models/Singleton/ApRestSiteModel.cs>)
- [client/StS2AP/Utils/RestSitePolicy.cs](<../../client/StS2AP/Utils/RestSitePolicy.cs>)
- [client/StS2AP/Entities/RestSite/ApRestSiteOption.cs](<../../client/StS2AP/Entities/RestSite/ApRestSiteOption.cs>)
- [client/StS2AP/Patches/Rooms/Patches_RestSiteRoom.cs](<../../client/StS2AP/Patches/Rooms/Patches_RestSiteRoom.cs>)
- [client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs](<../../client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs>)
- [client/StS2AP/Patches/Rooms/Patches_ShopPages.cs](<../../client/StS2AP/Patches/Rooms/Patches_ShopPages.cs>)

**Teach-back target:** Explain two players with different Rest/Smith unlocks, collected checks, and separate shop inventories. Trace a purchase before gold is committed.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/RestSitePolicyTests.cs](<../../client/StS2AP.RegressionTests/RestSitePolicyTests.cs>) | Added path | Test | 127 / 0 |
| A | [client/StS2AP/Entities/RestSite/ApRestSiteOption.cs](<../../client/StS2AP/Entities/RestSite/ApRestSiteOption.cs>) | Added path | Source | 112 / 0 |
| A | [client/StS2AP/Entities/RestSite/FakeRestSiteOption.cs](<../../client/StS2AP/Entities/RestSite/FakeRestSiteOption.cs>) | Added path | Source | 18 / 0 |
| D | `client/StS2AP/Extensions/RestSiteOptionExtensions.cs` (base only) | Deleted path | Source | 0 / 44 |
| A | [client/StS2AP/Models/Singleton/ApRestSiteModel.cs](<../../client/StS2AP/Models/Singleton/ApRestSiteModel.cs>) | Added path | Source | 115 / 0 |
| D | `client/StS2AP/Patches/Patches_CampfireSanity.cs` (base only) | Deleted path | Source | 0 / 268 |
| A | [client/StS2AP/Patches/Rooms/Patches_RestSiteRoom.cs](<../../client/StS2AP/Patches/Rooms/Patches_RestSiteRoom.cs>) | Added path | Source | 52 / 0 |
| R091 | [client/StS2AP/Patches/Rooms/Patches_ShopPages.cs](<../../client/StS2AP/Patches/Rooms/Patches_ShopPages.cs>)<br>From `client/StS2AP/Patches/Patches_ShopPages.cs` | Move + edits | Source | 48 / 39 |
| R063 | [client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs](<../../client/StS2AP/Patches/Rooms/Patches_ShopSanity.cs>)<br>From `client/StS2AP/Patches/Patches_ShopSanity.cs` | Move + edits | Source | 334 / 199 |
| M | [client/StS2AP/UI/ProgressiveCampfireTopBarUI.cs](<../../client/StS2AP/UI/ProgressiveCampfireTopBarUI.cs>) | Modified | Source | 3 / 2 |
| A | [client/StS2AP/Utils/RestSitePolicy.cs](<../../client/StS2AP/Utils/RestSitePolicy.cs>) | Added path | Source | 55 / 0 |
| M | [client/StS2AP/godot/Archipelago/localization/eng/rest_site_ui.json](<../../client/StS2AP/godot/Archipelago/localization/eng/rest_site_ui.json>) | Modified | Configuration/asset | 0 / 1 |

<a id="chapter-12"></a>

### 12. DeathLink routing, damage, and deduplication

**Question:** How does a death cross AP and STS multiplayer boundaries exactly once per intended recipient?

DeathLinkMultiplayer introduces inbound requests, host-coordinated combat/noncombat actions, outbound instructions, queues, and event tracking. DeathLinkEventLedger isolates deduplication and echo suppression policy. Death hooks and DeathLinkUtility integrate native death/damage with AP callbacks. Shared-slot peers are still valid here; do not remove that terminology merely because the old AP Guest mode is gone.

**Entry points:**
- [client/StS2AP/Utils/DeathLink/DeathLinkEventLedger.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkEventLedger.cs>)
- [client/StS2AP/Utils/DeathLink/DeathLinkMultiplayer.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkMultiplayer.cs>)
- [client/StS2AP/Utils/DeathLink/DeathLinkUtility.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkUtility.cs>)
- [client/StS2AP/Patches/Lifecycle/Patches_DeathLink.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DeathLink.cs>)
- [client/StS2AP/Patches/Lifecycle/Patches_DeathLinkPlayerDeath.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DeathLinkPlayerDeath.cs>)

**Teach-back target:** Trace incoming damage for same-slot peers, self echoes, lethal damage, and a later legitimate death. Separate damage execution from sending an AP event.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/DeathLinkTests.cs](<../../client/StS2AP.RegressionTests/DeathLinkTests.cs>) | Added path | Test | 112 / 0 |
| R100 | [client/StS2AP/Models/Custom/DeathLinkCurse.cs](<../../client/StS2AP/Models/Custom/DeathLinkCurse.cs>)<br>From `client/StS2AP/Models/DeathLinkCurse.cs` | Exact move | Source | 0 / 0 |
| A | [client/StS2AP/Multiplayer/Messages/DeathLinkActionMessage.cs](<../../client/StS2AP/Multiplayer/Messages/DeathLinkActionMessage.cs>) | Added path | Source | 23 / 0 |
| A | [client/StS2AP/Multiplayer/Messages/DeathLinkInboundRequestMessage.cs](<../../client/StS2AP/Multiplayer/Messages/DeathLinkInboundRequestMessage.cs>) | Added path | Source | 16 / 0 |
| A | [client/StS2AP/Multiplayer/Messages/DeathLinkSendInstructionMessage.cs](<../../client/StS2AP/Multiplayer/Messages/DeathLinkSendInstructionMessage.cs>) | Added path | Source | 15 / 0 |
| R078 | [client/StS2AP/Patches/Lifecycle/Patches_DeathLink.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DeathLink.cs>)<br>From `client/StS2AP/Patches/Patches_DeathLink.cs` | Move + edits | Source | 18 / 8 |
| A | [client/StS2AP/Patches/Lifecycle/Patches_DeathLinkPlayerDeath.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DeathLinkPlayerDeath.cs>) | Added path | Source | 29 / 0 |
| A | [client/StS2AP/Utils/DeathLink/DeathLinkEventLedger.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkEventLedger.cs>) | Added path | Source | 64 / 0 |
| A | [client/StS2AP/Utils/DeathLink/DeathLinkMultiplayer.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkMultiplayer.cs>) | Added path | Source | 992 / 0 |
| R083 | [client/StS2AP/Utils/DeathLink/DeathLinkUtility.cs](<../../client/StS2AP/Utils/DeathLink/DeathLinkUtility.cs>)<br>From `client/StS2AP/Utils/DeathLinkUtility.cs` | Move + edits | Source | 30 / 8 |

<a id="chapter-13"></a>

### 13. UI support and diagnostics

**Question:** How is state exposed, and what helps investigate divergence?

Tracker/notification updates, popup ownership, and small utility changes support the new lifecycle. Diagnostic exports, developer commands, merchant-transition diagnostics, and local launch/divergence scripts form a separate operational surface. Inspect scripts before executing: generating a map does not run the launch harness or export potentially sensitive runtime data.

**Entry points:**
- [client/StS2AP/Utils/Diagnostics/ApBugReport.cs](<../../client/StS2AP/Utils/Diagnostics/ApBugReport.cs>)
- [client/StS2AP/Utils/Diagnostics/ApBugReportFiles.cs](<../../client/StS2AP/Utils/Diagnostics/ApBugReportFiles.cs>)
- [client/StS2AP/Patches/Rooms/Patches_MerchantTransitionDiagnostics.cs](<../../client/StS2AP/Patches/Rooms/Patches_MerchantTransitionDiagnostics.cs>)
- [client/StS2AP/Utils/Diagnostics/APDevCommand.cs](<../../client/StS2AP/Utils/Diagnostics/APDevCommand.cs>)
- [client/StS2AP/UI/ConfirmPopupUI.cs](<../../client/StS2AP/UI/ConfirmPopupUI.cs>)

**Teach-back target:** Identify which diagnostics establish ordering and identity, and what still needs two-process runtime observation.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [client/StS2AP.RegressionTests/BugReportTests.cs](<../../client/StS2AP.RegressionTests/BugReportTests.cs>) | Added path | Test | 100 / 0 |
| A | [client/StS2AP/Patches/Rooms/Patches_MerchantTransitionDiagnostics.cs](<../../client/StS2AP/Patches/Rooms/Patches_MerchantTransitionDiagnostics.cs>) | Added path | Source | 479 / 0 |
| M | [client/StS2AP/UI/ArchipelagoCharTrackerUI.cs](<../../client/StS2AP/UI/ArchipelagoCharTrackerUI.cs>) | Modified | Source | 10 / 7 |
| M | [client/StS2AP/UI/ArchipelagoGoalTrackerUI.cs](<../../client/StS2AP/UI/ArchipelagoGoalTrackerUI.cs>) | Modified | Source | 7 / 2 |
| M | [client/StS2AP/UI/ArchipelagoNotificationUI.cs](<../../client/StS2AP/UI/ArchipelagoNotificationUI.cs>) | Modified | Source | 13 / 17 |
| M | [client/StS2AP/UI/Components/ItemCountLabel.cs](<../../client/StS2AP/UI/Components/ItemCountLabel.cs>) | Modified | Source | 2 / 3 |
| M | [client/StS2AP/UI/ConfirmPopupUI.cs](<../../client/StS2AP/UI/ConfirmPopupUI.cs>) | Modified | Source | 22 / 18 |
| R056 | [client/StS2AP/Utils/Diagnostics/APDevCommand.cs](<../../client/StS2AP/Utils/Diagnostics/APDevCommand.cs>)<br>From `client/StS2AP/Utils/APDevCommand.cs` | Move + edits | Source | 33 / 15 |
| A | [client/StS2AP/Utils/Diagnostics/ApBugReport.cs](<../../client/StS2AP/Utils/Diagnostics/ApBugReport.cs>) | Added path | Source | 71 / 0 |
| A | [client/StS2AP/Utils/Diagnostics/ApBugReportFiles.cs](<../../client/StS2AP/Utils/Diagnostics/ApBugReportFiles.cs>) | Added path | Source | 119 / 0 |
| M | [client/StS2AP/Utils/GodotUtility.cs](<../../client/StS2AP/Utils/GodotUtility.cs>) | Modified | Source | 1 / 6 |
| M | [client/StS2AP/Utils/LogUtility.cs](<../../client/StS2AP/Utils/LogUtility.cs>) | Modified | Source | 1 / 2 |
| M | [client/StS2AP/Utils/MenuUtility.cs](<../../client/StS2AP/Utils/MenuUtility.cs>) | Modified | Source | 39 / 10 |
| M | [client/StS2AP/Utils/NotificationUtility.cs](<../../client/StS2AP/Utils/NotificationUtility.cs>) | Modified | Source | 24 / 19 |
| M | [client/StS2AP/Utils/TextUtility.cs](<../../client/StS2AP/Utils/TextUtility.cs>) | Modified | Source | 4 / 17 |
| A | [docs/multiplayer-bug-reports.md](<../../docs/multiplayer-bug-reports.md>) | Added path | Documentation | 55 / 0 |
| A | [scripts/analyze_multiplayer_divergence.ps1](<../../scripts/analyze_multiplayer_divergence.ps1>) | Added path | Build/tooling | 400 / 0 |
| A | [scripts/test_multiplayer_local.ps1](<../../scripts/test_multiplayer_local.ps1>) | Added path | Build/tooling | 244 / 0 |

<a id="chapter-14"></a>

### 14. API variants, loader, build, packaging, and release

**Question:** How does the changed client compile and load on each supported game API?

The loader selects a packaged variant and accommodates variant type discovery. StS2AP.csproj/local.props and compatibility helpers select references, defines, output paths, and native API adaptations. Assembly initialization also registers the domain and multiplayer services. Python release orchestration and variant assembly scripts change packaging/distribution. This is substantial supporting scope in the diff, with its own review path; it must not disappear behind the multiplayer feature description.

**Entry points:**
- [client/StS2AP.Loader/Bootstrap.cs](<../../client/StS2AP.Loader/Bootstrap.cs>)
- [client/StS2AP.Loader/Patches_VariantTypeDiscovery.cs](<../../client/StS2AP.Loader/Patches_VariantTypeDiscovery.cs>)
- [client/StS2AP/StS2AP.csproj](<../../client/StS2AP/StS2AP.csproj>)
- [client/StS2AP/Utils/BetaMainCompatibility.cs](<../../client/StS2AP/Utils/BetaMainCompatibility.cs>)
- [client/StS2AP/ModEntry.cs](<../../client/StS2AP/ModEntry.cs>)
- [scripts/release.py](<../../scripts/release.py>)
- [scripts/assemble_client_variants.ps1](<../../scripts/assemble_client_variants.ps1>)

**Teach-back target:** Explain what lives in the common loader versus per-version implementation, and distinguish DLL-only compile from packaging and game load.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| M | [CONTRIBUTING.md](<../../CONTRIBUTING.md>) | Modified | Documentation | 5 / 2 |
| M | [README.md](<../../README.md>) | Modified | Documentation | 11 / 4 |
| M | [StS2AP.sln](<../../StS2AP.sln>) | Modified | Build/tooling | 18 / 0 |
| A | [client/StS2AP.Domain/StS2AP.Domain.fsproj](<../../client/StS2AP.Domain/StS2AP.Domain.fsproj>) | Added path | Build/tooling | 15 / 0 |
| A | [client/StS2AP.Loader/Bootstrap.cs](<../../client/StS2AP.Loader/Bootstrap.cs>) | Added path | Source | 288 / 0 |
| A | [client/StS2AP.Loader/Patches_VariantTypeDiscovery.cs](<../../client/StS2AP.Loader/Patches_VariantTypeDiscovery.cs>) | Added path | Source | 110 / 0 |
| A | [client/StS2AP.Loader/StS2AP.Loader.csproj](<../../client/StS2AP.Loader/StS2AP.Loader.csproj>) | Added path | Build/tooling | 33 / 0 |
| A | [client/StS2AP.RegressionTests/PackagingTests.cs](<../../client/StS2AP.RegressionTests/PackagingTests.cs>) | Added path | Test | 118 / 0 |
| M | [client/StS2AP/Archipelago.json](<../../client/StS2AP/Archipelago.json>) | Modified | Configuration/asset | 2 / 2 |
| M | [client/StS2AP/ModEntry.cs](<../../client/StS2AP/ModEntry.cs>) | Modified | Source | 10 / 11 |
| M | [client/StS2AP/ModSettingsRegistration.cs](<../../client/StS2AP/ModSettingsRegistration.cs>) | Modified | Source | 17 / 3 |
| R053 | [client/StS2AP/Patches/Lifecycle/Patches_DisplayAPVersion.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DisplayAPVersion.cs>)<br>From `client/StS2AP/Patches/Patches_DisplayAPVersion.cs` | Move + edits | Source | 3 / 9 |
| M | [client/StS2AP/StS2AP.csproj](<../../client/StS2AP/StS2AP.csproj>) | Modified | Build/tooling | 180 / 173 |
| M | [client/StS2AP/Utils/BetaMainCompatibility.cs](<../../client/StS2AP/Utils/BetaMainCompatibility.cs>) | Modified | Source | 118 / 31 |
| M | [client/StS2AP/local.props.template](<../../client/StS2AP/local.props.template>) | Modified | Configuration/asset | 20 / 10 |
| A | [docs/releasing.md](<../../docs/releasing.md>) | Added path | Documentation | 100 / 0 |
| A | [docs/sts2-api-compat.md](<../../docs/sts2-api-compat.md>) | Added path | Documentation | 41 / 0 |
| A | [scripts/README.md](<../../scripts/README.md>) | Added path | Documentation | 162 / 0 |
| A | [scripts/assemble_client_variants.ps1](<../../scripts/assemble_client_variants.ps1>) | Added path | Build/tooling | 135 / 0 |
| M | [scripts/release-notes-template.md](<../../scripts/release-notes-template.md>) | Modified | Documentation | 11 / 5 |
| M | [scripts/release.ps1](<../../scripts/release.ps1>) | Modified | Build/tooling | 8 / 401 |
| A | [scripts/release.py](<../../scripts/release.py>) | Added path | Build/tooling | 912 / 0 |
| A | [scripts/tests/test_release.py](<../../scripts/tests/test_release.py>) | Added path | Test | 253 / 0 |

<a id="chapter-15"></a>

### 15. Test infrastructure and validation boundaries

**Question:** What do the added tests actually execute?

Feature tests are filed under their feature chapters in this map. This chapter owns shared test projects, artifact-dependent test support, and CI. F# tests exercise domain decisions; C# tests link selected production helpers. Packaging tests require actual artifacts. These do not prove native callbacks, scheduler execution, or two-process timing. Read the test project source links before using a green suite as evidence for a feature.

**Entry points:**
- [client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj](<../../client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj>)
- [client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj](<../../client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj>)
- [client/StS2AP.RegressionTests/ArtifactFactAttribute.cs](<../../client/StS2AP.RegressionTests/ArtifactFactAttribute.cs>)
- [.github/workflows/test-domain.yml](<../../.github/workflows/test-domain.yml>)
- [.github/workflows/build-sts2-compat.yml](<../../.github/workflows/build-sts2-compat.yml>)

**Teach-back target:** For each PR claim, identify whether its evidence is a unit test, build, packaged artifact check, or game scenario.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| A | [.github/workflows/build-sts2-compat.yml](<../../.github/workflows/build-sts2-compat.yml>) | Added path | Build/tooling | 61 / 0 |
| A | [.github/workflows/test-domain.yml](<../../.github/workflows/test-domain.yml>) | Added path | Build/tooling | 35 / 0 |
| A | [client/StS2AP.Domain.Tests/README.md](<../../client/StS2AP.Domain.Tests/README.md>) | Added path | Test | 40 / 0 |
| A | [client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj](<../../client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj>) | Added path | Test | 25 / 0 |
| A | [client/StS2AP.RegressionTests/ArtifactFactAttribute.cs](<../../client/StS2AP.RegressionTests/ArtifactFactAttribute.cs>) | Added path | Test | 12 / 0 |
| A | [client/StS2AP.RegressionTests/README.md](<../../client/StS2AP.RegressionTests/README.md>) | Added path | Test | 90 / 0 |
| A | [client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj](<../../client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj>) | Added path | Test | 61 / 0 |

<a id="chapter-16"></a>

### 16. APWorld contract and version changes

**Question:** What changes on the Python side of this PR?

Only five APWorld files differ. Both version owners move from 1.0.1 to 1.1.0; compat_flag stays 1. options.py documents strict configured-character installation and corrects wording. regions.py adds a comment explaining unchanged location counts. option_tests.py adds the anytime-Neow contract test. No item/location table file or production region-generation logic changes in this comparison. This chapter cross-references strict client character validation and boss compensation.

**Entry points:**
- [world/spire2/world.py](<../../world/spire2/world.py>)
- [world/spire2/options.py](<../../world/spire2/options.py>)
- [world/spire2/regions.py](<../../world/spire2/regions.py>)
- [world/spire2/test/option_tests.py](<../../world/spire2/test/option_tests.py>)
- [world/spire2/archipelago.json](<../../world/spire2/archipelago.json>)

**Teach-back target:** Explain precisely what changes in the generated-world contract and what is only documentation/test coverage.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| M | [world/spire2/archipelago.json](<../../world/spire2/archipelago.json>) | Modified | Configuration/asset | 1 / 1 |
| M | [world/spire2/options.py](<../../world/spire2/options.py>) | Modified | Source | 5 / 7 |
| M | [world/spire2/regions.py](<../../world/spire2/regions.py>) | Modified | Source | 5 / 0 |
| M | [world/spire2/test/option_tests.py](<../../world/spire2/test/option_tests.py>) | Modified | Test | 28 / 1 |
| M | [world/spire2/world.py](<../../world/spire2/world.py>) | Modified | Source | 1 / 1 |

<a id="chapter-17"></a>

### 17. Source organization and repository housekeeping

**Question:** Which diff entries are mechanical or repository maintenance?

Global usings, folder reorganization, SDK/editor documentation, and removed generated Godot import artifacts add noise to the diff. Exact moves and moves with import-only changes are mechanically identified in the inventory. A rename with substantial edits is still assigned to its behavioral chapter. Files shown by Git as delete/add pairs are linked to same-named successors where present; this does not imply they are behavior-preserving.

**Entry points:**
- [client/StS2AP/GlobalUsings.cs](<../../client/StS2AP/GlobalUsings.cs>)
- [client/StS2AP/README.md](<../../client/StS2AP/README.md>)

**Teach-back target:** Account for every move and deletion without treating similarity percentages as proof of preserved behavior.

**Study status:** Mapped; detailed walkthrough not started.

| Git | File | Change classification | Surface | + / - |
| --- | --- | --- | --- | ---: |
| M | [.gitignore](<../../.gitignore>) | Modified | Configuration/asset | 4 / 0 |
| D | `.vscode/settings.json` (base only) | Deleted path | Configuration/asset | 0 / 4 |
| M | [client/StS2AP/Data/Constants.cs](<../../client/StS2AP/Data/Constants.cs>) | Modified | Source | 0 / 2 |
| A | [client/StS2AP/GlobalUsings.cs](<../../client/StS2AP/GlobalUsings.cs>) | Added path | Source | 4 / 0 |
| R094 | [client/StS2AP/Models/Configuration/ClientSettings.cs](<../../client/StS2AP/Models/Configuration/ClientSettings.cs>)<br>From `client/StS2AP/Models/ClientSettings.cs` | Move + imports/whitespace only | Source | 1 / 7 |
| R099 | [client/StS2AP/Models/Custom/RelicCoupons.cs](<../../client/StS2AP/Models/Custom/RelicCoupons.cs>)<br>From `client/StS2AP/Models/RelicCoupons.cs` | Move + imports/whitespace only | Source | 0 / 1 |
| R089 | [client/StS2AP/Patches/Lifecycle/Patches_DisableTutorial.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_DisableTutorial.cs>)<br>From `client/StS2AP/Patches/Patches_DisableTutorial.cs` | Move + imports/whitespace only | Source | 0 / 7 |
| R099 | [client/StS2AP/Patches/Lifecycle/Patches_PauseMenuBehavior.cs](<../../client/StS2AP/Patches/Lifecycle/Patches_PauseMenuBehavior.cs>)<br>From `client/StS2AP/Patches/Patches_PauseMenuBehavior.cs` | Move + imports/whitespace only | Source | 1 / 4 |
| R097 | [client/StS2AP/Patches/Rooms/Patches_LordsParasolFix.cs](<../../client/StS2AP/Patches/Rooms/Patches_LordsParasolFix.cs>)<br>From `client/StS2AP/Patches/Patches_LordsParasolFix.cs` | Move + imports/whitespace only | Source | 0 / 2 |
| A | [client/StS2AP/README.md](<../../client/StS2AP/README.md>) | Added path | Documentation | 56 / 0 |
| D | `client/StS2AP/godot/.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.ctex` (base only) | Deleted path | Configuration/asset | - / - |
| D | `client/StS2AP/godot/.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.md5` (base only) | Deleted path | Configuration/asset | 0 / 3 |
| D | `client/StS2AP/godot/images/APIcon.png.import` (base only) | Deleted path | Configuration/asset | 0 / 40 |
| D | `client/StS2AP/godot/images/ui/rest_site/option_filler.png.import` (base only) | Deleted path | Configuration/asset | 0 / 40 |
| D | `client/StS2AP/godot/images/ui/rest_site/option_progression.png.import` (base only) | Deleted path | Configuration/asset | 0 / 40 |
| D | `client/StS2AP/godot/images/ui/rest_site/option_trap.png.import` (base only) | Deleted path | Configuration/asset | 0 / 40 |
| D | `client/StS2AP/godot/images/ui/rest_site/option_useful.png.import` (base only) | Deleted path | Configuration/asset | 0 / 40 |

## Session cleanup overlay

The learning map exposed stale terminology. The following edits were uncommitted during the lesson and are not included in the pinned 234-entry totals. They are now included in commit cbf37df; see README.md for the newer branch state:

- Shop settings comment: replace removed AP Guest/HostCharacterOnly model with the local player's frozen slot settings.
- MultiplayerSupport: correct saved-progress ownership wording; remove unused GetHostSettingsForReceiptRelay (no repository references found).
- Gold binding comment: describe the local player's aggregate cursor.
- Pending check diagnostics: replace misleading host-owned labels with pending multiplayer checks.
- Item processor comment: describe the multiplayer feature gates.

These are terminology/dead-helper cleanup, not a protocol or gameplay redesign. Validation: git diff --check passed. The targeted maintained-source search found no remaining obsolete AP Guest, HostCharacterOnly, or ReceiptRelay references. DLL-only build attempts for 0.107.1 and 0.111.0 were blocked by NuGet access (NU1301); compilation and in-game behavior are unverified. No tests were added for this terminology cleanup.

## Important PR scope boundaries

- Do not present all 234 entries as newly implemented multiplayer behavior. The inventory includes structural movement, independent behavior changes, tests, and release infrastructure.
- Strict configured-character loading, campaign management, gold conversion, and per-version packaging deserve explicit reviewer attention.
- Keep disabled-feature behavior in the PR explanation: CombatEffects, DeathFragments, and UnknownReceivedItems are absent from EnabledExperimentalFeatures. Inspect item classification/deferral before claiming all received items behave identically between play modes.
- Existing docs contain prior validation reports; those are not fresh validation of this pinned revision.
- The next lesson is chapter 01: native NetId versus AP slot identity versus receipt index, then the two participation kinds and their resolver.
