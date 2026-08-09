/**
 * @file Declares wizard navigation and conditional question visibility.
 *
 * Add flow-level questions here, their persisted values in `WizardAnswers.ts`, and
 * their controls in the matching `components/wizard` step. This file must not know
 * Archipelago option keys; `wizard/compiler` owns that translation.
 */

import type { WizardAnswers } from "./WizardAnswers";

export interface WizardQuestion {
  id: string;
  title: string;
  isVisible?: (answers: WizardAnswers) => boolean;
}

export interface WizardStep {
  id: string;
  title: string;
  description?: string;
  questions: WizardQuestion[];
}

/**
 * Checks whether random character selection needs its count prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the random-count question should be shown.
 */
function usesRandomCharacterSelection(answers: WizardAnswers): boolean {
  // A count is irrelevant when every selected character is used.
  return answers.characters.selectionMode === "random";
}

/**
 * Checks whether character unlocking needs a fixed-start prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the starting-character question should be shown.
 */
function usesFixedStartingCharacter(answers: WizardAnswers): boolean {
  // A named character is irrelevant for all-at-start and random-start modes.
  return answers.characters.availability === "fixed";
}

/** Character Setup definition consumed by its component and wizard navigation. */
export const characterSetupStep: WizardStep = {
  id: "characters",
  title: "Character Setup",
  description:
    "Choose who appears in your world and how character progression works.",
  questions: [
    { id: "characters", title: "Which characters do you want to play?" },
    { id: "selection", title: "Should every selected character be used?" },
    {
      id: "random-count",
      title: "How many characters should be randomly selected?",
      isVisible: usesRandomCharacterSelection,
    },
    { id: "availability", title: "How should characters become available?" },
    {
      id: "starting-character",
      title: "Which character should start unlocked?",
      isVisible: usesFixedStartingCharacter,
    },
    {
      id: "goal",
      title: "How many characters must complete a run to finish your goal?",
    },
  ],
};

/** Filler Setup definition consumed by its component and wizard navigation. */
export const fillerSetupStep: WizardStep = {
  id: "filler",
  title: "Filler Items",
  description:
    "Choose how often each helpful filler reward appears relative to the others.",
  questions: [
    {
      id: "filler-weights",
      title: "How often should each filler item appear?",
    },
  ],
};

/** Declarative step and question ordering for the guided wizard. */
export const wizardSteps: WizardStep[] = [
  characterSetupStep,
  fillerSetupStep,
  {
    id: "review",
    title: "Review",
    questions: [],
  },
];

/**
 * Lists Character Setup questions applicable to the current answers.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Visible question identifiers in display order.
 * @remarks This evaluates presentation flow only; it does not validate or compile.
 */
export function visibleCharacterQuestionIds(answers: WizardAnswers): string[] {
  // Character Setup is the first guided step in the current vertical slice.
  const characterQuestions = characterSetupStep.questions;

  // Questions without an explicit predicate are always visible.
  const visibleQuestionIds: string[] = [];

  for (const question of characterQuestions) {
    const isVisible = question.isVisible?.(answers) ?? true;

    if (isVisible) {
      visibleQuestionIds.push(question.id);
    }
  }

  // Expose stable identifiers instead of mutable definition objects.
  return visibleQuestionIds;
}
