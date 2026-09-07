# Numbered players in one AP slot

Experimental implementation branch: `experimental/onebighost`.

Use `player_count: 1` through `4` in one YAML. Each numbered player owns a full copy of
the generated character roster's checks and items. Four players generate four times the
checks of the same one-player configuration. Each player's logic uses only their own
items; the slot goals when every player independently meets `num_chars_goal`.

```yaml
Slay the Spire II:
  player_count: 4
  characters: [Ironclad, Silent, Defect, Regent, Necrobinder]
  pick_num_characters: 4
  lock_characters: locked_random
```

The final roster must contain at least `player_count` characters. `pick_num_characters: 0`
retains the whole roster. These rules also apply to advanced and modded character rosters.

| Character locks | Starting characters |
| --- | --- |
| `unlocked` | Everyone starts with the entire generated roster unlocked. |
| `locked_random` | Each numbered player starts with a different character, determined by the AP seed. |
| `locked_fixed` | Player 1 starts with `unlocked_character`; the others receive distinct, seeded starts from the remaining roster. |

Everyone connects to the same AP slot using the updated APWorld and client. Before
connecting, select a **Player Number** under **Archipelago Settings → Multiplayer Settings**.
The number identifies AP progression across characters and lobby order. It is frozen while
the slot is attached; leave the AP slot and run/lobby before changing it. An out-of-range
number rejects the AP connection: P3 cannot connect to a `player_count: 1` or `2` slot.
The host also validates the count (1–4) and selected number in every AP lobby contribution.
Multiple people can select the same numbered player. They receive that player's items and
send the same checks; repeating a check does not add another check. The other numbered
players' goals still need to be completed. The host must confirm a duplicate-number popup
before launch. Cancel leaves the host unready, and changing the lineup requires new consent.
This warning applies within one STS lobby; it does not reserve numbers across other lobbies.

## Generation performance

Shared slots use batched progression prefill when `accessibility: full` and
`include_floor_checks: true`. Minimal accessibility and disabled floor checks
remain valid configurations and use Archipelago's original filler. Priority or
excluded locations and item-link groups also retain the original fill path.
No YAML option is rewritten or rejected for this optimization.

`SlayTheSpire2World.stage_fill_hook` calls `fill_shared_slots` in `fill.py`.
The hook places eligible shared-slot progression in batches keyed by the real AP
slot and numbered co-op player. It preserves item IDs and ownership and applies
the normal location item/access rules. Other progression items are left for the
framework to fill. If a batch attempt stalls, all tentative placements and RNG
changes are restored and the original filler receives its original pool order.
This uses the framework's supported hook; it does not patch Archipelago globals.

The all-options basic-spoiler benchmark (five characters, all ascensions, all
sanities and maximum shop slots; seeds 101/202/303) improved from a median
11.98 seconds to 2.46 seconds for one four-player slot. Four separate one-player
slots measured 2.33 seconds in the same run. Both layouts have 2,736 checks and
1,416 progression items. These timings exclude interpreter/framework startup.
The previous restrictive failure, seed `953136251`, now uses the original path
and reproduces the original placements and slot data exactly.

Generation diagnostics use `[StS2AP Fill] Batched ...` for a successful fast path
or `[StS2AP Fill] Batched prefill stalled ... restoring the original fill.` for
a restored attempt. As with other changes to fill order, the optimized path can
produce different placements than older builds while remaining seed-deterministic.

## Contract and control flow

- `world/spire2/world.py::_setup_players` creates per-player character configurations after
  the existing roster selection. `regions.py` and `rules.py` use each configuration's AP name
  and independent power key. Slot data includes `player_count` and `players[number]`.
- `world/spire2/coop.py` reserves blocks of 1,000,000 IDs per player. Player 1 keeps the
  existing names and IDs. Player 2 uses `P2 Ironclad Relic`, `P2 Free Attack`, etc. Character
  offsets remain unchanged inside a player's block. Universal items remain universal only
  across that player's characters. Character-specific groups are qualified the same way;
  broad groups such as `Relics` include all numbered players.
- `ArchipelagoClient.GetPlayerSettings` loads the selected roster into `ArchipelagoSettings`.
  `CoopSlot` and `ArchipelagoIdCodec` translate player identity at the receipt and check
  boundaries. `Patches_ItemProcessor`, multiplayer history preparation, gold reconstruction,
  and Ascension Down replay filter by recipient without renumbering SDK receipt indexes.
- `ApRunData` saves the player's settings in native multiplayer contributions. Returning-player
  validation and campaign metadata include the player number. `SerializableAP` records it for
  solo saves. Server save/buff keys, local recovery files and durable check outboxes are separated
  by numbered player; new-run resets retain the connection's identity.
- `RequireCompleteApLobbyBeforeLaunch` validates contributions and calls
  `ApCoopLobbyWarning.AllowLaunch` at the native final all-ready boundary. The popup defers
  to the main thread, unreadies the host through the native character-select screen, and
  resumes through `OnEmbarkPressed` after consent. `CoopPlayerSelection` binds consent to
  the exact peer/AP identity roster, including count and number; all launch checks run again.
- `GameUtility.TrySetGoalAchieved` writes a qualified character record using an atomic dictionary
  update. `RestoreGoaledCharsFromStorage` watches merged server records and `CoopGoalPolicy`
  requires every player's quota. Victory release uses only the winning player's character checks.

This does not change the existing STS multiplayer reward synchronization, gold consumption,
boss compensation, or host-controlled ascension behavior. AP items remain private to their
numbered owner. Existing game mechanics can still help teammates.

## In-game verification still required

### Integration with participant validation

The `multiplayer-squashed` integration keeps the received-item ledger and Ancient settings
overrides. `ParticipantAdapter` projects the selected player number into `ParticipantSlot`,
so both returning-lobby validation and post-launch binding reject another numbered player's
save. `ContributionReadiness` checks the player count and number before accepting prepared
history. `ApRunData` includes both fields when comparing lobby contributions.

`ApSessionIdentity` includes that same numbered slot in reconnect and outbox ownership.
Its JSON converter preserves the number for players 2–4, rejects invalid values, and keeps
the existing player-1 JSON and file key. Regression tests cover these persistence boundaries;
domain tests cover numbered-player resume matching and lobby bounds.

### Runtime scenarios

Compilation and framework tests are static/local validation; the following needs actual beta
0.111.0 clients with the matching RitsuLib runtime. No in-game confirmation is claimed.

| Scenario | Expected result |
| --- | --- |
| Four clients, random/fixed/unlocked locks | Shared roster; distinct seeded starts when locked; fixed Player 1 honored. |
| Two clients select the same player number; host or remote readies last | Host receives a sharing warning before either client starts. Confirm launches; Cancel leaves the host unready and the lobby usable. |
| A peer leaves/changes while the duplicate popup is open | Old confirmation cannot launch the changed lineup; Embark revalidates it. |
| Another modal is open when launch reaches the warning | Host becomes unready; the existing modal is preserved, and Embark can retry after it closes. |
| Same number in different AP slots, or distinct numbers in one slot | No duplicate-number warning. |
| P3 selected for a one- or two-player YAML | Connection rejected with the selected number/count and instructions to change it. Invalid lobby contributions also block launch. |
| Player 2 claims a floor, card, relic, Ancient, shop, campfire, gold or potion check | Only the corresponding `P2` check is sent; other players' copies remain unchecked. |
| Interleaved P1/P2 character rewards, unlocks and universal items | Only the intended recipient gets progression; reward assignments keep their original receipt indexes. |
| Disconnect/reconnect with queued checks and unclaimed rewards | The same player's checks replay; claimed rewards stay consumed; assignments do not reroll. |
| Save/continue, changed lobby order, changed player number | Correct identity resumes normally; a different number cannot load that player's progress. |
| One player reaches their quota; the final player later finishes | The first completion does not goal the slot; the final completion goals it, including concurrent victories. |
| Victory with release enabled/disabled | Only that player's winning character is released, or no checks are released. |
| One-player YAML | Existing IDs, character logic, save keys and recovery filename remain intact. |

Useful logs include `Using co-op Player 2/4 in AP slot ...`,
`Host confirmed shared AP player numbers: P1 (AP slot ...)`,
`Prepared AP multiplayer session .../player-2@...`,
`Recorded location check: ... (1000000+base ID)`, and
`All 4 co-op players completed their character goals. SetGoalAchieved sent.`

## Local validation

The fuzzer helper accepts `-MetaPath scripts/fuzz/player_count.yaml` to exercise
two to four players with a sufficiently large roster while randomizing the other
options. Use `-Suite Full -Runs 100 -Jobs 4 -Timeout 60` for the reduced matrix;
omitting `-Runs` uses the helper's full 14,500-case defaults. The larger multiplayer
worlds can exceed the helper's default 10-second generation timeout.

- All 111 APWorld tests passed (`fill_tests`, `coop_tests`, `option_tests`, `logic_tests`, `group_tests`),
  using an isolated Archipelago framework copy with this checkout's world.
  This includes two successful restrictive-fill seeds with four independent players.
- The fuzz helper's core suite passed 122 tests, including the relevant framework tests.
  Its stale `id_tests` reference was replaced with `coop_tests`.
- All 1,100 multiplayer-profile fuzz cases passed: 100 each for baseline generation,
  no restrictive starts, Universal Tracker, Gerpocalypse, item/location count, lambda
  capture, placement references, determinism, indirect conditions, collection
  accessibility, and static output placement. There were no failures, timeouts, or
  ignored cases. These used four workers and a 60-second generation timeout in a
  temporary copy of Archipelago `0.6.7` (`debe4cf0`), with this checkout's world.
  The conditional-fill implementation passed this matrix again; its reports are
  in `artifacts/fuzz/production-batching/matrix/`. Another 25 multiworld cases with
  two batching-eligible shared slots and an unrelated Empty world also passed
  with no failures, timeouts, or ignored cases. The default 14,500-case
  matrix was **NOT RUN**.
- An initial unconstrained 100-case run at the default 10-second timeout reported
  36 successes, 62 rejected option combinations, no generation errors, and two
  timeouts. Both four-player timeout cases completed and passed beatability checks
  when replayed with their original seeds: `491896094` in 12.07 seconds and
  `84412350` in 12.38 seconds. Replay evidence is in
  `artifacts/fuzz/player-count-replay/`.
- All 17 selected C# regression cases passed (`CoopSlotTests`, `ApSessionIdentityTests`,
  `CoopPlayerSelectionTests`), including YAML number bounds, duplicate identities and stale consent.
- Beta `0.111.0` compilation passed using `dotnet build client/StS2AP/StS2AP.csproj -c Debug -t:Compile --no-restore`.
  The compile-only target reported the Godot `ScriptPathAttributeGenerator` warning (CS8785)
  and generated `Main` type conflict warning (CS0436). No client packaging/deployment was run.
- The APWorld was built with Archipelago's builder in a temporary staging directory, validated
  as an `APWorldContainer`/ZIP, and copied to `dist/spire2.apworld`. The sibling world was not replaced.
- `git diff --check` and the generated option's JSON bounds/default/group checks passed.
- In-game multiplayer validation: **NOT RUN**. Use the matrix above before treating this as runtime-confirmed.
