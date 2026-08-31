/**
 * @file Protects the pure Checks & Rewards cross-field rules.
 *
 * The single dependency in this section — disabling Floor Checks must also clear
 * both progressive Starting Equipment selections — used to live inside the step
 * component's setter. These tests exercise the extracted transition directly.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { setCheckToggle } from "../checksTransitions";
import { createDefaultFillerAnswers } from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";

/** Creates a valid combined Checks & Rewards section from schema defaults. */
function createSection() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);
  const answers = createDefaultWizardAnswers(
    ["Ironclad"],
    fillerAnswers,
    optionCatalog,
  );

  return answers.checksAndRewards;
}

/** Verifies an ordinary toggle changes only its own nested field. */
function togglesIndependentChecks(): void {
  const section = createSection();
  const next = setCheckToggle(
    section,
    "neowSanity",
    !section.checks.neowSanity,
  );

  expect(next.checks.neowSanity).toBe(!section.checks.neowSanity);

  // The original must stay untouched and unrelated sections keep their identity.
  expect(section.checks.neowSanity).not.toBe(next.checks.neowSanity);
  expect(next.shop).toBe(section.shop);
  expect(next.startingEquipment).toBe(section.startingEquipment);
}

/** Verifies disabling Floor Checks clears both Starting Equipment selections. */
function disablingFloorChecksClearsStartingEquipment(): void {
  const configured = {
    ...createSection(),
    checks: { ...createSection().checks, includeFloorChecks: true },
    startingEquipment: {
      progressiveStarterCard: true,
      progressiveStarterRelic: true,
    },
  };

  const next = setCheckToggle(configured, "includeFloorChecks", false);

  expect(next.checks.includeFloorChecks).toBe(false);
  expect(next.startingEquipment.progressiveStarterCard).toBe(false);
  expect(next.startingEquipment.progressiveStarterRelic).toBe(false);
}

/** Verifies re-enabling Floor Checks does not resurrect cleared selections. */
function enablingFloorChecksLeavesStartingEquipmentAlone(): void {
  const section = setCheckToggle(createSection(), "includeFloorChecks", true);

  expect(section.checks.includeFloorChecks).toBe(true);
  expect(section.startingEquipment.progressiveStarterCard).toBe(false);
  expect(section.startingEquipment.progressiveStarterRelic).toBe(false);
}

describe("checksTransitions", () => {
  it("toggles independent checks", togglesIndependentChecks);
  it(
    "disabling floor checks clears starting equipment",
    disablingFloorChecksClearsStartingEquipment,
  );
  it(
    "enabling floor checks leaves starting equipment alone",
    enablingFloorChecksLeavesStartingEquipmentAlone,
  );
});
