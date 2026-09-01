/**
 * @file Pure reconciliation of Character Setup answers that depend on the roster.
 *
 * Editing the built-in or modded roster can strand three dependent answers: a random
 * selection count larger than the pool, a starting character that was deselected,
 * and a numeric completion goal above the generated count. This module owns that
 * cleanup as a pure function so the step component's watcher stays a thin caller
 * and the rules are unit-testable without mounting Vue.
 */

import { getConfiguredCharacterNames } from "./CharacterRoster";
import type { CharacterAnswers } from "./WizardAnswers";

/**
 * Reconciles dependent answers after the built-in or modded roster changes.
 *
 * @param answers - Current Character Setup answers, including the changed roster.
 * @returns A patch containing only stale fields, or `null` when nothing changed.
 * @remarks This improves form ergonomics. The compiler still performs authoritative
 * semantic checks and must not rely on this presentation-layer cleanup.
 */
export function reconcileRosterDependents(
  answers: CharacterAnswers,
): Partial<CharacterAnswers> | null {
  // Reconciliation reads the same unified roster shared by every roster question.
  const roster = getConfiguredCharacterNames(answers);

  // Accumulate every required correction before returning, avoiding partial states.
  const patch: Partial<CharacterAnswers> = {};

  // Clamp random selection when its previous count exceeds the smaller pool.
  if (answers.randomCharacterCount > roster.length) {
    patch.randomCharacterCount = Math.max(1, roster.length);
  }

  // Replace a fixed starting character that the player just deselected.
  if (
    answers.startingCharacter &&
    !roster.includes(answers.startingCharacter)
  ) {
    patch.startingCharacter = roster[0] ?? null;
  }

  // Resolve the post-patch generated count before checking a numeric completion goal.
  const reconciledRandomCount =
    patch.randomCharacterCount ?? answers.randomCharacterCount;
  const reconciledGeneratedCount =
    answers.selectionMode === "random" ? reconciledRandomCount : roster.length;

  // Fall back to "all" when a previous numeric goal no longer fits the setup.
  if (answers.goal !== "all" && answers.goal > reconciledGeneratedCount) {
    patch.goal = "all";
  }

  // Return nothing when the user's change did not invalidate any dependent answer.
  return Object.keys(patch).length > 0 ? patch : null;
}
