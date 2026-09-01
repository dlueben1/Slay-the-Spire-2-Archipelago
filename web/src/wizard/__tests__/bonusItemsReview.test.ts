/**
 * @file Protects the Review step's Bonus Items summary and structured rows.
 *
 * These tests verify that the review projection mirrors the player's configured
 * Bonus Items in order, uses the Small Capsule placeholder for randomized Wax
 * Relics, and reports the empty state explicitly.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import {
  getRandomWaxRelicImageUrl,
  getRelicById,
  RANDOM_WAX_RELIC_NAME,
} from "../BonusRelicData";
import { createDefaultFillerAnswers } from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import {
  buildBonusItemReviewRows,
  buildWizardReviewSections,
  summarizeBonusItemAnswers,
} from "../review";

/**
 * Creates a complete answer model suitable for review tests.
 *
 * @returns Wizard answers using generated defaults with an empty Bonus Items list.
 */
function createTestAnswers() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);
  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers, optionCatalog);
}

/**
 * Verifies the empty Bonus Items state reads explicitly and yields no rows.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function summarizesEmptyBonusItems(): void {
  expect(summarizeBonusItemAnswers([])).toBe(
    "No Bonus Items are added to the item pool.",
  );
  expect(buildBonusItemReviewRows([])).toEqual([]);
}

/**
 * Verifies configured rows project in order with the right imagery per mode.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function projectsConfiguredRowsInOrder(): void {
  const specificRelic = getRelicById("FAKE_STRIKE_DUMMY");
  const rows = buildBonusItemReviewRows([
    { kind: "WAX_RELIC", mode: "random", pools: ["Common", "Rare"] },
    { kind: "WAX_RELIC", mode: "specific", relicId: "FAKE_STRIKE_DUMMY" },
  ]);

  // The randomized row uses the placeholder image and lists its pools.
  expect(rows).toHaveLength(2);
  expect(rows[0]).toEqual({
    imageUrl: getRandomWaxRelicImageUrl(),
    name: RANDOM_WAX_RELIC_NAME,
    details: "From Pools: Common, Rare",
  });

  // The specific row uses the chosen relic's own image, name, and description.
  expect(rows[1]).toEqual({
    imageUrl: specificRelic.imageUrl,
    name: `Wax ${specificRelic.name}`,
    details: specificRelic.description,
  });
}

/**
 * Verifies the singular and plural summary wording.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function summarizesConfiguredCounts(): void {
  expect(
    summarizeBonusItemAnswers([
      { kind: "WAX_RELIC", mode: "specific", relicId: "AKABEKO" },
    ]),
  ).toBe("one Bonus Item is added to the item pool before any filler.");

  expect(
    summarizeBonusItemAnswers([
      { kind: "WAX_RELIC", mode: "specific", relicId: "AKABEKO" },
      { kind: "WAX_RELIC", mode: "random", pools: ["Shop"] },
    ]),
  ).toBe("two Bonus Items are added to the item pool before any filler.");
}

/**
 * Verifies the Checks & Rewards review section carries the structured rows.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function attachesRowsToChecksAndRewardsSection(): void {
  const answers = createTestAnswers();
  answers.checksAndRewards.bonusItems = [
    { kind: "WAX_RELIC", mode: "specific", relicId: "FAKE_STRIKE_DUMMY" },
  ];

  const sections = buildWizardReviewSections(answers);
  const checksSection = sections.find(
    (section) => section.title === "Checks & Rewards",
  );

  // The combined section must mention the configured count and list the row.
  expect(checksSection?.summary).toContain(
    "one Bonus Item is added to the item pool",
  );
  expect(checksSection?.items).toHaveLength(1);
  expect(checksSection?.items?.[0]?.name).toBe(
    `Wax ${getRelicById("FAKE_STRIKE_DUMMY").name}`,
  );

  // Other sections never carry structured item rows.
  for (const section of sections) {
    if (section.title !== "Checks & Rewards") {
      expect(section.items ?? []).toEqual([]);
    }
  }
}

/**
 * Registers Bonus Items review cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerBonusItemsReviewTests(): void {
  it("summarizes the empty state explicitly", summarizesEmptyBonusItems);
  it("projects configured rows in order", projectsConfiguredRowsInOrder);
  it("summarizes singular and plural counts", summarizesConfiguredCounts);
  it(
    "attaches rows to the Checks & Rewards section",
    attachesRowsToChecksAndRewardsSection,
  );
}

// Register the documented test callbacks as one focused review suite.
describe("bonus items review", registerBonusItemsReviewTests);
