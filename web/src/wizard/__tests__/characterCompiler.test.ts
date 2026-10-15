/**
 * @file Protects the Character Setup compiler and conditional question contract.
 *
 * These tests use player-facing answers and assert canonical option output, ensuring
 * changes to question semantics cannot silently drift from the generated schema. Add
 * representative compilation and visibility cases here whenever Character Setup gains
 * an answer, conditional prompt, or mapping rule.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { optionsToYaml } from "../../services/YamlService";
import { canDeselectBuiltInCharacter } from "../CharacterRoster";
import { createDefaultFillerAnswers } from "../FillerItem";
import { selectGuidedOptions } from "../GuidedOption";
import { createDefaultWizardAnswers } from "../WizardAnswers";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import { summarizeCharacterAnswers } from "../review";
import { characterSetupStep, getVisibleQuestionIds } from "../WizardStep";

/**
 * Creates a complete wizard answer model for Character Setup tests.
 *
 * @param availableCharacters - Character names to expose to the test arrangement.
 * @returns Complete answers with schema-derived filler defaults.
 */
function createTestAnswers(availableCharacters: readonly string[]) {
  // Keep unrelated Filler Setup state valid while Character behavior is isolated.
  const fillerAnswers = createDefaultFillerAnswers(optionCatalog);

  // Delegate Character defaults to the same public initializer used by the view.
  return createDefaultWizardAnswers(
    availableCharacters,
    fillerAnswers,
    optionCatalog,
  );
}

/**
 * Verifies the representative random-character setup from the feature specification.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesRandomCharacterSetup(): void {
  // Arrange player intent without referencing canonical option values.
  const answers = createTestAnswers(
    optionCatalog.options.characters!.valid_keys!,
  );
  answers.characters = {
    ...answers.characters,
    selectedCharacters: ["Ironclad", "Silent", "Defect"],
    selectionMode: "random",
    randomCharacterCount: 2,
    availability: "random",
    startingCharacter: null,
    goal: 2,
  };

  // Compile through the same root pipeline used by the view.
  const result = compileWizardAnswers(answers, optionCatalog);

  // Assert all five character mappings and complete-default reconstruction.
  expect(result.characters).toEqual(["Ironclad", "Silent", "Defect"]);
  expect(result.pick_num_characters).toBe(2);
  expect(result.num_chars_goal).toBe(2);
  expect(result.lock_characters).toBe("locked_random");
  expect(result.unlocked_character).toBe("");
  expect(Object.keys(result)).toHaveLength(optionCatalog.option_order.length);

  // Confirm review prose is derived from the same semantic answers.
  expect(summarizeCharacterAnswers(answers.characters)).toContain(
    "two of Ironclad, Silent, and Defect",
  );
}

/**
 * Verifies special `all` semantics and display-name-to-choice-name conversion.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesFixedStartingCharacter(): void {
  // Arrange a setup that exercises both zero-sentinel and fixed-choice mappings.
  const answers = createTestAnswers(["Ironclad", "Silent"]);
  answers.characters = {
    ...answers.characters,
    selectedCharacters: ["Ironclad", "Silent"],
    selectionMode: "all",
    randomCharacterCount: 1,
    availability: "fixed",
    startingCharacter: "Silent",
    goal: "all",
  };

  // Compile and assert the canonical technical representation.
  const result = compileWizardAnswers(answers, optionCatalog);
  expect(result.pick_num_characters).toBe(0);
  expect(result.num_chars_goal).toBe(0);
  expect(result.lock_characters).toBe("locked_fixed");
  expect(result.unlocked_character).toBe("silent");
}

/**
 * Verifies standard mode combines built-in and modded names with shared Ascensions.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesSharedModdedCharacterSetup(): void {
  // Arrange one built-in and one modded character under the standard YAML system.
  const answers = createTestAnswers(["Ironclad"]);
  answers.characters.moddedCharacters = [
    {
      name: "HermitMod",
      ascensions: {
        enabled: [1],
        ascensionDownsEnabled: false,
        downs: [],
      },
    },
  ];
  answers.characters.sharedAscensions = {
    enabled: [1, 3, 10],
    ascensionDownsEnabled: true,
    downs: [3],
  };
  answers.characters.availability = "fixed";
  answers.characters.startingCharacter = "HermitMod";

  // Compile the standard path through the complete validation pipeline.
  const result = compileWizardAnswers(answers, optionCatalog);
  const guided = selectGuidedOptions(result);

  // Built-in and modded arrays remain separate only at the Archipelago boundary.
  expect(result.characters).toEqual(["Ironclad"]);
  expect(result.modded_characters).toEqual(["HermitMod"]);
  expect(result.use_advanced_characters).toBe(false);
  expect(result.advanced_characters).toEqual({});
  expect(result.ascension).toEqual(["SwarmingElites", "Poverty", "DoubleBoss"]);
  expect(result.ascension_down).toEqual(["Poverty"]);
  expect(result.unlocked_character).toBe("HermitMod");

  // Review YAML should omit the advanced dictionary ignored by standard mode.
  expect(guided).toHaveProperty("modded_characters");
  expect(guided).not.toHaveProperty("advanced_characters");
}

/**
 * Verifies complete Ascension prefixes use Python's concise numeric syntax.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesSequentialAscensionShorthand(): void {
  // Arrange the state produced by clicking the A5 threshold while Downs are enabled.
  const answers = createTestAnswers(["Ironclad"]);
  answers.characters.sharedAscensions = {
    enabled: [1, 2, 3, 4, 5],
    ascensionDownsEnabled: true,
    downs: [1, 2, 3, 4, 5],
  };

  // Compile through the complete path so numeric OptionSet validation is exercised.
  const result = compileWizardAnswers(answers, optionCatalog);
  const yaml = optionsToYaml(selectGuidedOptions(result));

  // One number expands to the complete prefix, and five Downs cover all five actives.
  expect(result.ascension).toEqual([5]);
  expect(result.ascension_down).toEqual([5]);
  expect(yaml).toContain("  - 5");
}

/**
 * Verifies advanced mode writes one independent dictionary entry per roster member.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function compilesIndividualAscensionSetup(): void {
  // Arrange distinct settings for two built-ins and one modded character.
  const answers = createTestAnswers(["Ironclad", "Silent"]);
  answers.characters.selectedCharacters = ["Ironclad", "Silent"];
  answers.characters.ascensionMode = "individual";
  answers.characters.individualAscensions.Ironclad = {
    enabled: [1, 2],
    ascensionDownsEnabled: true,
    downs: [2],
  };
  answers.characters.individualAscensions.Silent = {
    enabled: [],
    ascensionDownsEnabled: false,
    downs: [],
  };
  answers.characters.moddedCharacters = [
    {
      name: "HermitMod",
      ascensions: {
        enabled: [10],
        ascensionDownsEnabled: true,
        downs: [10],
      },
    },
  ];

  // Compile the advanced path and select only active guided YAML fields.
  const result = compileWizardAnswers(answers, optionCatalog);
  const guided = selectGuidedOptions(result);
  const yaml = optionsToYaml(guided);

  // Ignored standard inputs are cleared and the dictionary preserves each setup.
  expect(result.use_advanced_characters).toBe(true);
  expect(result.characters).toEqual([]);
  expect(result.modded_characters).toEqual([]);
  expect(result.ascension).toEqual([]);
  expect(result.ascension_down).toEqual([]);
  expect(result.advanced_characters).toEqual({
    Ironclad: {
      ascension: [2],
      ascension_down: ["WearyTraveler"],
    },
    Silent: {
      ascension: [],
      ascension_down: [],
    },
    HermitMod: {
      ascension: ["DoubleBoss"],
      ascension_down: [1],
    },
  });

  // Review YAML exposes only the active advanced representation.
  expect(guided).toHaveProperty("advanced_characters");
  expect(guided).not.toHaveProperty("characters");
  expect(guided).not.toHaveProperty("modded_characters");
  expect(guided).not.toHaveProperty("ascension");
  expect(guided).not.toHaveProperty("ascension_down");

  // Nested dictionaries and intentionally empty lists must remain valid YAML shapes.
  expect(yaml).toContain('  "Ironclad":\n    "ascension":');
  expect(yaml).toContain('  "Silent":\n    "ascension": []');
  expect(yaml).toContain('    "ascension_down": []');
}

/**
 * Verifies an allocated modded slot can replace the final built-in character.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function allowsModdedOnlyCharacterSelection(): void {
  // The final built-in remains protected while no replacement slot exists.
  const answers = createTestAnswers(["Ironclad"]);
  expect(canDeselectBuiltInCharacter(answers.characters, "Ironclad")).toBe(
    false,
  );

  // Allocate the same blank row produced by the Modded Characters plus button.
  answers.characters.moddedCharacters = [
    {
      name: "",
      ascensions: {
        enabled: [],
        ascensionDownsEnabled: false,
        downs: [],
      },
    },
  ];

  // The vanilla portrait may now be removed while the visible row is completed.
  expect(canDeselectBuiltInCharacter(answers.characters, "Ironclad")).toBe(
    true,
  );
}

/**
 * Verifies semantic and schema-backed character errors fail loudly.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidCharacterAnswers(): void {
  // Arrange an unknown character not present in generated valid keys.
  const answers = createTestAnswers(["Ironclad"]);
  answers.characters.selectedCharacters = ["Watcher"];

  /** Compiles the current deliberately unknown-character answer. */
  function compileUnknownCharacter(): void {
    // Invoke the root compiler so the assertion observes its public failure behavior.
    compileWizardAnswers(answers, optionCatalog);
  }

  // Confirm generated character membership is enforced.
  expect(compileUnknownCharacter).toThrow("Unknown character");

  // Rearrange the same model to request a subset larger than its source pool.
  answers.characters.selectedCharacters = ["Ironclad"];
  answers.characters.selectionMode = "random";
  answers.characters.randomCharacterCount = 2;

  /** Compiles the current deliberately oversized random-character answer. */
  function compileOversizedRandomSelection(): void {
    // Invoke the root compiler so the assertion observes its public failure behavior.
    compileWizardAnswers(answers, optionCatalog);
  }

  // Confirm cross-answer selection constraints are enforced by the section compiler.
  expect(compileOversizedRandomSelection).toThrow("random character count");
}

/**
 * Verifies invalid modded names and impossible Ascension Downs fail clearly.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidModdedAndAscensionAnswers(): void {
  // An allocated modded row cannot compile until its internal ID is entered.
  const answers = createTestAnswers(["Ironclad"]);
  answers.characters.moddedCharacters = [
    {
      name: "",
      ascensions: {
        enabled: [1],
        ascensionDownsEnabled: false,
        downs: [],
      },
    },
  ];

  /** Compiles the deliberately incomplete modded-character row. */
  function compileEmptyModdedName(): void {
    // Exercise the public root compiler and its player-facing error contract.
    compileWizardAnswers(answers, optionCatalog);
  }

  expect(compileEmptyModdedName).toThrow("internal ID");

  // Modded names cannot collide with built-in names under case-insensitive lookup.
  answers.characters.moddedCharacters[0]!.name = "ironclad";

  /** Compiles the deliberately duplicated character roster. */
  function compileDuplicateCharacter(): void {
    // Exercise duplicate detection before an advanced dictionary could overwrite data.
    compileWizardAnswers(answers, optionCatalog);
  }

  expect(compileDuplicateCharacter).toThrow("must be unique");

  // An Ascension Down has no valid target when its matching Ascension is disabled.
  answers.characters.moddedCharacters = [];
  answers.characters.sharedAscensions = {
    enabled: [],
    ascensionDownsEnabled: true,
    downs: [1],
  };

  /** Compiles the deliberately orphaned Ascension Down. */
  function compileOrphanedAscensionDown(): void {
    // Exercise semantic validation owned by the character compiler.
    compileWizardAnswers(answers, optionCatalog);
  }

  expect(compileOrphanedAscensionDown).toThrow("requires Ascension A1");

  // A generated roster supports at most twenty modded characters.
  answers.characters.sharedAscensions = {
    enabled: [1],
    ascensionDownsEnabled: false,
    downs: [],
  };
  answers.characters.moddedCharacters = [];

  for (let index = 1; index <= 21; index += 1) {
    answers.characters.moddedCharacters.push({
      name: `Modded${index}`,
      ascensions: {
        enabled: [1],
        ascensionDownsEnabled: false,
        downs: [],
      },
    });
  }

  /** Compiles the deliberately oversized modded-character roster. */
  function compileTooManyModdedCharacters(): void {
    // Exercise the generated-roster limit independently of candidate row count.
    compileWizardAnswers(answers, optionCatalog);
  }

  expect(compileTooManyModdedCharacters).toThrow("no more than 20");
}

/**
 * Registers Character Setup compiler cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerCharacterCompilerTests(): void {
  // Group the representative mappings and failure contract under one suite.
  it(
    "compiles random selection, random unlocking, and a numeric goal",
    compilesRandomCharacterSetup,
  );
  it(
    "maps all and a fixed starting character to canonical values",
    compilesFixedStartingCharacter,
  );
  it(
    "compiles modded characters with shared Ascensions",
    compilesSharedModdedCharacterSetup,
  );
  it(
    "compacts sequential Ascension and Ascension Down selections",
    compilesSequentialAscensionShorthand,
  );
  it(
    "compiles independent advanced character Ascensions",
    compilesIndividualAscensionSetup,
  );
  it(
    "allows a modded slot to replace the final built-in character",
    allowsModdedOnlyCharacterSelection,
  );
  it(
    "rejects invalid schema-backed character and count values",
    rejectsInvalidCharacterAnswers,
  );
  it(
    "rejects invalid modded names and Ascension relationships",
    rejectsInvalidModdedAndAscensionAnswers,
  );
}

/**
 * Verifies dependent question visibility follows controlling semantic answers.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function revealsConditionalCharacterQuestions(): void {
  // Begin with modes whose dependent questions have no meaning.
  const answers = createTestAnswers(["Ironclad", "Silent"]);
  expect(getVisibleQuestionIds(characterSetupStep, answers)).not.toContain(
    "random-count",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).not.toContain(
    "starting-character",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "shared-ascensions",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "individual-ascensions",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).not.toContain(
    "modded-characters",
  );

  // Activate every controlling mode and confirm its dependent prompts enter the flow.
  answers.characters.selectionMode = "random";
  answers.characters.availability = "fixed";
  answers.characters.ascensionMode = "individual";
  answers.characters.moddedCharacters = [
    {
      name: "HermitMod",
      ascensions: {
        enabled: [1],
        ascensionDownsEnabled: false,
        downs: [],
      },
    },
  ];
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "random-count",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "starting-character",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "modded-characters",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).toContain(
    "individual-ascensions",
  );
  expect(getVisibleQuestionIds(characterSetupStep, answers)).not.toContain(
    "shared-ascensions",
  );
}

/**
 * Registers conditional-flow cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerConditionalQuestionTests(): void {
  // Keep flow behavior separate from technical compiler assertions.
  it(
    "only reveals dependent questions for their selected modes",
    revealsConditionalCharacterQuestions,
  );
}

// Register both behavioral areas using named, documented test callbacks.
describe("character wizard compiler", registerCharacterCompilerTests);
describe("conditional character questions", registerConditionalQuestionTests);

describe("per-character progressive starters", () => {
  it("preserves old output when overrides are missing or inherited", () => {
    const answers = createTestAnswers(["Ironclad"]);
    const baseline = compileWizardAnswers(answers, optionCatalog);
    answers.characters.individualStarterOverrides = {
      Ironclad: {
        progressiveStarterCard: "inherit",
        progressiveStarterRelic: "inherit",
      },
    };
    expect(compileWizardAnswers(answers, optionCatalog)).toEqual(baseline);
    delete answers.characters.individualStarterOverrides;
    expect(compileWizardAnswers(answers, optionCatalog)).toEqual(baseline);
    answers.characters.ascensionMode = "individual";
    expect(
      compileWizardAnswers(answers, optionCatalog).advanced_characters,
    ).toEqual({
      Ironclad: { ascension: [1], ascension_down: [] },
    });
  });

  it("uses shared Ascensions with explicit vanilla and modded starter overrides", () => {
    const answers = createTestAnswers(["Ironclad", "Silent"]);
    answers.characters.selectedCharacters = ["Ironclad", "Silent"];
    answers.characters.sharedAscensions = {
      enabled: [1, 2],
      ascensionDownsEnabled: true,
      downs: [1],
    };
    answers.characters.moddedCharacters = [
      {
        name: "INTOTHESPIREVERSE-SHADOW_IRONCLAD",
        ascensions: { enabled: [9], ascensionDownsEnabled: false, downs: [] },
        starterOverrides: {
          progressiveStarterCard: "enabled",
          progressiveStarterRelic: "disabled",
        },
      },
    ];
    answers.characters.individualStarterOverrides = {
      Ironclad: { progressiveStarterCard: "disabled" },
    };
    answers.checksAndRewards.startingEquipment = {
      progressiveStarterCard: true,
      progressiveStarterRelic: true,
    };
    const result = compileWizardAnswers(answers, optionCatalog);
    expect(result.use_advanced_characters).toBe(true);
    expect(result.advanced_characters).toEqual({
      Ironclad: {
        ascension: [2],
        ascension_down: ["SwarmingElites"],
        progressive_starter_card: false,
      },
      Silent: { ascension: [2], ascension_down: ["SwarmingElites"] },
      "INTOTHESPIREVERSE-SHADOW_IRONCLAD": {
        ascension: [2],
        ascension_down: ["SwarmingElites"],
        progressive_starter_card: true,
        progressive_starter_relic: false,
      },
    });
    const guided = selectGuidedOptions(result);
    expect(guided).not.toHaveProperty("ascension");
    expect(guided).not.toHaveProperty("modded_characters");
    expect(optionsToYaml(guided)).toContain(
      '"progressive_starter_card": false',
    );
    expect(optionsToYaml(guided)).not.toContain("null");
    expect(summarizeCharacterAnswers(answers.characters)).toContain(
      "Ironclad: progressive starter card disabled",
    );
  });

  it("preserves individual Ascensions and keeps an enabled override when globals are off", () => {
    const answers = createTestAnswers(["Ironclad"]);
    answers.characters.ascensionMode = "individual";
    answers.characters.individualAscensions.Ironclad = {
      enabled: [3],
      ascensionDownsEnabled: false,
      downs: [],
    };
    answers.characters.individualStarterOverrides = {
      Ironclad: { progressiveStarterRelic: "enabled" },
    };
    const result = compileWizardAnswers(answers, optionCatalog);
    expect(result.progressive_starter_relic).toBe(false);
    expect(result.advanced_characters).toEqual({
      Ironclad: {
        ascension: ["Poverty"],
        ascension_down: [],
        progressive_starter_relic: true,
      },
    });
  });

  it("disables overrides without Floor Checks and restores them when reenabled", () => {
    const answers = createTestAnswers(["Ironclad"]);
    answers.characters.individualStarterOverrides = {
      Ironclad: { progressiveStarterCard: "enabled" },
    };
    answers.checksAndRewards.checks.includeFloorChecks = false;
    expect(
      compileWizardAnswers(answers, optionCatalog).use_advanced_characters,
    ).toBe(false);
    answers.characters.ascensionMode = "individual";
    expect(
      compileWizardAnswers(answers, optionCatalog).advanced_characters,
    ).toEqual({
      Ironclad: { ascension: [1], ascension_down: [] },
    });
    expect(summarizeCharacterAnswers(answers.characters, false)).toContain(
      "inactive because Floor Checks are off",
    );
    answers.checksAndRewards.checks.includeFloorChecks = true;
    expect(
      compileWizardAnswers(answers, optionCatalog).advanced_characters,
    ).toMatchObject({
      Ironclad: { progressive_starter_card: true },
    });
  });

  it("ignores deselected characters and removes advanced mode when overrides return to inherit", () => {
    const answers = createTestAnswers(["Ironclad", "Silent"]);
    answers.characters.individualStarterOverrides = {
      Silent: { progressiveStarterCard: "enabled" },
    };
    expect(
      compileWizardAnswers(answers, optionCatalog).use_advanced_characters,
    ).toBe(false);
    answers.characters.individualStarterOverrides.Ironclad = {
      progressiveStarterRelic: "disabled",
    };
    expect(
      compileWizardAnswers(answers, optionCatalog).use_advanced_characters,
    ).toBe(true);
    answers.characters.individualStarterOverrides.Ironclad.progressiveStarterRelic =
      "inherit";
    expect(
      compileWizardAnswers(answers, optionCatalog).use_advanced_characters,
    ).toBe(false);
  });

  it("keeps modded row overrides attached when its ID changes", () => {
    const answers = createTestAnswers(["Ironclad"]);
    const character = {
      name: "OLD_ID",
      ascensions: answers.characters.sharedAscensions,
      starterOverrides: { progressiveStarterCard: "enabled" as const },
    };
    answers.characters.moddedCharacters = [character];
    character.name = "NEW_ID";
    const result = compileWizardAnswers(answers, optionCatalog);
    expect(result.advanced_characters).toHaveProperty(
      "NEW_ID.progressive_starter_card",
      true,
    );
    expect(result.advanced_characters).not.toHaveProperty("OLD_ID");
  });
});

describe("expanded modded rosters", () => {
  it("preserves twenty candidate IDs in both basic and advanced YAML order", () => {
    const names = [
      "WATCHER-WATCHER",
      "GUARDIAN-GUARDIAN",
      ...Array.from(
        { length: 18 },
        (_, i) => `Unknown-${String(18 - i).padStart(2, "0")}`,
      ),
    ];
    for (const mode of ["shared", "individual"] as const) {
      const answers = createTestAnswers(["Ironclad"]);
      answers.characters.selectedCharacters = [];
      answers.characters.startingCharacter = null;
      answers.characters.availability = "all";
      answers.characters.ascensionMode = mode;
      answers.characters.selectionMode = "all";
      answers.characters.goal = "all";
      answers.characters.moddedCharacters = names.map((name) => ({
        name,
        ascensions: { enabled: [1], ascensionDownsEnabled: false, downs: [] },
      }));
      const result = compileWizardAnswers(answers, optionCatalog);
      if (mode === "shared") {
        expect(result.modded_characters).toEqual(names);
      } else {
        expect(Object.keys(result.advanced_characters as object)).toEqual(
          names,
        );
      }
      const yaml = optionsToYaml(result);
      const positions = names.map((name) => yaml.indexOf(name));
      expect(positions).toEqual([...positions].sort((a, b) => a - b));
      expect(positions.every((position) => position >= 0)).toBe(true);
    }
  });

  it("accepts a larger candidate list only when the selected roster can fit", () => {
    const answers = createTestAnswers(["Ironclad"]);
    answers.characters.selectedCharacters = [];
    answers.characters.startingCharacter = null;
    answers.characters.availability = "all";
    answers.characters.selectionMode = "random";
    answers.characters.randomCharacterCount = 20;
    answers.characters.goal = "all";
    answers.characters.moddedCharacters = Array.from(
      { length: 25 },
      (_, i) => ({
        name: `Mod-${i}`,
        ascensions: { enabled: [1], ascensionDownsEnabled: false, downs: [] },
      }),
    );
    const result = compileWizardAnswers(answers, optionCatalog);
    expect(result.pick_num_characters).toBe(20);
    expect(result.modded_characters).toHaveLength(25);
    answers.characters.randomCharacterCount = 21;
    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      "no more than 20",
    );
    answers.characters.selectedCharacters = ["Ironclad"];
    expect(
      compileWizardAnswers(answers, optionCatalog).pick_num_characters,
    ).toBe(21);
  });
});
