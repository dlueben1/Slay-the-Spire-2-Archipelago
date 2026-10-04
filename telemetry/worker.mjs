// RitsuLib PostHog batch proxy. The project token exists only in the Worker secret.
const POSTHOG_URL = 'https://eu.i.posthog.com/batch/';
const MAX_BYTES = 5 * 1024 * 1024;
const APPLICANT = 'Archipelago.Diagnostics';
const GAMEPLAY_APPLICANT = 'Archipelago.Gameplay';
const GAMEPLAY_EVENTS = new Set(['ap.settings_observed', 'ap.run_started', 'ap.ancient_selected']);
const EVENTS = new Map([
  ['session_start', 'basic_usage'],
  ['ap.compatibility', 'basic_usage'],
  ['ap.exception', 'diagnostics'],
]);
const COMMON = ['session_id', 'ritsulib_version', 'game_version', 'game_release_label',
  'platform', 'os_name', 'os_version', 'process_architecture', 'dotnet_runtime', 'game_language'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value, max = 256) => typeof value === 'string' && value.length > 0 && value.length <= max;
const symbol = (value, max = 256) => text(value, max) && /^[A-Za-z0-9._+<>`\[\](),:_-]+$/.test(value);
const id = value => typeof value === 'string' && /^[a-f0-9]{32}$/.test(value);
const number = value => Number.isSafeInteger(value) && value >= 0;
const reply = (status, error) => Response.json({ error }, { status });

function invalid() { throw new TypeError('invalid_event'); }
function symbols(values, maxCount, maxLength = 256) {
  if (!Array.isArray(values) || values.length > maxCount || values.some(v => !symbol(v, maxLength))) invalid();
  return values;
}

function compatibility(value) {
  if (!object(value) || typeof value.snapshot_ready !== 'boolean' || value.telemetry_version !== 1) invalid();
  const result = {};
  for (const key of ['ap_version', 'patch_application', 'game_phase', 'game_mode', 'build_configuration']) {
    if (!symbol(value[key])) invalid();
    result[key] = value[key];
  }
  result.snapshot_ready = value.snapshot_ready;
  result.telemetry_version = 1;
  if (value.patch_failure_type != null) {
    if (!symbol(value.patch_failure_type)) invalid();
    result.patch_failure_type = value.patch_failure_type;
  }
  if (value.snapshot_ready) {
    if (!number(value.loaded_mod_count) || !number(value.ap_patched_target_count) ||
        typeof value.truncated !== 'boolean' || !Array.isArray(value.mods) || value.mods.length > 64 ||
        !Array.isArray(value.patches) || value.patches.length > 256) invalid();
    result.loaded_mod_count = value.loaded_mod_count;
    result.ap_patched_target_count = value.ap_patched_target_count;
    result.truncated = value.truncated;
    result.mods = value.mods.map(mod => {
      if (!object(mod) || !symbol(mod.id) || !symbol(mod.version, 64)) invalid();
      return { id: mod.id, version: mod.version };
    });
    result.patches = value.patches.map(patch => {
      if (!object(patch) || !symbol(patch.target)) invalid();
      const safe = { target: patch.target, owners: symbols(patch.owners, 16, 128) };
      for (const kind of ['prefixes', 'postfixes', 'transpilers', 'finalizers']) {
        if (!number(patch[kind])) invalid();
        safe[kind] = patch[kind];
      }
      return safe;
    });
    result.mod_versions = result.mods.map(mod => `${mod.id}@${mod.version}`);
    result.overlapping_patch_owners = [...new Set(result.patches.flatMap(patch => patch.owners)
      .filter(owner => owner !== 'archipelago.patch'))];
  }
  return result;
}

function exception(value) {
  if (!object(value) || !symbol(value.source, 128) || !/^[0-9A-F]{64}$/.test(value.fingerprint) ||
      !Array.isArray(value.exception_list) || value.exception_list.length < 1 || value.exception_list.length > 4) invalid();
  const list = value.exception_list.map(entry => {
    if (!object(entry) || !symbol(entry.type) || !object(entry.stacktrace) ||
        !Array.isArray(entry.stacktrace.frames) || entry.stacktrace.frames.length > 32) invalid();
    return {
      type: entry.type,
      value: entry.type === 'GodotEngineError' ? 'Engine error message omitted' : 'Exception message omitted',
      stacktrace: { type: 'raw', frames: entry.stacktrace.frames.map(frame => {
        if (!object(frame) || !symbol(frame.function) || !symbol(frame.module)) invalid();
        return { function: frame.function, module: frame.module };
      }) },
    };
  });
  const result = {
    $exception_list: list,
    $exception_type: list[0].type,
    $exception_message: list[0].value,
    $exception_fingerprint: value.fingerprint,
    $exception_level: 'error',
    capture_source: value.source,
  };
  if (value.engine_error_type != null) {
    if (!number(value.engine_error_type)) invalid();
    result.engine_error_type = value.engine_error_type;
  }
  return result;
}

function gameplay(value, event) {
  if (!object(value) || ![1, 2].includes(value.telemetry_version) ||
      !id(value.event_id) || !object(value.yaml_options)) invalid();
  const result = { telemetry_version: value.telemetry_version, event_id: value.event_id };
  if (value.telemetry_version === 2) {
    if (!id(value.campaign_id)) invalid();
    result.campaign_id = value.campaign_id;
    if (event !== 'ap.settings_observed') {
      if (!id(value.run_id)) invalid();
      result.run_id = value.run_id;
    }
  }
  for (const key of ['ap_version', 'apworld_version', 'build_configuration']) {
    if (!symbol(value[key], 64)) invalid();
    result[key] = value[key];
  }
  for (const key of ['progressive_starter_card', 'progressive_starter_relic', 'include_floor_checks',
    'neow_sanity', 'campfire_sanity', 'gold_sanity', 'potion_sanity', 'shop_sanity',
    'shuffle_all_cards', 'seeded', 'death_link', 'release_on_victory']) {
    if (value.yaml_options[key] !== undefined) {
      if (typeof value.yaml_options[key] !== 'boolean') invalid();
      result[`yaml_${key}`] = value.yaml_options[key];
    }
  }
  const modes = { ancient_relic_location: ['start_of_act', 'anytime'],
    ancient_relic_pool: ['balanced', 'chaos', 'true_chaos'] };
  for (const [key, allowed] of Object.entries(modes)) {
    if (value.yaml_options[key] !== undefined) {
      if (!allowed.includes(value.yaml_options[key])) invalid();
      result[`yaml_${key}`] = value.yaml_options[key];
    }
  }
  if (event !== 'ap.settings_observed') {
    if (!symbol(value.character, 128) || !['singleplayer', 'multiplayer'].includes(value.game_mode) ||
        !modes.ancient_relic_location.includes(value.ancient_location) ||
        !modes.ancient_relic_pool.includes(value.ancient_pool)) invalid();
    for (const key of ['character', 'game_mode', 'ancient_location', 'ancient_pool']) result[key] = value[key];
  }
  if (event === 'ap.ancient_selected') {
    if (!symbol(value.selected_relic, 128)) invalid();
    result.offered_relics = symbols(value.offered_relics, 16, 128);
    if (!result.offered_relics.includes(value.selected_relic)) invalid();
    result.selected_relic = value.selected_relic;
    result.reward_source = 'ap_reward_menu';
  }
  return result;
}

async function eventUuid(event) {
  // Stable across RitsuLib retry, proxy chunking, and partial upstream success. No client secret.
  const identity = event.properties.telemetry_version === 2
    ? [event.event, event.distinct_id, event.properties.event_id]
    : [
    event.event, event.distinct_id, event.timestamp, event.properties.session_id,
    event.properties.$exception_fingerprint ?? null,
  ];
  if (event.properties.telemetry_version !== 2 && event.properties.event_id != null)
    identity.push(event.properties.event_id);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(identity))));
  digest[6] = (digest[6] & 15) | 64;
  digest[8] = (digest[8] & 63) | 128;
  const hex = [...digest.slice(0, 16)].map(byte => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

async function convert(event) {
  if (!object(event) || (!EVENTS.has(event.event) && !GAMEPLAY_EVENTS.has(event.event)) || !text(event.distinct_id, 64) ||
      !/^[a-f0-9-]{32,36}$/i.test(event.distinct_id) || !text(event.timestamp, 64) ||
      !Number.isFinite(Date.parse(event.timestamp)) || !object(event.properties)) invalid();
  const p = event.properties;
  const isGameplay = GAMEPLAY_EVENTS.has(event.event);
  const applicant = isGameplay ? GAMEPLAY_APPLICANT : APPLICANT;
  if (p.applicant_id !== applicant || p.owner_mod_id !== 'Archipelago' ||
      p.request_id !== (isGameplay ? 'gameplay' : EVENTS.get(event.event)) || p.schema !== 'ritsulib.telemetry.v1' ||
      !text(p.session_id, 64) || !/^[a-f0-9-]{32,36}$/i.test(p.session_id)) invalid();
  const category = isGameplay ? 'Custom' : p.request_id === 'diagnostics' ? 'Diagnostics' : 'BasicUsage';
  if (p.category !== category) invalid();
  const props = { applicant_id: applicant, request_id: p.request_id };
  for (const key of COMMON) {
    if (p[key] != null) {
      if (!text(p[key])) invalid();
      props[key] = p[key];
    }
  }
  if (isGameplay) {
    Object.assign(props, gameplay(p.payload?.applicant_payload, event.event));
  } else {
    const context = p.payload?.private_contributions?.Archipelago?.[`compatibility_${category}`];
    Object.assign(props, compatibility(context));
    if (event.event === 'ap.exception') Object.assign(props, exception(p.payload?.applicant_payload));
  }
  // Do not forward client IP headers, $ip, person updates, arbitrary payloads or geo properties.
  props.$geoip_disable = true;
  props.$process_person_profile = false;
  const converted = {
    event: event.event === 'ap.exception' ? '$exception' : event.event,
    distinct_id: event.distinct_id,
    timestamp: event.timestamp,
    properties: props,
  };
  converted.uuid = await eventUuid(converted);
  return converted;
}

async function readBody(request) {
  if (!request.body) throw new SyntaxError('empty_body');
  const reader = request.body.getReader();
  const parts = [];
  let bytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      bytes += value.byteLength;
      if (bytes > MAX_BYTES) {
        await reader.cancel();
        throw new RangeError('body_too_large');
      }
      parts.push(value);
    }
  } finally { reader.releaseLock(); }
  const body = new Uint8Array(bytes);
  let offset = 0;
  for (const part of parts) { body.set(part, offset); offset += part.byteLength; }
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(body));
}

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (path === '/health' && request.method === 'GET') {
      return Response.json({ service: 'sts2-ap-telemetry', version: 1, configured:
        Boolean(env.POSTHOG_API_KEY && env.TELEMETRY_RATE_LIMITER) });
    }
    if (path !== '/batch/') return reply(404, 'not_found');
    if (request.method !== 'POST') return new Response(null, { status: 405, headers: { Allow: 'POST' } });
    if (request.headers.get('content-type')?.split(';')[0].trim().toLowerCase() !== 'application/json')
      return reply(415, 'json_required');
    if (!env.POSTHOG_API_KEY || !env.TELEMETRY_RATE_LIMITER) return reply(503, 'not_configured');
    let stage = 'rate_limit';
    try {
      // IPs are used only for coarse abuse protection at Cloudflare, never forwarded or logged here.
      // ponytail: shared NATs share this per-location budget; adjust if legitimate users hit it.
      // 300 requests allows a queued 1,000-event RitsuLib flush in four-event chunks.
      const key = request.headers.get('CF-Connecting-IP');
      if (!key) return reply(400, 'missing_client_address');
      if (!(await env.TELEMETRY_RATE_LIMITER.limit({ key })).success)
        return new Response(null, { status: 429, headers: { 'Retry-After': '60' } });
      if (Number(request.headers.get('content-length')) > MAX_BYTES) return reply(413, 'body_too_large');
      stage = 'read_body';
      const body = await readBody(request);
      if (!object(body) || !Array.isArray(body.batch) || body.batch.length < 1 || body.batch.length > 4)
        return reply(400, 'invalid_batch');
      stage = 'convert';
      const batch = await Promise.all(body.batch.map(convert));
      stage = 'upstream_fetch';
      const upstream = await fetch(POSTHOG_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ api_key: env.POSTHOG_API_KEY, batch }),
        signal: AbortSignal.timeout(9000),
        // workerd rejects redirect: 'error'; manual returns 3xx, which we reject below.
        redirect: 'manual',
      });
      // Never acknowledge failed delivery or echo upstream responses (which can contain the token).
      stage = 'upstream_response';
      await upstream.body?.cancel();
      return upstream.ok ? Response.json({ ok: true, accepted: batch.length }) : reply(502, 'upstream_failed');
    } catch (error) {
      if (error instanceof RangeError) return reply(413, 'body_too_large');
      if (error instanceof SyntaxError || (error instanceof TypeError && error.message === 'invalid_event'))
        return reply(400, 'invalid_payload');
      // Log only the operation and error class, never messages, requests or upstream bodies.
      console.warn(JSON.stringify({ stage, error_type: error?.name ?? 'unknown' }));
      return reply(502, 'delivery_failed');
    }
  },
};
