/**
 * @file Protects how the wizard handles option choices only the game's beta branch supports.
 *
 * The APWorld marks such a choice with the game feature it needs, and the generated
 * catalog carries the marker. The mod refuses a slot that needs a feature its game
 * version lacks, so the Review step tells the player before they generate. The tests
 * use their own catalog so they keep working once the marker is removed.
 */

import { describe, expect, it } from "vitest";
import {
  optionCatalog,
  type OptionCatalog,
} from "../../generated/optionCatalog";
import {
  getActiveBetaOnlyChallenges,
  isBetaOnlyChoice,
} from "../betaOnlyOptions";
import { createDefaultFillerAnswers } from "../FillerItem";
import { describeChallengeWarnings } from "../review";
import {
  createDefaultWizardAnswers,
  type GameModeChoice,
} from "../WizardAnswers";
import { CHALLENGE_OPTION_KEYS } from "../WizardOptionKey";

const KEY = CHALLENGE_OPTION_KEYS.gameMode;

/** Creates a catalog whose Dread game mode does or doesn't carry the beta-only marker. */
function createCatalog(isMarked: boolean): OptionCatalog {
  return {
    ...optionCatalog,
    options: {
      ...optionCatalog.options,
      [KEY]: {
        ...optionCatalog.options[KEY]!,
        required_game_features: isMarked ? { dread: "dread" } : undefined,
      },
    },
  };
}

/** Creates default Challenges answers with the opt-in switch and game mode set. */
function createChallenges(enabled: boolean, gameMode: GameModeChoice) {
  const { challenges } = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );

  return { ...challenges, enabled, gameMode };
}

describe("beta-only options", () => {
  it("reads the marker from the catalog", () => {
    expect(isBetaOnlyChoice(KEY, "dread", createCatalog(true))).toBe(true);
    expect(isBetaOnlyChoice(KEY, "standard", createCatalog(true))).toBe(false);
    expect(isBetaOnlyChoice(KEY, "dread", createCatalog(false))).toBe(false);
    expect(
      isBetaOnlyChoice("no_such_option", "dread", createCatalog(true)),
    ).toBe(false);
  });

  it("lists Dread only while it is chosen and marked", () => {
    const marked = createCatalog(true);

    expect(
      getActiveBetaOnlyChallenges(createChallenges(true, "dread"), marked),
    ).toEqual(["Dread"]);
    expect(
      getActiveBetaOnlyChallenges(createChallenges(true, "standard"), marked),
    ).toEqual([]);
    // The opt-in switch is off, so the choice never reaches the YAML.
    expect(
      getActiveBetaOnlyChallenges(createChallenges(false, "dread"), marked),
    ).toEqual([]);
    expect(
      getActiveBetaOnlyChallenges(
        createChallenges(true, "dread"),
        createCatalog(false),
      ),
    ).toEqual([]);
  });

  it("warns on the Review step while the generated catalog marks Dread", () => {
    expect(
      describeChallengeWarnings(createChallenges(true, "standard")),
    ).toBeUndefined();

    const warning = describeChallengeWarnings(createChallenges(true, "dread"));

    expect(warning).toMatch(/logic/i);

    // Nothing more to assert once the main branch has Dread.
    if (isBetaOnlyChoice(KEY, "dread")) {
      expect(warning).toContain("Dread is only in the game's beta branch");
    }
  });
});
