/**
 * @file Translates Bonus Item answers into the generated `bonus_items` option.
 *
 * Each configured row becomes one ordered entry keyed by its bonus item type, with
 * exactly one selector: a specific `Value` relic ID or a non-empty `Pools` list.
 * The Python `BonusItems` schema accepts that shape verbatim, so this compiler only
 * needs to validate semantic answers against the relic source data and preserve the
 * player's row order. Capacity against filler slots is deliberately not checked;
 * generation-time enforcement lives in the Python world.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { isBonusRelicPool, isEligibleSpecificRelicId } from "../BonusRelicData";
import type { BonusItemAnswer } from "../WizardAnswers";
import { BONUS_ITEM_OPTION_KEY } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/** The generated `bonus_items` value: one single-key mapping per configured row. */
type CompiledBonusItems = Record<string, Record<string, string | string[]>>[];

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
): Record<string, Record<string, string | string[]>> {
  const context = `Bonus Item ${index + 1}`;

  if (item.kind !== "WAX_RELIC") {
    throw new Error(`${context} uses unsupported type '${String(item.kind)}'.`);
  }

  if (item.mode === "specific") {
    if (!isEligibleSpecificRelicId(item.relicId)) {
      throw new Error(
        `${context} selects '${item.relicId}', which is not an eligible Wax Relic. It may be blacklisted, a pickup effect, or outside the valid pools.`,
      );
    }

    return { WAX_RELIC: { Value: item.relicId } };
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

    return { WAX_RELIC: { Pools: [...item.pools] } };
  }

  throw new Error(
    `${context} must be either a specific relic or a randomized pool selection.`,
  );
}

/**
 * Applies player-facing Bonus Item answers to the generated `bonus_items` option.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Ordered Bonus Item answers from the Checks & Rewards table.
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
