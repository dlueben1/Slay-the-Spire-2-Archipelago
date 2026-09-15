# C# regression and packaging tests

This is an xUnit project. Run it from the repository root:

```powershell
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release
```

The normal suite references the domain library and links production C# helpers. Each case is discoverable through xUnit.

## Human Note:
I've had a look at these test cases and for the most part they look roughly correct in my eyes
but maybe they aren't testing exactly what they should be but they should be a good baseline for at least
some tests. There might be a few suites which I haven't looked at as intensely.

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
| C# interop | Complete mirrored-reward decoding, actual wire/save DTO fixtures, immutable snapshots, save/reveal preservation and strict saved-card strategy rejection, and exception conversion |
| Card offers | Deferred recipes, independent-offer digests, receipt/card-offer mismatch rejection, canonical JSON object order versus significant array order, refreshed models through save/progress deltas, and rejection of unsupported card contracts |
| AP selection order | Delayed relic completion before reveal, invocation order rather than receipt order, repeated skips, per-player independence, failure blocking, and reopen waiting |
| Progressive starters | Shared singleplayer/multiplayer tier transitions, initialization-only removal, recipe identity, strict state/payload decoding, applied-state ordering, and save round trips |

The rest-site hook calls the same `RestSitePolicy` compiled into these tests. Tests supply
option availability and arbitrary location IDs; they do not reimplement native Smith behavior,
AP ID encoding, character resolution, or the Godot scene tree. The real hook still owns AP/guest
eligibility, reads per-player progress, and calls `LocationData` to construct real check IDs.

Card-offer tests use opaque serialized card fixtures. They exercise the actual codec, domain
validation, and persistence helpers; they do not execute MegaCrit's card factory, Egg hooks,
native picker, or live choice transport. Queue tests run the production async queue, but not its
Harmony interception or the native message dispatcher. Those require a game-backed integration harness.

F# domain rules and generated-input tests remain in `StS2AP.Domain.Tests`; C# logic need not be rewritten to test it.

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
| Two clients on 2.3.1: reveal/reopen cards, apply a progressive starter and Ascension Down, and trigger DeathLink | Matching offers verify; each ordered action applies once without message schema fields |
| Two clients on 2.3.1: publish initial progress and a delta, claim a banked relic, and finish a treasure room | Renamed snapshot/delta, relic receipt, and treasure readiness routes reach their handlers; progress agrees and both players can proceed |
| Connect a 2.3.0 client to a 2.3.1 host | Native lobby rejects the connection with `ModMismatch` before AP actions begin |
| Continue a current campaign, then try an unsupported saved AP schema | Current campaign continues; unsupported campaign stays blocked with a clear refusal and its save preserved |
| Continue a campaign with missing/owner-final card strategies or nonempty obsolete effect records | Campaign picker refuses before activation; log contains `Blocked AP campaign continue` and the saved campaign is preserved |
| Rest unlocked/locked; Smith with/without an upgrade target; both locked; relic-provided action | Valid native actions survive; an exit exists when needed; taking an AP check is optional |
| Campfire sanity off, vanilla guest, or unresolved AP progress | Native options remain unchanged |
| `!collect`, then enter another rest site with two AP slots | Collected checks stay hidden on all replicas; another slot's checks remain available |
| Claim a relic, reopen rewards, reconnect, save/continue | One grant at the established boundary; stable assignment and no repeated bank spending |
| Singleplayer: first reveal a regular/rare AP card reward, skip, reopen, and save/continue | Picker opens without a player-state snapshot; offer stays assigned, relic effects do not repeat, and claiming consumes the receipt once |
| Skip an AP card or try a potion with no space, then reopen rewards | Receipt remains claimable with its existing assignment |
| Open a backlog with Tress, then reveal 81 before 80 | Opening the list spends no uses; both replicas generate in reveal order and Tress is consumed on the first offer |
| Reveal, skip, obtain Toxic/Molten/Frozen Egg, then reopen (also after save/continue) | Eligible unupgraded skills/attacks/powers appear upgraded on both replicas; identities, ordering, enchantments, generation counters, and reroll availability survive |
| Reopen the upgraded offer repeatedly, then claim it | No additional generation effects; the deck receives the displayed upgrade and the receipt is consumed once |
| Obtain Prismatic Gem/Dingy Rug before first revealing a pending reward | The first roll uses the currently modified card pool |
| Obtain a relic, immediately reveal, skip and reopen with a delayed peer | Per-player AP selection order completes the grant before generation; the next menu waits for the preceding selection |
| Open/reopen card rewards using keyboard/controller with a delayed peer | Each replica generates locally and verifies the owner digest before interpreting its picker choice; input/focus and skip work normally |
| Intentionally introduce a card identity, order, upgrade, or enchantment mismatch on a replica | Digest disagreement rejects that replica's picker operation and invalidates AP claims; no correction or reroll is attempted |
| Two AP players with different modded pools reveal concurrently | Offers use the correct player's settings; each replica agrees with its owner, with no cross-player queue blocking |
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
For card offers, `Prepared replicated AP card offer` reports `firstReveal` and `localOwner`.
`Replicated AP card offer ... disagreed with the owner` is a terminal mismatch, not a retry.
The owner publishes a digest; it does not await an acknowledgment from every replica. Each
replica verifies before its own native picker execution. The eight-integer SHA-256 digest covers receipt identity,
offer configuration, first-reveal status, and ordered serialized cards. Singleplayer does not
calculate a verification digest. General gameplay state (including before/after relic counters
and RNG snapshots) is left to the game's native checksum system. In beta 0.111.0,
RewardsSetSynchronizer.SelectRewardForPlayer does not request a checksum, and
RunManager.SendPostActionChecksum runs during combat; this is not an immediate reward-side-effect
check. NetFullCombatState includes saved relic properties but explicitly excludes the Rewards
and Shops player RNG streams. Removing snapshots gives up their extra diagnostic coverage;
native checksums are not equivalent to the removed snapshot. Keeping the offer digest prevents
a remote picker index from selecting a different card.
Test with matching game/mod builds: the native lobby checks declared mod versions. Live messages
have no schema field, routing keys have no numeric version suffix, and the digest has no version prefix.
Saved-data schemas and campaign refusal checks remain. Client 2.3.1 removes obsolete effect fields and
the menu-time generation flag. Current saved card assignments require the replicated-card strategy;
missing strategies are rejected without a default. Nonempty obsolete effect records are refused
before campaign activation; an empty old field does not change an otherwise current contract.
The original replicated-generation migration requires a new run; old execution protocols
are not migrated. Crucible remains excluded by MegaCrit in multiplayer; its native behavior can
only be checked in single-player or an explicitly forced diagnostic scenario.
