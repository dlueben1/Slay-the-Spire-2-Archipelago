/**
 * @file Translates Death Link answers into generated multiplayer death options.
 *
 * Death Link itself controls whether deaths are shared. The wizard presents received
 * fragments, percentage damage, and immediate death as separate concepts even though
 * Python represents the last two with one 0-100 damage option. This compiler owns that
 * translation: no damage becomes 0, configured damage keeps its percentage, and lethal
 * mode becomes 100 while suppressing the mutually exclusive fragment effect.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { DeathLinkAnswers } from "../WizardAnswers";
import { DEATH_LINK_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies player-facing Death Link answers to generated options.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing Death Link toggle, fragment, and damage choices.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `DEATH_LINK_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option.
 */
export function applyDeathLinkOptions(
  target: CompiledOptions,
  answers: DeathLinkAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this section.
  for (const optionKey of Object.values(DEATH_LINK_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Validate the semantic slider only while its nonlethal damage mode is active.
  if (
    answers.receiveDamage &&
    !answers.beKilled &&
    (!Number.isInteger(answers.damagePercent) ||
      answers.damagePercent < 1 ||
      answers.damagePercent > 100)
  ) {
    throw new Error("Death Link damage must be a whole number from 1 to 100.");
  }

  // Lethal mode maps to Python's documented 100% damage representation.
  const compiledDamagePercent = answers.beKilled
    ? 100
    : answers.receiveDamage
      ? answers.damagePercent
      : 0;

  // Store all preferences even while Death Link is disabled for lossless toggling.
  target[DEATH_LINK_OPTION_KEYS.enabled] = answers.enabled;
  target[DEATH_LINK_OPTION_KEYS.enableFragments] =
    answers.receiveFragment && !answers.beKilled;
  target[DEATH_LINK_OPTION_KEYS.damagePercent] = compiledDamagePercent;
}
