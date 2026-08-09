/**
 * @file Protects the Character Setup compiler and conditional question contract.
 *
 * These tests use player-facing answers and assert canonical option output, ensuring
 * changes to question semantics cannot silently drift from the generated schema. Add
 * representative compilation and visibility cases here whenever Character Setup gains
 * an answer, conditional prompt, or mapping rule.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { createDefaultFillerAnswers } from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import { summarizeCharacterAnswers } from "../review";
import { visibleCharacterQuestionIds } from "../WizardStep";

/**
 * Creates a complete wizard answer model for Character Setup tests.
 *
 * @param availableCharacters - Character names to expose to the test arrangement.
 * @returns Complete answers with schema-derived filler defaults.
 */
function createTestAnswers(availableCharacters: readonly string[]) {
  // Keep unrelated Filler Setup state valid while Character behavior is isolated.
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  // Delegate Character defaults to the same public initializer used by the view.
  return createDefaultWizardAnswers(availableCharacters, fillerAnswers);
}

/**
 * Verifies the representative random-character setup from the feature specification.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesRandomCharacterSetup(): void {
  // Arrange player intent without referencing canonical option values.
  const answers = createTestAnswers(
    optionCatalog.options.characters!.valid_keys!,
  );
  answers.characters = {
    selectedCharacters: ["Ironclad", "Silent", "Defect"],
    selectionMode: "random",
    randomCharacterCount: 2,
    availability: "random",
    startingCharacter: null,
    goal: 2,
  };

  // Compile through the same root pipeline used by the view.
  const result = compileWizardAnswers(answers, optionCatalog);

  // Assert all five character mappings and complete-default reconstruction.
  expect(result.characters).toEqual(["Ironclad", "Silent", "Defect"]);
  expect(result.pick_num_characters).toBe(2);
  expect(result.num_chars_goal).toBe(2);
  expect(result.lock_characters).toBe("locked_random");
  expect(result.unlocked_character).toBe("");
  expect(Object.keys(result)).toHaveLength(optionCatalog.option_order.length);

  // Confirm review prose is derived from the same semantic answers.
  expect(summarizeCharacterAnswers(answers.characters)).toContain(
    "two of Ironclad, Silent, and Defect",
  );
}

/**
 * Verifies special `all` semantics and display-name-to-choice-name conversion.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesFixedStartingCharacter(): void {
  // Arrange a setup that exercises both zero-sentinel and fixed-choice mappings.
  const answers = createTestAnswers(["Ironclad", "Silent"]);
  answers.characters = {
    selectedCharacters: ["Ironclad", "Silent"],
    selectionMode: "all",
    randomCharacterCount: 1,
    availability: "fixed",
    startingCharacter: "Silent",
    goal: "all",
  };

  // Compile and assert the canonical technical representation.
  const result = compileWizardAnswers(answers, optionCatalog);
  expect(result.pick_num_characters).toBe(0);
  expect(result.num_chars_goal).toBe(0);
  expect(result.lock_characters).toBe("locked_fixed");
  expect(result.unlocked_character).toBe("silent");
}

/**
 * Verifies semantic and schema-backed character errors fail loudly.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidCharacterAnswers(): void {
  // Arrange an unknown character not present in generated valid keys.
  const answers = createTestAnswers(["Ironclad"]);
  answers.characters.selectedCharacters = ["Watcher"];

  /** Compiles the current deliberately unknown-character answer. */
  function compileUnknownCharacter(): void {
    // Invoke the root compiler so the assertion observes its public failure behavior.
    compileWizardAnswers(answers, optionCatalog);
  }

  // Confirm generated character membership is enforced.
  expect(compileUnknownCharacter).toThrow("Unknown character");

  // Rearrange the same model to request a subset larger than its source pool.
  answers.characters.selectedCharacters = ["Ironclad"];
  answers.characters.selectionMode = "random";
  answers.characters.randomCharacterCount = 2;

  /** Compiles the current deliberately oversized random-character answer. */
  function compileOversizedRandomSelection(): void {
    // Invoke the root compiler so the assertion observes its public failure behavior.
    compileWizardAnswers(answers, optionCatalog);
  }

  // Confirm cross-answer selection constraints are enforced by the section compiler.
  expect(compileOversizedRandomSelection).toThrow("random character count");
}

/**
 * Registers Character Setup compiler cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerCharacterCompilerTests(): void {
  // Group the representative mappings and failure contract under one suite.
  it(
    "compiles random selection, random unlocking, and a numeric goal",
    compilesRandomCharacterSetup,
  );
  it(
    "maps all and a fixed starting character to canonical values",
    compilesFixedStartingCharacter,
  );
  it(
    "rejects invalid schema-backed character and count values",
    rejectsInvalidCharacterAnswers,
  );
}

/**
 * Verifies dependent question visibility follows controlling semantic answers.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function revealsConditionalCharacterQuestions(): void {
  // Begin with modes whose dependent questions have no meaning.
  const answers = createTestAnswers(["Ironclad", "Silent"]);
  expect(visibleCharacterQuestionIds(answers)).not.toContain("random-count");
  expect(visibleCharacterQuestionIds(answers)).not.toContain(
    "starting-character",
  );

  // Activate both controlling modes and confirm both prompts enter the flow.
  answers.characters.selectionMode = "random";
  answers.characters.availability = "fixed";
  expect(visibleCharacterQuestionIds(answers)).toContain("random-count");
  expect(visibleCharacterQuestionIds(answers)).toContain("starting-character");
}

/**
 * Registers conditional-flow cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerConditionalQuestionTests(): void {
  // Keep flow behavior separate from technical compiler assertions.
  it(
    "only reveals dependent questions for their selected modes",
    revealsConditionalCharacterQuestions,
  );
}

// Register both behavioral areas using named, documented test callbacks.
describe("character wizard compiler", registerCharacterCompilerTests);
describe("conditional character questions", registerConditionalQuestionTests);
