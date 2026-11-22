import { expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { buildWizardYaml } from "../../services/YamlService";
import { createDefaultFillerAnswers } from "../FillerItem";
import { selectGuidedOptions } from "../GuidedOption";
import { TRAP_ITEM_DEFINITIONS, TRAP_WEIGHTS } from "../TrapItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { itemPoolSetupStep, getVisibleQuestionIds } from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";

function defaults() {
  return createDefaultWizardAnswers(
    ["Ironclad"],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
}

it("exports every trap weight through the guided YAML", () => {
  const answers = defaults();
  answers.itemPool.traps.chance = 60;
  TRAP_ITEM_DEFINITIONS.forEach((item, index) => {
    answers.itemPool.traps.weights[item.id] = TRAP_WEIGHTS[index % 4]!;
  });
  const guided = selectGuidedOptions(
    compileWizardAnswers(answers, optionCatalog),
  );
  const yaml = buildWizardYaml("Alice", guided);
  expect(yaml).toContain("  trap_chance: 60\n");
  TRAP_ITEM_DEFINITIONS.forEach((item, index) => {
    expect(guided[item.optionKey]).toBe(TRAP_WEIGHTS[index % 4]);
    expect(yaml).toContain(
      `  ${item.optionKey}: "${TRAP_WEIGHTS[index % 4]}"\n`,
    );
  });
});

it("allows disabled traps but rejects an enabled empty mix and invalid percentages", () => {
  const answers = defaults();
  for (const item of TRAP_ITEM_DEFINITIONS)
    answers.itemPool.traps.weights[item.id] = "none";
  answers.itemPool.traps.chance = 0;
  expect(() => compileWizardAnswers(answers, optionCatalog)).not.toThrow();
  answers.itemPool.traps.chance = 20;
  expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
    "Enable at least one trap",
  );
  answers.itemPool.traps.weights.vakuu = "high";
  for (const chance of [-1, 100.5, 101]) {
    answers.itemPool.traps.chance = chance;
    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow();
  }
});

it("reveals trap weights when enabled", () => {
  const answers = defaults();
  answers.itemPool.traps.chance = 0;
  expect(getVisibleQuestionIds(itemPoolSetupStep, answers)).not.toContain(
    "trap-weights",
  );
  answers.itemPool.traps.chance = 1;
  expect(getVisibleQuestionIds(itemPoolSetupStep, answers)).toContain(
    "trap-weights",
  );
});
