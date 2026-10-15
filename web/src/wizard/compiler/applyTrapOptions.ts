import {
  TRAP_ITEM_DEFINITIONS,
  TRAP_WEIGHTS,
  type TrapAnswers,
} from "../TrapItem";
import { TRAP_CHANCE_OPTION_KEY } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

export function applyTrapOptions(
  target: CompiledOptions,
  answers: TrapAnswers,
): void {
  target[TRAP_CHANCE_OPTION_KEY] = answers.chance;
  let enabled = false;
  for (const item of TRAP_ITEM_DEFINITIONS) {
    const weight = answers.weights[item.id];
    if (!TRAP_WEIGHTS.includes(weight))
      throw new Error(`Invalid weight for '${item.id}'.`);
    target[item.optionKey] = weight;
    enabled ||= weight !== "none";
  }
  if (answers.chance > 0 && !enabled) {
    throw new Error("Enable at least one trap type or set Trap Chance to 0%.");
  }
}
