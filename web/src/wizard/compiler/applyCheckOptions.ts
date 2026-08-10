/**
 * @file Translates Checks & Rewards answers into generated sanity options.
 *
 * These toggles decide which ordinary game events become Archipelago locations and
 * rewards. Neow, floor, campfire, gold, potion, and card-reward behavior is kept together so
 * maintainers can compare this mapping directly with location and item creation in the
 * Python world. Cross-section defaults remain the root compiler's responsibility.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { CheckAnswers } from "../WizardAnswers";
import { CHECK_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies player-facing check and reward toggles to generated options.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing decisions about shuffled checks and rewards.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `CHECK_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option.
 */
export function applyCheckOptions(
  target: CompiledOptions,
  answers: CheckAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this section.
  for (const optionKey of Object.values(CHECK_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Write each independent toggle without coupling UI state to Python option names.
  target[CHECK_OPTION_KEYS.neowSanity] = answers.neowSanity;
  target[CHECK_OPTION_KEYS.includeFloorChecks] = answers.includeFloorChecks;
  target[CHECK_OPTION_KEYS.campfireSanity] = answers.campfireSanity;
  target[CHECK_OPTION_KEYS.goldSanity] = answers.goldSanity;
  target[CHECK_OPTION_KEYS.potionSanity] = answers.potionSanity;
  target[CHECK_OPTION_KEYS.shuffleAllCards] = answers.shuffleAllCards;
}
