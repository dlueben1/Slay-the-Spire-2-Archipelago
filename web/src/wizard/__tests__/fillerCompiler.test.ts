/**
 * @file Protects the Filler Setup answer, compiler, and generated-schema contract.
 *
 * These tests ensure the friendly 0-3 slider levels remain independent from raw
 * generated weights while compiling to the four canonical choice names accepted by
 * every generated filler option.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import {
  createDefaultFillerAnswers,
  FILLER_ITEM_DEFINITIONS,
  FILLER_WEIGHT_NAMES,
} from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { wizardSteps } from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";

/**
 * Creates a complete answer model suitable for filler-focused tests.
 *
 * @returns Wizard answers using generated defaults for all filler sliders.
 */
function createTestAnswers() {
  // Derive filler defaults from the catalog under test.
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  // Keep the unrelated Character Setup section valid with one schema character.
  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers);
}

/**
 * Verifies every generated filler default round-trips through semantic slider state.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesGeneratedFillerDefaults(): void {
  // Compile a newly initialized wizard exactly as the view does.
  const answers = createTestAnswers();
  const options = compileWizardAnswers(answers, optionCatalog);

  // Each slider-derived canonical name must equal its generated option default.
  for (const definition of FILLER_ITEM_DEFINITIONS) {
    expect(options[definition.optionKey]).toBe(
      optionCatalog.options[definition.optionKey]!.default,
    );
  }
}

/**
 * Verifies all four slider notches map to their canonical generated choice names.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesEveryFillerWeightLevel(): void {
  // Assign successive definitions to levels 0, 1, 2, and 3.
  const answers = createTestAnswers();

  for (let level = 0; level <= 3; level += 1) {
    const definition = FILLER_ITEM_DEFINITIONS[level]!;
    answers.filler.weights[definition.id] = level as 0 | 1 | 2 | 3;
  }

  // Compile the semantic levels to canonical YAML choice names.
  const options = compileWizardAnswers(answers, optionCatalog);

  for (let level = 0; level <= 3; level += 1) {
    const definition = FILLER_ITEM_DEFINITIONS[level]!;
    expect(options[definition.optionKey]).toBe(FILLER_WEIGHT_NAMES[level]);
  }
}

/**
 * Verifies programmatically invalid slider state fails before YAML generation.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidFillerWeightLevels(): void {
  // Force a value outside the UI slider's supported bounds.
  const answers = createTestAnswers();
  const firstDefinition = FILLER_ITEM_DEFINITIONS[0]!;
  answers.filler.weights[firstDefinition.id] = 4 as 0;

  /** Compiles the deliberately invalid filler answer. */
  function compileInvalidFillerAnswers(): void {
    // Invoke the public root compiler so the assertion observes its error contract.
    compileWizardAnswers(answers, optionCatalog);
  }

  // The section compiler should reject corrupted or manually constructed state.
  expect(compileInvalidFillerAnswers).toThrow("integer from 0 through 3");
}

/**
 * Verifies the hand-authored filler mapping covers the complete generated group.
 *
 * @returns Nothing; Vitest records assertion failures.
 * @remarks This intentionally fails when a new generated filler option needs a UX decision.
 */
function coversEveryGeneratedFillerOption(): void {
  // Read expected option keys from the generated catalog in canonical order.
  const generatedFillerKeys: string[] = [];

  for (const optionKey of optionCatalog.option_order) {
    if (optionCatalog.options[optionKey]?.group === "Filler Items") {
      generatedFillerKeys.push(optionKey);
    }
  }

  // Read actual option keys from the hand-authored semantic mapping.
  const mappedFillerKeys: string[] = [];

  for (const definition of FILLER_ITEM_DEFINITIONS) {
    mappedFillerKeys.push(definition.optionKey);
  }

  // Require an explicit mapping update whenever generated filler coverage changes.
  expect(mappedFillerKeys).toEqual(generatedFillerKeys);
}

/**
 * Verifies Filler Setup remains between Character Setup and Review.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function preservesFillerStepOrder(): void {
  // Extract stable IDs because labels and descriptions may change independently.
  const stepIds: string[] = [];

  for (const step of wizardSteps) {
    stepIds.push(step.id);
  }

  // Protect the requested second-vertical-slice navigation order.
  expect(stepIds).toEqual(["characters", "filler", "review"]);
}

/**
 * Registers Filler Setup compiler cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerFillerCompilerTests(): void {
  // Cover defaults, every explicit notch, and defensive invalid-state handling.
  it("round-trips generated filler defaults", compilesGeneratedFillerDefaults);
  it("compiles all four filler weight levels", compilesEveryFillerWeightLevel);
  it("rejects invalid filler weight levels", rejectsInvalidFillerWeightLevels);
  it("covers every generated filler option", coversEveryGeneratedFillerOption);
  it("appears between Character Setup and Review", preservesFillerStepOrder);
}

// Register the documented test callbacks as one focused vertical-slice suite.
describe("filler wizard compiler", registerFillerCompilerTests);
