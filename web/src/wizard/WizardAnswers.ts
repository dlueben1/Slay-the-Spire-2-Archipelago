/**
 * @file Defines the persistent, player-facing state for the guided wizard.
 *
 * This represents an abstraction between the reactive UI state and the raw YAML/AP options.
 *
 * Add new answer fields here, initialize them in `createDefaultWizardAnswers`,
 * declare their questions in `WizardStep.ts`, and translate them in a compiler
 * module. Vue components should update this model only, never a YAML options object.
 */

import type { OptionCatalog } from "../generated/optionCatalog";
import {
  getDefaultAscensionLevels,
  type AscensionLevel,
} from "./AscensionModifier";
import {
  CHECK_OPTION_KEYS,
  DEATH_LINK_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_OPTION_KEYS,
} from "./WizardOptionKey";

export type CharacterSelectionMode = "all" | "random";
export type CharacterAvailability = "all" | "random" | "fixed";
export type CharacterGoal = "all" | number;
export type CharacterAscensionMode = "shared" | "individual";

export type AncientRelicLocation = "start_of_act" | "anytime";
export type AncientRelicPool = "balanced" | "chaos" | "true_chaos";
export type AccessibilityMode = "full" | "minimal";
export type ShopCostMode =
  "Fixed" | "Super_Discount_Tiered" | "Discount_Tiered" | "Tiered";

export type FillerWeightLevel = 0 | 1 | 2 | 3;

export type FillerItemId =
  | "oneGold"
  | "fiveGold"
  | "freeAttack"
  | "freePower"
  | "freeSkill"
  | "vigor"
  | "artifact"
  | "thorns"
  | "buffer"
  | "dexterity"
  | "strength"
  | "plating"
  | "friendship"
  | "postCombatCardUpgrade"
  | "postCombatCardRemoval"
  | "additionalCardReward";

/** Explicit checkbox state for one shared or character-specific Ascension setup. */
export interface AscensionConfigurationAnswers {
  enabled: AscensionLevel[];
  ascensionDownsEnabled: boolean;
  downs: AscensionLevel[];
}

/** Editable Steam Workshop character ID and its preserved individual settings. */
export interface ModdedCharacterAnswers {
  name: string;
  ascensions: AscensionConfigurationAnswers;
}

/** Unified Character Setup intent independent of Archipelago's two YAML systems. */
export interface CharacterAnswers {
  selectedCharacters: string[];
  moddedCharacters: ModdedCharacterAnswers[];
  ascensionMode: CharacterAscensionMode;
  sharedAscensions: AscensionConfigurationAnswers;
  individualAscensions: Record<string, AscensionConfigurationAnswers>;
  selectionMode: CharacterSelectionMode;
  randomCharacterCount: number;
  availability: CharacterAvailability;
  startingCharacter: string | null;
  goal: CharacterGoal;
}

export interface FillerAnswers {
  weights: Record<FillerItemId, FillerWeightLevel>;
}

export interface RunAnswers {
  ancientRelicLocation: AncientRelicLocation;
  ancientRelicPool: AncientRelicPool;
  relicChoiceCount: number;
  neowSanity: boolean;
  seeded: boolean;
  progressionBalancing: number;
  accessibility: AccessibilityMode;
}

export interface CheckAnswers {
  includeFloorChecks: boolean;
  campfireSanity: boolean;
  goldSanity: boolean;
  potionSanity: boolean;
  shuffleAllCards: boolean;
}

export interface ShopAnswers {
  enabled: boolean;
  cardSlots: number;
  neutralCardSlots: number;
  relicSlots: number;
  potionSlots: number;
  removeSlots: boolean;
  costs: ShopCostMode;
}

export interface ChecksAndRewardsAnswers {
  checks: CheckAnswers;
  shop: ShopAnswers;
  filler: FillerAnswers;
}

export interface DeathLinkAnswers {
  enabled: boolean;
  receiveFragment: boolean;
  receiveDamage: boolean;
  damagePercent: number;
  beKilled: boolean;
}

export interface WizardAnswers {
  playerName: string;
  characters: CharacterAnswers;
  run: RunAnswers;
  checksAndRewards: ChecksAndRewardsAnswers;
  deathLink: DeathLinkAnswers;
}

/**
 * Reads a required generated option default.
 *
 * @param catalog - Generated catalog containing the authoritative default.
 * @param optionKey - Generated option whose default is required.
 * @returns The untyped default value for further shape validation.
 * @throws When the generated option is absent.
 */
function getGeneratedDefault(
  catalog: OptionCatalog,
  optionKey: string,
): unknown {
  // Resolve the option before any section-specific type assertion occurs.
  const option = catalog.options[optionKey];

  if (!option) {
    throw new Error(`Generated option catalog is missing '${optionKey}'.`);
  }

  // Return metadata without cloning because callers only read primitive defaults here.
  return option.default;
}

/**
 * Reads a boolean default for a guided toggle.
 *
 * @param catalog - Generated catalog containing the toggle definition.
 * @param optionKey - Generated toggle option whose default is required.
 * @returns The schema-provided boolean default.
 * @throws When the generated default is not boolean.
 */
function getBooleanDefault(catalog: OptionCatalog, optionKey: string): boolean {
  // Read through the shared presence check before validating the primitive shape.
  const value = getGeneratedDefault(catalog, optionKey);

  if (typeof value !== "boolean") {
    throw new Error(`Generated option '${optionKey}' has no boolean default.`);
  }

  // Preserve the exact generated toggle default.
  return value;
}

/**
 * Reads a numeric default for a guided range.
 *
 * @param catalog - Generated catalog containing the range definition.
 * @param optionKey - Generated numeric option whose default is required.
 * @returns The schema-provided numeric default.
 * @throws When the generated default is not numeric.
 */
function getNumberDefault(catalog: OptionCatalog, optionKey: string): number {
  // Read through the shared presence check before validating the primitive shape.
  const value = getGeneratedDefault(catalog, optionKey);

  if (typeof value !== "number") {
    throw new Error(`Generated option '${optionKey}' has no numeric default.`);
  }

  // Preserve the exact generated range default.
  return value;
}

/**
 * Reads and narrows a generated choice default to a supported semantic value.
 *
 * @param catalog - Generated catalog containing the choice definition.
 * @param optionKey - Generated choice option whose default is required.
 * @param supported - Semantic choice names implemented by the guided section.
 * @returns The generated default narrowed to one supported value.
 * @throws When the default is not text or the generated choice contract drifted.
 */
function getChoiceDefault<const T extends string>(
  catalog: OptionCatalog,
  optionKey: string,
  supported: readonly T[],
): T {
  // Read through the shared presence check before validating the choice name.
  const value = getGeneratedDefault(catalog, optionKey);

  if (typeof value !== "string" || !supported.includes(value as T)) {
    throw new Error(
      `Generated option '${optionKey}' has unsupported default '${String(value)}'.`,
    );
  }

  // The membership guard narrows the schema value to this section's semantic union.
  return value as T;
}

/**
 * Creates the initial player-facing state for a guided setup session.
 *
 * @param available - Character names supplied by the generated catalog.
 * @param filler - Schema-derived initial filler answers.
 * @param catalog - Generated metadata supplying every other section default.
 * @returns A complete answer model initialized to a valid one-character setup.
 * @remarks This chooses UX defaults only. The compiler creates Archipelago options.
 */
export function createDefaultWizardAnswers(
  available: readonly string[],
  filler: FillerAnswers,
  catalog: OptionCatalog,
): WizardAnswers {
  // Use a schema-provided character so the initial form contains no duplicated fact.
  const selectedCharacters = available.slice(0, 1);

  // Normalize Archipelago's mixed Ascension syntax to explicit checkbox selections.
  const enabledAscensions = getDefaultAscensionLevels(catalog, "ascension");
  const enabledAscensionDowns = getDefaultAscensionLevels(
    catalog,
    "ascension_down",
  );
  const sharedAscensions: AscensionConfigurationAnswers = {
    enabled: enabledAscensions,
    ascensionDownsEnabled: enabledAscensionDowns.length > 0,
    downs: enabledAscensionDowns,
  };

  // Give every built-in character independent state before advanced mode is enabled.
  const individualAscensions: Record<string, AscensionConfigurationAnswers> =
    {};

  for (const character of available) {
    individualAscensions[character] = {
      enabled: [...sharedAscensions.enabled],
      ascensionDownsEnabled: sharedAscensions.ascensionDownsEnabled,
      downs: [...sharedAscensions.downs],
    };
  }

  // Interpret the technical 0/100 damage values as distinct player-facing modes.
  const generatedDeathLinkDamage = getNumberDefault(
    catalog,
    DEATH_LINK_OPTION_KEYS.damagePercent,
  );

  // Initialize every field explicitly to keep this the single setup entry point.
  return {
    playerName: "",
    characters: {
      selectedCharacters,
      moddedCharacters: [],
      ascensionMode: "shared",
      sharedAscensions,
      individualAscensions,
      selectionMode: "all",
      randomCharacterCount: 1,
      availability: "all",
      startingCharacter: selectedCharacters[0] ?? null,
      goal: "all",
    },
    run: {
      ancientRelicLocation: getChoiceDefault(
        catalog,
        RUN_OPTION_KEYS.ancientRelicLocation,
        ["start_of_act", "anytime"],
      ),
      ancientRelicPool: getChoiceDefault(
        catalog,
        RUN_OPTION_KEYS.ancientRelicPool,
        ["balanced", "chaos", "true_chaos"],
      ),
      relicChoiceCount: getNumberDefault(
        catalog,
        RUN_OPTION_KEYS.relicChoiceCount,
      ),
      neowSanity: getBooleanDefault(catalog, RUN_OPTION_KEYS.neowSanity),
      seeded: getBooleanDefault(catalog, RUN_OPTION_KEYS.seeded),
      progressionBalancing: getNumberDefault(
        catalog,
        RUN_OPTION_KEYS.progressionBalancing,
      ),
      accessibility: getChoiceDefault(catalog, RUN_OPTION_KEYS.accessibility, [
        "full",
        "minimal",
      ]),
    },
    checksAndRewards: {
      checks: {
        includeFloorChecks: getBooleanDefault(
          catalog,
          CHECK_OPTION_KEYS.includeFloorChecks,
        ),
        campfireSanity: getBooleanDefault(
          catalog,
          CHECK_OPTION_KEYS.campfireSanity,
        ),
        goldSanity: getBooleanDefault(catalog, CHECK_OPTION_KEYS.goldSanity),
        potionSanity: getBooleanDefault(
          catalog,
          CHECK_OPTION_KEYS.potionSanity,
        ),
        shuffleAllCards: getBooleanDefault(
          catalog,
          CHECK_OPTION_KEYS.shuffleAllCards,
        ),
      },
      shop: {
        enabled: getBooleanDefault(catalog, SHOP_OPTION_KEYS.enabled),
        cardSlots: getNumberDefault(catalog, SHOP_OPTION_KEYS.cardSlots),
        neutralCardSlots: getNumberDefault(
          catalog,
          SHOP_OPTION_KEYS.neutralCardSlots,
        ),
        relicSlots: getNumberDefault(catalog, SHOP_OPTION_KEYS.relicSlots),
        potionSlots: getNumberDefault(catalog, SHOP_OPTION_KEYS.potionSlots),
        removeSlots: getBooleanDefault(catalog, SHOP_OPTION_KEYS.removeSlots),
        costs: getChoiceDefault(catalog, SHOP_OPTION_KEYS.costs, [
          "Fixed",
          "Super_Discount_Tiered",
          "Discount_Tiered",
          "Tiered",
        ]),
      },
      filler,
    },
    deathLink: {
      enabled: getBooleanDefault(catalog, DEATH_LINK_OPTION_KEYS.enabled),
      receiveFragment: getBooleanDefault(
        catalog,
        DEATH_LINK_OPTION_KEYS.enableFragments,
      ),
      receiveDamage:
        generatedDeathLinkDamage > 0 && generatedDeathLinkDamage < 100,
      damagePercent: Math.max(1, generatedDeathLinkDamage),
      beKilled: generatedDeathLinkDamage === 100,
    },
  };
}
