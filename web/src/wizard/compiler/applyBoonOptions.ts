/**
 * @file Translates Boons answers into the `boons` set and the Blitz merchant chance.
 *
 * Both make runs easier and logic accounts for neither. Unlike Challenges they need
 * no opt-in, so the answers always compile as they are.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { getOrderedBoons } from "../Boon";
import type { BoonAnswers } from "../WizardAnswers";
import { BOON_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

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

  // A renamed Boon must fail here rather than in Archipelago's generator.
  const validBoons = catalog.options[BOON_OPTION_KEYS.boons]!.valid_keys ?? [];
  const boons = getOrderedBoons(answers.selected);

  for (const boon of boons) {
    if (!validBoons.includes(boon)) {
      throw new Error(
        `Generated option '${BOON_OPTION_KEYS.boons}' no longer accepts '${boon}'.`,
      );
    }
  }

  target[BOON_OPTION_KEYS.boons] = boons;
  target[BOON_OPTION_KEYS.blitzMerchantChance] = answers.blitzMerchantChance;
}
