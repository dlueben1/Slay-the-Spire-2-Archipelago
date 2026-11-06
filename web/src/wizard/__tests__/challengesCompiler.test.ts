/**
 * @file Protects the Challenges section contract.
 *
 * These tests verify that challenge answers start from generated defaults, compile to
 * canonical option values, keep the Run Modifier catalog in step with the schema, and
 * that the review shows its warning only while a challenge is on.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { createDefaultFillerAnswers } from "../FillerItem";
import { getGuidedOptionKeys } from "../GuidedOption";
import { RUN_MODIFIERS } from "../RunModifier";
import {
  CHALLENGE_OPT_IN_BANNERS,
  tickChallengeOptInBanner,
} from "../challengeOptIn";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import {
  CHALLENGE_OPTION_KEYS,
  DEATH_LINK_OPTION_KEYS,
} from "../WizardOptionKey";
import {
  challengeSetupStep,
  getVisibleQuestionIds,
  wizardSteps,
} from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import {
  buildWizardReviewSections,
  summarizeChallengeAnswers,
} from "../review";

/**
 * Creates complete schema-derived answers for Challenges tests.
 *
 * @returns A valid wizard answer model initialized from the generated catalog.
 */
function createTestAnswers() {
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers, optionCatalog);
}

/**
 * Finds the Challenges review section.
 *
 * @param answers - Complete wizard answers.
 * @returns The Challenges review section.
 */
function getChallengesReview(answers: ReturnType<typeof createTestAnswers>) {
  const section = buildWizardReviewSections(answers).find(
    (candidate) => candidate.title === "Challenges",
  );

  expect(section).toBeDefined();
  return section!;
}

/**
 * Verifies every challenge option round-trips its generated default.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesGeneratedChallengeDefaults(): void {
  const options = compileWizardAnswers(createTestAnswers(), optionCatalog);

  for (const optionKey of Object.values(CHALLENGE_OPTION_KEYS)) {
    expect(options[optionKey]).toEqual(
      optionCatalog.options[optionKey]!.default,
    );
  }
}

/**
 * Verifies configured challenges compile to canonical values in catalog order.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesConfiguredChallenges(): void {
  const answers = createTestAnswers();
  answers.challenges = {
    enabled: true,
    // Out of catalog order on purpose: the compiler restores table order.
    runModifiers: ["FrailStart", "RisingStrength"],
    enemyHpScaling: 15,
    enemyDamageScaling: 10,
    hiddenIntents: "one",
    hiddenHealth: "two",
    strictCardRewards: true,
  };

  const options = compileWizardAnswers(answers, optionCatalog);

  expect(options[CHALLENGE_OPTION_KEYS.runModifiers]).toEqual([
    "RisingStrength",
    "FrailStart",
  ]);
  expect(options[CHALLENGE_OPTION_KEYS.enemyHpScaling]).toBe(15);
  expect(options[CHALLENGE_OPTION_KEYS.enemyDamageScaling]).toBe(10);
  expect(options[CHALLENGE_OPTION_KEYS.hiddenIntents]).toBe(1);
  expect(options[CHALLENGE_OPTION_KEYS.hiddenHealth]).toBe(2);
  expect(options[CHALLENGE_OPTION_KEYS.strictCardRewards]).toBe(true);
}

/**
 * Verifies ticked challenges are ignored while the opt-in switch is off.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function ignoresChallengesUntilOptedIn(): void {
  const answers = createTestAnswers();

  expect(answers.challenges.enabled).toBe(false);

  answers.challenges = {
    enabled: false,
    runModifiers: ["CursedRun"],
    enemyHpScaling: 12,
    enemyDamageScaling: 8,
    hiddenIntents: "two",
    hiddenHealth: "one",
    strictCardRewards: true,
  };

  const options = compileWizardAnswers(answers, optionCatalog);

  for (const optionKey of Object.values(CHALLENGE_OPTION_KEYS)) {
    expect(options[optionKey]).toEqual(
      optionCatalog.options[optionKey]!.default,
    );
  }
  expect(getChallengesReview(answers).warning).toBeUndefined();
}

/**
 * Verifies the challenge questions appear only after opting in.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function showsQuestionsOnlyAfterOptingIn(): void {
  const answers = createTestAnswers();

  expect(getVisibleQuestionIds(challengeSetupStep, answers)).toEqual([
    "challenges-enabled",
  ]);

  answers.challenges.enabled = true;

  expect(getVisibleQuestionIds(challengeSetupStep, answers)).toEqual([
    "challenges-enabled",
    "run-modifiers",
    "enemy-hp-scaling",
    "enemy-damage-scaling",
    "hidden-intents",
    "hidden-health",
    "strict-card-rewards",
  ]);
}

/**
 * Verifies the display catalog lists exactly the modifiers the schema accepts.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function coversEveryGeneratedRunModifier(): void {
  const generated =
    optionCatalog.options[CHALLENGE_OPTION_KEYS.runModifiers]!.valid_keys ?? [];
  const displayed = RUN_MODIFIERS.map((modifier) => modifier.optionName);

  expect([...displayed].sort()).toEqual([...generated].sort());
}

/**
 * Verifies challenge keys appear in guided YAML right after Death Link.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function placesChallengeKeysAfterDeathLink(): void {
  const keys = getGuidedOptionKeys();
  const lastDeathLinkKey = keys.indexOf(DEATH_LINK_OPTION_KEYS.damagePercent);

  const challengeKeys = Object.values(CHALLENGE_OPTION_KEYS);

  expect(
    keys.slice(lastDeathLinkKey + 1, lastDeathLinkKey + 1 + challengeKeys.length),
  ).toEqual(challengeKeys);
}

/**
 * Verifies the step carries its warning and sits between Death Link and Progression.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function warnsBeforeAnyChallenge(): void {
  const stepIds = wizardSteps.map((step) => step.id);

  expect(stepIds.indexOf("challenges")).toBe(stepIds.indexOf("death-link") + 1);
  expect(stepIds.indexOf("progression")).toBe(
    stepIds.indexOf("challenges") + 1,
  );
  expect(challengeSetupStep.warning?.title).toBeTruthy();
  expect(challengeSetupStep.warning?.description).toMatch(/logic/i);
}

/**
 * Verifies the review warns only while a challenge is on.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function reviewWarnsOnlyWithActiveChallenges(): void {
  const answers = createTestAnswers();

  expect(getChallengesReview(answers).warning).toBeUndefined();
  expect(summarizeChallengeAnswers(answers.challenges)).toBe(
    "No challenges are on.",
  );

  answers.challenges.enabled = true;
  answers.challenges.enemyDamageScaling = 10;

  expect(getChallengesReview(answers).warning).toMatch(/logic/i);
  expect(summarizeChallengeAnswers(answers.challenges)).toContain(
    "10% more damage in Acts 2 and 3",
  );

  answers.challenges.hiddenHealth = "two";

  expect(summarizeChallengeAnswers(answers.challenges)).toContain(
    "either of two Health Sights",
  );
}

/**
 * Verifies challenges turn on only once all three banners are ticked, in order.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function asksThreeTimesBeforeOptingIn(): void {
  expect(CHALLENGE_OPT_IN_BANNERS).toHaveLength(3);

  const first = tickChallengeOptInBanner(0, 0, true);
  const second = tickChallengeOptInBanner(first.ticked, 1, true);
  const third = tickChallengeOptInBanner(second.ticked, 2, true);

  expect(first).toEqual({ ticked: 1, enabled: false });
  expect(second).toEqual({ ticked: 2, enabled: false });
  expect(third).toEqual({ ticked: 3, enabled: true });
  // A banner that is not next in line cannot be ticked.
  expect(tickChallengeOptInBanner(0, 2, true)).toEqual({
    ticked: 0,
    enabled: false,
  });
}

/**
 * Verifies unticking any banner turns challenges off and clears the banners after it.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function untickingABannerTurnsChallengesOff(): void {
  expect(tickChallengeOptInBanner(3, 1, false)).toEqual({
    ticked: 1,
    enabled: false,
  });
  expect(tickChallengeOptInBanner(2, 0, false)).toEqual({
    ticked: 0,
    enabled: false,
  });
}

/**
 * Registers Challenges compiler, catalog, and review tests.
 *
 * @returns Nothing; Vitest owns execution.
 */
function registerChallengesTests(): void {
  it("round-trips generated defaults", compilesGeneratedChallengeDefaults);
  it("compiles configured challenges", compilesConfiguredChallenges);
  it("ignores ticked challenges until opted in", ignoresChallengesUntilOptedIn);
  it(
    "shows challenge questions only after opting in",
    showsQuestionsOnlyAfterOptingIn,
  );
  it("lists every generated Run Modifier", coversEveryGeneratedRunModifier);
  it(
    "places challenge keys after Death Link",
    placesChallengeKeysAfterDeathLink,
  );
  it("warns on the Challenges step", warnsBeforeAnyChallenge);
  it("asks three times before opting in", asksThreeTimesBeforeOptingIn);
  it(
    "turns challenges off when a banner is unticked",
    untickingABannerTurnsChallengesOff,
  );
  it(
    "warns in Review only while a challenge is on",
    reviewWarnsOnlyWithActiveChallenges,
  );
}

describe("challenges wizard section", registerChallengesTests);
