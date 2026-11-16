/**
 * @file Presentation model for configured Bonus Items.
 *
 * Maps the semantic `BonusItemAnswer` state to the image/name/details triple used by
 * the Item Pool table and the Review list, so both renderers stay in sync and
 * never duplicate relic lookups.
 */

import {
  findBonusEnchantment,
  getRandomEnchantmentImageUrl,
} from "./BonusEnchantmentData";
import {
  getRandomWaxRelicImageUrl,
  getRelicById,
  RANDOM_WAX_RELIC_NAME,
} from "./BonusRelicData";
import {
  BONUS_CARDS_IMAGE_RELIC_ID,
  describeBonusCardPool,
  parseBonusCardPool,
} from "./BonusCardData";
import { isEnchantmentBonusItem, type BonusItemAnswer } from "./WizardAnswers";

/** One renderable Bonus Item row. */
export interface BonusItemDisplayRow {
  imageUrl: string;
  name: string;
  details: string;
}

/** Relic whose art stands for a Max HP row, as in the game's reward menu. */
const MAX_HP_IMAGE_RELIC_ID = "STRAWBERRY";

/**
 * Builds the display row for one configured Bonus Item.
 *
 * @param item - Semantic bonus item answer from wizard state.
 * @returns Image, name, and detail text for table and review rendering.
 * @throws When a specific row references an unknown relic or enchantment.
 */
export function getBonusItemDisplayRow(
  item: BonusItemAnswer,
): BonusItemDisplayRow {
  if (item.kind === "MAX_HP") {
    return {
      imageUrl: getRelicById(MAX_HP_IMAGE_RELIC_ID).imageUrl,
      name: `+${item.amount} Max HP`,
      details: "Raises max HP and current HP when claimed.",
    };
  }

  if (isEnchantmentBonusItem(item)) {
    const what =
      item.kind === "ENCHANTMENT" ? "Enchant a card" : "Enchanted card reward";
    if (item.mode === "specific") {
      const enchantment = findBonusEnchantment(item.enchantment);
      if (!enchantment) {
        throw new Error(`Unknown bonus enchantment '${item.enchantment}'.`);
      }
      return {
        imageUrl: enchantment.imageUrl,
        name: `${what}: ${enchantment.displayName}`,
        details: enchantment.description,
      };
    }
    return {
      imageUrl: getRandomEnchantmentImageUrl(),
      name: `${what}: random`,
      details: `From tiers: ${item.tiers.join(", ")}`,
    };
  }

  if (item.kind === "CARDS") {
    const imageUrl = getRelicById(BONUS_CARDS_IMAGE_RELIC_ID).imageUrl;
    if (item.mode === "specific") {
      return {
        imageUrl,
        name: `Card: ${item.card}`,
        details: "A card reward offering this one card.",
      };
    }
    const pools = item.pools.map((pool) => {
      const parts = parseBonusCardPool(pool);
      if ("error" in parts) {
        throw new Error(`Invalid bonus card pool '${pool}'.`);
      }
      return describeBonusCardPool(parts);
    });
    return {
      imageUrl,
      name: "Card reward",
      details: `Choose 1 of 3 from: ${pools.join(", ")}`,
    };
  }

  const prefix = item.kind === "WAX_RELIC" ? "Wax " : "";
  if (item.mode === "specific") {
    const relic = getRelicById(item.relicId);

    return {
      imageUrl: relic.imageUrl,
      name: `${prefix}${relic.name}`,
      details: relic.description,
    };
  }

  if (item.mode === "random") {
    return {
      imageUrl: getRandomWaxRelicImageUrl(),
      name: item.kind === "WAX_RELIC" ? RANDOM_WAX_RELIC_NAME : "Random Relic",
      details: `From Pools: ${item.pools.join(", ")}`,
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
