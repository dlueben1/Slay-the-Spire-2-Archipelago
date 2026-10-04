# Archipelago telemetry experiment

Validated reusable analyses are in [queries.md](queries.md).

## Dashboards

- [STS2 AP — Gameplay](https://eu.posthog.com/project/288289/dashboard/984156):
  character popularity, YAML options enabled, and Ancient picks divided by offers.
- [STS2 AP — Diagnostics & Harmony](https://eu.posthog.com/project/288289/dashboard/984157):
  telemetry coverage, errors by affected session, and mod/Harmony combinations.

Both default to the last 30 days and respect the dashboard date selector. These are
opt-in observations, not population-wide adoption figures. Development data currently
comes from one installation. The saved SQL uses `{filters}`; the standalone examples
in `queries.md` use a fixed relative window. Update these existing dashboards rather
than creating another pair on each analysis pass.

## Offline Harmony review

`harmony_analysis.py` uses Python's standard library. It deduplicates event UUIDs,
selects the latest ready snapshot per installation/session, groups equivalent mod
and patch configurations, and counts each error fingerprint once per session.
Ordering differences in mod/owner/target arrays do not create new combinations.

The output is a short `report.md` for an agent to read first and a complete
`combinations.json` for drilling into captured targets, session IDs and error stacks.
Common overlaps are summarized once; AP-only targets stay in the JSON. The report
prioritizes failed patch applications, new combinations and sessions with errors.
Pass `--baseline previous/diagnostics.json` to identify changes since a prior export.
This baseline is not assumed to be a known-good configuration.

Captured snapshots cover **AP-patched targets**, not every patch in the process.
Patch-kind counts belong to AP; other owners' ordering, priority and patch bodies
are not transmitted. Symbols are sanitized and length-bounded. Session-level error
association does not establish which patch caused an error or whether the game crashed.
The dashboard's coarse owner/mod combinations can combine configurations that the
offline analyzer separates because their per-target patch counts differ.

### Repeatable export and analysis

Use PostHog's [native file exports](https://posthog.com/docs/cdp/file-download-exports)
for downloads. Its [query API](https://posthog.com/docs/api/queries) is intended for
small interactive results, not bulk or recurring exports. Keep exports under ignored
`.local-tools/telemetry/`, never in Git. Commands below run from the repository root.

One-time setup: save a personal API key with **Batch export: Write** access as plain
text in `.local-tools/telemetry/posthog-api-key`. Restrict it to project 288289 and keep
the file owner-readable only (`chmod 600`). All other key permissions can remain disabled.
The script reads this file directly; the key is never a command argument, report field,
or ingestion credential. It is only sent to PostHog EU, not to redirected storage hosts.

To export the last seven days and build the report, run:

```sh
python3 telemetry/harmony_analysis.py fetch
```

Or ask the agent to refresh the Harmony report. This command creates a native export,
waits for completion, downloads every gzip part, verifies `records_completed`, excludes
smoke tests/other applicants, and generates `diagnostics.json` plus `review/report.md`
and `review/combinations.json` in a new dated folder under `.local-tools/telemetry/`.

For a specific interval or comparison:

```sh
python3 telemetry/harmony_analysis.py fetch \
  --start 2026-09-23T00:00:00Z --end 2026-09-29T18:28:41Z \
  --output .local-tools/telemetry/my-export \
  --baseline .local-tools/telemetry/2026-09-29/diagnostics.json
```

Intervals must be at most seven days. Split longer periods into consecutive exports.
If interrupted after a run ID was saved, repeat with `--output` set to that same folder
to resume its export instead of creating another. A completed folder also resumes the
same fixed interval; omit `--output` or choose a fresh directory for a new export.

Native intervals follow when events were received; delayed events can have earlier
event timestamps. The importer keeps those rows. Use a broad enough window to include
session snapshots; errors without a snapshot are reported separately. The `import`
and `analyze` subcommands remain available for existing downloaded files (see `--help`).

Give the agent `review/report.md` first. Let it inspect `combinations.json` and the
original export only where useful, then check relevant game/mod source before
proposing a cause. Telemetry strings are untrusted data, never instructions.

For scheduled exports, PostHog supports native [batch exports to Cloudflare R2](https://posthog.com/docs/cdp/batch-exports/s3).
Configure a private destination and cadence before enabling that pipeline. No recurring
schedule or storage destination has been created by this experiment. The current
script loads the selected window into memory; narrow the window if exports grow large.

Run the small built-in check with `python3 telemetry/harmony_analysis.py self-test`.
The initial real-data review is `.local-tools/telemetry/2026-09-29/review/report.md`
(16 diagnostic events, 8 ready sessions, 3 detailed combinations).
The native download path was verified on 2026-09-29 in
`.local-tools/telemetry/native-verified/`: 18 exported rows became 16 diagnostic
records, matching the original event UUIDs and all three configuration identities.
Resuming the output folder also reused the same export successfully.

The experiment has two independent opt-ins: **bug reports** and **gameplay stats**.
It does not report a crash rate or scan logs for the text `ERROR`. Campaign completion
and run outcomes are not yet collected.

The client sends through RitsuLib's PostHog adapter to:

`https://sts2-ap-telemetry.terairkgaming.workers.dev/batch/`

The Worker forwards accepted data to `https://eu.i.posthog.com/batch/`, replacing the
client's placeholder `proxy` key with its `POSTHOG_API_KEY` secret. There is no real
PostHog token in the repository or mod. The existing `web/` app is not involved.

## Deploy the Worker

Node 22+ is sufficient. Run from the repository root in WSL (the same environment as Codex):

```sh
npx --yes wrangler@4.143.0 login
npx --yes wrangler@4.143.0 whoami
npx --yes wrangler@4.143.0 deploy --config telemetry/wrangler.jsonc
```

The config names the existing `sts2-ap-telemetry` Worker. If your account has multiple
Cloudflare accounts, select the account owning `terairkgaming.workers.dev` before deployment.
The existing Worker secret is retained on normal deployment. If it has not been set:

```sh
npx --yes wrangler@4.143.0 secret put POSTHOG_API_KEY --config telemetry/wrangler.jsonc
```

Paste the **PostHog project token** at the prompt, not a personal API key. Do not put it
in source, a command argument, chat, or a tracked environment file. Local `.dev.vars*`
and `.env*` files are ignored in this directory.

Deploy with Wrangler, not just by pasting `worker.mjs` into the dashboard: the config also
creates the required native rate-limit binding. It fails closed if that binding is missing.
`29092601` is this Worker's rate-limit namespace; keep it unique within your account.
Cloudflare Access must be **off** for the public ingestion route; the mod cannot log in.

Check the deployed version without sending telemetry:

```sh
curl --fail https://sts2-ap-telemetry.terairkgaming.workers.dev/health
```

Expected: `{"service":"sts2-ap-telemetry","version":1,"configured":true}`.
This checks bindings, not whether the PostHog token belongs to the EU project or whether
PostHog accepted any event. Deploy this proxy **before** installing the experiment client:
the default Hello World Worker can return HTTP 200 without actually ingesting anything.

Keep PostHog Analytics and Error Tracking spending caps at the intended limit (e.g. $0
for the experiment). Cloudflare's rate limiter is an approximate per-location burst
control, not a hard monthly spending cap. Both providers' quotas can stop ingestion.

## Data and consent

A separate `Archipelago.Diagnostics` applicant appears as "Archipelago bug reports (experimental)"
in RitsuLib's telemetry prompt/settings. The destination label is "PostHog EU via Cloudflare";
the transport still uses the Worker URL above. RitsuLib supplies the surrounding dialog text.
Unknown, rejected or revoked consent must not upload events. Revoking clears RitsuLib's queue;
it cannot recall requests already in flight or data already received by PostHog.

The consent text covers random installation/session IDs, game/AP/RitsuLib versions,
OS/runtime/language, loaded mod identifiers and versions, and AP Harmony patch status/owners.
It also covers errors from other mods, which are needed to compare compatibility. RitsuLib's
own telemetry is a different applicant with its own consent.

Mod/Harmony inventory is sampled locally on the first main menu, after initialization.
`session_start` is replayed by RitsuLib after consent; it can precede the finished inventory
for previously consenting installations. `ap.compatibility` adds the finished inventory.
Use the latest `snapshot_ready=true` event per session for mod-combination denominators.
A first-time grant gets the current inventory in its replayed `session_start`.
Snapshots are bounded to 64 loaded mods, 256 AP targets and 16 owners per target; `truncated`
marks clipping. Inventory order is not claimed to be Harmony execution order.

The diagnostics filter intercepts RitsuLib's .NET/Sentry/Godot capture sources after consent,
builds a separate `ap.exception` event, and rejects the original raw event **before queueing**.
Only exception types, method/module symbols and coarse source/phase are retained. No raw
exception messages, filesystem paths, Godot message/code/backtrace text, AP addresses,
passwords, seed/slot data, player names, account IDs, full logs or run-history objects are
included in our reports. Inner exceptions are bounded to four and each trace to 32 frames.
An AP patch-application failure is also reported explicitly if already authorized; otherwise
its failure type/status is available in the later compatibility snapshot.

Game method/manifest identifiers are retained as symbols; underscores replace characters
outside a bounded ASCII identifier alphabet. This intentionally reduces fidelity for
non-ASCII mod identifiers. Frame filenames and line numbers are omitted. Use the existing
manual bug-report export when full context is needed.

The Worker projects only approved properties, disables PostHog person profiles and GeoIP,
and converts `ap.exception` to `$exception` with structured frames and an explicit fingerprint.
It does not forward player IP headers or `$ip`, and contains no request-body logging.
Unexpected delivery failures log only an operation stage and error class, never error messages.
Cloudflare still processes source IPs to serve requests and enforce its rate limit; this
is not a promise that infrastructure providers never process connection metadata.

## Reliability and interpretation

- Count **distinct affected sessions / distinct observed consenting sessions**. Do not
  divide raw exception occurrences by installation counts. Compare the same game/AP versions.
- Mod overlap is a lead to investigate, not proof of a conflict. Successful `PatchAll` plus
  installed target counts does not establish correct runtime behaviour of every patch.
- Handled exceptions, engine errors and unhandled exceptions are not interchangeable with
  crashes. No inferred unclean-exit/crash event is implemented; native crashes can evade capture.
- Repeated exception fingerprints are suppressed per process; max 20 unique reports/minute
  and 200/process. RitsuLib additionally suppresses some sources. These are affected-session
  signals, not exact error occurrence counts. Godot grouping is intentionally coarse.
- RitsuLib keeps consent-aware disk queues (2,000 events/applicant). Our adapter delegates
  transport to its PostHog adapter in four-event chunks, checking permission between chunks.
  A failure keeps the batch queued; later RitsuLib flush triggers retry it. There is no new timer.
- The Worker enforces a real streamed 5 MiB body limit and max four events/request. Its
  300 requests/minute/IP/location limit permits a 1,000-event queued flush (250 requests),
  although shared NATs or a distributed attack can affect that budget.
- Retry UUIDs are deterministic across partial batch failures. PostHog can deduplicate by UUID;
  queries should still use distinct session IDs. There is no claim of transactional exactly-once delivery.
- Non-2xx upstream responses/timeouts return failure; the Worker does not acknowledge them or
  echo upstream response bodies. A 2xx is ingestion acceptance, not proof of final visibility or
  survival of PostHog spending limits/retention. There is no additional Worker-side durable queue.

## Gameplay opt-in

`Archipelago.Gameplay` appears separately as "Archipelago gameplay stats (experimental)".
Allowing diagnostics never enables it. Its single `gameplay` request uses RitsuLib's Custom
category, with no automatic run-history, mod-inventory or exception subscriptions.
The common random installation/session IDs, system/language details and game/RitsuLib versions
still accompany these events. AP and APWorld versions are explicit event properties.

| Event | When | Data |
| --- | --- | --- |
| `ap.settings_observed` | First consenting AP connection per campaign/options combination | Allowlisted resolved YAML options |
| `ap.run_started` | Successful new singleplayer or multiplayer launch | Local character model ID, mode, YAML options and effective Ancient settings |
| `ap.ancient_selected` | Successful local AP-menu Ancient selection and AP progress commit | Chosen relic model ID, offered alternatives, character, mode and both YAML/effective Ancient settings |

Capture checks gameplay consent before reading settings. Guests and remote players are not
reported. There is no retrospective collection: after granting, reconnect to observe options
or start a new run to record a character choice. Loading a save is not a new run. Ancient
selections from loaded runs can still be recorded if permission is already granted.

The allowlist includes progressive starter cards/relics; floor, Neow, campfire, gold, potion
and shop sanity; shuffle-all-cards, seeded, DeathLink, release-on-victory; and Ancient timing/pool.
Only the boolean seeded option is sent, never its seed value. Missing options remain unknown.
These are generated settings, not YAML filenames, weights, full slot data, character seeds,
custom bonus-item definitions, server addresses, slot names or network/player identifiers.

PostHog gets flat `yaml_*` properties for configured options. `ancient_location` and
`ancient_pool` describe the actual saved run policy, including local overrides; the YAML
properties remain the original slot defaults. Ancient selections are scoped explicitly to
`reward_source=ap_reward_menu`, not vanilla Ancient-event choices.

For YAML option popularity, count distinct `campaign_id` values with `telemetry_version=2`,
using the latest settings observation per campaign, and exclude `build_configuration=SmokeTest`.
Missing options are unknown. For "portion of players", use distinct installations instead;
installations approximate devices/accounts, not unique humans, and opt-in introduces selection bias.
For character popularity count distinct `run_id` values on `ap.run_started` (or installations
for "people who chose this character"). For relic preference count distinct `event_id` values,
compare chosen relics against `offered_relics`, and group by effective timing/pool.
These events describe observed choices, not all possible offers.

### Persistent identity and deduplication

Gameplay schema 2 adds random `campaign_id` and `run_id`. A campaign means one AP
room/team/slot/co-op player on this local game profile; switching back to it restores its ID.
The local ledger maps the mod's existing saved run UUID to an unrelated random telemetry run
ID. A fresh attempt gets a new ID even if it uses the same game seed. Loading any checkpoint
from that attempt keeps the same ID. Custom singleplayer checkpoints explicitly store `run_id`
in the AP envelope and restore it into the shared run data before saved-run setup, because
serializing native run JSON bypasses RitsuLib's normal save writer. Older experimental
checkpoints without this field still load but skip gameplay telemetry; start a fresh run
with this fix to test reloads. No migration or fallback identity is generated.
No seed, slot, co-op player number, receipt index or
native run UUID is transmitted.

The ledger lives outside saves at `user://ArchipelagoTelemetry/profile-<id>/<local-hash>.json`.
The filename hashes the AP identity for local lookup only; that hash is never sent. Entries
are created only after gameplay consent. Atomic replacement and an in-process lock protect
updates. A damaged ledger suppresses capture and logs the error type; it is not silently reset.

- Settings are recorded once per campaign and distinct allowlisted settings combination.
- Run starts are recorded once per campaign/run, only from new-run launch.
- Ancient choices are recorded once per campaign/run/AP receipt. Restoring an earlier save
  and choosing another relic does not create a second report: the first observed choice wins.
  The same receipt in a genuinely new attempt can be reported again.
- Gameplay event IDs derive from these identities. Schema 2 PostHog UUIDs ignore the process
  session and timestamp, so the logical event retains its UUID across restart/retry.
  The Worker still accepts queued schema 1 events with their original UUID calculation;
  those historical observations cannot be retroactively deduplicated by campaign/run.

The ledger records an event before calling RitsuLib's public `void` capture API. A crash or
queue failure in that gap can lose an observation. This deliberately favors avoiding inflated
counts, and is not transactional exactly-once delivery. RitsuLib remains responsible for
queued delivery and revocation. The ledger survives revocation/re-grant; it contains IDs and
hashes, not a telemetry backlog, and never replays events collected without consent.
Deleting this separate ledger or moving to another device/profile creates new telemetry
identities. Simultaneous game processes sharing the same profile are not supported by the
in-process lock. Campaign completion tracking remains unimplemented.

Gameplay runtime check: allow only gameplay, connect to AP, start a new run, and claim an
Ancient from the AP reward menu. Verify the three event types and compare `yaml_ancient_*`
with the actual run mode/pool. Repeat with gameplay declined to check that it sends nothing. Restart/reconnect and confirm
no second settings event; reload a pre-choice checkpoint and choose again to confirm no second
Ancient event. Start a fresh attempt and confirm a different `run_id` with the same `campaign_id`.

## Local verification

```sh
node --test telemetry/worker.test.mjs
npx --yes wrangler@4.143.0 deploy --dry-run --config telemetry/wrangler.jsonc

dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj --filter FullyQualifiedName~TelemetryTests

dotnet build client/StS2AP/StS2AP.csproj -p:BuildMode=CompileOnly -p:UseSts2RefLib=true -p:Sts2ApiCompat=0.107.1
dotnet build client/StS2AP/StS2AP.csproj -p:BuildMode=CompileOnly -p:UseSts2RefLib=true -p:Sts2ApiCompat=0.111.0
```

Tests cover privacy projection, structured exceptions, stable retry UUIDs, malformed/foreign
batches, actual body size without Content-Length, missing configuration, rate rejection,
and upstream failure. No real telemetry is sent by these tests.

## Before releasing

On both supported game builds, verify the actual UI and delivery with the deployed Worker:

1. Fresh/dismissed/rejected consent sends nothing for `Archipelago.Diagnostics`.
2. Allow diagnostics and confirm `session_start`/`ap.compatibility` appear in PostHog EU;
   inspect versions, mod list, patch owners and random IDs. Count the session once.
3. Generate a controlled caught exception in a development build and verify its `$exception`
   issue, symbol frames, fingerprint and context in Error Tracking. Repeat it and check suppression.
   Verify a Godot engine error separately. No test exception should interrupt gameplay.
4. Disconnect networking, generate a report, restart/reconnect and verify queued retry/deduplication.
5. Revoke permission with queued events and verify they are cleared and later errors are not uploaded.
6. Verify another co-op participant's consent is independent. No peer identities should appear.

Local compile/tests do not establish runtime consent UI, native PostHog issue grouping, or delivery.

References:
- [RitsuLib/PostHog/Cloudflare tutorial](https://tutorials.sts2modding.com/en/docs/04-ritsulib/04-29-telemetry/#setting-up-a-simple-telemetry-service-with-posthog-cloudflare)
- [Cloudflare secrets](https://developers.cloudflare.com/workers/configuration/secrets/)
- [Cloudflare rate limiting](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/)
- [PostHog exception events](https://posthog.com/docs/error-tracking/capture)

The architecture follows the tutorial. The Worker is independently implemented with an explicit
schema, actual body limits, native rate limiting, privacy projection and failure handling; the
tutorial's IP/GeoIP forwarding is intentionally omitted.

## Deployment verification — 2026-09-29

Deployed to the configured Worker, version `733d22d8-07de-4bd1-8d60-bd414e670099`.
The live `/health` endpoint returned HTTP 200 with `configured: true`; an empty batch
returned HTTP 400. A synthetic session and exception batch returned HTTP 200 with
`accepted: 2`, confirming the Worker received a successful response from PostHog EU.

The smoke session is `219f32a9792f4407a38b01cf57e65d4b`, timestamp
`2026-09-29T15:47:21.760817+00:00`, build configuration `SmokeTest`; the exception type
is `Archipelago.TelemetrySmokeTest`. Exclude `build_configuration = SmokeTest` from
real compatibility statistics. These are fabricated test records, not player data.
The project owner confirmed both synthetic records are visible in the PostHog dashboard, including the Error Tracking test issue.

After the owner's in-game reject/allow check, MCP verification found a real Debug
`session_start` at `2026-09-29T16:15:59.345Z`, session
`a9737296b47941bd8a1898b49614e88d`, with `snapshot_ready=true`,
`patch_application=succeeded` and 150 AP patch targets. This confirms delivery after
allowing; rejection has not been independently verified against local queue/log evidence.
Offline retry/revocation checks are deferred at the owner's request.

Live testing caught a workerd incompatibility with `redirect: error`; requests now use
`manual`, reject 3xx responses and never forward the token to redirect destinations.
This was exercised in Wrangler's local runtime and covered by the redirect regression test.
The proxy unit suite now contains eight passing tests.

Subsequent diagnostics verification via PostHog MCP found both `session_start` and
`ap.compatibility` at `2026-09-29T16:40:28Z`, with the compatibility event reporting a ready
snapshot, successful patch application and 150 AP targets. Gameplay adds two proxy checks
(ten total) and one C# option-privacy check (four telemetry checks total); its new in-game
events still require the gameplay runtime check above.

Gameplay proxy version `f7a4232a-2596-452d-8906-13545b9af95e` was deployed and accepted
three synthetic events at `2026-09-29T16:52:11.255262Z`, session
`5741589381f7499fb315d005609f612a`. PostHog MCP confirmed all three are stored, marked
`build_configuration=SmokeTest`, with YAML pool `balanced` kept separate from actual pool
`true_chaos`. Both locally installed game variants contain the gameplay registration.

Persistent gameplay identity update: both supported client variants compiled and were installed
with verified manifest hashes. Five C# telemetry checks and eleven proxy checks pass. The live
proxy accepted three schema 2 SmokeTest events for campaign `fae6c50558244bbdb73c9b11d8b24ccd`;
PostHog MCP confirmed all three, sharing the campaign ID and (for run/choice) the run ID.
This verifies transport/schema, not the in-game restart/checkpoint runtime test.
