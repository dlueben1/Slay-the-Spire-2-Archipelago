/** Translates Progression answers into common Archipelago options. */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ProgressionAnswers } from "../WizardAnswers";
import { PROGRESSION_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/** Applies the Progression step to compiled options. */
export function applyProgressionOptions(
  target: CompiledOptions,
  answers: ProgressionAnswers,
  catalog: OptionCatalog,
): void {
  for (const optionKey of Object.values(PROGRESSION_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  target[PROGRESSION_OPTION_KEYS.progressionBalancing] =
    answers.progressionBalancing;
  target[PROGRESSION_OPTION_KEYS.accessibility] = answers.accessibility;
}
