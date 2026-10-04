# Reusable PostHog queries

Validated against STS2 Archipelago project 288289 on 2026-09-29.
These are working analysis definitions, not approved data-catalog metrics.

Copy one SQL block into PostHog's SQL editor/SQL insight and run it. Save the insight
to reuse it or add it to a dashboard. I can also rerun these exact queries through the MCP.
Each query uses a rolling 30-day window; change every `INTERVAL 30 DAY` in a query
to adjust it. These fixed SQL windows do not automatically follow a dashboard date selector.

All queries exclude `SmokeTest`. They deliberately include your real Debug gameplay tests.
Before interpreting release popularity, also filter out internal test installations or use
a release rollout date/build filter. As of this check there is only one real installation.

Gameplay queries use schema 2 and persistent IDs. Reports lost during the earlier
checkpoint-identity bug cannot be reconstructed, so current totals are observed reports.
Win/loss and campaign completion events do not exist yet; no valid rates can be calculated.

## Current sample (2026-09-29)

- One installation and one observed gameplay campaign.
- Four recorded new runs, all Silent; seven Ancient choices.
- Fishing Rod: picked 2/2 recorded offers. Small Capsule: 1/2.
- Nutritious Oyster, Pael's Legion, Prismatic Gem, and Sai: each 1/1.
- All recorded Ancient choices used Anytime + True Chaos.
- Both progressive starters and all five sanity options were enabled in the one settings report.
  Floor checks, shuffle-all-cards and release-on-victory were also enabled.
  DeathLink and seeded were disabled.
- Eight diagnostic sessions; three reports across two Godot error fingerprints, affecting two
  sessions. The ready inventory snapshots all reported successful AP patch application.
- Two mod combinations appear, with only 5 and 3 sessions respectively. They are development
  tests with changing code and one installation; their differences are not evidence of a conflict.

## Activity overview

Raw rows are useful for checking ingestion. Use the distinct identity counts for sessions/campaigns/runs; these are not unique people. Gameplay IDs exist only on schema 2 events.

```sql
SELECT event, count() AS rows, count(DISTINCT distinct_id) AS installations,
 count(DISTINCT properties.session_id) AS sessions,
 count(DISTINCT properties.campaign_id) AS campaigns,
 count(DISTINCT properties.run_id) AS runs
FROM events WHERE timestamp >= now() - INTERVAL 30 DAY
  AND properties.build_configuration != 'SmokeTest'
 AND properties.applicant_id IN ('Archipelago.Gameplay','Archipelago.Diagnostics')
GROUP BY event ORDER BY event LIMIT 20
```

## Character popularity

One vote per observed new run, with a separate distinct-installation count. A loaded save is not a new run. Installations can appear under several characters, so their counts need not sum to a unique player total.

```sql
SELECT character, count() AS runs, count(DISTINCT installation) AS installations,
 round(100.0 * count() / sum(count()) OVER (), 1) AS share_of_runs_pct
FROM (
 SELECT DISTINCT distinct_id AS installation, properties.campaign_id AS campaign,
  properties.run_id AS run, properties.character AS character
 FROM events WHERE timestamp >= now() - INTERVAL 30 DAY
  AND properties.build_configuration != 'SmokeTest'
  AND properties.applicant_id = 'Archipelago.Gameplay'
  AND properties.telemetry_version = 2
  AND event = 'ap.run_started' AND properties.run_id IS NOT NULL
)
GROUP BY character ORDER BY runs DESC LIMIT 100
```

## YAML option usage

One vote per campaign per option, using its latest known value observed in this window. Missing values are excluded from that option’s denominator. Settings are only sent for a new campaign/settings combination: this measures campaigns whose settings were observed in the window, not every campaign played during it. Campaign identity is local to an installation/profile and co-op participant; this is not unique YAML filenames or unique humans.

```sql
SELECT option, count() AS campaigns_with_known_value,
 countIf(enabled = true) AS enabled_campaigns,
 round(100.0 * countIf(enabled = true) / count(), 1) AS enabled_pct
FROM (
 SELECT installation, campaign, tupleElement(setting, 1) AS option,
  argMax(tupleElement(setting, 2), observed_at) AS enabled
 FROM (
  SELECT distinct_id AS installation, properties.campaign_id AS campaign, timestamp AS observed_at,
   arrayJoin([
    tuple('progressive_starter_card', properties.yaml_progressive_starter_card),
    tuple('progressive_starter_relic', properties.yaml_progressive_starter_relic),
    tuple('include_floor_checks', properties.yaml_include_floor_checks),
    tuple('neow_sanity', properties.yaml_neow_sanity),
    tuple('campfire_sanity', properties.yaml_campfire_sanity),
    tuple('gold_sanity', properties.yaml_gold_sanity),
    tuple('potion_sanity', properties.yaml_potion_sanity),
    tuple('shop_sanity', properties.yaml_shop_sanity),
    tuple('shuffle_all_cards', properties.yaml_shuffle_all_cards),
    tuple('seeded', properties.yaml_seeded),
    tuple('death_link', properties.yaml_death_link),
    tuple('release_on_victory', properties.yaml_release_on_victory)
   ]) AS setting
  FROM events WHERE timestamp >= now() - INTERVAL 30 DAY
  AND properties.build_configuration != 'SmokeTest'
  AND properties.applicant_id = 'Archipelago.Gameplay'
  AND properties.telemetry_version = 2
   AND event = 'ap.settings_observed' AND properties.campaign_id IS NOT NULL
 )
 WHERE tupleElement(setting, 2) IS NOT NULL
 GROUP BY installation, campaign, option
)
GROUP BY option ORDER BY enabled_pct DESC, option LIMIT 20
```

## Ancient choices when offered

One vote per first observed AP reward-menu choice. Denominator: recorded choice events whose offered list contains that relic—not every offer displayed, because skipped/unclaimed menus do not emit events. Results are separated by actual saved Ancient timing/pool. YAML defaults may differ. Restoring a save and choosing another relic does not replace the first choice. A 100% result from one offer is not strong evidence of preference.

```sql
SELECT ancient_location, ancient_pool, relic,
 count() AS times_offered, countIf(chosen = relic) AS times_picked,
 round(100.0 * countIf(chosen = relic) / count(), 1) AS pick_when_offered_pct
FROM (
 SELECT ancient_location, ancient_pool, chosen,
  arrayJoin(JSONExtract(ifNull(offered, '[]'), 'Array(String)')) AS relic
 FROM (
  SELECT distinct_id AS installation, properties.event_id AS choice,
   argMin(properties.ancient_location, timestamp) AS ancient_location,
   argMin(properties.ancient_pool, timestamp) AS ancient_pool,
   argMin(properties.selected_relic, timestamp) AS chosen,
   argMin(properties.offered_relics, timestamp) AS offered
  FROM events WHERE timestamp >= now() - INTERVAL 30 DAY
  AND properties.build_configuration != 'SmokeTest'
  AND properties.applicant_id = 'Archipelago.Gameplay'
  AND properties.telemetry_version = 2
   AND event = 'ap.ancient_selected' AND properties.event_id IS NOT NULL
  GROUP BY installation, choice
 )
)
GROUP BY ancient_location, ancient_pool, relic
ORDER BY times_picked DESC, times_offered DESC, relic LIMIT 100
```

## Common captured errors

Rank error fingerprints by affected consenting sessions, not raw occurrences. Reports are filtered/rate-limited; fingerprints can group several Godot messages because raw text is intentionally omitted. These are captured errors, not confirmed crashes.

```sql
SELECT properties.$exception_type AS error_type,
 properties.$exception_fingerprint AS fingerprint,
 properties.capture_source AS source,
 count(DISTINCT tuple(distinct_id, properties.session_id)) AS affected_sessions,
 count(DISTINCT distinct_id) AS affected_installations,
 min(timestamp) AS first_seen, max(timestamp) AS last_seen
FROM events
WHERE timestamp >= now() - INTERVAL 30 DAY
 AND event = '$exception'
 AND properties.applicant_id = 'Archipelago.Diagnostics'
 AND properties.build_configuration != 'SmokeTest'
GROUP BY error_type, fingerprint, source
ORDER BY affected_sessions DESC, last_seen DESC LIMIT 50
```

## Errors by mod and Harmony combination

Use the latest ready compatibility snapshot per installation/session and join errors from that same session. The denominator includes only sessions with a ready snapshot. Mod lists and patch owners are sorted so list order does not split the same combination. An owner overlapping AP targets is not proof of incompatibility. Compare substantial samples at the same AP/game version; truncated snapshots are explicitly marked.

```sql
WITH sessions AS (
 SELECT distinct_id AS installation, properties.session_id AS session,
  arraySort(JSONExtract(ifNull(argMax(properties.mod_versions, timestamp), '[]'), 'Array(String)')) AS mods,
  arraySort(JSONExtract(ifNull(argMax(properties.overlapping_patch_owners, timestamp), '[]'), 'Array(String)')) AS overlapping_owners,
  argMax(properties.ap_version, timestamp) AS ap_version,
  argMax(properties.game_version, timestamp) AS game_version,
  argMax(properties.patch_application, timestamp) AS patch_application,
  argMax(properties.truncated, timestamp) AS truncated
 FROM events
 WHERE timestamp >= now() - INTERVAL 30 DAY
  AND event IN ('ap.compatibility', 'session_start')
  AND properties.applicant_id = 'Archipelago.Diagnostics'
  AND properties.build_configuration != 'SmokeTest'
  AND properties.snapshot_ready = true
 GROUP BY installation, session
), errors AS (
 SELECT DISTINCT distinct_id AS installation, properties.session_id AS session, 1 AS has_error
 FROM events
 WHERE timestamp >= now() - INTERVAL 30 DAY AND event = '$exception'
  AND properties.applicant_id = 'Archipelago.Diagnostics'
  AND properties.build_configuration != 'SmokeTest'
)
SELECT s.ap_version, s.game_version, s.mods, s.overlapping_owners,
 s.patch_application, s.truncated, count() AS observed_sessions,
 sum(ifNull(e.has_error, 0)) AS sessions_with_errors,
 round(100.0 * sum(ifNull(e.has_error, 0)) / count(), 1) AS sessions_with_errors_pct
FROM sessions s LEFT JOIN errors e ON s.installation = e.installation AND s.session = e.session
GROUP BY s.ap_version, s.game_version, s.mods, s.overlapping_owners, s.patch_application, s.truncated
ORDER BY observed_sessions DESC LIMIT 100
```

## Useful variations

- Character choice by starter options: include the relevant `yaml_*` properties in the
  deduplicated run subquery and group by them alongside character.
- Ancient preferences by character: add `argMin(properties.character, timestamp) AS character`
  to the deduplicated choice subquery, select it in the array expansion, and group by it.
- Run counts over time: keep each run's earliest observed start timestamp and group by day/week.
- Compatibility investigation: inspect a specific fingerprint and AP/game version, then compare
  affected-session counts for a particular owner/mod against sessions without it. Keep sample
  sizes visible; never infer causation from the raw combination percentages.
- Win rates and cross-run campaign completion must wait for explicit outcome events.
  Absence of an event does not establish loss, abandonment or a crash.

References: [SQL insights](https://posthog.com/docs/product-analytics/sql),
[SQL editor](https://posthog.com/docs/data-warehouse/sql),
[Saving insights on dashboards](https://posthog.com/docs/product-analytics/dashboards).
