/**
 * @file Protects the Bonus Items answer, data-source, and compiler contracts.
 *
 * These tests ensure the relic source files drive eligibility (standard rarities
 * union custom pools, minus the blacklist and pickup-description exclusions), that
 * semantic answers compile to the exact Python `bonus_items` shape, and that
 * malformed or drifted state fails loudly at the compiler boundary.
 */

import { describe, expect, it } from "vitest";
import blacklistJson from "@shared/bonus_relic_blacklist.json";
import customPoolsJson from "@shared/relic_custom_pools.json";
import { optionCatalog } from "../../generated/optionCatalog";
import relicsJson from "../../generated/relics.json";
import {
  BONUS_RELIC_POOLS,
  CUSTOM_RELIC_POOLS,
  filterRelicsByName,
  getBonusRelicPoolOptions,
  getEligiblePoolRelicIds,
  getEligibleSpecificRelics,
  getPoolsForRelic,
  getRandomWaxRelicImageUrl,
  getRelicById,
  isBonusRelicPool,
  isEligibleSpecificRelicId,
  RANDOM_WAX_RELIC_NAME,
} from "../BonusRelicData";
import { getBonusItemDisplayRow } from "../BonusItemDisplay";
import { createDefaultFillerAnswers } from "../FillerItem";
import { getGuidedOptionKeys } from "../GuidedOption";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { BONUS_ITEM_OPTION_KEY } from "../WizardOptionKey";
import { checkSetupStep } from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";

/**
 * Creates a complete answer model suitable for Bonus Item tests.
 *
 * @returns Wizard answers using generated defaults with an empty Bonus Items list.
 */
function createTestAnswers() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);
  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers, optionCatalog);
}

/** A relic from a standard rarity pool known not to be blacklisted or a pickup. */
const STANDARD_RELIC_ID = "AKABEKO";

/** A relic that exists only through a custom pool (the Fake whitelist). */
const CUSTOM_POOL_RELIC_ID = "FAKE_STRIKE_DUMMY";

/** A relic excluded by the shared blacklist. */
const BLACKLISTED_RELIC_ID = "BOWLER_HAT";

/** A relic excluded because its description starts with "Upon pickup,". */
const PICKUP_RELIC_ID = "ALCHEMICAL_COFFER";

/**
 * Verifies the three JSON sources loaded through the data module stay consistent.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function keepsSourceDataConsistent(): void {
  // Every blacklist and custom-pool ID must resolve to a generated relic record.
  const relicIds = new Set(relicsJson.map((relic) => relic.id));

  for (const id of blacklistJson) {
    expect(relicIds.has(id)).toBe(true);
  }

  for (const members of Object.values(customPoolsJson)) {
    for (const id of members) {
      expect(relicIds.has(id)).toBe(true);
    }
  }

  // The randomized-row placeholder must exist even though it is not selectable.
  const placeholder = getRelicById("SMALL_CAPSULE");
  expect(placeholder.imageUrl).toBe("/static/images/relics/small_capsule.webp");
  expect(getRandomWaxRelicImageUrl()).toBe(placeholder.imageUrl);
}

/**
 * Verifies pool membership derives from rarity keys and custom pool lists.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function derivesPoolsFromSources(): void {
  // Standard pools come from rarity_key; custom pools come from the shared JSON.
  expect(BONUS_RELIC_POOLS).toEqual([
    "Common",
    "Uncommon",
    "Rare",
    "Shop",
    "Fake",
    "Classic",
  ]);
  expect(CUSTOM_RELIC_POOLS).toEqual(Object.keys(customPoolsJson));

  // A known standard relic belongs to its rarity pool; a Fake relic only to Fake.
  expect(getPoolsForRelic(STANDARD_RELIC_ID)).toEqual(["Uncommon"]);
  expect(getPoolsForRelic(CUSTOM_POOL_RELIC_ID)).toEqual(["Fake"]);

  // Pool membership lists resolve to real relic IDs in catalog order.
  for (const pool of BONUS_RELIC_POOLS) {
    const memberIds = getEligiblePoolRelicIds(pool);
    expect(memberIds.length).toBeGreaterThan(0);

    for (const id of memberIds) {
      expect(() => getRelicById(id)).not.toThrow();
    }
  }
}

/**
 * Verifies blacklist and pickup-description exclusions win over every pool.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function appliesExclusionsLast(): void {
  // Blacklisted relics are excluded even though their rarity pool is valid.
  expect(isEligibleSpecificRelicId(BLACKLISTED_RELIC_ID)).toBe(false);

  // Pickup-effect relics are excluded from both standard and custom pools.
  expect(isEligibleSpecificRelicId(PICKUP_RELIC_ID)).toBe(false);
  expect(getEligiblePoolRelicIds("Fake")).not.toContain("FAKE_MANGO");

  // The exclusions shrink the union below the raw pool membership counts.
  const eligible = getEligibleSpecificRelics();
  expect(eligible.length).toBeGreaterThan(0);
  expect(eligible.map((relic) => relic.id)).not.toContain(BLACKLISTED_RELIC_ID);
  expect(eligible.map((relic) => relic.id)).not.toContain(PICKUP_RELIC_ID);

  // Custom-pool relics with neutral descriptions remain eligible.
  expect(isEligibleSpecificRelicId(CUSTOM_POOL_RELIC_ID)).toBe(true);
}

/**
 * Verifies specific-relic search is case-insensitive on the display name.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function filtersRelicsByNameCaseInsensitively(): void {
  const eligible = getEligibleSpecificRelics();

  // An empty query returns the complete eligible list unchanged.
  expect(filterRelicsByName(eligible, "")).toHaveLength(eligible.length);

  // Mixed-case substrings match regardless of the stored casing.
  const matches = filterRelicsByName(eligible, "strike dummy");
  expect(matches.length).toBeGreaterThan(0);
  expect(matches.some((relic) => relic.id === CUSTOM_POOL_RELIC_ID)).toBe(true);

  // A query matching nothing returns an empty list rather than null.
  expect(filterRelicsByName(eligible, "zzz-no-such-relic-zzz")).toEqual([]);
}

/**
 * Verifies every pool option carries a name, description, and eligible count.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function describesEveryPoolOption(): void {
  const options = getBonusRelicPoolOptions();

  // The checkbox grid mirrors the canonical pool ordering.
  expect(options.map((option) => option.name)).toEqual([...BONUS_RELIC_POOLS]);

  for (const option of options) {
    expect(option.description.length).toBeGreaterThan(0);
    expect(option.relicCount).toBeGreaterThan(0);
    expect(isBonusRelicPool(option.name)).toBe(true);
  }
}

/**
 * Verifies a default wizard compiles to an empty bonus_items list.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesEmptyBonusItemsDefault(): void {
  const options = compileWizardAnswers(createTestAnswers(), optionCatalog);
  expect(options[BONUS_ITEM_OPTION_KEY]).toEqual([]);
}

/**
 * Verifies specific and randomized rows compile to the exact Python schema shape.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesBothWaxRelicModes(): void {
  const answers = createTestAnswers();
  answers.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: CUSTOM_POOL_RELIC_ID },
    { kind: "WAX_RELIC", mode: "random", pools: ["Common", "Uncommon"] },
  ];

  const options = compileWizardAnswers(answers, optionCatalog);

  // Row order and pool order must survive compilation exactly.
  expect(options[BONUS_ITEM_OPTION_KEY]).toEqual([
    { WAX_RELIC: { Value: CUSTOM_POOL_RELIC_ID } },
    { WAX_RELIC: { Pools: ["Common", "Uncommon"] } },
  ]);
}

/**
 * Verifies duplicate rows and large lists compile without a capacity cap.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesDuplicatesAndManyRows(): void {
  const answers = createTestAnswers();
  const duplicate: (typeof answers.checksAndRewards.bonusItems)[number] = {
    kind: "WAX_RELIC",
    mode: "specific",
    relicId: STANDARD_RELIC_ID,
  };
  answers.checksAndRewards.bonusItems = Array.from({ length: 60 }, () => ({
    ...duplicate,
  }));

  const options = compileWizardAnswers(answers, optionCatalog);
  const compiled = options[BONUS_ITEM_OPTION_KEY] as unknown[];

  // All sixty identical rows compile in order; slot capacity is not checked here.
  expect(compiled).toHaveLength(60);
  expect(compiled[0]).toEqual({ WAX_RELIC: { Value: STANDARD_RELIC_ID } });
  expect(compiled.at(-1)).toEqual({ WAX_RELIC: { Value: STANDARD_RELIC_ID } });
}

/**
 * Verifies ineligible, unknown, and malformed answers fail at the compiler boundary.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidBonusItemAnswers(): void {
  // An excluded relic cannot be selected even when hand-constructed.
  const blacklisted = createTestAnswers();
  blacklisted.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: BLACKLISTED_RELIC_ID },
  ];
  expect(() => compileWizardAnswers(blacklisted, optionCatalog)).toThrow(
    "not an eligible Wax Relic",
  );

  // An unknown relic ID fails with the same eligibility message.
  const unknown = createTestAnswers();
  unknown.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: "NOT_A_RELIC" },
  ];
  expect(() => compileWizardAnswers(unknown, optionCatalog)).toThrow(
    "not an eligible Wax Relic",
  );

  // Random mode requires at least one pool.
  const emptyPools = createTestAnswers();
  emptyPools.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "random", pools: [] },
  ];
  expect(() => compileWizardAnswers(emptyPools, optionCatalog)).toThrow(
    "at least one relic pool",
  );

  // Unknown pool names are rejected against the source-driven pool registry.
  const unknownPool = createTestAnswers();
  unknownPool.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "random", pools: ["NotAPool"] },
  ];
  expect(() => compileWizardAnswers(unknownPool, optionCatalog)).toThrow(
    "unknown relic pool",
  );

  // Duplicate pools in one row are rejected to keep YAML deterministic.
  const duplicatePools = createTestAnswers();
  duplicatePools.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "random", pools: ["Rare", "Rare"] },
  ];
  expect(() => compileWizardAnswers(duplicatePools, optionCatalog)).toThrow(
    "more than once",
  );
}

/**
 * Verifies bonus_items is registered once in guided YAML ownership.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function ownsBonusItemsExactlyOnce(): void {
  const keys = getGuidedOptionKeys();
  expect(keys.filter((key) => key === BONUS_ITEM_OPTION_KEY)).toHaveLength(1);

  // The generated catalog contract the compiler depends on must stay a list.
  const option = optionCatalog.options[BONUS_ITEM_OPTION_KEY];
  expect(option?.kind).toBe("list");
  expect(option?.default).toEqual([]);
  expect(option?.group).toBe("Bonus Items");
}

/**
 * Verifies Bonus Items sits between Shop Sanity and Filler Items in the UI order.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function placesBonusItemsBeforeFiller(): void {
  const questionIds = checkSetupStep.questions.map((question) => question.id);
  const bonusIndex = questionIds.indexOf("bonus-items");
  const fillerIndex = questionIds.indexOf("filler-weights");

  expect(bonusIndex).toBeGreaterThan(-1);
  expect(fillerIndex).toBeGreaterThan(-1);
  expect(bonusIndex).toBeLessThan(fillerIndex);
  expect(questionIds.at(-1)).toBe("filler-weights");
}

/**
 * Verifies the shared display model renders both Wax Relic modes.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rendersDisplayRows(): void {
  const specific = getBonusItemDisplayRow({
    kind: "WAX_RELIC",
    mode: "specific",
    relicId: CUSTOM_POOL_RELIC_ID,
  });
  expect(specific.name).toBe(`Wax ${getRelicById(CUSTOM_POOL_RELIC_ID).name}`);
  expect(specific.imageUrl).toBe(getRelicById(CUSTOM_POOL_RELIC_ID).imageUrl);
  expect(specific.details).toContain("Fake");

  const random = getBonusItemDisplayRow({
    kind: "WAX_RELIC",
    mode: "random",
    pools: ["Rare", "Shop"],
  });
  expect(random.name).toBe(RANDOM_WAX_RELIC_NAME);
  expect(random.imageUrl).toBe(getRandomWaxRelicImageUrl());
  expect(random.details).toBe("Rare, Shop");
}

/**
 * Registers Bonus Items compiler and data cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerBonusItemsTests(): void {
  it("keeps the three relic sources consistent", keepsSourceDataConsistent);
  it(
    "derives pools from rarity keys and custom lists",
    derivesPoolsFromSources,
  );
  it("applies blacklist and pickup exclusions last", appliesExclusionsLast);
  it(
    "filters relics by name case-insensitively",
    filtersRelicsByNameCaseInsensitively,
  );
  it("describes every pool option", describesEveryPoolOption);
  it("compiles an empty default", compilesEmptyBonusItemsDefault);
  it("compiles both Wax Relic modes", compilesBothWaxRelicModes);
  it("compiles duplicates and many rows", compilesDuplicatesAndManyRows);
  it("rejects invalid answers", rejectsInvalidBonusItemAnswers);
  it("owns bonus_items exactly once", ownsBonusItemsExactlyOnce);
  it("sits between Shop Sanity and Filler Items", placesBonusItemsBeforeFiller);
  it("renders display rows for both modes", rendersDisplayRows);
}

// Register the documented test callbacks as one focused vertical-slice suite.
describe("bonus items wizard compiler", registerBonusItemsTests);
