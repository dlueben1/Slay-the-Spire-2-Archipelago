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
import { itemPoolSetupStep } from "../WizardStep";
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

/** A character-specific relic that must not enter randomized shared pools. */
const CHARACTER_SPECIFIC_RELIC_ID = "BIG_HAT";

/** A relic that exists only through a custom pool (the Fake whitelist). */
const CUSTOM_POOL_RELIC_ID = "FAKE_STRIKE_DUMMY";

/** A relic whose description starts with "Upon pickup,", so it can't be wax. */
const PICKUP_RELIC_ID = "STRAWBERRY";

/** An Ancient pickup relic the description check alone would miss. */
const UNMARKED_PICKUP_RELIC_ID = "PANDORAS_BOX";

/** Relics no pool rolls, though they can be named. */
const NEVER_RANDOM_RELIC_IDS = ["GLITTER", "BOWLER_HAT", "WHITE_STAR"];

/**
 * Verifies the three JSON sources loaded through the data module stay consistent.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function keepsSourceDataConsistent(): void {
  // Every blacklist and custom-pool ID must resolve to a generated relic record.
  const relicIds = new Set(relicsJson.map((relic) => relic.id));

  for (const id of blacklistJson.never_random) {
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
    "Ancient",
    "Fake",
    "Classic",
  ]);
  expect(CUSTOM_RELIC_POOLS).toEqual(Object.keys(customPoolsJson));

  // A known standard relic belongs to its rarity pool; a Fake relic only to Fake.
  expect(getPoolsForRelic(STANDARD_RELIC_ID)).toEqual(["Uncommon"]);
  expect(getPoolsForRelic(CUSTOM_POOL_RELIC_ID)).toEqual(["Fake"]);

  // Pool membership lists resolve to real relic IDs in catalog order.
  for (const pool of BONUS_RELIC_POOLS) {
    const memberIds = getEligiblePoolRelicIds(pool, "WAX_RELIC");
    expect(memberIds.length).toBeGreaterThan(0);

    for (const id of memberIds) {
      expect(() => getRelicById(id)).not.toThrow();
    }
  }

  // Randomized pools count only shared relics from the generated source data.
  const sharedRares = relicsJson.filter(
    (relic) =>
      relic.rarity_key === "Rare" &&
      relic.pool === "shared" &&
      !blacklistJson.never_random.includes(relic.id),
  );
  const rareCount = (kind: "WAX_RELIC" | "RELIC") =>
    getBonusRelicPoolOptions(kind).find((option) => option.name === "Rare")
      ?.relicCount;
  expect(rareCount("RELIC")).toBe(sharedRares.length);
  expect(rareCount("WAX_RELIC")).toBe(
    sharedRares.filter((relic) => !relic.description.startsWith("Upon pickup,"))
      .length,
  );
  expect(getEligiblePoolRelicIds("Rare", "RELIC")).not.toContain(
    CHARACTER_SPECIFIC_RELIC_ID,
  );
}

/**
 * Verifies blacklist and pickup-description exclusions win over every pool.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function appliesExclusionsLast(): void {
  // Pickup-effect relics can't be wax, named or rolled, but a permanent Relic can be one.
  for (const id of [PICKUP_RELIC_ID, UNMARKED_PICKUP_RELIC_ID]) {
    expect(isEligibleSpecificRelicId(id, "WAX_RELIC")).toBe(false);
    expect(isEligibleSpecificRelicId(id, "RELIC")).toBe(true);
  }
  expect(getEligiblePoolRelicIds("Fake", "WAX_RELIC")).not.toContain(
    "FAKE_MANGO",
  );
  expect(getEligiblePoolRelicIds("Fake", "RELIC")).toContain("FAKE_MANGO");
  expect(getEligiblePoolRelicIds("Ancient", "WAX_RELIC")).not.toContain(
    UNMARKED_PICKUP_RELIC_ID,
  );
  expect(getEligiblePoolRelicIds("Ancient", "RELIC")).toContain(
    UNMARKED_PICKUP_RELIC_ID,
  );

  // Some relics are too strong to roll every run, but can still be named.
  for (const kind of ["WAX_RELIC", "RELIC"] as const) {
    const rolled = BONUS_RELIC_POOLS.flatMap((pool) =>
      getEligiblePoolRelicIds(pool, kind),
    );
    for (const id of NEVER_RANDOM_RELIC_IDS) {
      expect(rolled).not.toContain(id);
      expect(isEligibleSpecificRelicId(id, kind)).toBe(true);
    }
  }

  // The exclusions shrink the union below the raw pool membership counts.
  const eligible = getEligibleSpecificRelics("WAX_RELIC");
  expect(eligible.length).toBeGreaterThan(0);
  expect(eligible.map((relic) => relic.id)).not.toContain(PICKUP_RELIC_ID);

  // Custom-pool relics with neutral descriptions remain eligible.
  expect(isEligibleSpecificRelicId(CUSTOM_POOL_RELIC_ID, "WAX_RELIC")).toBe(
    true,
  );

  // Character-specific relics remain available only for explicit selection.
  expect(
    isEligibleSpecificRelicId(CHARACTER_SPECIFIC_RELIC_ID, "WAX_RELIC"),
  ).toBe(true);
}

/**
 * Verifies specific-relic search is case-insensitive on the display name.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function filtersRelicsByNameCaseInsensitively(): void {
  const eligible = getEligibleSpecificRelics("WAX_RELIC");

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
  const options = getBonusRelicPoolOptions("WAX_RELIC");

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
  answers.itemPool.bonusItems = [
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
  const duplicate: (typeof answers.itemPool.bonusItems)[number] = {
    kind: "WAX_RELIC",
    mode: "specific",
    relicId: STANDARD_RELIC_ID,
  };
  answers.itemPool.bonusItems = Array.from({ length: 40 }, () => ({
    ...duplicate,
  }));

  const options = compileWizardAnswers(answers, optionCatalog);
  const compiled = options[BONUS_ITEM_OPTION_KEY] as unknown[];

  // All forty identical rows compile in order. They fit one character's filler
  // slots; fillerBudget.test.ts covers capacity.
  expect(compiled).toHaveLength(40);
  expect(compiled[0]).toEqual({ WAX_RELIC: { Value: STANDARD_RELIC_ID } });
  expect(compiled.at(-1)).toEqual({ WAX_RELIC: { Value: STANDARD_RELIC_ID } });
}

/**
 * Verifies ineligible, unknown, and malformed answers fail at the compiler boundary.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidBonusItemAnswers(): void {
  // A pickup-effect relic cannot be a Wax Relic even when hand-constructed.
  const pickupWax = createTestAnswers();
  pickupWax.itemPool.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: PICKUP_RELIC_ID },
  ];
  expect(() => compileWizardAnswers(pickupWax, optionCatalog)).toThrow(
    "not an eligible bonus relic",
  );

  // An unknown relic ID fails with the same eligibility message.
  const unknown = createTestAnswers();
  unknown.itemPool.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: "NOT_A_RELIC" },
  ];
  expect(() => compileWizardAnswers(unknown, optionCatalog)).toThrow(
    "not an eligible bonus relic",
  );

  // Random mode requires at least one pool.
  const emptyPools = createTestAnswers();
  emptyPools.itemPool.bonusItems = [
    { kind: "WAX_RELIC", mode: "random", pools: [] },
  ];
  expect(() => compileWizardAnswers(emptyPools, optionCatalog)).toThrow(
    "at least one relic pool",
  );

  // Unknown pool names are rejected against the source-driven pool registry.
  const unknownPool = createTestAnswers();
  unknownPool.itemPool.bonusItems = [
    { kind: "WAX_RELIC", mode: "random", pools: ["NotAPool"] },
  ];
  expect(() => compileWizardAnswers(unknownPool, optionCatalog)).toThrow(
    "unknown relic pool",
  );

  // Duplicate pools in one row are rejected to keep YAML deterministic.
  const duplicatePools = createTestAnswers();
  duplicatePools.itemPool.bonusItems = [
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
 * Verifies Bonus Items comes before Filler Items on the Item Pool step.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function placesBonusItemsBeforeFiller(): void {
  const questionIds = itemPoolSetupStep.questions.map(
    (question) => question.id,
  );
  const bonusIndex = questionIds.indexOf("bonus-items");
  const fillerIndex = questionIds.indexOf("filler-weights");

  expect(bonusIndex).toBeGreaterThan(-1);
  expect(fillerIndex).toBeGreaterThan(-1);
  expect(bonusIndex).toBeLessThan(fillerIndex);
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
  expect(specific.details).toBe(getRelicById(CUSTOM_POOL_RELIC_ID).description);

  const random = getBonusItemDisplayRow({
    kind: "WAX_RELIC",
    mode: "random",
    pools: ["Rare", "Shop"],
  });
  expect(random.name).toBe(RANDOM_WAX_RELIC_NAME);
  expect(random.imageUrl).toBe(getRandomWaxRelicImageUrl());
  expect(random.details).toBe("From Pools: Rare, Shop");
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
  it("comes before Filler Items", placesBonusItemsBeforeFiller);
  it("renders display rows for both modes", rendersDisplayRows);
}

// Register the documented test callbacks as one focused vertical-slice suite.
describe("bonus items wizard compiler", registerBonusItemsTests);
