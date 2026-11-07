/**
 * @file Protects saving and restoring builder answers between visits.
 *
 * Saves can come from an older deployment, so these tests focus on restoring only
 * what still fits the current answer shape.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import {
  clearSavedWizardState,
  loadSavedWizardState,
  saveWizardState,
  type AnswerStorage,
} from "../answerPersistence";
import { createDefaultFillerAnswers } from "../FillerItem";
import {
  createDefaultWizardAnswers,
  type WizardAnswers,
} from "../WizardAnswers";

function createDefaults(): WizardAnswers {
  return createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
}

/** An in-memory stand-in for `localStorage`. */
function createMemoryStorage(): AnswerStorage & {
  values: Map<string, string>;
} {
  const values = new Map<string, string>();
  return {
    values,
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => void values.set(key, value),
    removeItem: (key) => void values.delete(key),
  };
}

describe("answer persistence", () => {
  it("restores saved answers and the step", () => {
    const storage = createMemoryStorage();
    const answers = createDefaults();
    answers.playerName = "Tester";
    answers.characters.goal = 2;
    answers.characters.individualStarterOverrides = {
      Ironclad: { progressiveStarterCard: "enabled" },
    };
    answers.checksAndRewards.shop.cardSlots = 4;

    saveWizardState(storage, { answers, stepId: "checks" });
    const restored = loadSavedWizardState(storage, createDefaults());

    expect(restored?.stepId).toBe("checks");
    expect(restored?.answers).toEqual(answers);
  });

  it("returns null when nothing usable is saved", () => {
    const storage = createMemoryStorage();
    expect(loadSavedWizardState(storage, createDefaults())).toBeNull();

    storage.values.set("sts2ap.builder.answers.v1", "{not json");
    expect(loadSavedWizardState(storage, createDefaults())).toBeNull();

    expect(loadSavedWizardState(undefined, createDefaults())).toBeNull();
  });

  it("keeps defaults for missing or mistyped values from an older save", () => {
    const storage = createMemoryStorage();
    const defaults = createDefaults();
    storage.values.set(
      "sts2ap.builder.answers.v1",
      JSON.stringify({
        stepId: "run",
        answers: {
          playerName: "Old",
          run: { seeded: "yes" },
          checksAndRewards: { shop: "not an object" },
        },
      }),
    );

    const restored = loadSavedWizardState(storage, createDefaults());

    expect(restored?.answers.playerName).toBe("Old");
    expect(restored?.answers.run.seeded).toBe(defaults.run.seeded);
    expect(restored?.answers.checksAndRewards.shop).toEqual(
      defaults.checksAndRewards.shop,
    );
    expect(restored?.answers.deathLink).toEqual(defaults.deathLink);
  });

  it("forgets the save on clear and ignores storage that throws", () => {
    const storage = createMemoryStorage();
    saveWizardState(storage, { answers: createDefaults(), stepId: "run" });
    clearSavedWizardState(storage);
    expect(storage.values.size).toBe(0);

    const throwing: AnswerStorage = {
      getItem: () => {
        throw new Error("blocked");
      },
      setItem: () => {
        throw new Error("blocked");
      },
      removeItem: () => {
        throw new Error("blocked");
      },
    };
    expect(loadSavedWizardState(throwing, createDefaults())).toBeNull();
    expect(() =>
      saveWizardState(throwing, { answers: createDefaults(), stepId: "" }),
    ).not.toThrow();
    expect(() => clearSavedWizardState(throwing)).not.toThrow();
  });
});
