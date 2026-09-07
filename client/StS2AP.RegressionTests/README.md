# C# regression and packaging tests

This is an xUnit project. Run it from the repository root:

```powershell
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release
```

The normal suite references the domain library and links production C# helpers. It does not
build the game client or require Godot, RitsuLib, a game installation, an AP server, local.props,
or files from the locally excluded admission harness. Each case is discoverable through xUnit.

| Suite | Coverage |
| --- | --- |
| Received item ledger | Duplicate registration, independent receipt indexes, restore before history, partial/failed catalogue replacement, new-run reset, immutable read views, and per-player consumption |
| Relic receipts | Receipt ownership, chest/menu conflicts, late receipts, stale progress, exact assignments, consumption, JSON round trips |
| Campaign saves | Checksums, immutable checkpoint/recovery payloads, corrupt/missing files, path validation; isolated temporary directories |
| DeathLink | Per-recipient deduplication, self-echo tracking, lethal suppression, expiry, reset; explicit timestamps without sleeping |
| Universal buff gold | Fractional shares, live/history equivalence, independent character balances, missing configuration |
| Reward travel | Stale menu generations, travel/loading gates, reset, old travel completion |
| Noncombat admission | Idle admission and each independently unsafe state; scheduler execution is excluded |
| Rest-site policy | Rest/Smith locks, disabled actions, safe exits, relic actions, reached-act check ordering, collected checks, independent player inputs |
| Session identity | Server-qualified destination versus run-slot equality, validated persisted identities, missing/invalid field rejection, and existing outbox JSON/file-key compatibility |
| Participant interop | Existing wire kinds, missing/null inputs, and validated identities across C# mutation and fresh readiness checks; F# tests cover readiness and resume decisions |
| Replica construction | Initialization, local counters, compensation, restore |
| C# interop | Complete mirrored-reward decoding, actual wire/save DTO fixtures, immutable snapshots, save/reveal/effect preservation, and exception conversion |
| Progressive starters | Shared singleplayer/multiplayer tier transitions, initialization-only removal, recipe identity, strict state/payload decoding, applied-state ordering, and save round trips |

The rest-site hook calls the same `RestSitePolicy` compiled into these tests. Tests supply
option availability and arbitrary location IDs; they do not reimplement native Smith behavior,
AP ID encoding, character resolution, or the Godot scene tree. The real hook still owns AP/guest
eligibility, reads per-player progress, and calls `LocationData` to construct real check IDs.
The persisted `ApRewardEffectSpec` is also linked from production, not replaced by a stub.

The excluded `StS2AP.AdmissionTests` console harness is not a dependency and remains local.
Its scheduler tests and Godot/game stubs are intentionally not migrated. F# domain rules and
generated-input tests remain in `StS2AP.Domain.Tests`; C# logic need not be rewritten to test it.

Packaging tests need built artifacts. Without the corresponding environment variable,
xUnit reports them as skipped. A supplied path that is missing or invalid fails the test.

```powershell
# Check an intermediate or output client DLL. Repeat for each compatibility variant.
$env:STS2AP_TEST_ASSEMBLY = (Resolve-Path 'client/StS2AP/obj/0.111.0/Debug/net9.0/Archipelago.dll').Path
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release --filter 'Category=Manifest'

# Check a complete bundle from a full client build, including both variants and the loader.
$env:STS2AP_TEST_BUNDLE = (Resolve-Path 'artifacts/fsharp-trial-game/mods/Archipelago').Path
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release --filter 'Category=Bundle'

Remove-Item Env:STS2AP_TEST_ASSEMBLY, Env:STS2AP_TEST_BUNDLE
```

The manifest test opens both embedded JSON manifests and checks their version fields.
The bundle test also checks the external mod version and uses the shipped loader to call
the C# adapter in both variants. It verifies that FSharp.Core and the domain DLL load from
the bundle, so the test runner cannot hide a missing packaged dependency.
Neither test starts Godot or proves in-game behavior.

CI runs all non-artifact cases on Windows and Linux. Pushes changing any client source or
either test project trigger the workflow, as do pull requests. The compatibility workflow
runs the manifest test after compile-only validation and again after an incremental build
for both game API versions. The complete bundle test runs locally against a full build.

## Runtime validation kept separate

These checks require the installed supported game and matching RitsuLib. Run multiplayer
checks with two processes on beta 0.111.0; repeat affected singleplayer behavior on the public
variant. Unit tests establish the behavior of our policies, not native callbacks or network timing.

| In-game scenario | Expected result |
| --- | --- |
| Rest unlocked/locked; Smith with/without an upgrade target; both locked; relic-provided action | Valid native actions survive; an exit exists when needed; taking an AP check is optional |
| Campfire sanity off, vanilla guest, or unresolved AP progress | Native options remain unchanged |
| `!collect`, then enter another rest site with two AP slots | Collected checks stay hidden on all replicas; another slot's checks remain available |
| Claim a relic, reopen rewards, reconnect, save/continue | One grant at the established boundary; stable assignment and no repeated bank spending |
| Skip an AP card or try a potion with no space, then reopen rewards | Receipt remains claimable with its existing assignment |
| Consume AP rewards, continue the save, then start a fresh run | Continue preserves consumption while history is rebuilt; the fresh run resets consumption and retains known receipts |
| Reconnect to the same AP destination; try a different room/team/slot when continuing | The same destination retains deferred receipts/outbox ownership; mismatched saved participation is rejected |
| Own-slot lobby with delayed preparation, then prepared empty history | Ready/launch remains blocked with `ap-history-incomplete` until preparation; zero receipts do not block a prepared participant |
| Explicit vanilla guest, absent contribution, or a participant joining just before launch | Vanilla needs no AP data; missing/new contributors are checked against the latest roster at the final launch boundary |
| Continue as the same participant, then try the wrong participation kind or slot | Matching preserves saved progress; mismatches report `Saved AP multiplayer identity mismatch` if reached during binding and disable AP claims |
| DeathLink with same-slot peers, lethal damage, and death prevention | Intended recipients are affected once; no echo loop; later legitimate deaths still send |
| Open rewards while starting travel, then return or start a new run | Stale pending menu work cannot open in the next room/run |
| Save at a checkpoint, advance, then continue/recover | Correct checkpoint/recovery selected and native run data restored |

Useful existing logs include `Applied AP rest-site options for player`,
`Leaving native rest-site options unchanged`, and `Published ... campfire check(s) from an AP
location update`. Check both replicas and actual grants/saves; logs alone are not runtime proof.
Keep generated saves, diagnostic logs, installed binaries, and decompiled references local.
