/**
 * @file Compiles the Shops step: Shop Sanity and the shop's stock.
 *
 * Shop Sanity adds locations; Random Shop Cards, Relics and Potions only change what a
 * shop sells and work with or without it. Each family keeps its own focused compiler.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ShopsAnswers } from "../WizardAnswers";
import type { CompiledOptions } from "./applyCharacterOptions";
import { applyShopCardOptions } from "./applyShopCardOptions";
import { applyShopOptions } from "./applyShopOptions";
import { applyShopStockOptions } from "./applyShopStockOptions";

/**
 * Applies every option owned by the Shops step.
 *
 * @param target - Mutable complete option snapshot being assembled by the root compiler.
 * @param answers - Shop Sanity and shop stock intent.
 * @param catalog - Generated schema used by each focused option-family compiler.
 * @returns Nothing; the supplied option snapshot is updated in place.
 */
export function applyShopsOptions(
  target: CompiledOptions,
  answers: ShopsAnswers,
  catalog: OptionCatalog,
): void {
  applyShopOptions(target, answers.shop, catalog);
  applyShopCardOptions(target, answers.shopCards, catalog);
  applyShopStockOptions(target, answers, catalog);
}
