/**
 * @file Translates Bonus Item answers into the generated `bonus_items` option.
 *
 * Each configured row becomes one ordered entry keyed by its bonus item type, with
 * exactly one selector: a specific `Value` (relic ID, enchantment name or Max HP amount)
 * or a non-empty `Pools` list (relic pools or enchantment tiers).
 * The Python `BonusItems` schema accepts that shape verbatim, so this compiler only
 * needs to validate semantic answers against the relic source data and preserve the
 * player's row order. Capacity against filler slots is deliberately not checked;
 * generation-time enforcement lives in the Python world.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import {
  findBonusEnchantment,
  isBonusEnchantmentTier,
} from "../BonusEnchantmentData";
import { formatBonusCardPool, parseBonusCardPool } from "../BonusCardData";
import { isBonusRelicPool, isEligibleSpecificRelicId } from "../BonusRelicData";
import {
  isEnchantmentBonusItem,
  type BonusItemAnswer,
} from "../WizardAnswers";
import { BONUS_ITEM_OPTION_KEY } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/** The generated `bonus_items` value: one single-key mapping per configured row. */
type CompiledBonusItem = Record<string, Record<string, string | number | string[]>>;
type CompiledBonusItems = CompiledBonusItem[];

/** Max HP a new Max HP row starts with. */
export const BONUS_MAX_HP_DEFAULT = 6;
/** The largest Max HP bonus the APWorld accepts (`BONUS_MAX_HP_LIMIT` in items.py). */
export const BONUS_MAX_HP_LIMIT = 50;

/**
 * Validates and converts an enchantment row.
 *
 * @throws When the row names an unknown enchantment or tier.
 */
function compileEnchantmentItem(
  item: Extract<BonusItemAnswer, { kind: "ENCHANTMENT" | "ENCHANTED_CARDS" }>,
  context: string,
): CompiledBonusItem {
  if (item.mode === "specific") {
    const enchantment = findBonusEnchantment(item.enchantment);
    if (!enchantment) {
      throw new Error(
        `${context} selects '${item.enchantment}', which is not an enchantment bonus items can give.`,
      );
    }
    return { [item.kind]: { Value: enchantment.name } };
  }

  if (!Array.isArray(item.tiers) || item.tiers.length === 0) {
    throw new Error(`${context} must select at least one enchantment tier.`);
  }
  const seenTiers = new Set<string>();
  for (const tier of item.tiers) {
    if (!isBonusEnchantmentTier(tier)) {
      throw new Error(`${context} selects unknown enchantment tier '${tier}'.`);
    }
    if (seenTiers.has(tier)) {
      throw new Error(`${context} selects tier '${tier}' more than once.`);
    }
    seenTiers.add(tier);
  }
  return { [item.kind]: { Pools: [...item.tiers] } };
}

/**
 * Validates and converts a Bonus Cards row.
 *
 * @throws When the row names no card or has an invalid or repeated pool.
 */
function compileCardsItem(
  item: Extract<BonusItemAnswer, { kind: "CARDS" }>,
  context: string,
): CompiledBonusItem {
  if (item.mode === "specific") {
    const card = item.card.trim();
    if (!card) {
      throw new Error(`${context} must name a card.`);
    }
    return { CARDS: { Value: card } };
  }

  if (!Array.isArray(item.pools) || item.pools.length === 0) {
    throw new Error(`${context} must have at least one card pool.`);
  }
  const pools: string[] = [];
  for (const pool of item.pools) {
    const parts = parseBonusCardPool(pool);
    if ("error" in parts) {
      throw new Error(`${context} has card pool '${pool}', which is not valid: ${parts.error}.`);
    }
    const canonical = formatBonusCardPool(parts);
    if (pools.includes(canonical)) {
      throw new Error(`${context} has card pool '${canonical}' more than once.`);
    }
    pools.push(canonical);
  }
  return { CARDS: { Pools: pools } };
}

/**
 * Validates and converts one semantic Bonus Item answer.
 *
 * @param item - Player-facing Bonus Item answer to compile.
 * @param index - Zero-based row position used in error messages.
 * @returns The ordered single-key mapping matching the Python option schema.
 * @throws When the row is malformed, ineligible, or references unknown source data.
 */
function compileBonusItem(
  item: BonusItemAnswer,
  index: number,
): CompiledBonusItem {
  const context = `Bonus Item ${index + 1}`;

  if (item.kind === "MAX_HP") {
    if (
      !Number.isInteger(item.amount) ||
      item.amount < 1 ||
      item.amount > BONUS_MAX_HP_LIMIT
    ) {
      throw new Error(
        `${context} must give between 1 and ${BONUS_MAX_HP_LIMIT} Max HP.`,
      );
    }
    return { MAX_HP: { Value: item.amount } };
  }

  if (isEnchantmentBonusItem(item)) {
    return compileEnchantmentItem(item, context);
  }

  if (item.kind === "CARDS") {
    return compileCardsItem(item, context);
  }

  if (item.kind !== "WAX_RELIC" && item.kind !== "RELIC") {
    throw new Error(
      `${context} uses unsupported type '${String((item as BonusItemAnswer).kind)}'.`,
    );
  }

  if (item.mode === "specific") {
    if (!isEligibleSpecificRelicId(item.relicId, item.kind)) {
      throw new Error(
        `${context} selects '${item.relicId}', which is not an eligible bonus relic. It may be a pickup effect on a Wax Relic, or outside the valid pools.`,
      );
    }

    return { [item.kind]: { Value: item.relicId } };
  }

  if (item.mode === "random") {
    if (!Array.isArray(item.pools) || item.pools.length === 0) {
      throw new Error(`${context} must select at least one relic pool.`);
    }

    const seenPools = new Set<string>();
    for (const pool of item.pools) {
      if (!isBonusRelicPool(pool)) {
        throw new Error(`${context} selects unknown relic pool '${pool}'.`);
      }
      if (seenPools.has(pool)) {
        throw new Error(`${context} selects pool '${pool}' more than once.`);
      }
      seenPools.add(pool);
    }

    return { [item.kind]: { Pools: [...item.pools] } };
  }

  throw new Error(
    `${context} must be either a specific relic or a randomized pool selection.`,
  );
}

/**
 * Applies player-facing Bonus Item answers to the generated `bonus_items` option.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Ordered Bonus Item answers from the Item Pool table.
 * @param catalog - Generated schema used to detect missing or drifted options.
 * @returns Nothing; replaces the `bonus_items` entry in the supplied snapshot.
 * @throws When the generated option is missing, has the wrong kind, or any row is invalid.
 */
export function applyBonusItemOptions(
  target: CompiledOptions,
  answers: BonusItemAnswer[],
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing anything for this section.
  const option = catalog.options[BONUS_ITEM_OPTION_KEY];
  if (!option) {
    throw new Error(
      `Generated option catalog is missing '${BONUS_ITEM_OPTION_KEY}'.`,
    );
  }
  if (option.kind !== "list") {
    throw new Error(
      `Generated option '${BONUS_ITEM_OPTION_KEY}' must be a list, got '${option.kind}'.`,
    );
  }

  const compiled: CompiledBonusItems = answers.map(compileBonusItem);
  target[BONUS_ITEM_OPTION_KEY] = compiled;
}
