/**
 * @file Translates Random Shop Relics and Potions answers into their switches and weights.
 *
 * Like Random Shop Cards, these change what a shop sells. They are independent of Shop
 * Sanity: they add no locations and work whether or not shop slots are shuffled.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { FILLER_WEIGHT_NAMES } from "../FillerItem";
import {
  SHOP_POTION_KIND_DEFINITIONS,
  SHOP_RELIC_KIND_DEFINITIONS,
  type ShopStockKindDefinition,
} from "../ShopStockKind";
import type {
  ChecksAndRewardsAnswers,
  FillerWeightLevel,
  ShopStockAnswers,
} from "../WizardAnswers";
import {
  SHOP_POTION_OPTION_KEY,
  SHOP_RELIC_OPTION_KEY,
} from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies one family's switch and every kind's weight.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param switchKey - Generated option holding the family's switch.
 * @param definitions - The family's kinds.
 * @param answers - The switch plus one slider level per kind.
 * @param catalog - Generated schema that must still accept each weight name.
 * @returns Nothing; replaces only the switch and the weight options.
 * @throws When an owned option is missing or a level has no generated choice.
 */
function applyShopStockFamily<KindId extends string>(
  target: CompiledOptions,
  switchKey: string,
  definitions: readonly ShopStockKindDefinition<KindId>[],
  answers: ShopStockAnswers<KindId>,
  catalog: OptionCatalog,
): void {
  if (!catalog.options[switchKey]) {
    throw new Error(`Generated option catalog is missing '${switchKey}'.`);
  }
  target[switchKey] = answers.enabled;

  // Weights are kept while the switch is off so turning it back on is lossless.
  for (const definition of definitions) {
    const level: FillerWeightLevel = answers.weights[definition.id];
    const choiceName = FILLER_WEIGHT_NAMES[level];
    const choices = catalog.options[definition.optionKey]?.choices ?? [];
    if (
      choiceName === undefined ||
      !choices.some((choice) => choice.name === choiceName)
    ) {
      throw new Error(
        `Shop weight '${definition.id}' level ${String(level)} has no generated choice in '${definition.optionKey}'.`,
      );
    }
    target[definition.optionKey] = choiceName;
  }
}

/**
 * Applies the Random Shop Relics and Random Shop Potions answers.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Both families' answers.
 * @param catalog - Generated schema that must still accept each weight name.
 * @returns Nothing; replaces only the switches and the weight options.
 */
export function applyShopStockOptions(
  target: CompiledOptions,
  answers: Pick<ChecksAndRewardsAnswers, "shopRelics" | "shopPotions">,
  catalog: OptionCatalog,
): void {
  applyShopStockFamily(
    target,
    SHOP_RELIC_OPTION_KEY,
    SHOP_RELIC_KIND_DEFINITIONS,
    answers.shopRelics,
    catalog,
  );
  applyShopStockFamily(
    target,
    SHOP_POTION_OPTION_KEY,
    SHOP_POTION_KIND_DEFINITIONS,
    answers.shopPotions,
    catalog,
  );
}
