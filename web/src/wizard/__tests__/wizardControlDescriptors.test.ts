/**
 * @file Structurally validates every declarative control descriptor.
 *
 * Control descriptors bind questions to answer fields through dot paths that
 * TypeScript cannot check. This suite resolves every binding against schema-derived
 * default answers so a renamed answer field, a broken range name, or a malformed
 * choice list fails in tests instead of crashing the rendered wizard.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { getAnswerAtPath } from "../answerPath";
import { createDefaultFillerAnswers } from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { wizardSteps, type WizardStep } from "../WizardStep";

/**
 * Creates complete schema-derived answers for descriptor resolution.
 *
 * @returns A valid wizard answer model initialized from the generated catalog.
 */
function createTestAnswers() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  // Use the same public initializer as the production wizard view.
  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers, optionCatalog);
}

/** Steps that declare at least one generically rendered control. */
function getStepsWithControls(): WizardStep[] {
  return wizardSteps.filter((step) =>
    step.questions.some((question) => question.control),
  );
}

/** Verifies every field path and value type binds to the step's section. */
function resolvesEveryFieldBinding(): void {
  const answers = createTestAnswers();

  for (const step of getStepsWithControls()) {
    // A step cannot render generic controls without a declared answer section.
    expect(
      step.sectionKey,
      `step '${step.id}' needs a sectionKey`,
    ).toBeDefined();
    const section = answers[step.sectionKey!];

    for (const question of step.questions) {
      const control = question.control;

      if (!control) {
        continue;
      }

      const context = `question '${question.id}'`;

      // Every bound path must resolve to a value of the control's expected type.
      if (control.kind === "radio") {
        expect(typeof getAnswerAtPath(section, control.field), context).toBe(
          "string",
        );
      } else if (control.kind === "checkbox") {
        expect(typeof getAnswerAtPath(section, control.field), context).toBe(
          "boolean",
        );
      } else if (control.kind === "checkbox-grid") {
        for (const item of control.items) {
          expect(
            typeof getAnswerAtPath(section, item.field),
            `${context} item '${item.field}'`,
          ).toBe("boolean");
        }
      } else if (control.kind === "number" || control.kind === "slider") {
        expect(typeof getAnswerAtPath(section, control.field), context).toBe(
          "number",
        );
      } else if (control.kind === "number-grid") {
        for (const gridField of control.fields) {
          expect(
            typeof getAnswerAtPath(section, gridField.field),
            `${context} field '${gridField.field}'`,
          ).toBe("number");
        }
      }
    }
  }
}

/** Verifies radio choices are non-empty with unique values. */
function declaresWellFormedChoices(): void {
  const answers = createTestAnswers();

  for (const step of getStepsWithControls()) {
    for (const question of step.questions) {
      if (question.control?.kind !== "radio") {
        continue;
      }

      const values = question.control.choices.map((choice) => choice.value);
      expect(values.length, `question '${question.id}'`).toBeGreaterThan(0);
      expect(new Set(values).size, `question '${question.id}'`).toBe(
        values.length,
      );

      // The stored default must be offered, or the radio would render unselected.
      const current = getAnswerAtPath(
        answers[step.sectionKey!],
        question.control.field,
      );
      expect(values, `question '${question.id}' default`).toContain(current);
    }
  }
}

/** Verifies every descriptor range resolves to usable inclusive bounds. */
function resolvesEveryRange(): void {
  const answers = createTestAnswers();

  for (const step of getStepsWithControls()) {
    for (const question of step.questions) {
      const control = question.control;
      const ranges =
        control?.kind === "number" || control?.kind === "slider"
          ? [control.range]
          : control?.kind === "number-grid"
            ? control.fields.map((gridField) => gridField.range)
            : [];

      for (const range of ranges) {
        const resolved = range(answers);
        expect(
          resolved.minimum,
          `question '${question.id}'`,
        ).toBeLessThanOrEqual(resolved.maximum);
      }
    }
  }
}

/** Verifies bound transitions accept their section and return a new object. */
function exercisesEveryTransition(): void {
  const answers = createTestAnswers();

  for (const step of getStepsWithControls()) {
    const section = answers[step.sectionKey!];

    for (const question of step.questions) {
      const control = question.control;
      const transitions =
        control?.kind === "checkbox-grid"
          ? control.items.flatMap((item) =>
              item.applyChange ? [item.applyChange] : [],
            )
          : (control?.kind === "checkbox" || control?.kind === "radio") &&
              control.applyChange
            ? [control.applyChange]
            : [];

      for (const transition of transitions) {
        // Toggling on is a safe smoke value for every current boolean transition.
        const result = transition(section, true);
        expect(typeof result, `question '${question.id}'`).toBe("object");
        expect(result, `question '${question.id}'`).not.toBeNull();
      }
    }
  }
}

describe("wizard control descriptors", () => {
  it("resolves every field binding", resolvesEveryFieldBinding);
  it("declares well-formed choices", declaresWellFormedChoices);
  it("resolves every range", resolvesEveryRange);
  it("exercises every transition", exercisesEveryTransition);
});
