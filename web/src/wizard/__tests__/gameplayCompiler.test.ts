/**
 * @file Protects the Run, combined Checks & Rewards, and Death Link contracts.
 *
 * These tests verify that newly guided game settings begin from generated defaults,
 * compile representative player intent to canonical option values, enforce the
 * nested Shop Sanity constraint, and expose dependent questions only when useful.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { createDefaultFillerAnswers } from "../FillerItem";
import { getGuidedOptionKeys } from "../GuidedOption";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import {
  CHECK_OPTION_KEYS,
  DEATH_LINK_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_OPTION_KEYS,
} from "../WizardOptionKey";
import {
  visibleCheckQuestionIds,
  visibleDeathLinkQuestionIds,
} from "../WizardStep";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";

/**
 * Creates complete schema-derived answers for gameplay-section tests.
 *
 * @returns A valid wizard answer model initialized from the generated catalog.
 */
function createTestAnswers() {
  // Keep the existing Filler section valid while testing other compilers.
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  // Use the same public initializer as the production wizard view.
  return createDefaultWizardAnswers(["Ironclad"], fillerAnswers, optionCatalog);
}

/**
 * Verifies every new guided section round-trips its generated defaults.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesGeneratedGameplayDefaults(): void {
  // Compile a newly initialized wizard without changing player-facing controls.
  const options = compileWizardAnswers(createTestAnswers(), optionCatalog);
  const optionKeys = [
    ...Object.values(RUN_OPTION_KEYS),
    ...Object.values(CHECK_OPTION_KEYS),
    ...Object.values(SHOP_OPTION_KEYS),
    ...Object.values(DEATH_LINK_OPTION_KEYS),
  ];

  // Every compiled field should still equal its regenerated source-of-truth default.
  for (const optionKey of optionKeys) {
    expect(options[optionKey]).toEqual(
      optionCatalog.options[optionKey]!.default,
    );
  }
}

/**
 * Verifies representative choices across all three visible gameplay sections.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesConfiguredGameplaySections(): void {
  // Arrange non-default intent in each new player-facing answer section.
  const answers = createTestAnswers();
  answers.run = {
    ancientRelicLocation: "start_of_act",
    ancientRelicPool: "true_chaos",
    relicChoiceCount: 5,
    neowSanity: true,
    seeded: true,
    progressionBalancing: 99,
    accessibility: "minimal",
  };
  answers.checksAndRewards.checks = {
    includeFloorChecks: false,
    campfireSanity: true,
    goldSanity: true,
    potionSanity: true,
    shuffleAllCards: true,
  };
  answers.checksAndRewards.shop = {
    enabled: true,
    cardSlots: 5,
    neutralCardSlots: 2,
    relicSlots: 3,
    potionSlots: 3,
    removeSlots: true,
    costs: "Tiered",
  };
  answers.deathLink = {
    enabled: true,
    receiveFragment: false,
    receiveDamage: true,
    damagePercent: 37,
    beKilled: false,
  };

  // Compile through the same complete pipeline used by review and YAML generation.
  const options = compileWizardAnswers(answers, optionCatalog);

  // Assert representative output from every new section boundary.
  expect(options.ancient_relic_location).toBe("start_of_act");
  expect(options.ancient_relic_pool).toBe("true_chaos");
  expect(options.relic_choice_count).toBe(5);
  expect(options.neow_sanity).toBe(true);
  expect(options.seeded).toBe(true);
  expect(options.progression_balancing).toBe(99);
  expect(options.accessibility).toBe("minimal");
  expect(options.include_floor_checks).toBe(false);
  expect(options.campfire_sanity).toBe(true);
  expect(options.gold_sanity).toBe(true);
  expect(options.potion_sanity).toBe(true);
  expect(options.shuffle_all_cards).toBe(true);
  expect(options.shop_sanity).toBe(true);
  expect(options.shop_card_slots).toBe(5);
  expect(options.shop_neutral_card_slots).toBe(2);
  expect(options.shop_relic_slots).toBe(3);
  expect(options.shop_potion_slots).toBe(3);
  expect(options.shop_remove_slots).toBe(true);
  expect(options.shop_sanity_costs).toBe("Tiered");
  expect(options.death_link).toBe(true);
  expect(options.enable_death_fragments).toBe(false);
  expect(options.death_link_damage_percent).toBe(37);
}

/**
 * Verifies lethal received Death Links override every nonlethal preference.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesLethalDeathLinkMode(): void {
  // Preserve contradictory preferences to prove the compiler makes lethal mode final.
  const answers = createTestAnswers();
  answers.deathLink = {
    enabled: true,
    receiveFragment: true,
    receiveDamage: true,
    damagePercent: 25,
    beKilled: true,
  };

  // Compile through the complete pipeline used by the YAML preview.
  const options = compileWizardAnswers(answers, optionCatalog);

  // Python treats 100% damage as death, and fragments are disabled in lethal mode.
  expect(options.death_link).toBe(true);
  expect(options.enable_death_fragments).toBe(false);
  expect(options.death_link_damage_percent).toBe(100);
}

/**
 * Verifies disabled nonlethal damage compiles to Python's technical zero value.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesDeathLinkWithoutDamage(): void {
  // Enable Death Link and its fragment effect while leaving damage unselected.
  const answers = createTestAnswers();
  answers.deathLink.enabled = true;
  answers.deathLink.receiveFragment = true;
  answers.deathLink.receiveDamage = false;
  answers.deathLink.damagePercent = 64;

  // The preserved slider value must not leak into generated options while disabled.
  const options = compileWizardAnswers(answers, optionCatalog);
  expect(options.enable_death_fragments).toBe(true);
  expect(options.death_link_damage_percent).toBe(0);
}

/**
 * Verifies enabled Shop Sanity cannot silently produce zero locations.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsEmptyEnabledShop(): void {
  // Match the contradictory state that the Python world would otherwise disable.
  const answers = createTestAnswers();
  answers.checksAndRewards.shop.enabled = true;
  answers.checksAndRewards.shop.cardSlots = 0;
  answers.checksAndRewards.shop.neutralCardSlots = 0;
  answers.checksAndRewards.shop.relicSlots = 0;
  answers.checksAndRewards.shop.potionSlots = 0;
  answers.checksAndRewards.shop.removeSlots = false;

  /** Compiles the deliberately empty enabled shop. */
  function compileEmptyShop(): void {
    // Invoke the public root compiler so the assertion observes its error contract.
    compileWizardAnswers(answers, optionCatalog);
  }

  // Player intent should fail clearly instead of being silently rewritten.
  expect(compileEmptyShop).toThrow("Enable at least one shop slot");
}

/**
 * Verifies Range options and active Death Link damage reject fractional values.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsFractionalRangeAnswers(): void {
  // Exercise generic generated-schema validation through progression balancing.
  const fractionalProgressionAnswers = createTestAnswers();
  fractionalProgressionAnswers.run.progressionBalancing = 50.5;

  /** Compiles the deliberately fractional progression value. */
  function compileFractionalProgression(): void {
    // Invoke the root compiler so final generated validation observes the value.
    compileWizardAnswers(fractionalProgressionAnswers, optionCatalog);
  }

  // Exercise the Death Link compiler's more specific 1-100 semantic validation.
  const fractionalDamageAnswers = createTestAnswers();
  fractionalDamageAnswers.deathLink.enabled = true;
  fractionalDamageAnswers.deathLink.receiveDamage = true;
  fractionalDamageAnswers.deathLink.damagePercent = 12.5;

  /** Compiles the deliberately fractional Death Link damage percentage. */
  function compileFractionalDamage(): void {
    // Invoke the same complete path used by review navigation.
    compileWizardAnswers(fractionalDamageAnswers, optionCatalog);
  }

  // Both UX promises require whole-number output.
  expect(compileFractionalProgression).toThrow("must be a whole number");
  expect(compileFractionalDamage).toThrow(
    "Death Link damage must be a whole number",
  );
}

/**
 * Verifies dependent Shop and Death Link questions follow their controlling toggles.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function revealsDependentGameplayQuestions(): void {
  // Defaults disable both sections, leaving only their controlling questions visible.
  const answers = createTestAnswers();
  expect(visibleCheckQuestionIds(answers)).toEqual([
    "check-types",
    "filler-weights",
  ]);
  expect(visibleDeathLinkQuestionIds(answers)).toEqual(["death-link-enabled"]);

  // Enabling each section should reveal every dependent configuration question.
  answers.checksAndRewards.shop.enabled = true;
  answers.deathLink.enabled = true;
  expect(visibleCheckQuestionIds(answers)).toEqual([
    "check-types",
    "shop-slots",
    "shop-removal",
    "shop-costs",
    "filler-weights",
  ]);
  expect(visibleDeathLinkQuestionIds(answers)).toEqual([
    "death-link-enabled",
    "death-link-effects",
  ]);

  // The percentage question appears only for active, nonlethal damage.
  answers.deathLink.receiveDamage = true;
  expect(visibleDeathLinkQuestionIds(answers)).toEqual([
    "death-link-enabled",
    "death-link-effects",
    "death-link-damage",
  ]);

  // Lethal mode hides the percentage because the compiler supplies its fixed 100%.
  answers.deathLink.beKilled = true;
  expect(visibleDeathLinkQuestionIds(answers)).toEqual([
    "death-link-enabled",
    "death-link-effects",
  ]);
}

/**
 * Verifies guided YAML ownership contains no duplicate option keys.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function keepsGuidedOptionOwnershipUnique(): void {
  // Read the ordered registry exactly as the review YAML selector does.
  const optionKeys = getGuidedOptionKeys();

  // A duplicate would hide one section's output during object construction.
  expect(new Set(optionKeys).size).toBe(optionKeys.length);
}

/**
 * Verifies every game-specific generated option is owned by the guided wizard.
 *
 * @returns Nothing; Vitest records assertion failures.
 * @remarks The two guided Archipelago defaults are checked separately from this set.
 */
function coversEveryGameSpecificOption(): void {
  // Collect all generated options except inherited generic Archipelago template fields.
  const generatedGameOptionKeys: string[] = [];

  for (const optionKey of optionCatalog.option_order) {
    if (
      optionCatalog.options[optionKey]?.group !== "Common Archipelago Options"
    ) {
      generatedGameOptionKeys.push(optionKey);
    }
  }

  // Retain only game-specific guided keys; common AP settings are tested separately.
  const decidedOptionKeys: string[] = [];

  for (const optionKey of getGuidedOptionKeys()) {
    if (
      optionCatalog.options[optionKey]?.group !== "Common Archipelago Options"
    ) {
      decidedOptionKeys.push(optionKey);
    }
  }

  // Compare key sets so navigation order may differ from generated serialization order.
  expect([...decidedOptionKeys].sort()).toEqual(
    [...generatedGameOptionKeys].sort(),
  );
}

/**
 * Registers gameplay vertical-slice cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerGameplayCompilerTests(): void {
  // Cover default fidelity, explicit mappings, dependencies, and key ownership.
  it("round-trips generated defaults", compilesGeneratedGameplayDefaults);
  it(
    "compiles configured gameplay sections",
    compilesConfiguredGameplaySections,
  );
  it("compiles lethal Death Link as 100% damage", compilesLethalDeathLinkMode);
  it(
    "compiles Death Link without damage as 0%",
    compilesDeathLinkWithoutDamage,
  );
  it("rejects enabled Shop Sanity with no locations", rejectsEmptyEnabledShop);
  it("rejects fractional Range answers", rejectsFractionalRangeAnswers);
  it(
    "reveals dependent questions when enabled",
    revealsDependentGameplayQuestions,
  );
  it("keeps guided option ownership unique", keepsGuidedOptionOwnershipUnique);
  it("covers every game-specific option", coversEveryGameSpecificOption);
}

// Register documented callbacks as one focused expanded-wizard suite.
describe("gameplay wizard compilers", registerGameplayCompilerTests);
