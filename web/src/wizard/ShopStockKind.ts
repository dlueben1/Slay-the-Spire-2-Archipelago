/**
 * @file Defines the Random Shop Relics and Potions kinds used by Checks & Rewards.
 *
 * Each kind has one generated weight option with the same four choices as the filler
 * weights (`none`, `low`, `medium`, `high`), so the answers reuse the filler slider
 * levels. The generated catalog stays authoritative for defaults and descriptions.
 */

import type { OptionCatalog } from "../generated/optionCatalog";
import { weightNameToLevel } from "./FillerItem";
import type {
  FillerWeightLevel,
  ShopPotionKindId,
  ShopRelicKindId,
} from "./WizardAnswers";

export interface ShopStockKindDefinition<KindId extends string> {
  id: KindId;
  optionKey: string;
}

export interface ShopStockKindDisplayItem<
  KindId extends string,
> extends ShopStockKindDefinition<KindId> {
  name: string;
  description: string;
}

/** Stable semantic-to-technical mapping, in the APWorld's option order. */
export const SHOP_RELIC_KIND_DEFINITIONS: readonly ShopStockKindDefinition<ShopRelicKindId>[] =
  [
    { id: "common", optionKey: "shop_relic_common_weight" },
    { id: "uncommon", optionKey: "shop_relic_uncommon_weight" },
    { id: "rare", optionKey: "shop_relic_rare_weight" },
    { id: "shop", optionKey: "shop_relic_shop_weight" },
    { id: "event", optionKey: "shop_relic_event_weight" },
    { id: "ancient", optionKey: "shop_relic_ancient_weight" },
  ];

/** Stable semantic-to-technical mapping, in the APWorld's option order. */
export const SHOP_POTION_KIND_DEFINITIONS: readonly ShopStockKindDefinition<ShopPotionKindId>[] =
  [
    { id: "common", optionKey: "shop_potion_common_weight" },
    { id: "uncommon", optionKey: "shop_potion_uncommon_weight" },
    { id: "rare", optionKey: "shop_potion_rare_weight" },
    { id: "otherClass", optionKey: "shop_potion_other_class_weight" },
    { id: "event", optionKey: "shop_potion_event_weight" },
  ];

/**
 * Reads a required generated option entry.
 *
 * @param catalog - Generated option catalog.
 * @param optionKey - Shop weight option to look up.
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
 * Creates schema-derived initial weights for every kind in a family.
 *
 * @param definitions - The relic or potion kinds.
 * @param catalog - Generated option catalog containing the weight defaults.
 * @returns One slider level per kind.
 */
export function createDefaultShopStockWeights<KindId extends string>(
  definitions: readonly ShopStockKindDefinition<KindId>[],
  catalog: OptionCatalog,
): Record<KindId, FillerWeightLevel> {
  const weights = {} as Record<KindId, FillerWeightLevel>;
  for (const definition of definitions) {
    weights[definition.id] = weightNameToLevel(
      getOption(catalog, definition.optionKey).default,
      definition.optionKey,
    );
  }
  return weights;
}

/**
 * Builds the weight table rows: "Shop Relic Ancient Weight" is shown as "Ancient".
 *
 * @param definitions - The relic or potion kinds.
 * @param catalog - Generated option catalog containing labels and descriptions.
 * @returns Ordered display rows keyed by semantic kind.
 */
export function createShopStockDisplayItems<KindId extends string>(
  definitions: readonly ShopStockKindDefinition<KindId>[],
  catalog: OptionCatalog,
): ShopStockKindDisplayItem<KindId>[] {
  return definitions.map((definition) => {
    const option = getOption(catalog, definition.optionKey);
    return {
      ...definition,
      name: option.display_name
        .replace(/^Shop (Relic|Potion) /, "")
        .replace(/ Weight$/, ""),
      description: option.description,
    };
  });
}
