/**
 * @file Protects how the wizard handles options only the game's beta branch supports.
 *
 * The APWorld marks such an option with the game feature it needs, and the generated
 * catalog carries the marker. The mod plays a slot without a feature its game version
 * lacks, so the Review step tells the player before they generate. The tests
 * use their own catalog so they keep working once the marker is removed.
 */

import { describe, expect, it } from "vitest";
import {
  optionCatalog,
  type OptionCatalog,
} from "../../generated/optionCatalog";
import {
  getActiveBetaOnlyChallenges,
  isBetaOnlyOption,
} from "../betaOnlyOptions";
import { createDefaultFillerAnswers } from "../FillerItem";
import { describeChallengeWarnings } from "../review";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { CHALLENGE_OPTION_KEYS } from "../WizardOptionKey";

/** Creates a catalog whose Dread Mode option does or doesn't carry the beta-only marker. */
function createCatalog(requiredGameFeature: string | undefined): OptionCatalog {
  const dreadMode = optionCatalog.options[CHALLENGE_OPTION_KEYS.dreadMode]!;

  return {
    ...optionCatalog,
    options: {
      ...optionCatalog.options,
      [CHALLENGE_OPTION_KEYS.dreadMode]: {
        ...dreadMode,
        required_game_feature: requiredGameFeature,
      },
    },
  };
}

/** Creates default Challenges answers with the opt-in switch and Dread Mode set. */
function createChallenges(enabled: boolean, dreadMode: boolean) {
  const { challenges } = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );

  return { ...challenges, enabled, dreadMode };
}

describe("beta-only options", () => {
  it("reads the marker from the catalog", () => {
    const key = CHALLENGE_OPTION_KEYS.dreadMode;

    expect(isBetaOnlyOption(key, createCatalog("dread"))).toBe(true);
    expect(isBetaOnlyOption(key, createCatalog(undefined))).toBe(false);
    expect(isBetaOnlyOption("no_such_option", createCatalog("dread"))).toBe(
      false,
    );
  });

  it("lists Dread Mode only while it is on and marked", () => {
    const marked = createCatalog("dread");

    expect(
      getActiveBetaOnlyChallenges(createChallenges(true, true), marked),
    ).toEqual(["Dread Mode"]);
    expect(
      getActiveBetaOnlyChallenges(createChallenges(true, false), marked),
    ).toEqual([]);
    // The opt-in switch is off, so the tick never reaches the YAML.
    expect(
      getActiveBetaOnlyChallenges(createChallenges(false, true), marked),
    ).toEqual([]);
    expect(
      getActiveBetaOnlyChallenges(
        createChallenges(true, true),
        createCatalog(undefined),
      ),
    ).toEqual([]);
  });

  it("warns on the Review step while the generated catalog marks Dread Mode", () => {
    expect(
      describeChallengeWarnings(createChallenges(true, false)),
    ).toBeUndefined();

    const warning = describeChallengeWarnings(createChallenges(true, true));

    expect(warning).toMatch(/logic/i);

    // Nothing more to assert once the main branch has Dread.
    if (isBetaOnlyOption(CHALLENGE_OPTION_KEYS.dreadMode)) {
      expect(warning).toContain("Dread Mode is only in the game's beta branch");
    }
  });
});
