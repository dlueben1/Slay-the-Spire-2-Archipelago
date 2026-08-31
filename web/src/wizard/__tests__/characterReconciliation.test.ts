/**
 * @file Protects Character Setup answers that depend on the configured roster.
 *
 * These tests exercise the pure reconciliation boundary used by the component
 * watcher, including built-in and modded character roster changes.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { reconcileRosterDependents } from "../characterReconciliation";
import { createDefaultFillerAnswers } from "../FillerItem";
import {
  createDefaultWizardAnswers,
  type CharacterAnswers,
} from "../WizardAnswers";

/** Creates valid Character Setup answers for reconciliation tests. */
function createCharacterAnswers(): CharacterAnswers {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  return createDefaultWizardAnswers(
    ["Ironclad", "Silent"],
    fillerAnswers,
    optionCatalog,
  ).characters;
}

/** Verifies valid dependent answers require no patch. */
function leavesValidAnswersUnchanged(): void {
  expect(reconcileRosterDependents(createCharacterAnswers())).toBeNull();
}

/** Verifies random selection cannot exceed the configured roster. */
function clampsRandomCharacterCount(): void {
  const answers = {
    ...createCharacterAnswers(),
    selectedCharacters: ["Ironclad"],
    randomCharacterCount: 2,
  };

  expect(reconcileRosterDependents(answers)).toEqual({
    randomCharacterCount: 1,
  });
}

/** Verifies a removed fixed starter is replaced by the first roster entry. */
function replacesStaleStartingCharacter(): void {
  const answers = {
    ...createCharacterAnswers(),
    selectedCharacters: ["Silent"],
    startingCharacter: "Ironclad",
  };

  expect(reconcileRosterDependents(answers)).toEqual({
    startingCharacter: "Silent",
  });
}

/** Verifies goals above the generated character count fall back to all. */
function resetsOversizedGoal(): void {
  const answers = {
    ...createCharacterAnswers(),
    selectionMode: "random" as const,
    randomCharacterCount: 1,
    goal: 2,
  };

  expect(reconcileRosterDependents(answers)).toEqual({ goal: "all" });
}

/** Verifies only named modded rows contribute to roster-dependent limits. */
function countsOnlyNamedModdedCharacters(): void {
  const base = createCharacterAnswers();
  const answers: CharacterAnswers = {
    ...base,
    selectedCharacters: [],
    moddedCharacters: [
      {
        name: "  CUSTOM_CHARACTER  ",
        ascensions: base.sharedAscensions,
      },
      { name: "   ", ascensions: base.sharedAscensions },
    ],
    randomCharacterCount: 2,
    startingCharacter: "Ironclad",
  };

  expect(reconcileRosterDependents(answers)).toEqual({
    randomCharacterCount: 1,
    startingCharacter: "CUSTOM_CHARACTER",
  });
}

describe("characterReconciliation", () => {
  it("leaves valid answers unchanged", leavesValidAnswersUnchanged);
  it("clamps random character count", clampsRandomCharacterCount);
  it("replaces a stale starting character", replacesStaleStartingCharacter);
  it("resets an oversized goal", resetsOversizedGoal);
  it("counts only named modded characters", countsOnlyNamedModdedCharacters);
});
