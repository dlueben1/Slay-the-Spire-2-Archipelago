import { test } from 'node:test';
import assert from 'node:assert/strict';
import worker from './worker.mjs';

const secret = 'test-project-token-never-echo';
function fixture(name = 'session_start') {
  const category = name === 'ap.exception' ? 'Diagnostics' : 'BasicUsage';
  return {
    event: name, distinct_id: 'a'.repeat(32), timestamp: '2026-09-29T12:00:00.1234567+00:00',
    properties: {
      schema: 'ritsulib.telemetry.v1', applicant_id: 'Archipelago.Diagnostics', owner_mod_id: 'Archipelago',
      request_id: category === 'Diagnostics' ? 'diagnostics' : 'basic_usage', category,
      session_id: 'b'.repeat(32), game_version: '0.107.1', ritsulib_version: '0.6.0',
      payload: {
        private_contributions: { Archipelago: { [`compatibility_${category}`]: {
          snapshot_ready: true, telemetry_version: 1, ap_version: '2.4.0.0', patch_application: 'succeeded',
          game_phase: 'menu', game_mode: 'none', build_configuration: 'Debug',
          loaded_mod_count: 1, ap_patched_target_count: 1, truncated: false,
          mods: [{ id: 'Archipelago', version: '2.4.0' }],
          patches: [{ target: 'Game.Target()', owners: ['archipelago.patch', 'OtherMod'],
            prefixes: 1, postfixes: 0, transpilers: 0, finalizers: 0 }],
        } } },
        applicant_payload: {
          source: 'ap_patch_application', fingerprint: 'A'.repeat(64),
          exception_list: [{ type: 'System.InvalidOperationException', value: 'DO NOT FORWARD',
            stacktrace: { frames: [{ function: 'Mod.Target', module: 'Archipelago', filename: '/private/path' }] } }],
        },
      },
    },
  };
}
const environment = () => ({ POSTHOG_API_KEY: secret, TELEMETRY_RATE_LIMITER: { limit: async () => ({ success: true }) } });
function request(body, headers = {}) {
  return new Request('https://proxy.invalid/batch/', { method: 'POST',
    headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '192.0.2.1', ...headers },
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
}

test('RitsuLib batch forwards to EU with a server token and a stable UUID', async t => {
  const sent = [];
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    sent.push({ url, ...options, body: JSON.parse(options.body) });
    return new Response('{}', { status: 200 });
  });
  for (let i = 0; i < 2; i++) {
    const response = await worker.fetch(request({ api_key: 'proxy', batch: [fixture()] }), environment());
    assert.equal(response.status, 200);
    assert.equal((await response.json()).accepted, 1);
  }
  assert.equal(sent[0].url, 'https://eu.i.posthog.com/batch/');
  assert.equal(sent[0].body.api_key, secret);
  assert.equal(sent[0].body.batch[0].uuid, sent[1].body.batch[0].uuid);
  assert.match(sent[0].body.batch[0].uuid, /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/);
  assert.deepEqual(sent[0].body.batch[0].properties.mod_versions, ['Archipelago@2.4.0']);
  assert.deepEqual(sent[0].body.batch[0].properties.overlapping_patch_owners, ['OtherMod']);
});

test('native exception projection strips raw messages, paths, IPs and person operations', async t => {
  let forwarded;
  t.mock.method(globalThis, 'fetch', async (_url, options) => {
    forwarded = options;
    return new Response('{}');
  });
  const event = fixture('ap.exception');
  Object.assign(event.properties, { $ip: '192.0.2.1', $set: { email: 'SECRET' },
    $geoip_disable: false, $process_person_profile: true });
  const response = await worker.fetch(request({ batch: [event], api_key: 'ATTACKER' }), environment());
  assert.equal(response.status, 200);
  const parsed = JSON.parse(forwarded.body).batch[0];
  assert.equal(parsed.event, '$exception');
  assert.equal(parsed.properties.$exception_list[0].stacktrace.frames[0].function, 'Mod.Target');
  assert.equal(parsed.properties.$geoip_disable, true);
  assert.equal(parsed.properties.$process_person_profile, false);
  for (const forbidden of ['DO NOT FORWARD', '/private/path', '192.0.2.1', 'SECRET', 'ATTACKER'])
    assert.ok(!forwarded.body.includes(forbidden));
  assert.deepEqual(forwarded.headers, { 'Content-Type': 'application/json' });
});

test('malformed, foreign and over-limit batches never reach PostHog', async t => {
  t.mock.method(globalThis, 'fetch', () => assert.fail('unexpected upstream call'));
  const cases = [null, {}, { batch: [] }, { batch: [null] }, { batch: Array(5).fill(fixture()) }];
  for (const mutate of [e => e.event = '$identify', e => e.properties.applicant_id = 'OtherMod',
    e => e.distinct_id = 'player@example.com', e => e.properties.session_id = 'bad',
    e => e.properties.request_id = 'diagnostics', e => e.timestamp = 'invalid',
    e => e.properties.payload.private_contributions.Archipelago.compatibility_BasicUsage.patches[0].owners = null]) {
    const event = fixture(); mutate(event); cases.push({ batch: [event] });
  }
  for (const body of cases) assert.equal((await worker.fetch(request(body), environment())).status, 400);
  assert.equal((await worker.fetch(request('{broken'), environment())).status, 400);
  assert.equal((await worker.fetch(request('{}', { 'Content-Type': 'text/plain' }), environment())).status, 415);
});

test('real body limit applies without Content-Length', async t => {
  t.mock.method(globalThis, 'fetch', () => assert.fail('unexpected upstream call'));
  assert.equal((await worker.fetch(request(' '.repeat(5 * 1024 * 1024 + 1)), environment())).status, 413);
});

test('rate limiting and missing bindings fail closed', async t => {
  t.mock.method(globalThis, 'fetch', () => assert.fail('unexpected upstream call'));
  const env = environment();
  env.TELEMETRY_RATE_LIMITER.limit = async ({ key }) => {
    assert.equal(key, '192.0.2.1'); return { success: false };
  };
  const response = await worker.fetch(request({ batch: [fixture()] }), env);
  assert.equal(response.status, 429);
  assert.equal(response.headers.get('Retry-After'), '60');
  assert.equal((await worker.fetch(request({}), { POSTHOG_API_KEY: secret })).status, 503);
});

test('upstream failures and network errors stay retryable and do not leak responses', async t => {
  const fetch = t.mock.method(globalThis, 'fetch', async () => new Response(secret, { status: 401 }));
  const failed = await worker.fetch(request({ batch: [fixture()] }), environment());
  assert.equal(failed.status, 502);
  assert.ok(!(await failed.text()).includes(secret));
  fetch.mock.mockImplementation(async () => { throw new TypeError('network ' + secret); });
  const unavailable = await worker.fetch(request({ batch: [fixture()] }), environment());
  assert.equal(unavailable.status, 502);
  assert.ok(!(await unavailable.text()).includes(secret));
});

test('health is read-only and other routes/methods are rejected', async () => {
  const response = await worker.fetch(new Request('https://proxy.invalid/health'), environment());
  assert.deepEqual(await response.json(), { service: 'sts2-ap-telemetry', version: 1, configured: true });
  assert.equal((await worker.fetch(new Request('https://proxy.invalid/batch/'), environment())).status, 405);
  assert.equal((await worker.fetch(new Request('https://proxy.invalid/'), environment())).status, 404);
});

// Node accepts redirect: error, but Cloudflare's workerd runtime does not.
test('upstream redirects are rejected without following or exposing the project token', async t => {
  let calls = 0;
  t.mock.method(globalThis, 'fetch', async (_url, options) => {
    calls++;
    assert.equal(options.redirect, 'manual');
    return new Response(null, { status: 307, headers: { Location: 'https://other.invalid/' } });
  });
  const response = await worker.fetch(request({ batch: [fixture()] }), environment());
  assert.equal(response.status, 502);
  assert.equal(calls, 1);
  assert.ok(!(await response.text()).includes(secret));
});

function gameplayFixture(name = 'ap.ancient_selected') {
  const event = fixture();
  event.event = name;
  Object.assign(event.properties, {
    applicant_id: 'Archipelago.Gameplay', request_id: 'gameplay', category: 'Custom',
    payload: { applicant_payload: {
      telemetry_version: 1, event_id: 'c'.repeat(32), ap_version: '2.4.0.0',
      apworld_version: '2.4.0', build_configuration: 'Debug', character: 'IRONCLAD',
      game_mode: 'singleplayer', ancient_location: 'anytime', ancient_pool: 'true_chaos',
      selected_relic: 'RELIC_A', offered_relics: ['RELIC_A', 'RELIC_B', 'RELIC_C'],
      yaml_options: { progressive_starter_card: true, progressive_starter_relic: false,
        shop_sanity: true, ancient_relic_location: 'start_of_act', ancient_relic_pool: 'balanced',
        seed: 'SECRET', player_name: 'SECRET', characters: [{ seed: 'SECRET' }] },
      raw_slot_data: 'SECRET', peer_net_id: 'SECRET',
    } },
  });
  return event;
}

test('gameplay projects safe options and separates YAML defaults from actual Ancient choices', async t => {
  const sent = [];
  t.mock.method(globalThis, 'fetch', async (_url, options) => {
    sent.push(JSON.parse(options.body).batch[0]); return new Response('{}');
  });
  for (const name of ['ap.settings_observed', 'ap.run_started', 'ap.ancient_selected']) {
    assert.equal((await worker.fetch(request({ batch: [gameplayFixture(name)] }), environment())).status, 200);
  }
  assert.equal(sent[0].properties.character, undefined);
  assert.equal(sent[1].properties.selected_relic, undefined);
  const props = sent[2].properties;
  assert.equal(props.yaml_progressive_starter_card, true);
  assert.equal(props.yaml_progressive_starter_relic, false);
  assert.equal(props.yaml_gold_sanity, undefined);
  assert.equal(props.yaml_ancient_relic_pool, 'balanced');
  assert.equal(props.ancient_pool, 'true_chaos');
  assert.equal(props.ancient_location, 'anytime');
  assert.deepEqual(props.offered_relics, ['RELIC_A', 'RELIC_B', 'RELIC_C']);
  assert.equal(props.reward_source, 'ap_reward_menu');
  assert.equal(props.$process_person_profile, false);
  assert.equal(props.$geoip_disable, true);
  assert.ok(!JSON.stringify(sent).includes('SECRET'));
  const retry = gameplayFixture();
  await worker.fetch(request({ batch: [retry] }), environment());
  assert.equal(sent[2].uuid, sent[3].uuid);
  retry.properties.payload.applicant_payload.event_id = 'd'.repeat(32);
  await worker.fetch(request({ batch: [retry] }), environment());
  assert.notEqual(sent[3].uuid, sent[4].uuid);
});

test('gameplay rejects applicant crossover, invalid options and choices that were not offered', async t => {
  t.mock.method(globalThis, 'fetch', () => assert.fail('unexpected upstream call'));
  for (const mutate of [
    e => e.properties.applicant_id = 'Archipelago.Diagnostics',
    e => e.properties.request_id = 'basic_usage',
    e => e.properties.category = 'RunHistory',
    e => e.properties.payload.applicant_payload.event_id = 'bad',
    e => e.properties.payload.applicant_payload.yaml_options.shop_sanity = 'true',
    e => e.properties.payload.applicant_payload.ancient_pool = 'invalid',
    e => e.properties.payload.applicant_payload.selected_relic = 'NOT_OFFERED',
    e => e.properties.payload.applicant_payload.offered_relics = Array(17).fill('RELIC_A'),
  ]) {
    const event = gameplayFixture(); mutate(event);
    assert.equal((await worker.fetch(request({ batch: [event] }), environment())).status, 400);
  }
  const diagnostic = fixture(); diagnostic.properties.applicant_id = 'Archipelago.Gameplay';
  assert.equal((await worker.fetch(request({ batch: [diagnostic] }), environment())).status, 400);
});


test('persistent gameplay IDs survive a new session and reject invalid campaign/run IDs', async t => {
  const sent = [];
  t.mock.method(globalThis, 'fetch', async (_url, options) => {
    sent.push(JSON.parse(options.body).batch[0]); return new Response('{}');
  });
  const event = gameplayFixture();
  const payload = event.properties.payload.applicant_payload;
  Object.assign(payload, { telemetry_version: 2, campaign_id: 'a'.repeat(32), run_id: 'b'.repeat(32) });
  assert.equal((await worker.fetch(request({ batch: [event] }), environment())).status, 200);
  event.timestamp = '2026-09-30T12:00:00Z';
  event.properties.session_id = 'e'.repeat(32);
  payload.selected_relic = 'RELIC_B'; // Same receipt after restoring a checkpoint.
  assert.equal((await worker.fetch(request({ batch: [event] }), environment())).status, 200);
  assert.equal(sent[0].uuid, sent[1].uuid);
  assert.equal(sent[0].properties.campaign_id, payload.campaign_id);
  assert.equal(sent[0].properties.run_id, payload.run_id);
  payload.event_id = 'd'.repeat(32);
  await worker.fetch(request({ batch: [event] }), environment());
  assert.notEqual(sent[1].uuid, sent[2].uuid);
  for (const key of ['event_id', 'campaign_id', 'run_id']) {
    const valid = payload[key];
    for (const invalid of [undefined, null, '', 'seed-slot-name', ['a'.repeat(32)]]) {
      payload[key] = invalid;
      assert.equal((await worker.fetch(request({ batch: [event] }), environment())).status, 400);
    }
    payload[key] = valid;
  }
  event.event = 'ap.settings_observed';
  delete payload.run_id;
  assert.equal((await worker.fetch(request({ batch: [event] }), environment())).status, 200);
  assert.equal(sent.at(-1).properties.run_id, undefined);
});
