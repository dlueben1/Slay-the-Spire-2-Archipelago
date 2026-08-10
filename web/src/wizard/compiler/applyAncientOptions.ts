/** Translates Ancient reward choices into their generated options. */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { AncientAnswers } from "../WizardAnswers";
import { ANCIENT_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/** Applies the Ancient subsection of Checks & Rewards to compiled options. */
export function applyAncientOptions(
  target: CompiledOptions,
  answers: AncientAnswers,
  catalog: OptionCatalog,
): void {
  for (const optionKey of Object.values(ANCIENT_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  target[ANCIENT_OPTION_KEYS.relicLocation] = answers.relicLocation;
  target[ANCIENT_OPTION_KEYS.relicPool] = answers.relicPool;
}
