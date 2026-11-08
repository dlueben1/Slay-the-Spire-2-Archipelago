/**
 * @file Translates Random Shop Cards answers into the switch and its seven weights.
 *
 * Random Shop Cards changes which cards a shop sells. It is independent of Shop Sanity:
 * it adds no locations and works whether or not shop slots are shuffled. The weights
 * reuse the filler slider levels, so they compile to the same four choice names.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { FILLER_WEIGHT_NAMES } from "../FillerItem";
import { SHOP_CARD_KIND_DEFINITIONS } from "../ShopCardKind";
import type { ShopCardAnswers } from "../WizardAnswers";
import { SHOP_CARD_OPTION_KEY } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies the Random Shop Cards switch and every kind's weight.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - The switch plus one slider level per card kind.
 * @param catalog - Generated schema that must still accept each weight name.
 * @returns Nothing; replaces only the switch and the weight options.
 * @throws When an owned option is missing or a level has no generated choice.
 */
export function applyShopCardOptions(
  target: CompiledOptions,
  answers: ShopCardAnswers,
  catalog: OptionCatalog,
): void {
  if (!catalog.options[SHOP_CARD_OPTION_KEY]) {
    throw new Error(
      `Generated option catalog is missing '${SHOP_CARD_OPTION_KEY}'.`,
    );
  }
  target[SHOP_CARD_OPTION_KEY] = answers.enabled;

  // Weights are kept while the switch is off so turning it back on is lossless.
  for (const definition of SHOP_CARD_KIND_DEFINITIONS) {
    const level = answers.weights[definition.id];
    const choiceName = FILLER_WEIGHT_NAMES[level];
    const choices = catalog.options[definition.optionKey]?.choices ?? [];
    if (
      choiceName === undefined ||
      !choices.some((choice) => choice.name === choiceName)
    ) {
      throw new Error(
        `Shop card weight '${definition.id}' level ${String(level)} has no generated choice in '${definition.optionKey}'.`,
      );
    }
    target[definition.optionKey] = choiceName;
  }
}
