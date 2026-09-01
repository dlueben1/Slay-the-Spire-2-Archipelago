/**
 * @file Pure cross-field rules for the combined Checks & Rewards answer section.
 *
 * Most check toggles are independent booleans, but Floor Checks feeds the filler
 * slots that progressive Starting Equipment items occupy. This transition owns that
 * dependency so the step component and the declarative check-types grid never
 * re-implement it; the Starting Equipment controls themselves stay disabled through
 * their question's `isEnabled` predicate while the prerequisite is unmet.
 */

import type { WizardSectionTransition } from "./QuestionControl";
import type { CheckAnswers, ChecksAndRewardsAnswers } from "./WizardAnswers";

/**
 * Updates one ordinary check or reward toggle.
 *
 * @param section - Current combined Checks & Rewards answers.
 * @param answerKey - Semantic answer field represented by the changed row.
 * @param value - Narrowed boolean state for that row.
 * @returns A new combined section with the toggle and its dependents updated.
 */
export function setCheckToggle(
  section: ChecksAndRewardsAnswers,
  answerKey: keyof CheckAnswers,
  value: boolean,
): ChecksAndRewardsAnswers {
  // Clone the nested check answers before replacing them in the combined section.
  const checks = {
    ...section.checks,
    [answerKey]: value,
  };

  // Progressive starter items cannot exist without Floor Check filler slots.
  if (answerKey === "includeFloorChecks" && !value) {
    return {
      ...section,
      checks,
      startingEquipment: {
        progressiveStarterCard: false,
        progressiveStarterRelic: false,
      },
    };
  }

  return { ...section, checks };
}

/**
 * Transition wrapper for the Floor Checks card in the check-types grid.
 *
 * @remarks The cast is safe because the descriptor lives on a question inside the
 * step that owns the `checksAndRewards` section; descriptor integrity tests
 * exercise the binding. Other check cards write their fields directly because they
 * have no dependents.
 */
export const includeFloorChecksTransition: WizardSectionTransition = (
  section,
  value,
) =>
  setCheckToggle(
    section as ChecksAndRewardsAnswers,
    "includeFloorChecks",
    value as boolean,
  );
