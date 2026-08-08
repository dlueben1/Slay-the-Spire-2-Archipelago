/**
 * @file Translates Character Setup answers into Archipelago character options.
 *
 * Section compilers answer "which option values express this player intent?" They may
 * map one friendly answer to several technical keys. This module owns exactly the five
 * Character Options fields and performs semantic checks that require understanding the
 * relationship between answers. Generic type/range/schema checks remain in
 * `wizard/validation/validateOptions.ts` and run after every section compiler finishes.
 */

import type { OptionCatalog, OptionValue } from "../../generated/optionCatalog";
import type { CharacterAnswers } from "../WizardAnswers";

export type CompiledOptions = Record<string, OptionValue>;

const KEYS = [
  "characters",
  "pick_num_characters",
  "num_chars_goal",
  "lock_characters",
  "unlocked_character",
] as const;

/**
 * Applies player-facing Character Setup answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object being assembled by the root compiler.
 * @param answers - Player-facing Character Setup answers to translate.
 * @param catalog - Generated schema used for character names and canonical choices.
 * @returns Nothing; the function replaces only the five keys listed in `KEYS`.
 * @throws When required schema entries are absent or the answers contradict each other.
 * @remarks Call only from the root compiler. Vue components must never invoke section
 * compilers or hold onto `target` as persistent state.
 */
export function applyCharacterOptions(
  target: CompiledOptions,
  answers: CharacterAnswers,
  catalog: OptionCatalog,
): void {
  // Fail immediately if schema drift removed an option owned by this compiler.
  for (const key of KEYS) {
    if (!catalog.options[key]) {
      throw new Error(`Generated option catalog is missing '${key}'.`);
    }
  }

  // Character Setup cannot represent an empty world.
  if (!answers.selectedCharacters.length) {
    throw new Error("Select at least one character.");
  }

  // Character names are always sourced and checked against generated valid keys.
  const available = catalog.options.characters!.valid_keys ?? [];
  const unknown: string[] = [];

  for (const character of answers.selectedCharacters) {
    if (!available.includes(character)) {
      unknown.push(character);
    }
  }

  if (unknown.length) {
    throw new Error(`Unknown character selection: ${unknown.join(", ")}.`);
  }

  // Resolve the actual number of generated characters for downstream checks.
  const count =
    answers.selectionMode === "random"
      ? answers.randomCharacterCount
      : answers.selectedCharacters.length;

  // Random selection cannot request zero or more characters than its source pool.
  if (count < 1 || count > answers.selectedCharacters.length) {
    throw new Error(
      "The random character count must be between 1 and the number selected.",
    );
  }

  // A numeric completion goal cannot exceed the characters that will be generated.
  if (answers.goal !== "all" && (answers.goal < 1 || answers.goal > count)) {
    throw new Error(
      "The goal count must be between 1 and the number of generated characters.",
    );
  }

  // Fixed unlock mode requires the otherwise-conditional starting answer.
  if (answers.availability === "fixed" && !answers.startingCharacter) {
    throw new Error("Choose a starting character.");
  }

  // Prevent stale fixed-start answers after a character is deselected.
  if (
    answers.startingCharacter &&
    !answers.selectedCharacters.includes(answers.startingCharacter)
  ) {
    throw new Error(
      "The starting character must be one of the selected characters.",
    );
  }

  // Convert the display name selected by the player to the canonical choice name.
  let fixedChoiceName: string | undefined;

  for (const choice of catalog.options.unlocked_character!.choices ?? []) {
    if (choice.display_name === answers.startingCharacter) {
      fixedChoiceName = choice.name;
      break;
    }
  }

  // Write the five canonical character values as one atomic compilation step.
  target.characters = [...answers.selectedCharacters];
  target.pick_num_characters = answers.selectionMode === "all" ? 0 : count;
  target.num_chars_goal = answers.goal === "all" ? 0 : answers.goal;
  target.lock_characters = {
    all: "unlocked",
    random: "locked_random",
    fixed: "locked_fixed",
  }[answers.availability];

  // Clear conditional technical values when their controlling mode is inactive.
  target.unlocked_character =
    answers.availability === "fixed"
      ? (fixedChoiceName ?? answers.startingCharacter ?? "")
      : "";
}
