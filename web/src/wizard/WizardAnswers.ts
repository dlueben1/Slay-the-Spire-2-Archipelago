/**
 * @file Defines the persistent, player-facing state for the guided wizard.
 *
 * This represents an abstraction between the reactive UI state and the raw YAML/AP options.
 *
 * Add new answer fields here, initialize them in `createDefaultWizardAnswers`,
 * declare their questions in `WizardStep.ts`, and translate them in a compiler
 * module. Vue components should update this model only, never a YAML options object.
 */

export type CharacterSelectionMode = "all" | "random";
export type CharacterAvailability = "all" | "random" | "fixed";
export type CharacterGoal = "all" | number;

export type FillerWeightLevel = 0 | 1 | 2 | 3;

export type FillerItemId =
  | "oneGold"
  | "fiveGold"
  | "freeAttack"
  | "freePower"
  | "freeSkill"
  | "vigor"
  | "artifact"
  | "thorns"
  | "buffer"
  | "dexterity"
  | "strength"
  | "plating"
  | "friendship"
  | "postCombatCardUpgrade"
  | "postCombatCardRemoval"
  | "additionalCardReward";

export interface CharacterAnswers {
  selectedCharacters: string[];
  selectionMode: CharacterSelectionMode;
  randomCharacterCount: number;
  availability: CharacterAvailability;
  startingCharacter: string | null;
  goal: CharacterGoal;
}

export interface FillerAnswers {
  weights: Record<FillerItemId, FillerWeightLevel>;
}

export interface WizardAnswers {
  characters: CharacterAnswers;
  filler: FillerAnswers;
}

/**
 * Creates the initial player-facing state for a guided setup session.
 *
 * @param available - Character names supplied by the generated catalog.
 * @param filler - Schema-derived initial filler answers.
 * @returns A complete answer model initialized to a valid one-character setup.
 * @remarks This chooses UX defaults only. The compiler creates Archipelago options.
 */
export function createDefaultWizardAnswers(
  available: readonly string[],
  filler: FillerAnswers,
): WizardAnswers {
  // Use a schema-provided character so the initial form contains no duplicated fact.
  const selectedCharacters = available.slice(0, 1);

  // Initialize every field explicitly to keep this the single setup entry point.
  return {
    characters: {
      selectedCharacters,
      selectionMode: "all",
      randomCharacterCount: 1,
      availability: "all",
      startingCharacter: selectedCharacters[0] ?? null,
      goal: "all",
    },
    filler,
  };
}
