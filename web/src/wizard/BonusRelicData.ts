/**
 * @file Source-driven Bonus Item relic data for the guided wizard.
 *
 * Three JSON sources define everything this module exposes:
 *   - `web/src/generated/relics.json` — Spire Codex relic records (id, name,
 *     description, rarity_key, pool, image_url). Regenerate with
 *     `scripts/download_latest_relics.ps1`.
 *   - `shared/bonus_relic_blacklist.json` — relic IDs a random pool never rolls
 *     (they can still be named), and the Ancient relics with a pickup effect.
 *   - `shared/relic_custom_pools.json` — named custom pools (currently Fake and
 *     Classic) mapping pool name to an explicit list of relic IDs.
 *
 * All lookups, eligibility sets, and display models are derived from those files at
 * module load. If any source drifts (malformed record, duplicate ID, unresolved
 * reference), this module throws during import so the failure is loud instead of
 * producing a silently broken relic picker.
 */

import relicsJson from "../generated/relics.json";
import bonusRelicBlacklistJson from "@shared/bonus_relic_blacklist.json";
import relicCustomPoolsJson from "@shared/relic_custom_pools.json";
import type { RelicBonusKind } from "./WizardAnswers";

/** One relic record from the generated Spire Codex export. */
export interface GeneratedRelic {
  id: string;
  name: string;
  description: string;
  rarity_key: string;
  pool: string;
  image_url: string;
}

/** Player-facing relic model used by the picker, table, and review list. */
export interface BonusRelicInfo {
  id: string;
  name: string;
  description: string;
  imageUrl: string;
  rarityKey: string;
}

/** Player-facing pool model for the Random Relic checkbox grid. */
export interface BonusRelicPoolOption {
  name: string;
  description: string;
  relicCount: number;
}

/**
 * Standard rarity-backed pools. Membership comes from each relic's `rarity_key`.
 * These names are part of the world's YAML contract and are intentionally fixed.
 */
const STANDARD_RELIC_POOLS = [
  "Common",
  "Uncommon",
  "Rare",
  "Shop",
  "Ancient",
] as const;

type StandardRelicPool = (typeof STANDARD_RELIC_POOLS)[number];

/** Custom pool names are derived from the shared JSON keys (currently Fake/Classic). */
type CustomRelicPool = keyof typeof relicCustomPoolsJson;

/** Every pool name selectable for a randomized Wax Relic. */
export type BonusRelicPool = StandardRelicPool | CustomRelicPool;

/** Relic whose image represents a randomized Wax Relic row. */
const RANDOM_WAX_RELIC_IMAGE_ID = "SMALL_CAPSULE";

/** Display name for a randomized Wax Relic row in the table and review. */
export const RANDOM_WAX_RELIC_NAME = "Random Wax Relic";

/**
 * Descriptions that begin with this prefix grant an immediate pickup effect rather
 * than a lasting relic, so they are never valid Wax Relics regardless of pool: the
 * effect would outlast the melting. A permanent Relic may have one.
 */
const PICKUP_DESCRIPTION_PREFIX = "Upon pickup,";

/** Authored descriptions for known pools; unknown future pools get a generic one. */
const POOL_DESCRIPTIONS: Record<string, string> = {
  Common: "Widely available relics with modest, straightforward effects.",
  Uncommon: "Mid-tier relics with stronger or more specialized effects.",
  Rare: "Powerful relics that can define a run's strategy.",
  Shop: "Relics normally sold by the Merchant.",
  Ancient: "Relics that Ancients offer. Very strong.",
  Fake: "Knockoff relics with weaker, suspiciously familiar effects.",
  Classic: "Relics returning from the original Slay the Spire.",
};

/** Asserts a condition and throws a source-data error when it fails. */
function requireSourceData(condition: boolean, message: string): void {
  if (!condition) {
    throw new Error(`Bonus relic source data is invalid: ${message}`);
  }
}

/** Validates and narrows the generated relic export. */
function parseRelics(value: unknown): GeneratedRelic[] {
  requireSourceData(Array.isArray(value), "relics.json must be a JSON array.");

  const relics: GeneratedRelic[] = [];
  const seenIds = new Set<string>();

  for (const [index, entry] of (value as unknown[]).entries()) {
    const context = `relics.json entry ${index}`;
    requireSourceData(
      entry !== null && typeof entry === "object",
      `${context} must be an object.`,
    );

    const record = entry as Record<string, unknown>;
    for (const field of [
      "id",
      "name",
      "description",
      "rarity_key",
      "pool",
      "image_url",
    ]) {
      requireSourceData(
        typeof record[field] === "string" &&
          (record[field] as string).length > 0,
        `${context} must have a non-empty '${field}' string.`,
      );
    }

    const relic = entry as unknown as GeneratedRelic;
    requireSourceData(
      !seenIds.has(relic.id),
      `relics.json contains duplicate id '${relic.id}'.`,
    );
    seenIds.add(relic.id);
    relics.push(relic);
  }

  requireSourceData(relics.length > 0, "relics.json must not be empty.");
  return relics;
}

/** The exclusion lists of the shared blacklist file. */
interface BonusRelicRules {
  /** Never rolled from a pool, but can be named. */
  neverRandom: string[];
  /** Ancient relics with a pickup effect, whatever their description says. */
  ancientPickupEffects: string[];
}

/** Validates and narrows the shared blacklist file. */
function parseRelicRules(value: unknown): BonusRelicRules {
  requireSourceData(
    value !== null && typeof value === "object" && !Array.isArray(value),
    "bonus_relic_blacklist.json must be an object of relic ID lists.",
  );
  const record = value as Record<string, unknown>;

  const readIds = (key: string): string[] => {
    const ids = record[key];
    requireSourceData(
      Array.isArray(ids) &&
        ids.every((entry) => typeof entry === "string" && entry.length > 0),
      `bonus_relic_blacklist.json '${key}' must be an array of non-empty relic ID strings.`,
    );
    return ids as string[];
  };

  return {
    neverRandom: readIds("never_random"),
    ancientPickupEffects: readIds("ancient_pickup_effects"),
  };
}

/** Validates and narrows the shared custom pools. */
function parseCustomPools(value: unknown): Record<string, string[]> {
  requireSourceData(
    value !== null && typeof value === "object" && !Array.isArray(value),
    "relic_custom_pools.json must be an object mapping pool names to relic IDs.",
  );

  const pools: Record<string, string[]> = {};
  for (const [poolName, members] of Object.entries(
    value as Record<string, unknown>,
  )) {
    requireSourceData(
      poolName.length > 0,
      "custom pool names must be non-empty.",
    );
    requireSourceData(
      Array.isArray(members) &&
        members.length > 0 &&
        members.every(
          (member) => typeof member === "string" && member.length > 0,
        ),
      `custom pool '${poolName}' must be a non-empty array of relic ID strings.`,
    );
    // The validation above proves every member is a non-empty string.
    pools[poolName] = [...(members as string[])];
  }

  requireSourceData(
    Object.keys(pools).length > 0,
    "relic_custom_pools.json must define at least one pool.",
  );
  return pools;
}

/** Strips Spire Codex markup tags for plain-text display in the wizard. */
function formatRelicDescription(description: string): string {
  // [energy:2] -> "2 Energy", [star:3] -> "3 Stars", [blue]x[/blue] -> "x".
  return description
    .replace(/\[energy:(\d+)\]/g, "$1 Energy")
    .replace(/\[star:(\d+)\]/g, "$1 Stars")
    .replace(/\[\/[a-z]+\]/g, "")
    .replace(/\[[a-z]+\]/g, "");
}

// --- Module initialization: parse, validate, and index the three sources. ---

const relics = parseRelics(relicsJson);
const relicRules = parseRelicRules(bonusRelicBlacklistJson);
const neverRandomIds = new Set(relicRules.neverRandom);
const ancientPickupEffectIds = new Set(relicRules.ancientPickupEffects);
const customPools = parseCustomPools(relicCustomPoolsJson);

const relicById = new Map<string, GeneratedRelic>();
for (const relic of relics) {
  relicById.set(relic.id, relic);
}

// Every referenced ID must resolve to a real relic so source edits fail loudly. The
// pickup-effect list is exempt: it also names relics newer than the relics.json export.
for (const id of neverRandomIds) {
  requireSourceData(
    relicById.has(id),
    `never-rolled relic '${id}' is missing from relics.json.`,
  );
}
for (const [poolName, members] of Object.entries(customPools)) {
  requireSourceData(
    !STANDARD_RELIC_POOLS.includes(poolName as StandardRelicPool),
    `custom pool '${poolName}' collides with a standard rarity pool.`,
  );
  for (const id of members) {
    requireSourceData(
      relicById.has(id),
      `custom pool '${poolName}' references unknown relic '${id}'.`,
    );
  }
}

const randomWaxRelicSource = relicById.get(RANDOM_WAX_RELIC_IMAGE_ID);
requireSourceData(
  randomWaxRelicSource !== undefined,
  `placeholder relic '${RANDOM_WAX_RELIC_IMAGE_ID}' is missing from relics.json.`,
);

/** Converts a generated relic record into the display model. */
function toRelicInfo(relic: GeneratedRelic): BonusRelicInfo {
  return {
    id: relic.id,
    name: relic.name,
    description: formatRelicDescription(relic.description),
    imageUrl: relic.image_url,
    rarityKey: relic.rarity_key,
  };
}

/** All custom pool names in their JSON declaration order (currently Fake, Classic). */
export const CUSTOM_RELIC_POOLS = Object.keys(customPools) as CustomRelicPool[];

/** Every selectable pool: standard rarities first, then custom pools. */
export const BONUS_RELIC_POOLS: readonly BonusRelicPool[] = [
  ...STANDARD_RELIC_POOLS,
  ...CUSTOM_RELIC_POOLS,
];

/** Relic IDs that may appear in each pool, before exclusions. */
const poolMemberIds = new Map<BonusRelicPool, Set<string>>();
for (const pool of STANDARD_RELIC_POOLS) {
  poolMemberIds.set(
    pool,
    new Set(
      relics.filter((relic) => relic.rarity_key === pool).map((r) => r.id),
    ),
  );
}
for (const [poolName, members] of Object.entries(customPools)) {
  poolMemberIds.set(poolName as CustomRelicPool, new Set(members));
}

/** Whether picking the relic up does something, so it cannot be a Wax Relic. */
function hasPickupEffect(relic: GeneratedRelic): boolean {
  return (
    relic.description.startsWith(PICKUP_DESCRIPTION_PREFIX) ||
    ancientPickupEffectIds.has(relic.id)
  );
}

/** Whether a relic may be named for this kind of bonus, whatever its pool. */
function isEligibleRelic(relic: GeneratedRelic, kind: RelicBonusKind): boolean {
  return kind === "RELIC" || !hasPickupEffect(relic);
}

/** Whether a pool may roll this relic for this kind of bonus. */
function isRandomEligibleRelic(
  relic: GeneratedRelic,
  kind: RelicBonusKind,
): boolean {
  return (
    isEligibleRelic(relic, kind) &&
    relic.pool === "shared" &&
    !neverRandomIds.has(relic.id)
  );
}

/** Every relic that belongs to at least one pool, sorted by name. */
const pooledRelics: readonly GeneratedRelic[] = (() => {
  const unionIds = new Set<string>();
  for (const memberIds of poolMemberIds.values()) {
    for (const id of memberIds) {
      unionIds.add(id);
    }
  }

  return [...unionIds]
    .map((id) => relicById.get(id)!)
    .sort((a, b) => a.name.localeCompare(b.name));
})();

/** Sorted relics that can be named for each kind of bonus. */
const eligibleSpecificRelics: Record<RelicBonusKind, readonly BonusRelicInfo[]> =
  {
    WAX_RELIC: pooledRelics
      .filter((relic) => isEligibleRelic(relic, "WAX_RELIC"))
      .map(toRelicInfo),
    RELIC: pooledRelics
      .filter((relic) => isEligibleRelic(relic, "RELIC"))
      .map(toRelicInfo),
  };

/** Returns the display model for any relic ID, or throws when the ID is unknown. */
export function getRelicById(id: string): BonusRelicInfo {
  const relic = relicById.get(id);
  if (!relic) {
    throw new Error(`Unknown relic '${id}' in generated relic data.`);
  }
  return toRelicInfo(relic);
}

/** Returns every relic that can be named for this kind of bonus, sorted by name. */
export function getEligibleSpecificRelics(
  kind: RelicBonusKind,
): readonly BonusRelicInfo[] {
  return eligibleSpecificRelics[kind];
}

/** Whether a relic ID can be named for this kind of bonus. */
export function isEligibleSpecificRelicId(
  id: string,
  kind: RelicBonusKind,
): boolean {
  return eligibleSpecificRelics[kind].some((relic) => relic.id === id);
}

/** Whether a value is one of the known pool names. */
export function isBonusRelicPool(value: unknown): value is BonusRelicPool {
  return (
    typeof value === "string" &&
    (BONUS_RELIC_POOLS as readonly string[]).includes(value)
  );
}

/** Returns pool display models, counting the relics each pool can roll for this kind. */
export function getBonusRelicPoolOptions(
  kind: RelicBonusKind,
): readonly BonusRelicPoolOption[] {
  return BONUS_RELIC_POOLS.map((pool) => ({
    name: pool,
    description:
      POOL_DESCRIPTIONS[pool] ?? `Relics from the ${pool} custom pool.`,
    relicCount: getEligiblePoolRelicIds(pool, kind).length,
  }));
}

/** Relic IDs one pool can roll for this kind of bonus, in relic catalog order. */
export function getEligiblePoolRelicIds(
  pool: BonusRelicPool,
  kind: RelicBonusKind,
): readonly string[] {
  const memberIds = poolMemberIds.get(pool) ?? new Set<string>();
  return relics
    .filter(
      (relic) => memberIds.has(relic.id) && isRandomEligibleRelic(relic, kind),
    )
    .map((relic) => relic.id);
}

/** Names of the pools a relic belongs to, in canonical pool order. */
export function getPoolsForRelic(id: string): readonly string[] {
  return BONUS_RELIC_POOLS.filter((pool) => poolMemberIds.get(pool)?.has(id));
}

/** Filters relics by case-insensitive name substring; empty query returns all. */
export function filterRelicsByName(
  candidates: readonly BonusRelicInfo[],
  query: string,
): BonusRelicInfo[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return [...candidates];
  }
  return candidates.filter((relic) =>
    relic.name.toLowerCase().includes(normalized),
  );
}

/** Image URL for randomized Wax Relic rows, resolved from the relic data. */
export function getRandomWaxRelicImageUrl(): string {
  return randomWaxRelicSource!.image_url;
}
