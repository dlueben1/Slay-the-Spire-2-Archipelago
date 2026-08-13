/**
 * @file Translates Gameplay Modifiers answers into generated relic and seed options.
 *
 * The Gameplay Modifiers step groups settings that change how an individual climb is offered
 * and rewarded without changing the selected character roster. This compiler owns the
 * immediate Relic availability, victory release, and seeded-run fields. Generic
 * catalog validation still runs after every section. The removed relic-choice-count
 * mapping remains commented pending clarification from the Python-world maintainers.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { RunAnswers } from "../WizardAnswers";
import { RUN_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies player-facing Gameplay Modifiers answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing Relic availability, victory, and seed choices.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `RUN_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option.
 */
export function applyRunOptions(
  target: CompiledOptions,
  answers: RunAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this section.
  for (const optionKey of Object.values(RUN_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Map semantic answers to the canonical generated fields owned by this section.
  // TODO: Restore or remove this assignment after collaborators confirm whether
  // `relic_choice_count` will return to the generated schema.
  // target[RUN_OPTION_KEYS.relicChoiceCount] = answers.relicChoiceCount;
  target[RUN_OPTION_KEYS.relicRewardsAvailableAnytime] =
    answers.relicRewardsAvailableAnytime;
  target[RUN_OPTION_KEYS.releaseOnVictory] = answers.releaseOnVictory;
  target[RUN_OPTION_KEYS.seeded] = answers.seeded;
}
