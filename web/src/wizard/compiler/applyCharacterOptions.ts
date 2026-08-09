/**
 * @file Translates Character Setup answers into Archipelago character options.
 *
 * Character Setup deliberately hides Archipelago's competing basic and advanced
 * character systems. This compiler validates one unified roster, applies the questions
 * shared by both systems, and then writes either the global Ascension arrays or the
 * per-character dictionary. Ignored fields are cleared so generated YAML cannot imply
 * that both systems are active. Generic schema checks remain in the validation layer.
 */

import type { OptionCatalog, OptionValue } from "../../generated/optionCatalog";
import {
  getAscensionModifier,
  isAscensionLevel,
  type AscensionLevel,
} from "../AscensionModifier";
import {
  getConfiguredCharacterNames,
  getNamedModdedCharacters,
  MAX_MODDED_CHARACTERS,
} from "../CharacterRoster";
import type {
  AscensionConfigurationAnswers,
  CharacterAnswers,
} from "../WizardAnswers";
import { CHARACTER_OPTION_KEYS } from "../WizardOptionKey";

export type CompiledOptions = Record<string, OptionValue>;

interface CompiledAscensionConfiguration {
  ascension: string[];
  ascension_down: string[];
}

/**
 * Compiles explicit Ascension checkbox levels to generated canonical names.
 *
 * @param configuration - Player-facing enabled and Ascension Down selections.
 * @param catalog - Generated schema used to verify accepted set members.
 * @returns Explicit named arrays for standard or advanced character YAML.
 * @throws When levels are invalid, an Ascension Down is not enabled, or schema drift
 * removes one of the hand-authored canonical option names.
 */
function compileAscensionConfiguration(
  configuration: AscensionConfigurationAnswers,
  catalog: OptionCatalog,
): CompiledAscensionConfiguration {
  // Generated standard options define the accepted names used by both YAML modes.
  const ascensionOption = catalog.options.ascension;
  const ascensionDownOption = catalog.options.ascension_down;

  if (!ascensionOption || !ascensionDownOption) {
    throw new Error(
      "Generated Ascension options are missing from the catalog.",
    );
  }

  const validAscensions = new Set(ascensionOption.valid_keys ?? []);
  const validAscensionDowns = new Set(ascensionDownOption.valid_keys ?? []);
  const enabledLevels = new Set<AscensionLevel>();
  const downLevels = new Set<AscensionLevel>();

  // Validate and deduplicate every enabled checkbox before translating names.
  for (const level of configuration.enabled) {
    if (!isAscensionLevel(level)) {
      throw new Error(
        `Ascension level '${String(level)}' must be from 1 through 10.`,
      );
    }

    enabledLevels.add(level);
  }

  // Ascension Downs can disable only modifiers that are part of the same setup.
  for (const level of configuration.downs) {
    if (!isAscensionLevel(level)) {
      throw new Error(
        `Ascension Down level '${String(level)}' must be from 1 through 10.`,
      );
    }

    if (!enabledLevels.has(level)) {
      throw new Error(
        `Ascension Down A${level} requires Ascension A${level} to be enabled.`,
      );
    }

    downLevels.add(level);
  }

  // Emit names in level order regardless of the order controls were toggled.
  const ascension: string[] = [];
  const ascensionDown: string[] = [];

  for (let level = 1; level <= 10; level += 1) {
    const ascensionLevel = level as AscensionLevel;
    const modifier = getAscensionModifier(ascensionLevel);

    if (!validAscensions.has(modifier.optionName)) {
      throw new Error(
        `Generated Ascension does not accept '${modifier.optionName}'.`,
      );
    }

    if (!validAscensionDowns.has(modifier.optionName)) {
      throw new Error(
        `Generated Ascension Down does not accept '${modifier.optionName}'.`,
      );
    }

    if (enabledLevels.has(ascensionLevel)) {
      ascension.push(modifier.optionName);
    }

    if (downLevels.has(ascensionLevel)) {
      ascensionDown.push(modifier.optionName);
    }
  }

  return {
    ascension,
    ascension_down: ascensionDown,
  };
}

/**
 * Validates the built-in and modded entries that form the unified character roster.
 *
 * @param answers - Player-facing Character Setup answers to inspect.
 * @param catalog - Generated schema containing valid built-in character names.
 * @returns The complete ordered roster after trimming modded character IDs.
 * @throws When the roster is empty, incomplete, duplicated, unknown, or exceeds the
 * world's five-modded-character limit.
 */
function validateCharacterRoster(
  answers: CharacterAnswers,
  catalog: OptionCatalog,
): string[] {
  // Character Setup cannot represent an empty world.
  if (!answers.selectedCharacters.length && !answers.moddedCharacters.length) {
    throw new Error("Select at least one character.");
  }

  // Modded rows have a hard world limit and must all contain complete internal IDs.
  if (answers.moddedCharacters.length > MAX_MODDED_CHARACTERS) {
    throw new Error(
      `Configure no more than ${MAX_MODDED_CHARACTERS} modded characters.`,
    );
  }

  for (const moddedCharacter of answers.moddedCharacters) {
    if (!moddedCharacter.name.trim()) {
      throw new Error("Enter an internal ID for every modded character row.");
    }
  }

  // Built-in names remain strictly constrained by the generated character set.
  const available = catalog.options.characters?.valid_keys ?? [];
  const unknown: string[] = [];

  for (const character of answers.selectedCharacters) {
    if (!available.includes(character)) {
      unknown.push(character);
    }
  }

  if (unknown.length) {
    throw new Error(`Unknown character selection: ${unknown.join(", ")}.`);
  }

  // Archipelago rejects duplicate advanced dictionary keys and duplicate world names.
  const roster = getConfiguredCharacterNames(answers);
  const normalizedNames = new Set<string>();

  for (const character of roster) {
    const normalizedName = character.toLowerCase();

    if (normalizedNames.has(normalizedName)) {
      throw new Error(
        `Character names must be unique; found '${character}' twice.`,
      );
    }

    normalizedNames.add(normalizedName);
  }

  return roster;
}

/**
 * Applies player-facing Character Setup answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object being assembled by the root compiler.
 * @param answers - Player-facing Character Setup answers to translate.
 * @param catalog - Generated schema used for character names and canonical choices.
 * @returns Nothing; the function replaces only `CHARACTER_OPTION_KEYS`.
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
  for (const key of CHARACTER_OPTION_KEYS) {
    if (!catalog.options[key]) {
      throw new Error(`Generated option catalog is missing '${key}'.`);
    }
  }

  // Validate the two source collections as one player-facing roster.
  const roster = validateCharacterRoster(answers, catalog);
  const moddedCharacterNames = getNamedModdedCharacters(answers);

  // Reject corrupted state instead of treating every unknown value as advanced mode.
  if (
    answers.ascensionMode !== "shared" &&
    answers.ascensionMode !== "individual"
  ) {
    throw new Error(
      `Unknown character Ascension mode '${answers.ascensionMode}'.`,
    );
  }

  // Resolve the actual number of generated characters for downstream checks.
  const count =
    answers.selectionMode === "random"
      ? answers.randomCharacterCount
      : roster.length;

  // Random selection cannot request zero or more characters than its source pool.
  if (count < 1 || count > roster.length) {
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
    !roster.includes(answers.startingCharacter)
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

  // Write questions shared by basic and advanced character processing.
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

  // Standard mode uses separate built-in, modded, and shared Ascension options.
  if (answers.ascensionMode === "shared") {
    const sharedAscensions = compileAscensionConfiguration(
      answers.sharedAscensions,
      catalog,
    );

    target.characters = [...answers.selectedCharacters];
    target.modded_characters = moddedCharacterNames;
    target.use_advanced_characters = false;
    target.advanced_characters = {};
    target.ascension = sharedAscensions.ascension;
    target.ascension_down = sharedAscensions.ascension_down;
    return;
  }

  // Advanced mode builds one dictionary entry for every built-in and modded character.
  const advancedCharacters: Record<string, CompiledAscensionConfiguration> = {};

  for (const character of answers.selectedCharacters) {
    const configuration = answers.individualAscensions[character];

    if (!configuration) {
      throw new Error(
        `Missing individual Ascension settings for '${character}'.`,
      );
    }

    advancedCharacters[character] = compileAscensionConfiguration(
      configuration,
      catalog,
    );
  }

  for (const moddedCharacter of answers.moddedCharacters) {
    const characterName = moddedCharacter.name.trim();
    advancedCharacters[characterName] = compileAscensionConfiguration(
      moddedCharacter.ascensions,
      catalog,
    );
  }

  // Clear the four ignored basic fields so complete output reflects one active system.
  target.characters = [];
  target.modded_characters = [];
  target.use_advanced_characters = true;
  target.advanced_characters = advancedCharacters;
  target.ascension = [];
  target.ascension_down = [];
}
