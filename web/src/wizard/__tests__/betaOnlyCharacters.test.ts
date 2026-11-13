/**
 * @file Protects how the wizard handles characters only the game's beta branch has.
 *
 * The APWorld marks such a character and the generated catalog lists it. Generation
 * can't know a player's game branch, so the Review step is where the player is told.
 * The tests use their own catalog so they keep working once the marker is removed.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { getSelectedBetaOnlyCharacters } from "../CharacterRoster";
import { createDefaultFillerAnswers } from "../FillerItem";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { buildWizardReviewSections } from "../review";

/** Creates complete answers whose selected built-in characters are the given names. */
function createAnswersWith(selectedCharacters: string[]) {
  const answers = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
  answers.characters.selectedCharacters = selectedCharacters;

  return answers;
}

describe("beta-only characters", () => {
  it("lists only the selected characters the catalog marks", () => {
    const answers = createAnswersWith(["Ironclad", "Future", "Silent"]);
    const catalog = {
      ...optionCatalog,
      characters: {
        ...optionCatalog.characters,
        beta_only: ["Future", "Other"],
      },
    };

    expect(getSelectedBetaOnlyCharacters(answers.characters, catalog)).toEqual([
      "Future",
    ]);
  });

  it("warns on the Review step for each one in the generated catalog", () => {
    const [betaOnly] = optionCatalog.characters.beta_only;
    const characterSection = (selected: string[]) =>
      buildWizardReviewSections(createAnswersWith(selected)).find(
        (section) => section.title === "Character Setup",
      );

    expect(characterSection(["Ironclad"])?.warning).toBeUndefined();

    // Nothing to assert once the main branch has every character.
    if (betaOnly) {
      expect(characterSection(["Ironclad", betaOnly])?.warning).toContain(
        `${betaOnly} is only in the game's beta branch`,
      );
    }
  });
});
