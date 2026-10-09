import { expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { buildWizardYaml } from "../../services/YamlService";
import { createDefaultFillerAnswers } from "../FillerItem";
import { selectGuidedOptions } from "../GuidedOption";
import { TRAP_ITEM_DEFINITIONS, TRAP_WEIGHTS } from "../TrapItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import {
  checkSetupStep,
  getVisibleQuestionIds,
  progressionSetupStep,
  getQuestionById,
  resolveQuestionHelp,
} from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";

function defaults() {
  return createDefaultWizardAnswers(
    ["Ironclad"],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
}

it("exports every trap weight and both logic choices through the guided YAML", () => {
  const answers = defaults();
  answers.checksAndRewards.traps.chance = 60;
  TRAP_ITEM_DEFINITIONS.forEach((item, index) => {
    answers.checksAndRewards.traps.weights[item.id] = TRAP_WEIGHTS[index % 4]!;
  });
  for (const useNewLogic of [true, false]) {
    answers.progression.useNewLogic = useNewLogic;
    const guided = selectGuidedOptions(
      compileWizardAnswers(answers, optionCatalog),
    );
    const yaml = buildWizardYaml("Alice", guided);
    expect(yaml).toContain("  trap_chance: 60\n");
    expect(yaml).toContain(`  use_new_logic: ${useNewLogic}\n`);
    TRAP_ITEM_DEFINITIONS.forEach((item, index) => {
      expect(guided[item.optionKey]).toBe(TRAP_WEIGHTS[index % 4]);
      expect(yaml).toContain(
        `  ${item.optionKey}: "${TRAP_WEIGHTS[index % 4]}"\n`,
      );
    });
  }
});

it("allows disabled traps but rejects an enabled empty mix and invalid percentages", () => {
  const answers = defaults();
  for (const item of TRAP_ITEM_DEFINITIONS)
    answers.checksAndRewards.traps.weights[item.id] = "none";
  answers.checksAndRewards.traps.chance = 0;
  expect(() => compileWizardAnswers(answers, optionCatalog)).not.toThrow();
  answers.checksAndRewards.traps.chance = 20;
  expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
    "Enable at least one trap",
  );
  answers.checksAndRewards.traps.weights.vakuu = "high";
  for (const chance of [-1, 100.5, 101]) {
    answers.checksAndRewards.traps.chance = chance;
    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow();
  }
});

it("reveals trap weights when enabled and explains the automatic old-logic fallback", () => {
  const answers = defaults();
  answers.checksAndRewards.traps.chance = 0;
  expect(getVisibleQuestionIds(checkSetupStep, answers)).not.toContain(
    "trap-weights",
  );
  answers.checksAndRewards.traps.chance = 1;
  expect(getVisibleQuestionIds(checkSetupStep, answers)).toContain(
    "trap-weights",
  );
  answers.progression.useNewLogic = true;
  Object.assign(answers.checksAndRewards.checks, {
    includeFloorChecks: false,
    goldSanity: false,
    potionSanity: false,
  });
  expect(
    resolveQuestionHelp(
      getQuestionById(progressionSetupStep, "use-new-logic"),
      answers,
    ),
  ).toContain("Previous logic will still be used");
  expect(compileWizardAnswers(answers, optionCatalog).use_new_logic).toBe(true);
});
