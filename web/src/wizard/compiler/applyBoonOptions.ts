/**
 * @file Translates Boons answers into the `boons` set and the Blitz merchant chance.
 *
 * Both make runs easier and logic accounts for neither. Unlike Challenges they need
 * no opt-in, so the answers always compile as they are.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { BOONS } from "../Boon";
import { getOrderedOptionNames } from "../ModifierTableRow";
import type { BoonAnswers } from "../WizardAnswers";
import { BOON_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";
import { requireAcceptedNames } from "./requireAcceptedNames";

/**
 * Applies player-facing Boons answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Selected Boons and the Blitz merchant percentage.
 * @param catalog - Generated schema used to detect missing options and Boon names.
 * @returns Nothing; replaces only options declared in `BOON_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option or accepts a
 * selected Boon.
 */
export function applyBoonOptions(
  target: CompiledOptions,
  answers: BoonAnswers,
  catalog: OptionCatalog,
): void {
  for (const optionKey of Object.values(BOON_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  const boons = getOrderedOptionNames(BOONS, answers.selected);
  requireAcceptedNames(catalog, BOON_OPTION_KEYS.boons, boons);

  target[BOON_OPTION_KEYS.boons] = boons;
  target[BOON_OPTION_KEYS.blitzMerchantChance] = answers.blitzMerchantChance;
}
