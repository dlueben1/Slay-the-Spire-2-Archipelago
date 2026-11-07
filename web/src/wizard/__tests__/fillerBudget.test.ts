/**
 * @file Protects the filler-slot count that mirrors the Python world's `create_items`.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import { FLOOR_CHECK_FILLER_SLOTS, getFillerBudget } from "../fillerBudget";
import { createDefaultFillerAnswers } from "../FillerItem";
import {
  createDefaultWizardAnswers,
  type BonusItemAnswer,
  type WizardAnswers,
} from "../WizardAnswers";

function createAnswers(characters: string[]): WizardAnswers {
  const answers = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
  answers.playerName = "Tester";
  answers.characters.selectedCharacters = characters;
  answers.characters.sharedAscensions = {
    enabled: [],
    ascensionDownsEnabled: false,
    downs: [],
  };
  answers.checksAndRewards.checks.includeFloorChecks = true;
  answers.checksAndRewards.startingEquipment = {
    progressiveStarterCard: false,
    progressiveStarterRelic: false,
  };
  answers.checksAndRewards.bonusItems = [];
  return answers;
}

const waxRelic: BonusItemAnswer = {
  kind: "WAX_RELIC",
  mode: "random",
  pools: ["Common"],
};

describe("filler budget", () => {
  it("gives each character the floor-check filler slots", () => {
    const budget = getFillerBudget(createAnswers(["Ironclad", "Silent"]));
    expect(budget.available).toBe(2 * FLOOR_CHECK_FILLER_SLOTS);
    expect(budget.used).toBe(0);
  });

  it("subtracts Downs and starters and adds Double Boss's extra floor", () => {
    const answers = createAnswers(["Ironclad"]);
    answers.characters.sharedAscensions = {
      enabled: [1, 2, 3, 10],
      ascensionDownsEnabled: true,
      downs: [2, 3],
    };
    answers.checksAndRewards.startingEquipment.progressiveStarterCard = true;
    // 48 - 2 Downs + 1 Double Boss - 2 starter cards
    expect(getFillerBudget(answers).available).toBe(45);

    answers.characters.sharedAscensions.downs = [2, 3, 10];
    // Downing Double Boss removes its floor and takes a slot: 48 - 3 - 2
    expect(getFillerBudget(answers).available).toBe(43);
  });

  it("applies per-character starter overrides", () => {
    const answers = createAnswers(["Ironclad", "Silent"]);
    answers.characters.individualStarterOverrides = {
      Silent: { progressiveStarterRelic: "enabled" },
    };
    expect(getFillerBudget(answers).available).toBe(
      2 * FLOOR_CHECK_FILLER_SLOTS - 2,
    );
  });

  it("counts Bonus Items and Sight items only while challenges are on", () => {
    const answers = createAnswers(["Ironclad"]);
    answers.checksAndRewards.bonusItems = [waxRelic, waxRelic];
    answers.challenges.hiddenIntents = "two";
    answers.challenges.enabled = false;
    expect(getFillerBudget(answers).used).toBe(2);

    answers.challenges.enabled = true;
    expect(getFillerBudget(answers).used).toBe(4);
  });

  it("uses the smallest pick when characters are drawn at random", () => {
    const answers = createAnswers(["Ironclad", "Silent", "Defect"]);
    answers.characters.ascensionMode = "individual";
    const none = { enabled: [], ascensionDownsEnabled: false, downs: [] };
    answers.characters.individualAscensions = {
      Ironclad: { ...none },
      Silent: { enabled: [1, 2], ascensionDownsEnabled: true, downs: [1, 2] },
      Defect: { enabled: [1], ascensionDownsEnabled: true, downs: [1] },
    };
    answers.characters.selectionMode = "random";
    answers.characters.randomCharacterCount = 2;

    const budget = getFillerBudget(answers);
    expect(budget.available).toBe(46 + 47);
    expect(budget.isWorstCase).toBe(true);

    // A fixed starting character is always drawn.
    answers.characters.availability = "fixed";
    answers.characters.startingCharacter = "Ironclad";
    expect(getFillerBudget(answers).available).toBe(48 + 46);
  });

  it("has no slots without Floor Checks and refuses to compile when over", () => {
    const answers = createAnswers(["Ironclad"]);
    answers.checksAndRewards.checks.includeFloorChecks = false;
    expect(getFillerBudget(answers).available).toBe(0);

    answers.checksAndRewards.bonusItems = [waxRelic];
    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /Floor Checks/,
    );

    answers.checksAndRewards.checks.includeFloorChecks = true;
    answers.checksAndRewards.bonusItems =
      Array<BonusItemAnswer>(49).fill(waxRelic);
    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /need 49 filler slots, but only 48/,
    );
  });
});
