/**
 * @file Protects the pure Checks cross-field rules.
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

/** Creates a valid Checks section from schema defaults. */
function createSection() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);
  const answers = createDefaultWizardAnswers(
    ["Ironclad"],
    fillerAnswers,
    optionCatalog,
  );

  return answers.checks;
}

/** Verifies an ordinary toggle changes only its own nested field. */
function togglesIndependentChecks(): void {
  const section = createSection();
  const next = setCheckToggle(section, "neowSanity", !section.types.neowSanity);

  expect(next.types.neowSanity).toBe(!section.types.neowSanity);

  // The original must stay untouched and unrelated sections keep their identity.
  expect(section.types.neowSanity).not.toBe(next.types.neowSanity);
  expect(next.startingEquipment).toBe(section.startingEquipment);
}

/** Verifies disabling Floor Checks clears both Starting Equipment selections. */
function disablingFloorChecksClearsStartingEquipment(): void {
  const configured = {
    ...createSection(),
    types: { ...createSection().types, includeFloorChecks: true },
    startingEquipment: {
      progressiveStarterCard: true,
      progressiveStarterRelic: true,
    },
  };

  const next = setCheckToggle(configured, "includeFloorChecks", false);

  expect(next.types.includeFloorChecks).toBe(false);
  expect(next.startingEquipment.progressiveStarterCard).toBe(false);
  expect(next.startingEquipment.progressiveStarterRelic).toBe(false);
}

/** Verifies re-enabling Floor Checks does not resurrect cleared selections. */
function enablingFloorChecksLeavesStartingEquipmentAlone(): void {
  const section = setCheckToggle(createSection(), "includeFloorChecks", true);

  expect(section.types.includeFloorChecks).toBe(true);
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
