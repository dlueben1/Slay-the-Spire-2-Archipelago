/**
 * @file Reads the answers of every step except Character Setup from option values.
 *
 * Each function here inverts one section compiler in `../compiler/`. They receive the
 * complete resolved option values (generated defaults overlaid with the YAML), already
 * in the canonical shapes the compilers write, so most fields copy straight across.
 * The interesting cases are the ones where the answer model is richer than the
 * options: Death Link's single damage percentage, the Challenges opt-in switch, Sight
 * item counts, filler slider levels, and Bonus Item rows.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import {
  BONUS_RELIC_POOLS,
  getEligibleSpecificRelics,
} from "../BonusRelicData";
import { FILLER_ITEM_DEFINITIONS, FILLER_WEIGHT_NAMES } from "../FillerItem";
import { getOrderedRunModifiers } from "../RunModifier";
import { SHOP_CARD_KIND_DEFINITIONS } from "../ShopCardKind";
import {
  SHOP_POTION_KIND_DEFINITIONS,
  SHOP_RELIC_KIND_DEFINITIONS,
  type ShopStockKindDefinition,
} from "../ShopStockKind";
import { TRAP_ITEM_DEFINITIONS, type TrapWeight } from "../TrapItem";
import {
  SIGHT_COPY_COUNTS,
  type BonusItemAnswer,
  type ChallengeAnswers,
  type ChecksAndRewardsAnswers,
  type DeathLinkAnswers,
  type FillerAnswers,
  type FillerWeightLevel,
  type ProgressionAnswers,
  type RunAnswers,
  type ShopCardAnswers,
  type ShopStockAnswers,
  type SightCopies,
} from "../WizardAnswers";
import {
  ANCIENT_OPTION_KEYS,
  BONUS_ITEM_OPTION_KEY,
  CHALLENGE_OPTION_KEYS,
  CHECK_OPTION_KEYS,
  DEATH_LINK_OPTION_KEYS,
  OP_RELIC_CATEGORY_KEYS,
  PROGRESSION_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_CARD_OPTION_KEY,
  SHOP_OPTION_KEYS,
  SHOP_POTION_OPTION_KEY,
  SHOP_RELIC_OPTION_KEY,
  STARTING_EQUIPMENT_OPTION_KEYS,
  TRAP_CHANCE_OPTION_KEY,
} from "../WizardOptionKey";
import type { CompiledOptions } from "../compiler/applyCharacterOptions";
import { describeValue, isMapping } from "./readOptionValue";

/** Adds one player-readable warning. */
type Warn = (message: string) => void;

/** The Bonus Item type the builder supports, as the YAML names it. */
const WAX_RELIC = "WAX_RELIC";

/**
 * Checks whether two resolved option values are equal, ignoring set order.
 *
 * @param left - First value.
 * @param right - Second value.
 * @returns Whether both values mean the same setting.
 */
function sameOptionValue(left: unknown, right: unknown): boolean {
  if (Array.isArray(left) && Array.isArray(right)) {
    return (
      left.length === right.length &&
      left.every((entry) => right.includes(entry))
    );
  }
  return JSON.stringify(left) === JSON.stringify(right);
}

/**
 * Reads Run Settings answers.
 *
 * @param options - Resolved option values.
 * @returns Relic availability and removal, victory release, and seeding answers.
 */
export function readRunAnswers(options: CompiledOptions): RunAnswers {
  const removed = options[RUN_OPTION_KEYS.removeOpRelics] as string[];
  return {
    relicRewardsAvailableAnytime: options[
      RUN_OPTION_KEYS.relicRewardsAvailableAnytime
    ] as number,
    removeOpRelics: {
      ancient: removed.includes(OP_RELIC_CATEGORY_KEYS.ancient),
      rare: removed.includes(OP_RELIC_CATEGORY_KEYS.rare),
    },
    releaseOnVictory: options[RUN_OPTION_KEYS.releaseOnVictory] as boolean,
    seeded: options[RUN_OPTION_KEYS.seeded] as boolean,
    blitzMerchantChance: options[RUN_OPTION_KEYS.blitzMerchantChance] as number,
  };
}

/**
 * Finds the eligible relic a Wax Relic `Value` names.
 *
 * @param value - Relic ID or display name, in any letter case.
 * @returns The relic ID the compiler accepts, or `undefined`.
 */
function findSpecificRelicId(value: string): string | undefined {
  const key = value.trim().toLowerCase();
  return getEligibleSpecificRelics().find(
    (relic) =>
      relic.id.toLowerCase() === key || relic.name.toLowerCase() === key,
  )?.id;
}

/**
 * Reads one `bonus_items` entry as a Bonus Item row.
 *
 * @param entry - Raw list entry, such as `{WAX_RELIC: {Pools: [Common]}}`.
 * @param context - Row named in warnings.
 * @param warn - Receives warnings for anything that cannot be loaded.
 * @returns The row, or `null` when it was left out.
 */
function readBonusItem(
  entry: unknown,
  context: string,
  warn: Warn,
): BonusItemAnswer | null {
  const [type, ...extraTypes] = isMapping(entry) ? Object.keys(entry) : [];
  if (!type || extraTypes.length || type.toUpperCase() !== WAX_RELIC) {
    warn(
      `${context}: left out ${describeValue(entry)}; the builder supports Wax Relics only.`,
    );
    return null;
  }

  const selector = (entry as Record<string, unknown>)[type];
  if (!isMapping(selector) || "Value" in selector === "Pools" in selector) {
    warn(
      `${context}: left out a Wax Relic that needs exactly one of Value or Pools.`,
    );
    return null;
  }

  if ("Value" in selector) {
    const relicId = findSpecificRelicId(String(selector.Value));
    if (!relicId) {
      warn(
        `${context}: left out ${describeValue(selector.Value)}, which is not an eligible Wax Relic.`,
      );
      return null;
    }
    return { kind: WAX_RELIC, mode: "specific", relicId };
  }

  const rawPools = Array.isArray(selector.Pools)
    ? selector.Pools
    : [selector.Pools];
  const pools: string[] = [];
  for (const rawPool of rawPools) {
    const pool = BONUS_RELIC_POOLS.find(
      (name) => name.toLowerCase() === String(rawPool).trim().toLowerCase(),
    );
    if (!pool) {
      warn(
        `${context}: ignored ${describeValue(rawPool)}, which is not a relic pool.`,
      );
    } else if (!pools.includes(pool)) {
      pools.push(pool);
    }
  }
  if (!pools.length) {
    warn(`${context}: left out a Wax Relic without any known relic pool.`);
    return null;
  }
  return { kind: WAX_RELIC, mode: "random", pools };
}

/**
 * Reads filler weights as slider levels.
 *
 * @param options - Resolved option values holding canonical weight names.
 * @returns One level per filler item.
 */
function readFillerAnswers(options: CompiledOptions): FillerAnswers {
  const weights = {} as FillerAnswers["weights"];
  for (const definition of FILLER_ITEM_DEFINITIONS) {
    weights[definition.id] = FILLER_WEIGHT_NAMES.indexOf(
      options[definition.optionKey] as (typeof FILLER_WEIGHT_NAMES)[number],
    ) as FillerWeightLevel;
  }
  return { weights };
}

/**
 * Reads the Random Shop Cards switch and each card kind's weight as a slider level.
 *
 * @param options - Resolved option values holding canonical weight names.
 * @returns The switch plus one level per card kind.
 */
function readShopCardAnswers(options: CompiledOptions): ShopCardAnswers {
  const weights = {} as ShopCardAnswers["weights"];
  for (const definition of SHOP_CARD_KIND_DEFINITIONS) {
    weights[definition.id] = FILLER_WEIGHT_NAMES.indexOf(
      options[definition.optionKey] as (typeof FILLER_WEIGHT_NAMES)[number],
    ) as FillerWeightLevel;
  }
  return { enabled: options[SHOP_CARD_OPTION_KEY] as boolean, weights };
}

/**
 * Reads a Random Shop Relics or Potions switch and each kind's weight as a slider level.
 *
 * @param options - Resolved option values holding canonical weight names.
 * @param switchKey - Generated option holding the family's switch.
 * @param definitions - The family's kinds.
 * @returns The switch plus one level per kind.
 */
function readShopStockAnswers<KindId extends string>(
  options: CompiledOptions,
  switchKey: string,
  definitions: readonly ShopStockKindDefinition<KindId>[],
): ShopStockAnswers<KindId> {
  const weights = {} as ShopStockAnswers<KindId>["weights"];
  for (const definition of definitions) {
    weights[definition.id] = FILLER_WEIGHT_NAMES.indexOf(
      options[definition.optionKey] as (typeof FILLER_WEIGHT_NAMES)[number],
    ) as FillerWeightLevel;
  }
  return { enabled: options[switchKey] as boolean, weights };
}

/**
 * Reads Checks & Rewards answers.
 *
 * @param options - Resolved option values.
 * @param warn - Receives warnings for Bonus Items and Shop settings.
 * @returns Starting Equipment, Ancient, check, Shop, Bonus Item, filler and trap answers.
 */
export function readChecksAndRewardsAnswers(
  options: CompiledOptions,
  warn: Warn,
): ChecksAndRewardsAnswers {
  const shop = {
    enabled: options[SHOP_OPTION_KEYS.enabled] as boolean,
    cardSlots: options[SHOP_OPTION_KEYS.cardSlots] as number,
    neutralCardSlots: options[SHOP_OPTION_KEYS.neutralCardSlots] as number,
    relicSlots: options[SHOP_OPTION_KEYS.relicSlots] as number,
    potionSlots: options[SHOP_OPTION_KEYS.potionSlots] as number,
    removeSlots: options[SHOP_OPTION_KEYS.removeSlots] as boolean,
    costs: options[
      SHOP_OPTION_KEYS.costs
    ] as ChecksAndRewardsAnswers["shop"]["costs"],
  };

  // The Python world turns Shop Sanity off when it has no locations to add.
  const hasShopLocations =
    shop.cardSlots +
      shop.neutralCardSlots +
      shop.relicSlots +
      shop.potionSlots >
      0 || shop.removeSlots;
  if (shop.enabled && !hasShopLocations) {
    warn(
      "Shop Sanity was turned off because every shop slot count is 0, as Archipelago would do.",
    );
    shop.enabled = false;
  }

  const rawBonusItems = options[BONUS_ITEM_OPTION_KEY] as unknown[];
  const bonusItems: BonusItemAnswer[] = [];
  rawBonusItems.forEach((entry, index) => {
    const item = readBonusItem(entry, `Bonus Item ${index + 1}`, warn);
    if (item) {
      bonusItems.push(item);
    }
  });

  const trapWeights = {} as ChecksAndRewardsAnswers["traps"]["weights"];
  for (const item of TRAP_ITEM_DEFINITIONS) {
    trapWeights[item.id] = options[item.optionKey] as TrapWeight;
  }

  return {
    startingEquipment: {
      progressiveStarterCard: options[
        STARTING_EQUIPMENT_OPTION_KEYS.progressiveStarterCard
      ] as boolean,
      progressiveStarterRelic: options[
        STARTING_EQUIPMENT_OPTION_KEYS.progressiveStarterRelic
      ] as boolean,
    },
    ancients: {
      relicLocation: options[
        ANCIENT_OPTION_KEYS.relicLocation
      ] as ChecksAndRewardsAnswers["ancients"]["relicLocation"],
      relicPool: options[
        ANCIENT_OPTION_KEYS.relicPool
      ] as ChecksAndRewardsAnswers["ancients"]["relicPool"],
    },
    checks: {
      neowSanity: options[CHECK_OPTION_KEYS.neowSanity] as boolean,
      includeFloorChecks: options[
        CHECK_OPTION_KEYS.includeFloorChecks
      ] as boolean,
      campfireSanity: options[CHECK_OPTION_KEYS.campfireSanity] as boolean,
      goldSanity: options[CHECK_OPTION_KEYS.goldSanity] as boolean,
      potionSanity: options[CHECK_OPTION_KEYS.potionSanity] as boolean,
      shuffleAllCards: options[CHECK_OPTION_KEYS.shuffleAllCards] as boolean,
    },
    shop,
    shopCards: readShopCardAnswers(options),
    shopRelics: readShopStockAnswers(
      options,
      SHOP_RELIC_OPTION_KEY,
      SHOP_RELIC_KIND_DEFINITIONS,
    ),
    shopPotions: readShopStockAnswers(
      options,
      SHOP_POTION_OPTION_KEY,
      SHOP_POTION_KIND_DEFINITIONS,
    ),
    bonusItems,
    filler: readFillerAnswers(options),
    traps: {
      chance: options[TRAP_CHANCE_OPTION_KEY] as number,
      weights: trapWeights,
    },
  };
}

/**
 * Reads Death Link answers from its toggle, fragment toggle and damage percentage.
 *
 * @param options - Resolved option values.
 * @param defaults - Default answers, which keep the damage slider's last position.
 * @returns Death Link answers that compile back to the same three options.
 * @remarks 100% damage is the builder's Die effect and 0% means no damage. With Die,
 * fragments cannot apply, so the compiler always writes them off.
 */
export function readDeathLinkAnswers(
  options: CompiledOptions,
  defaults: DeathLinkAnswers,
): DeathLinkAnswers {
  const damagePercent = options[DEATH_LINK_OPTION_KEYS.damagePercent] as number;
  const beKilled = damagePercent >= 100;
  const receiveDamage = damagePercent > 0 && !beKilled;
  return {
    enabled: options[DEATH_LINK_OPTION_KEYS.enabled] as boolean,
    receiveFragment:
      !beKilled && (options[DEATH_LINK_OPTION_KEYS.enableFragments] as boolean),
    receiveDamage,
    damagePercent: receiveDamage ? damagePercent : defaults.damagePercent,
    beKilled,
  };
}

/**
 * Reads a Sight item count as its radio choice.
 *
 * @param count - Resolved `hidden_*` value.
 * @returns The matching choice.
 */
function readSightCopies(count: unknown): SightCopies {
  const choice = (Object.keys(SIGHT_COPY_COUNTS) as SightCopies[]).find(
    (candidate) => SIGHT_COPY_COUNTS[candidate] === count,
  );
  return choice ?? "off";
}

/**
 * Reads Challenges answers, turning the opt-in switch on when any challenge is set.
 *
 * @param options - Resolved option values.
 * @param defaults - Default answers, kept while every challenge is at its default.
 * @param catalog - Generated catalog supplying each challenge's default.
 * @returns Challenge answers that compile back to the same options.
 */
export function readChallengeAnswers(
  options: CompiledOptions,
  defaults: ChallengeAnswers,
  catalog: OptionCatalog,
): ChallengeAnswers {
  const enabled = Object.values(CHALLENGE_OPTION_KEYS).some(
    (key) => !sameOptionValue(options[key], catalog.options[key]?.default),
  );
  if (!enabled) {
    return { ...defaults, runModifiers: [...defaults.runModifiers] };
  }

  return {
    enabled,
    runModifiers: getOrderedRunModifiers(
      options[CHALLENGE_OPTION_KEYS.runModifiers] as string[],
    ),
    enemyHpScaling: options[CHALLENGE_OPTION_KEYS.enemyHpScaling] as number,
    enemyDamageScaling: options[
      CHALLENGE_OPTION_KEYS.enemyDamageScaling
    ] as number,
    dreadMode: options[CHALLENGE_OPTION_KEYS.dreadMode] as boolean,
    hiddenIntents: readSightCopies(
      options[CHALLENGE_OPTION_KEYS.hiddenIntents],
    ),
    hiddenHealth: readSightCopies(options[CHALLENGE_OPTION_KEYS.hiddenHealth]),
    hiddenPiles: readSightCopies(options[CHALLENGE_OPTION_KEYS.hiddenPiles]),
    hiddenMap: readSightCopies(options[CHALLENGE_OPTION_KEYS.hiddenMap]),
    hiddenCards: readSightCopies(options[CHALLENGE_OPTION_KEYS.hiddenCards]),
    strictCardRewards: options[
      CHALLENGE_OPTION_KEYS.strictCardRewards
    ] as boolean,
  };
}

/**
 * Reads Progression answers.
 *
 * @param options - Resolved option values.
 * @returns Logic version, progression balancing and accessibility answers.
 */
export function readProgressionAnswers(
  options: CompiledOptions,
): ProgressionAnswers {
  return {
    useNewLogic: options[PROGRESSION_OPTION_KEYS.useNewLogic] as boolean,
    progressionBalancing: options[
      PROGRESSION_OPTION_KEYS.progressionBalancing
    ] as number,
    accessibility: options[
      PROGRESSION_OPTION_KEYS.accessibility
    ] as ProgressionAnswers["accessibility"],
  };
}
