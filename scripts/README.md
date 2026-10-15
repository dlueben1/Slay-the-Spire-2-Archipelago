# Local client build and deployment

Rider Build and `dotnet build client/StS2AP/StS2AP.csproj` default to `BuildMode=Local`:
export the PCK, build both supported game API variants and the loader, and copy
the bundle into `$(STS2GamePath)/mods/Archipelago`. The bundle step uses Python 3
on Windows, native Linux, and WSL; set `PythonExe` in `client/StS2AP/local.props` if Python
is not on PATH. `GodotExePath` must point to an editor executable for the OS
running the build. WSL builds use Linux paths, including `/mnt/d/...` for a
Windows game installation; native Windows builds use Windows paths.

Remove `BuildMode=CompileOnly` and any staging `ModsOutputDir` override from
`local.props` to enable normal deployment. For a check without deployment, run
`dotnet build client/StS2AP/StS2AP.csproj -p:BuildMode=CompileOnly`.
The APWorld is copied from `dist/spire2.apworld` when present; build it on Linux/WSL
with `.venv/bin/python scripts/build_apworld_local.py`.

# Release preparation

See [the release guide](../docs/releasing.md) for the supported manual workflow.
`python scripts/release.py validate` checks the source versions;
`python scripts/release.py build` creates a clean loader/both-variants bundle,
APWorld, YAML template and build manifest without installing or publishing them.
`publish` creates a GitHub draft and requires an explicit `--branch` and a clean,
pushed commit. Pick `--remote upstream` for the public release repository and
`--remote origin --prerelease` for a beta draft in your fork.

`scripts/build_world.ps1` delegates to the same headless Python APWorld builder.
`scripts/release.ps1` is a Windows convenience wrapper; use the Python CLI directly
on Linux. `fuzz_world.ps1` remains a historical Windows/Archipelago 0.6.7 Index/UT
fuzz harness with explicit branch/environment assumptions. It is not a required
release command and has not been ported or validated for native Linux/0.6.8.

# GitHub Actions check ownership

- `pr-checks.yml` runs script tests and builds the APWorld for PRs.
- `build-sts2-compat.yml` owns public/beta client compilation, loader compilation,
  and manifest checks after compile-only and incremental builds.
- `test-domain.yml` runs domain and client regression tests on Linux and Windows.
- The latter two use `ci-event.yml` to skip their push-side work when an open,
  mergeable PR in the same repository covers that exact commit. Conflicting PRs,
  unknown mergeability and branches without PRs retain push checks, subject to
  the existing branch/path filters. Manual runs always run. The selector fails
  if the GitHub query fails rather than silently skipping.
- Release packaging continues to use `build-client.yml` and `build-apworld.yml`.

# Local multiplayer test on Linux, Windows or WSL

This harness opens two isolated StS2 beta accounts with Steam disabled, then
uses the game's local fast-multiplayer path after each AP slot connects.
Install the development mod **and RitsuLib locally under the game's `mods/`**;
Workshop-only subscriptions are not loaded with Steam disabled. Start an AP
session containing your chosen slot(s) before the multiplayer test.

On native Linux, the shell script uses the Python 3 standard-library launcher:

```bash
./scripts/test_multiplayer_local.sh --dry-run
./scripts/test_multiplayer_local.sh --settings-only
./scripts/test_multiplayer_local.sh --ap-server localhost:38281 --host-slot Alice --client-slot Bob
```

It reads an absolute `STS2GamePath` from `client/StS2AP/local.props`, then checks
common Linux Steam locations for the native `SlayTheSpire2` executable. For a
custom Steam library, use `--exe-path "/path/to/Slay the Spire 2/SlayTheSpire2"`.
No Proton or PowerShell is required on native Linux. `--dry-run` prints both
commands without launching or writing files. It also accepts the PowerShell-style
option names below, plus `-DryRun`.

On Windows use `scripts/test_multiplayer_local.ps1`. On WSL the same shell wrapper
continues to invoke Windows PowerShell and translates a WSL `-ExePath`:

```bash
./scripts/test_multiplayer_local.sh -SettingsOnly
./scripts/test_multiplayer_local.sh -ApServer localhost:38281 -HostSlot Alice -ClientSlot Bob
```

WSL forwards the existing PowerShell options; Linux-only `--dry-run` is not
available in the Windows launcher. Both launchers default to client IDs 1 and
1000. Change them with `--host-client-id` / `--client-client-id` on Linux or
`-HostClientId` / `-ClientClientId` on Windows/WSL; they must be distinct. AP slot
names can be the same for a shared-slot test. The launch delay defaults to two
seconds (`--launch-delay-seconds` or `-LaunchDelaySeconds`).

Run settings-only once to enable **Experimental Multiplayer** in Archipelago
Settings in both windows, then close them and rerun normally. Connect the host
slot first; once its native lobby opens, connect the client slot.

Logs are written to `logs/multiplayer/host_standard-<id>.log` and `join-<id>.log`.
Linux also captures each process's terminal output in a matching `.console.log`.
These paths are reused on the next launch, so save relevant logs before rerunning.
The Linux processes survive the launcher exiting; close their windows to stop them.
The launcher does not build or install mods, start an AP server, or verify in-game
synchronization. A dry run validates discovery/arguments, not multiplayer behavior.

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
