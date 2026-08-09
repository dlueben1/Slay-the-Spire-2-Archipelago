/**
 * @file Translates Shop answers into generated shop location and pricing options.
 *
 * Shop Sanity is a dependent option family: enabling it exposes counts for four slot
 * types, an optional progressive card-removal unlock, and a price model. The Python
 * world disables Shop Sanity when all resulting shop locations equal zero; this
 * compiler reports that contradictory player intent instead of silently changing it.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ShopAnswers } from "../WizardAnswers";
import { SHOP_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies player-facing Shop answers to generated shop options.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing Shop Sanity, slot-count, removal, and cost choices.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `SHOP_OPTION_KEYS`.
 * @throws When an owned option is absent or enabled Shop Sanity creates no locations.
 */
export function applyShopOptions(
  target: CompiledOptions,
  answers: ShopAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this dependent section.
  for (const optionKey of Object.values(SHOP_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Count enabled slot locations exactly as the Python world's early setup does.
  const purchasableSlotCount =
    answers.cardSlots +
    answers.neutralCardSlots +
    answers.relicSlots +
    answers.potionSlots;
  const removalLocationCount = answers.removeSlots ? 3 : 0;

  // Reject a UI state that the Python world would silently turn back off.
  if (answers.enabled && purchasableSlotCount + removalLocationCount === 0) {
    throw new Error(
      "Enable at least one shop slot or progressive card-removal location.",
    );
  }

  // Preserve all dependent values so disabling and reenabling the section is lossless.
  target[SHOP_OPTION_KEYS.enabled] = answers.enabled;
  target[SHOP_OPTION_KEYS.cardSlots] = answers.cardSlots;
  target[SHOP_OPTION_KEYS.neutralCardSlots] = answers.neutralCardSlots;
  target[SHOP_OPTION_KEYS.relicSlots] = answers.relicSlots;
  target[SHOP_OPTION_KEYS.potionSlots] = answers.potionSlots;
  target[SHOP_OPTION_KEYS.removeSlots] = answers.removeSlots;
  target[SHOP_OPTION_KEYS.costs] = answers.costs;
}
