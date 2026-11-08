/**
 * @file Defines the Random Shop Cards kinds used by the Run Settings step.
 *
 * Each kind has one generated weight option with the same four choices as the filler
 * weights (`none`, `low`, `medium`, `high`), so the answers reuse the filler slider
 * levels. The generated catalog stays authoritative for defaults and descriptions.
 */

import type { OptionCatalog } from "../generated/optionCatalog";
import { weightNameToLevel } from "./FillerItem";
import type { FillerWeightLevel, ShopCardKindId } from "./WizardAnswers";

export interface ShopCardKindDefinition {
  id: ShopCardKindId;
  optionKey: string;
}

export interface ShopCardKindDisplayItem extends ShopCardKindDefinition {
  name: string;
  description: string;
}

/** Stable semantic-to-technical mapping, in the APWorld's option order. */
export const SHOP_CARD_KIND_DEFINITIONS: readonly ShopCardKindDefinition[] = [
  { id: "attack", optionKey: "shop_attack_weight" },
  { id: "skill", optionKey: "shop_skill_weight" },
  { id: "power", optionKey: "shop_power_weight" },
  { id: "common", optionKey: "shop_common_weight" },
  { id: "uncommon", optionKey: "shop_uncommon_weight" },
  { id: "rare", optionKey: "shop_rare_weight" },
  { id: "colorless", optionKey: "shop_colorless_weight" },
];

/**
 * Reads a required generated option entry.
 *
 * @param catalog - Generated option catalog.
 * @param optionKey - Shop card weight option to look up.
 * @returns The generated option entry.
 * @throws When the option is missing from the catalog.
 */
function getOption(catalog: OptionCatalog, optionKey: string) {
  const option = catalog.options[optionKey];
  if (!option) {
    throw new Error(`Generated option catalog is missing '${optionKey}'.`);
  }
  return option;
}

/**
 * Creates schema-derived initial weights for every shop card kind.
 *
 * @param catalog - Generated option catalog containing the weight defaults.
 * @returns One slider level per kind.
 */
export function createDefaultShopCardWeights(
  catalog: OptionCatalog,
): Record<ShopCardKindId, FillerWeightLevel> {
  const weights = {} as Record<ShopCardKindId, FillerWeightLevel>;
  for (const definition of SHOP_CARD_KIND_DEFINITIONS) {
    weights[definition.id] = weightNameToLevel(
      getOption(catalog, definition.optionKey).default,
      definition.optionKey,
    );
  }
  return weights;
}

/**
 * Builds the weight table rows: "Shop Rare Weight" is shown as "Rare".
 *
 * @param catalog - Generated option catalog containing labels and descriptions.
 * @returns Ordered display rows keyed by semantic kind.
 */
export function createShopCardKindDisplayItems(
  catalog: OptionCatalog,
): ShopCardKindDisplayItem[] {
  return SHOP_CARD_KIND_DEFINITIONS.map((definition) => {
    const option = getOption(catalog, definition.optionKey);
    return {
      ...definition,
      name: option.display_name.replace(/^Shop /, "").replace(/ Weight$/, ""),
      description: option.description,
    };
  });
}
