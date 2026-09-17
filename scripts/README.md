# Multiplayer divergence analyzer

Use `analyze_multiplayer_divergence.ps1` before manually comparing Slay the
Spire II multiplayer state dumps. The game usually prints a large amount of
identical state around one or two meaningful differences; this helper reduces
the dump to those differing fields.

This is a diagnostic tool. It identifies the state that differs at a checksum
boundary, but it does not prove which earlier operation caused that state.

## Quick use

Analyze one of the logs produced by `test_multiplayer_local.ps1`:

```powershell
.\scripts\analyze_multiplayer_divergence.ps1 `
    .\logs\multiplayer\host_standard-1.log
```

Analyze every local multiplayer log:

```powershell
.\scripts\analyze_multiplayer_divergence.ps1 `
    .\logs\multiplayer\*.log
```

If the divergence was copied from the in-game log viewer, put the complete
message on the clipboard and omit the path:

```powershell
.\scripts\analyze_multiplayer_divergence.ps1
```

Text can also be piped directly:

```powershell
Get-Content .\divergence.txt -Raw |
    .\scripts\analyze_multiplayer_divergence.ps1
```

By default the script prints at most 50 differences per dump. Increase this
only when the initial report is truncated:

```powershell
.\scripts\analyze_multiplayer_divergence.ps1 `
    .\divergence.txt `
    -MaxDifferences 200
```

The input must contain the detailed `LOCAL STATE DUMP` and
`REMOTE STATE DUMP` sections. The shorter "checksum doesn't match" exception
does not contain enough state to compare.

## Recommended debugging workflow

1. Preserve both process logs immediately after the first divergence. The
   local launcher writes separate host and guest logs under
   `logs/multiplayer/`; starting another test can overwrite them.
2. Analyze both logs. A peer may contain an earlier or more informative dump
   than the host.
3. Start with the earliest checksum divergence. Later mismatches are often
   consequences of the first one.
4. Record the checksum ID, reported client, room/action context, local and
   remote checksum values, and every field reported as different.
5. Search the raw logs immediately before that checksum for AP receipt,
   reward, option-construction, and managed-action messages. The state dump
   describes the result; the preceding log window usually describes the cause.
6. Trace the relevant base-game and mod control flow. Treat decompiled source
   as static evidence and verify runtime-sensitive fixes in a two-process run.
7. Re-run the exact scenario, including save/reconnect or reward reopening if
   those actions preceded the original divergence.

Do not begin with the final divergence in a long run, and do not assume that a
relic named in the test is responsible merely because the divergence occurred
after obtaining it.

## Reading the report

The heading identifies the source log and the dump's order within that log:

```text
=== host_standard-1.log :: divergence 1 ===
Checksum ID: 64 | Reported client: 1000
Context: Exiting event room EVENT.SOME_EVENT.
Checksums: local=123 remote=456
Differences: 1 | Matching parsed fields: 65
```

`LOCAL` is the state of the process whose log contains the detailed dump.
`REMOTE` is the state sent by the reported peer. It does not necessarily mean
that local is correct: determine authority from the game action and ownership
rules.

Common mismatch categories:

| Report key | Usually investigate |
| --- | --- |
| `Run/Choice IDs` | Replicated option construction, filtering, ordering, or selection |
| `Run/Reward IDs` | Reward insertion, nested rewards, claim order, or removal |
| `Player <id>/Relic/<relic>` | Wrong owner, duplicate/missing grant, or differing relic properties |
| `Player <id>/RNG/<stream>` | An operation advanced that RNG stream on only some replicas |
| `Player <id>/Relic grab bag/<rarity>` | Relic pull/removal was not mirrored, or the bag order differs |
| `Global/RNG/<stream>` | A shared construction path or global random operation differs |
| Player gold, energy, piles, or counts | An action applied to the wrong owner or executed a different number of times |

For relic bags, the script distinguishes different contents from identical
contents in a different order. Order-only differences are still important:
they may not affect the current relic but can change a later pull.

The final `Focus:` line is a search hint based on the mismatch category. It is
not a root-cause determination.

## Interpreting apparently clean dumps

If the script reports no differing parsed fields but the checksums differ:

- inspect the raw dump for a state format the parser does not yet recognize;
- check state included in the checksum but omitted from the printed dump;
- look for timing-sensitive state that changed between checksum creation and
  dump generation;
- compare the corresponding host and guest log windows rather than relying on
  one process;
- add a narrowly scoped runtime log if source tracing identifies an unprinted
  state candidate.

Do not treat "no parsed differences" as proof that the game states match.

## Extending the parser

The script is intentionally dependency-free and compatible with Windows
PowerShell 5.1. Parsing is centralized in `ConvertTo-StateEntries`.

When the base game adds or changes dump fields:

1. Preserve a real raw divergence sample.
2. Add a stable key for the new line format in `ConvertTo-StateEntries`.
3. Include the owning player or global scope in the key.
4. Preserve ordering for sequences where order changes future behavior.
5. Run the script against both the new sample and an older sample.
6. Confirm that identical fields remain collapsed and only real differences
   are printed.

Avoid special-casing a specific relic, room, character, or checksum ID. The
parser should describe state generically so it remains useful for the next
multiplayer feature.

## Handoff checklist

A useful multiplayer divergence handoff should include:

- the host and guest raw logs;
- the analyzer output for the earliest divergence;
- the exact reproduction sequence;
- player IDs, AP slots, characters, and which process hosted the game;
- relevant AP options and received item indices;
- whether the run was new, continued, reconnected, or rolled back;
- whether the issue reproduced after restarting from the same checkpoint;
- the expected ownership and consumption behavior.

Keep runtime confirmation separate from source evidence and compilation. A
successful build cannot confirm that multiplayer replicas execute the same
actions in the same order.
