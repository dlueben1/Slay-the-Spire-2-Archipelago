/**
 * @file Presentation model for configured Bonus Items.
 *
 * Maps the semantic `BonusItemAnswer` state to the image/name/details triple used by
 * the Checks & Rewards table and the Review list, so both renderers stay in sync and
 * never duplicate relic lookups.
 */

import {
  getPoolsForRelic,
  getRandomWaxRelicImageUrl,
  getRelicById,
  RANDOM_WAX_RELIC_NAME,
} from "./BonusRelicData";
import type { BonusItemAnswer } from "./WizardAnswers";

/** One renderable Bonus Item row. */
export interface BonusItemDisplayRow {
  imageUrl: string;
  name: string;
  details: string;
}

/**
 * Builds the display row for one configured Bonus Item.
 *
 * @param item - Semantic bonus item answer from wizard state.
 * @returns Image, name, and detail text for table and review rendering.
 * @throws When a specific Wax Relic references an unknown relic ID.
 */
export function getBonusItemDisplayRow(
  item: BonusItemAnswer,
): BonusItemDisplayRow {
  if (item.kind === "WAX_RELIC" && item.mode === "specific") {
    const relic = getRelicById(item.relicId);
    const pools = getPoolsForRelic(item.relicId);

    return {
      imageUrl: relic.imageUrl,
      name: `Wax ${relic.name}`,
      details: pools.length ? pools.join(", ") : relic.rarityKey,
    };
  }

  if (item.kind === "WAX_RELIC" && item.mode === "random") {
    return {
      imageUrl: getRandomWaxRelicImageUrl(),
      name: RANDOM_WAX_RELIC_NAME,
      details: item.pools.join(", "),
    };
  }

  // Defensive: the discriminated union is exhaustive today, but corrupted state or a
  // future kind without a display mapping should fail loudly rather than render blank.
  throw new Error("Unsupported Bonus Item configuration.");
}

/** Builds display rows for every configured Bonus Item, preserving answer order. */
export function getBonusItemDisplayRows(
  items: readonly BonusItemAnswer[],
): BonusItemDisplayRow[] {
  return items.map(getBonusItemDisplayRow);
}
