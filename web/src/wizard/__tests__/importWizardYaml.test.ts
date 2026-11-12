/**
 * @file Protects loading an existing YAML back into the builder.
 *
 * The importer is the inverse of the builder pipeline, so the central property is a
 * round trip: answers → compiled options → builder YAML → imported answers → compiled
 * options must give the same options. Further cases cover the spellings Archipelago
 * accepts in hand-written files (weights, `random`, multiple documents, aliases) and
 * the errors and warnings a player sees when something cannot be loaded.
 */

import { describe, expect, it } from "vitest";
import spire2Template from "../../../../world/spire2/docs/Spire2-template.yaml?raw";
import { optionCatalog } from "../../generated/optionCatalog";
import { buildWizardYaml, WIZARD_GAME_NAME } from "../../services/YamlService";
import { builderYamlComments } from "../../services/YamlTemplateComments";
import { getEligibleSpecificRelics } from "../BonusRelicData";
import { createDefaultFillerAnswers } from "../FillerItem";
import { selectGuidedOptions } from "../GuidedOption";
import {
  createDefaultWizardAnswers,
  type WizardAnswers,
} from "../WizardAnswers";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import { importWizardYaml } from "../import/importWizardYaml";

/** Builds the default answers the builder starts with, with a player name. */
function createTestAnswers(): WizardAnswers {
  const answers = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
  answers.playerName = "Climber";
  return answers;
}

/** Makes the YAML the builder would download for some answers. */
function buildYaml(answers: WizardAnswers): string {
  const compiled = compileWizardAnswers(answers, optionCatalog);
  return buildWizardYaml(
    answers.playerName,
    selectGuidedOptions(compiled),
    builderYamlComments("https://builder.test/#/builder"),
  );
}

/** Wraps game options in a minimal hand-written YAML document. */
function handWritten(gameOptions: string, name = "Hand"): string {
  return [
    `name: ${name}`,
    `game: ${WIZARD_GAME_NAME}`,
    `${WIZARD_GAME_NAME}:`,
    ...gameOptions.split("\n").map((line) => `  ${line}`),
    "",
  ].join("\n");
}

/** Reads an option's display name from the catalog. */
function displayName(key: string): string {
  return optionCatalog.options[key]!.display_name;
}

/** Answer sets that together touch every builder section. */
const ROUND_TRIP_CASES: [string, (answers: WizardAnswers) => void][] = [
  ["defaults", () => {}],
  [
    "a random pick from built-in and modded characters with shared Downs",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Ironclad", "Silent", "Defect"];
      characters.moddedCharacters = [
        { name: "Watcher", ascensions: characters.sharedAscensions },
        {
          name: "SOME_MOD-CUSTOM_HERO",
          ascensions: characters.sharedAscensions,
        },
      ];
      characters.selectionMode = "random";
      characters.randomCharacterCount = 3;
      characters.goal = 2;
      characters.availability = "all";
      characters.sharedAscensions = {
        enabled: [1, 3, 5],
        ascensionDownsEnabled: true,
        downs: [3, 5],
      };
    },
  ],
  [
    "a fixed built-in start with every Ascension Down",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Silent", "Regent"];
      characters.availability = "fixed";
      characters.startingCharacter = "Regent";
      characters.sharedAscensions = {
        enabled: [1, 2, 3, 4],
        ascensionDownsEnabled: true,
        downs: [1, 2, 3, 4],
      };
    },
  ],
  [
    "a fixed modded start and no Ascensions",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Ironclad"];
      characters.moddedCharacters = [
        {
          name: "Hermit",
          ascensions: { enabled: [], ascensionDownsEnabled: false, downs: [] },
        },
      ];
      characters.availability = "fixed";
      characters.startingCharacter = "Hermit";
      characters.sharedAscensions = {
        enabled: [],
        ascensionDownsEnabled: false,
        downs: [],
      };
    },
  ],
  [
    "individual Ascensions with Downs",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Ironclad", "Necrobinder"];
      characters.ascensionMode = "individual";
      characters.individualAscensions.Ironclad = {
        enabled: [1, 2, 7],
        ascensionDownsEnabled: true,
        downs: [7],
      };
      characters.individualAscensions.Necrobinder = {
        enabled: [],
        ascensionDownsEnabled: false,
        downs: [],
      };
      characters.moddedCharacters = [
        {
          name: "Captain",
          ascensions: {
            enabled: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            ascensionDownsEnabled: true,
            downs: [9, 10],
          },
        },
      ];
    },
  ],
  [
    "shared Ascensions with starter overrides",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Ironclad", "Defect"];
      characters.individualStarterOverrides = {
        Ironclad: { progressiveStarterCard: "enabled" },
        Defect: {
          progressiveStarterRelic: "disabled",
          progressiveStarterCard: "inherit",
        },
      };
      characters.moddedCharacters = [
        {
          name: "Slime Boss",
          ascensions: characters.sharedAscensions,
          starterOverrides: { progressiveStarterRelic: "enabled" },
        },
      ];
      answers.checksAndRewards.checks.includeFloorChecks = true;
    },
  ],
  [
    "individual Ascensions with starter overrides",
    (answers) => {
      const characters = answers.characters;
      characters.selectedCharacters = ["Silent"];
      characters.startingCharacter = "Silent";
      characters.ascensionMode = "individual";
      characters.individualAscensions.Silent = {
        enabled: [2, 4],
        ascensionDownsEnabled: false,
        downs: [],
      };
      characters.individualStarterOverrides = {
        Silent: { progressiveStarterCard: "disabled" },
      };
      answers.checksAndRewards.checks.includeFloorChecks = true;
    },
  ],
  [
    "every check, Shop Sanity, Random Shop Cards and Run Settings changed",
    (answers) => {
      const rewards = answers.checksAndRewards;
      const checks = rewards.checks;
      checks.neowSanity = !checks.neowSanity;
      checks.includeFloorChecks = true;
      checks.campfireSanity = !checks.campfireSanity;
      checks.goldSanity = !checks.goldSanity;
      checks.potionSanity = !checks.potionSanity;
      checks.shuffleAllCards = !checks.shuffleAllCards;
      rewards.startingEquipment.progressiveStarterCard = true;
      rewards.startingEquipment.progressiveStarterRelic = true;
      rewards.ancients = {
        relicLocation: "start_of_act",
        relicPool: "true_chaos",
      };
      rewards.shop = {
        enabled: true,
        cardSlots: 5,
        neutralCardSlots: 0,
        relicSlots: 3,
        potionSlots: 0,
        removeSlots: true,
        costs: "Tiered",
      };
      rewards.shopCards.enabled = true;
      rewards.shopCards.weights.attack = 0;
      rewards.shopCards.weights.rare = 3;
      rewards.shopRelics.enabled = true;
      rewards.shopRelics.weights.ancient = 3;
      rewards.shopRelics.weights.common = 0;
      rewards.shopPotions.enabled = true;
      rewards.shopPotions.weights.otherClass = 1;
      answers.run = {
        relicRewardsAvailableAnytime: 7,
        removeOpRelics: { ancient: true, rare: true },
        releaseOnVictory: !answers.run.releaseOnVictory,
        seeded: !answers.run.seeded,
      };
    },
  ],
  [
    "Floor Checks off with Shop Sanity off but customised",
    (answers) => {
      const rewards = answers.checksAndRewards;
      rewards.checks.includeFloorChecks = false;
      rewards.shop = {
        ...rewards.shop,
        enabled: false,
        cardSlots: 0,
        costs: "Fixed",
      };
    },
  ],
  [
    "Bonus Items of both Wax Relic modes, filler weights and traps",
    (answers) => {
      const rewards = answers.checksAndRewards;
      rewards.bonusItems = [
        {
          kind: "WAX_RELIC",
          mode: "specific",
          relicId: getEligibleSpecificRelics()[0]!.id,
        },
        { kind: "WAX_RELIC", mode: "random", pools: ["Uncommon", "Common"] },
      ];
      rewards.filler.weights.oneGold = 3;
      rewards.filler.weights.fiveGold = 0;
      rewards.filler.weights.vigor = 1;
      rewards.filler.weights.additionalCardReward = 2;
      rewards.traps.chance = 25;
      rewards.traps.weights.weak = "none";
      rewards.traps.weights.vakuu = "high";
      rewards.traps.weights.dazed = "low";
    },
  ],
  [
    "Death Link with fragments only",
    (answers) => {
      answers.deathLink = {
        enabled: true,
        receiveFragment: true,
        receiveDamage: false,
        damagePercent: 40,
        beKilled: false,
      };
    },
  ],
  [
    "Death Link with damage and fragments",
    (answers) => {
      answers.deathLink = {
        enabled: true,
        receiveFragment: true,
        receiveDamage: true,
        damagePercent: 35,
        beKilled: false,
      };
    },
  ],
  [
    "Death Link with damage only",
    (answers) => {
      answers.deathLink = {
        enabled: true,
        receiveFragment: false,
        receiveDamage: true,
        damagePercent: 99,
        beKilled: false,
      };
    },
  ],
  [
    "Death Link that kills",
    (answers) => {
      answers.deathLink = {
        enabled: true,
        receiveFragment: false,
        receiveDamage: false,
        damagePercent: 20,
        beKilled: true,
      };
    },
  ],
  [
    "Death Link off with saved preferences",
    (answers) => {
      answers.deathLink = {
        enabled: false,
        receiveFragment: false,
        receiveDamage: true,
        damagePercent: 12,
        beKilled: false,
      };
    },
  ],
  [
    "every challenge, including Run Modifiers and Sight items",
    (answers) => {
      answers.challenges = {
        enabled: true,
        runModifiers: ["RisingStrength", "CursedRun", "DangerousUnknowns"],
        enemyHpScaling: 10,
        enemyDamageScaling: 15,
        hiddenIntents: "one",
        hiddenHealth: "two",
        hiddenPiles: "one",
        hiddenMap: "two",
        hiddenCards: "one",
        strictCardRewards: true,
      };
      answers.checksAndRewards.checks.includeFloorChecks = true;
    },
  ],
  [
    "challenges ticked while the opt-in switch is off",
    (answers) => {
      answers.challenges.runModifiers = ["SlowStart"];
      answers.challenges.hiddenMap = "one";
    },
  ],
  [
    "disabled progression balancing, minimal accessibility and old logic",
    (answers) => {
      answers.progression = {
        useNewLogic: !answers.progression.useNewLogic,
        progressionBalancing: 0,
        accessibility: "minimal",
      };
    },
  ],
  [
    "extreme and custom progression balancing",
    (answers) => {
      answers.progression.progressionBalancing = 99;
      answers.characters.goal = 1;
    },
  ],
  [
    "custom progression balancing",
    (answers) => {
      answers.progression.progressionBalancing = 37;
    },
  ],
];

describe("importWizardYaml round trips", () => {
  for (const [label, configure] of ROUND_TRIP_CASES) {
    it(`reloads builder YAML for ${label}`, () => {
      const original = createTestAnswers();
      configure(original);
      const yaml = buildYaml(original);

      const imported = importWizardYaml(yaml, optionCatalog);

      expect(imported.warnings).toEqual([]);
      expect(imported.answers.playerName).toBe(original.playerName);
      expect(compileWizardAnswers(imported.answers, optionCatalog)).toEqual(
        compileWizardAnswers(original, optionCatalog),
      );
      // Loading the reloaded YAML again changes nothing.
      expect(buildYaml(imported.answers)).toBe(yaml);
    });
  }
});

describe("importWizardYaml reading hand-written values", () => {
  it("uses a weighted value whose other weights are zero", () => {
    const goldDefault = optionCatalog.options.gold_sanity!.default as boolean;
    const { answers, warnings } = importWizardYaml(
      handWritten(`gold_sanity: {${!goldDefault}: 50, ${goldDefault}: 0}`),
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    expect(answers.checksAndRewards.checks.goldSanity).toBe(!goldDefault);
  });

  it("keeps defaults and names the options that are random or weighted", () => {
    const { answers, warnings } = importWizardYaml(
      handWritten(
        ["trap_chance: random-low", "gold_sanity: {true: 50, false: 50}"].join(
          "\n",
        ),
      ),
      optionCatalog,
    );

    expect(warnings).toHaveLength(1);
    expect(warnings[0]).toContain(displayName("trap_chance"));
    expect(warnings[0]).toContain(displayName("gold_sanity"));
    expect(answers.checksAndRewards.traps.chance).toBe(
      optionCatalog.options.trap_chance!.default,
    );
    expect(answers.checksAndRewards.checks.goldSanity).toBe(
      optionCatalog.options.gold_sanity!.default,
    );
  });

  it("accepts the spellings Archipelago's option classes accept", () => {
    const { answers, warnings } = importWizardYaml(
      handWritten(
        [
          "death_link: on",
          "seeded: 1",
          "neow_sanity: 'yes'",
          "shop_sanity_costs: super_discount_tiered",
          "ancient_relic_pool: 1",
          "progression_balancing: extreme",
          "accessibility: items",
          "relic_rewards_available_anytime: high",
          "five_gold_filler_weight: 3",
          "remove_op_relics: rare",
          "run_modifiers: [slowstart]",
        ].join("\n"),
      ),
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    expect(answers.deathLink.enabled).toBe(true);
    expect(answers.run.seeded).toBe(true);
    expect(answers.checksAndRewards.checks.neowSanity).toBe(true);
    expect(answers.checksAndRewards.shop.costs).toBe("Super_Discount_Tiered");
    expect(answers.checksAndRewards.ancients.relicPool).toBe("chaos");
    expect(answers.progression.progressionBalancing).toBe(99);
    expect(answers.progression.accessibility).toBe("full");
    expect(answers.run.relicRewardsAvailableAnytime).toBe(
      optionCatalog.options.relic_rewards_available_anytime!.maximum,
    );
    expect(answers.checksAndRewards.filler.weights.fiveGold).toBe(2);
    expect(answers.run.removeOpRelics).toEqual({ ancient: false, rare: true });
    expect(answers.challenges.enabled).toBe(true);
    expect(answers.challenges.runModifiers).toEqual(["SlowStart"]);
  });

  it("reads Ascension shorthand like the Python world", () => {
    const { answers, warnings } = importWizardYaml(
      handWritten(["ascension: [5]", "ascension_down: [2]"].join("\n")),
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    expect(answers.characters.sharedAscensions).toEqual({
      enabled: [1, 2, 3, 4, 5],
      ascensionDownsEnabled: true,
      downs: [4, 5],
    });
  });

  it("reads hand-written advanced characters", () => {
    const { answers, warnings } = importWizardYaml(
      handWritten(
        [
          "use_advanced_characters: true",
          "advanced_characters:",
          "  ironclad:",
          "    ascension: [ToughEnemies, 2]",
          "    ascension_down: [toughenemies]",
          "  Watcher: {}",
        ].join("\n"),
      ),
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    const characters = answers.characters;
    expect(characters.selectedCharacters).toEqual(["Ironclad"]);
    expect(characters.ascensionMode).toBe("individual");
    expect(characters.individualAscensions.Ironclad).toEqual({
      enabled: [2, 8],
      ascensionDownsEnabled: true,
      downs: [8],
    });
    expect(characters.moddedCharacters).toEqual([
      {
        name: "Watcher",
        ascensions: { enabled: [1], ascensionDownsEnabled: false, downs: [] },
      },
    ]);
  });

  it("starts with a random character when a fixed start names none", () => {
    const { answers, warnings } = importWizardYaml(
      handWritten(
        ["lock_characters: locked_fixed", 'unlocked_character: ""'].join("\n"),
      ),
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    expect(answers.characters.availability).toBe("random");
  });
});

describe("importWizardYaml documents", () => {
  it("loads the first Slay the Spire II player of a multi-document file", () => {
    const text = [
      "name: Other",
      "game: A Link to the Past",
      "A Link to the Past: {}",
      "---",
      "name: Second",
      `game: {${WIZARD_GAME_NAME}: 3, A Link to the Past: 0}`,
      `${WIZARD_GAME_NAME}:`,
      "  seeded: true",
      "---",
      handWritten("seeded: false", "Third"),
    ].join("\n");

    const { answers, warnings } = importWizardYaml(text, optionCatalog);

    expect(answers.playerName).toBe("Second");
    expect(answers.run.seeded).toBe(true);
    expect(warnings).toHaveLength(1);
    expect(warnings[0]).toContain("2 Slay the Spire II players");
  });

  it("rejects text that is not YAML", () => {
    expect(() => importWizardYaml("name: [unclosed", optionCatalog)).toThrow(
      /not valid YAML/,
    );
  });

  it("rejects a file for another game", () => {
    expect(() =>
      importWizardYaml("name: Link\ngame: A Link to the Past\n", optionCatalog),
    ).toThrow(/no Slay the Spire II settings/);
  });

  it("rejects a file that only sometimes plays Slay the Spire II", () => {
    const text = `name: Mixed\ngame: {${WIZARD_GAME_NAME}: 1, A Link to the Past: 1}\n`;
    expect(() => importWizardYaml(text, optionCatalog)).toThrow(
      /some of the time/,
    );
  });

  it("rejects a file without the game's option section", () => {
    expect(() =>
      importWizardYaml(
        `name: Empty\ngame: ${WIZARD_GAME_NAME}\n`,
        optionCatalog,
      ),
    ).toThrow(/no "Slay the Spire II:" section/);
  });

  it("lists dropped and unknown settings once each", () => {
    const { warnings } = importWizardYaml(
      handWritten(
        [
          "local_items: []",
          "start_inventory: {Ironclad Relic: 1}",
          "item_links:",
          "  - name: Shared",
          "    item_pool: [Everything]",
          "triggers: []",
          "not_an_option: 1",
          "also_unknown: true",
        ].join("\n"),
      ),
      optionCatalog,
    );

    expect(warnings).toHaveLength(2);
    const [dropped, unknown] = warnings as [string, string];
    expect(dropped).toContain(displayName("start_inventory"));
    expect(dropped).toContain(displayName("item_links"));
    expect(dropped).toContain("triggers");
    expect(dropped).not.toContain(displayName("local_items"));
    expect(unknown).toContain("not_an_option");
    expect(unknown).toContain("also_unknown");
  });

  it("imports the hand-edited release template", () => {
    const { answers, warnings } = importWizardYaml(
      spire2Template,
      optionCatalog,
    );

    expect(warnings).toEqual([]);
    expect(answers.playerName).toBe("Spire2Player");
    expect(answers.characters.selectedCharacters).toEqual([
      "Ironclad",
      "Silent",
      "Necrobinder",
    ]);
    expect(answers.checksAndRewards.shop.costs).toBe("Discount_Tiered");
    expect(() => compileWizardAnswers(answers, optionCatalog)).not.toThrow();
  });
});
