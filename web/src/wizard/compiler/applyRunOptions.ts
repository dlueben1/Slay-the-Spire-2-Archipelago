/**
 * @file Translates Run Rules answers into generated relic, Neow, and seed options.
 *
 * The Run Rules step groups settings that change how an individual climb is offered
 * and rewarded without changing the selected character roster. This compiler owns the
 * Ancient reward timing and pool, Archipelago relic choice count, Neow Sanity,
 * seeded-run fields, progression balancing, and accessibility. Generic catalog
 * validation still runs after every section.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { RunAnswers } from "../WizardAnswers";
import { RUN_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies player-facing Run Rules answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing relic, Neow, and seed choices.
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
  target[RUN_OPTION_KEYS.ancientRelicLocation] = answers.ancientRelicLocation;
  target[RUN_OPTION_KEYS.ancientRelicPool] = answers.ancientRelicPool;
  target[RUN_OPTION_KEYS.relicChoiceCount] = answers.relicChoiceCount;
  target[RUN_OPTION_KEYS.neowSanity] = answers.neowSanity;
  target[RUN_OPTION_KEYS.seeded] = answers.seeded;
  target[RUN_OPTION_KEYS.progressionBalancing] = answers.progressionBalancing;
  target[RUN_OPTION_KEYS.accessibility] = answers.accessibility;
}
