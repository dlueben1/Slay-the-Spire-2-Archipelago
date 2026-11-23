/**
 * @file Builds player-facing review copy from wizard answers.
 *
 * Review text is derived from the same player intent sent to the compiler, but it does
 * not inspect or translate Archipelago options. Add summary builders here when adding
 * guided sections, then compose them in the review UI. This keeps prose generation out
 * of Vue templates and keeps the compiler focused on technical values.
 */

import type {
  AscensionConfigurationAnswers,
  AncientAnswers,
  BonusItemAnswer,
  BoonAnswers,
  ChallengeAnswers,
  CharacterAnswers,
  CharacterStarterOverrides,
  ChecksAnswers,
  CheckTypeAnswers,
  DeathLinkAnswers,
  FillerAnswers,
  ItemAnswers,
  ItemPoolAnswers,
  ProgressionAnswers,
  RunAnswers,
  ShopCardAnswers,
  ShopCardKindId,
  ShopPotionKindId,
  ShopRelicKindId,
  ShopStockAnswers,
  ShopAnswers,
  ShopsAnswers,
  StartingEquipmentAnswers,
  WizardAnswers,
} from "./WizardAnswers";
import {
  countSightItems,
  hasSecondSightCopy,
  SIGHTS,
  type SightCopies,
} from "./Sight";
import { itemName } from "./ItemNames";
import { optionCatalog } from "../generated/optionCatalog";
import {
  getBonusItemDisplayRows,
  type BonusItemDisplayRow,
} from "./BonusItemDisplay";
import {
  getConfiguredCharacterNames,
  getSelectedBetaOnlyCharacters,
} from "./CharacterRoster";
import { getActiveBetaOnlyChallenges } from "./betaOnlyOptions";
import { BOONS } from "./Boon";
import { RUN_MODIFIERS } from "./RunModifier";

export interface WizardReviewSection {
  title: string;
  summary: string;
  /** Optional structured rows rendered beneath the prose summary (Bonus Items). */
  items?: BonusItemDisplayRow[];
  /** Optional caution shown with the summary, such as challenges logic ignores. */
  warning?: string;
}

/**
 * Formats a list of names as natural English with an Oxford comma.
 *
 * @param names - Ordered display names to join.
 * @returns A readable phrase such as `Ironclad, Silent, and Defect`.
 * @remarks The empty fallback is defensive; valid Character Setup answers are non-empty.
 */
function joinNames(names: readonly string[]): string {
  // Zero and one item do not need a conjunction.
  if (names.length < 2) {
    return names[0] ?? "no characters";
  }

  // Two items use a conjunction without a comma.
  if (names.length === 2) {
    return `${names[0]} and ${names[1]}`;
  }

  // Longer lists use comma separators and an Oxford comma before the last item.
  return `${names.slice(0, -1).join(", ")}, and ${names.at(-1)}`;
}

/**
 * Converts a small count into a word for friendly review prose.
 *
 * @param count - Non-negative count to display.
 * @returns An English word for zero through ten, otherwise the numeric string.
 */
function countWord(count: number): string {
  // Character ranges currently stop at ten, but retain a safe fallback for later data.
  return (
    [
      "zero",
      "one",
      "two",
      "three",
      "four",
      "five",
      "six",
      "seven",
      "eight",
      "nine",
      "ten",
    ][count] ?? String(count)
  );
}

/**
 * Formats one shared or per-character Ascension configuration for review copy.
 *
 * @param configuration - Enabled Ascensions and shuffled Ascension Down levels.
 * @returns A compact human-readable phrase using stable A1 through A10 labels.
 */
function summarizeAscensionConfiguration(
  configuration: AscensionConfigurationAnswers,
): string {
  // Describe an intentionally empty configuration explicitly.
  if (!configuration.enabled.length) {
    return "no active Ascensions";
  }

  // Build display labels without leaking canonical generated option names.
  const enabledLabels: string[] = [];
  const downLabels: string[] = [];

  for (const level of configuration.enabled) {
    enabledLabels.push(`A${level}`);
  }

  if (configuration.ascensionDownsEnabled) {
    for (const level of configuration.downs) {
      downLabels.push(`A${level}`);
    }
  }

  // Ascension Downs are a dependent suffix only when at least one is shuffled.
  const downSummary = downLabels.length
    ? `; Downs for ${joinNames(downLabels)}`
    : "; no Ascension Downs";

  return `${joinNames(enabledLabels)}${downSummary}`;
}

/**
 * Summarizes standard shared Ascensions or the advanced per-character dictionary.
 *
 * @param answers - Valid current Character Setup answers.
 * @returns Review prose describing the active wizard-to-YAML strategy.
 */
function summarizeCharacterAscensions(answers: CharacterAnswers): string {
  // Standard mode has one concise configuration shared by the complete roster.
  if (answers.ascensionMode === "shared") {
    return `Every character uses ${summarizeAscensionConfiguration(answers.sharedAscensions)}.`;
  }

  // Advanced mode lists each selected built-in character's independent settings.
  const summaries: string[] = [];

  for (const character of answers.selectedCharacters) {
    const configuration = answers.individualAscensions[character];

    if (configuration) {
      summaries.push(
        `${character}: ${summarizeAscensionConfiguration(configuration)}`,
      );
    }
  }

  // Modded rows own independent settings alongside their editable internal IDs.
  for (const moddedCharacter of answers.moddedCharacters) {
    const characterName =
      moddedCharacter.name.trim() || "unnamed modded character";
    summaries.push(
      `${characterName}: ${summarizeAscensionConfiguration(moddedCharacter.ascensions)}`,
    );
  }

  return `Ascensions are configured separately — ${summaries.join("; ")}.`;
}

/**
 * Warns when the roster includes a character only the game's beta branch has.
 *
 * @param answers - Valid current Character Setup answers.
 * @returns The caution to show with the Character Setup summary, if any.
 */
export function describeBetaOnlyCharacters(
  answers: CharacterAnswers,
): string | undefined {
  const names = getSelectedBetaOnlyCharacters(answers);

  if (!names.length) {
    return undefined;
  }

  const verb = names.length === 1 ? "is" : "are";

  return `${joinNames(names)} ${verb} only in the game's beta branch, so this YAML needs Slay the Spire II on the beta branch.`;
}

/**
 * Summarizes Character Setup choices in gameplay language.
 *
 * @param answers - Valid current Character Setup answers.
 * @returns A short paragraph covering selection, unlocking, and completion goal.
 * @remarks Compile and validate before displaying this summary so impossible answer
 * combinations cannot be presented as if they were valid.
 */
export function summarizeCharacterAnswers(
  answers: CharacterAnswers,
  includeFloorChecks = true,
): string {
  // Merge built-in portraits and complete modded IDs for all shared roster behavior.
  const roster = getConfiguredCharacterNames(answers);

  // Resolve how many characters actually enter the generated world.
  const count =
    answers.selectionMode === "random"
      ? answers.randomCharacterCount
      : roster.length;

  // Describe whether the selected pool is used directly or sampled randomly.
  const selection =
    answers.selectionMode === "random"
      ? `${countWord(count)} of ${joinNames(roster)} will be randomly selected.`
      : `You will play ${joinNames(roster)}.`;

  // Describe the player's chosen character-unlock experience.
  const availability =
    answers.availability === "all"
      ? "All generated characters will be available from the start."
      : answers.availability === "random"
        ? "You will begin with one random character and unlock the rest through the multiworld."
        : `You will begin with ${answers.startingCharacter ?? "your chosen character"} and unlock the rest through the multiworld.`;

  // Resolve the friendly "all" concept to a count only for sentence construction.
  const goalCount = answers.goal === "all" ? count : answers.goal;

  // Use more natural wording when every generated character must finish.
  const goal =
    goalCount === count
      ? `${count === 1 ? "That character" : "All of them"} must complete a run to reach your goal.`
      : `${countWord(goalCount)} ${goalCount === 1 ? "character" : "characters"} must complete a run to reach your goal.`;

  // Explain the active standard or advanced Ascension strategy after roster behavior.
  const ascensions = summarizeCharacterAscensions(answers);

  // Join the independently derived clauses into the final review paragraph.
  const starters: string[] = [];
  const configurations: Array<[string, CharacterStarterOverrides | undefined]> =
    [
      ...answers.selectedCharacters.map(
        (character) =>
          [character, answers.individualStarterOverrides?.[character]] as [
            string,
            CharacterStarterOverrides | undefined,
          ],
      ),
      ...answers.moddedCharacters.map(
        (character) =>
          [character.name.trim(), character.starterOverrides] as [
            string,
            CharacterStarterOverrides | undefined,
          ],
      ),
    ];
  for (const [name, overrides] of configurations) {
    for (const [key, label] of [
      ["progressiveStarterCard", "starter card"],
      ["progressiveStarterRelic", "starter relic"],
    ] as const) {
      const value = overrides?.[key];
      if (value === "enabled" || value === "disabled")
        starters.push(`${name}: progressive ${label} ${value}`);
    }
  }
  const overrideSummary = starters.length
    ? includeFloorChecks
      ? ` Per-character overrides: ${starters.join("; ")}. Other starter settings inherit the global defaults.`
      : " Saved per-character starter overrides are inactive because Floor Checks are off."
    : "";
  return `${selection} ${availability} ${goal} ${ascensions}${overrideSummary}`;
}

/**
 * Summarizes the distribution selected in the Filler Items subsection.
 *
 * @param answers - Valid current filler-weight answers.
 * @returns A short sentence describing enabled items and their relative levels.
 * @remarks The summary intentionally describes relative odds rather than percentages.
 */
export function summarizeFillerAnswers(answers: FillerAnswers): string {
  // Count each semantic level without depending on generated option names or raw weights.
  let disabledCount = 0;
  let lowCount = 0;
  let mediumCount = 0;
  let highCount = 0;

  for (const level of Object.values(answers.weights)) {
    if (level === 0) {
      disabledCount += 1;
    } else if (level === 1) {
      lowCount += 1;
    } else if (level === 2) {
      mediumCount += 1;
    } else {
      highCount += 1;
    }
  }

  // Derive the enabled total so the first clause is easy to scan.
  const totalCount = Object.keys(answers.weights).length;
  const enabledCount = totalCount - disabledCount;

  // Explain the relative distribution without promising exact random percentages.
  return `Your filler pool enables ${enabledCount} of ${totalCount} reward types: ${lowCount} at low odds, ${mediumCount} at medium odds, and ${highCount} at high odds.`;
}

/**
 * Describes the shop's card layout.
 *
 * @param answers - Valid current Random Shop Cards answers.
 * @returns One sentence naming the kinds a random shop card slot can hold.
 */
export function summarizeShopCardAnswers(answers: ShopCardAnswers): string {
  const kinds = SHOP_CARD_KIND_LABELS.filter(
    ([id]) => answers.weights[id] > 0,
  ).map(([, label]) => label);
  if (!answers.enabled || kinds.length === 0) {
    return "Shops sell their usual cards.";
  }
  return `Each shop card slot holds a random kind of card: ${kinds.join(", ")}.`;
}

/** Plural labels for the review sentence, in option order. */
const SHOP_CARD_KIND_LABELS: readonly [ShopCardKindId, string][] = [
  ["attack", "Attacks"],
  ["skill", "Skills"],
  ["power", "Powers"],
  ["common", "Commons"],
  ["uncommon", "Uncommons"],
  ["rare", "Rares"],
  ["colorless", "colorless cards"],
  ["event", "Event cards"],
];

/**
 * Describes the shop's relic layout.
 *
 * @param answers - Valid current Random Shop Relics answers.
 * @returns One sentence naming the kinds a random shop relic slot can hold.
 */
export function summarizeShopRelicAnswers(
  answers: ShopStockAnswers<ShopRelicKindId>,
): string {
  const kinds = SHOP_RELIC_KIND_LABELS.filter(
    ([id]) => answers.weights[id] > 0,
  ).map(([, label]) => label);
  if (!answers.enabled || kinds.length === 0) {
    return "Shops sell their usual relics.";
  }
  return `Each shop relic slot holds a random kind of relic: ${kinds.join(", ")}.`;
}

/**
 * Describes the shop's potion layout.
 *
 * @param answers - Valid current Random Shop Potions answers.
 * @returns One sentence naming the kinds a random shop potion slot can hold.
 */
export function summarizeShopPotionAnswers(
  answers: ShopStockAnswers<ShopPotionKindId>,
): string {
  const kinds = SHOP_POTION_KIND_LABELS.filter(
    ([id]) => answers.weights[id] > 0,
  ).map(([, label]) => label);
  if (!answers.enabled || kinds.length === 0) {
    return "Shops sell their usual potions.";
  }
  return `Each shop potion slot holds a random kind of potion: ${kinds.join(", ")}.`;
}

/** Plural labels for the review sentence, in option order. */
const SHOP_RELIC_KIND_LABELS: readonly [ShopRelicKindId, string][] = [
  ["common", "Commons"],
  ["uncommon", "Uncommons"],
  ["rare", "Rares"],
  ["shop", "Shop relics"],
  ["event", "Event relics"],
  ["ancient", "Ancient relics"],
];

/** Plural labels for the review sentence, in option order. */
const SHOP_POTION_KIND_LABELS: readonly [ShopPotionKindId, string][] = [
  ["common", "Commons"],
  ["uncommon", "Uncommons"],
  ["rare", "Rares"],
  ["otherClass", "other classes' potions"],
  ["event", "Event potions"],
];

/**
 * Summarizes the run settings that affect an individual climb.
 *
 * @param answers - Valid current Run Settings answers.
 * @returns A compact paragraph describing Relic availability and removal, victory, seeds,
 * and Ancient rewards.
 */
export function summarizeRunAnswers(answers: RunAnswers): string {
  const immediateRelics =
    answers.relicRewardsAvailableAnytime === 0
      ? "Every Archipelago Relic item requires an earned in-run relic reward before it can be claimed."
      : `The first ${countWord(
          answers.relicRewardsAvailableAnytime,
        )} Archipelago Relic ${
          answers.relicRewardsAvailableAnytime === 1 ? "item is" : "items are"
        } available immediately.`;
  const removedCategories: string[] = [];
  if (answers.removeOpRelics.ancient) {
    removedCategories.push("Anytime Ancient");
  }
  if (answers.removeOpRelics.rare) {
    removedCategories.push("Rare");
  }
  const opRelics = removedCategories.length
    ? `Overpowered ${removedCategories.join(" and ")} relics are removed.`
    : "Every relic can appear.";
  const victoryRelease = answers.releaseOnVictory
    ? "A winning character's remaining checks are released when their goal is recorded."
    : "A winning character's remaining checks are not released automatically.";
  const eventActs = answers.eventsInAnyAct
    ? "In singleplayer, events can appear in any act."
    : "Events appear only in their usual acts.";
  const seeded = answers.seeded
    ? "Each character uses a fixed seed."
    : "Runs are not assigned fixed seeds.";

  const ancients = summarizeAncientAnswers(answers.ancients);

  return `${immediateRelics} ${opRelics} ${victoryRelease} ${eventActs} ${seeded} ${ancients}`;
}

/**
 * Summarizes the selected Boons and Blitz merchants.
 *
 * @param answers - Current Boons answers.
 * @returns A sentence or two naming what makes runs easier, or that nothing does.
 */
export function summarizeBoonAnswers(answers: BoonAnswers): string {
  const names = BOONS.filter((boon) =>
    answers.selected.includes(boon.optionName),
  ).map((boon) => boon.name);
  const boons = names.length
    ? `${joinNames(names)} ${names.length === 1 ? "applies" : "apply"} to every run; logic ignores ${names.length === 1 ? "it" : "them"}.`
    : "No Boons are selected.";
  const blitzMerchants = answers.blitzMerchantChance
    ? ` ${answers.blitzMerchantChance}% of shops are Blitz merchants. They are very strong and logic ignores them; they need the game's beta branch.`
    : "";

  return `${boons}${blitzMerchants}`;
}

/**
 * Checks whether any challenge is turned on.
 *
 * @param answers - Current Challenges answers.
 * @returns Whether the player opted in and chose anything that makes runs harder.
 */
export function hasActiveChallenges(answers: ChallengeAnswers): boolean {
  return (
    answers.enabled &&
    (answers.runModifiers.length > 0 ||
      answers.enemyHpScaling > 0 ||
      answers.enemyDamageScaling > 0 ||
      answers.dreadMode ||
      countSightItems(answers) > 0 ||
      answers.strictCardRewards)
  );
}

/**
 * Builds the cautions shown with the Challenges summary.
 *
 * @param answers - Current Challenges answers.
 * @returns The warning text, or nothing while no challenge is on.
 */
export function describeChallengeWarnings(
  answers: ChallengeAnswers,
): string | undefined {
  if (!hasActiveChallenges(answers)) {
    return undefined;
  }

  const warnings = [
    "Challenges are on. Logic doesn't account for them, so a seed can get stuck if runs become too hard to win.",
  ];
  const betaOnly = getActiveBetaOnlyChallenges(answers);

  if (betaOnly.length) {
    const verb = betaOnly.length === 1 ? "is" : "are";
    const pronoun = betaOnly.length === 1 ? "it" : "them";
    warnings.push(
      `${joinNames(betaOnly)} ${verb} only in the game's beta branch. On the main branch the slot plays without ${pronoun}.`,
    );
  }

  return warnings.join(" ");
}

/**
 * Names the Sight items a hidden-information challenge waits for.
 *
 * @param copies - Selected Sight item count other than `off`.
 * @param itemName - Display name of the Sight item.
 * @returns A phrase such as "Intent Sight" or "either of two Intent Sights".
 */
function describeSightItems(
  copies: Exclude<SightCopies, "off">,
  itemName: string,
): string {
  if (copies === "one") {
    return itemName;
  }
  return copies === "two"
    ? `either of two ${itemName}s`
    : `any of ${copies} ${itemName}s`;
}

/**
 * Names the Sights whose second copy adds a bonus.
 *
 * @param answers - Valid current Challenges answers.
 * @returns A sentence, or an empty string when no Sight has a bonus to give.
 */
function describeSightBonuses(answers: ChallengeAnswers): string {
  const names = SIGHTS.filter(
    (sight) =>
      answers.sightBonuses[sight.bonusField] &&
      hasSecondSightCopy(answers, sight),
  ).map((sight) => sight.itemName);
  if (!names.length) {
    return "";
  }
  return names.length === 1
    ? `A second ${names[0]} adds its Sight Bonus.`
    : `Second copies of ${joinNames(names)} add their Sight Bonuses.`;
}

/**
 * Summarizes the selected challenges.
 *
 * @param answers - Valid current Challenges answers.
 * @returns A sentence per active challenge, or a note that none are on.
 */
export function summarizeChallengeAnswers(answers: ChallengeAnswers): string {
  if (!hasActiveChallenges(answers)) {
    return "No challenges are on.";
  }

  const sentences: string[] = [];

  if (answers.runModifiers.length) {
    const names = RUN_MODIFIERS.filter((modifier) =>
      answers.runModifiers.includes(modifier.optionName),
    ).map((modifier) => modifier.name);
    sentences.push(`Run Modifiers: ${joinNames(names)}.`);
  }

  if (answers.enemyHpScaling > 0) {
    sentences.push(
      `Enemies have ${answers.enemyHpScaling}% more max HP in Acts 2 and 3.`,
    );
  }

  if (answers.enemyDamageScaling > 0) {
    sentences.push(
      `Enemies deal ${answers.enemyDamageScaling}% more damage in Acts 2 and 3.`,
    );
  }

  if (answers.dreadMode) {
    sentences.push(
      "Runs are Dread runs, with the Ascensions from Character Setup.",
    );
  }

  for (const sight of SIGHTS) {
    const copies = answers[sight.copiesField];
    if (copies !== "off") {
      sentences.push(
        `${sight.hiddenSummary} until ${describeSightItems(copies, sight.itemName)} is found.`,
      );
    }
  }

  const sightBonuses = describeSightBonuses(answers);
  if (sightBonuses) {
    sentences.push(sightBonuses);
  }

  if (answers.strictCardRewards) {
    sentences.push("Card Rewards in the AP Menu are picked one at a time.");
  }

  return sentences.join(" ");
}

/**
 * Summarizes the progressive choices in the Starting Equipment subsection.
 *
 * @param answers - Progressive starter-card and starter-relic choices.
 * @param includeFloorChecks - Whether the required Floor Check budget is enabled.
 * @returns A sentence describing enabled equipment or its disabled dependency.
 */
export function summarizeStartingEquipmentAnswers(
  answers: StartingEquipmentAnswers,
  includeFloorChecks: boolean,
): string {
  // Match compilation and Python normalization when Floor Checks are unavailable.
  if (!includeFloorChecks) {
    return "Progressive Starting Equipment is disabled because Floor Checks are off.";
  }

  // Collect enabled equipment in the same order as the subsection controls.
  const enabled: string[] = [];

  if (answers.progressiveStarterCard) {
    enabled.push("starter cards");
  }

  if (answers.progressiveStarterRelic) {
    enabled.push("starter relics");
  }

  // Keep the all-disabled state explicit in the final review.
  if (!enabled.length) {
    return "Progressive Starting Equipment is disabled by default.";
  }

  // Describe the selected progressive item families without exposing YAML keys.
  return `Progressive ${joinNames(enabled)} are enabled by default.`;
}

/** Summarizes the Ancient choices on Run Settings. */
export function summarizeAncientAnswers(answers: AncientAnswers): string {
  // Translate Ancient timing into language about how the player claims rewards.
  const timing =
    answers.relicLocation === "anytime"
      ? "Progressive Ancient rewards can be claimed as soon as they arrive."
      : "Progressive Ancient rewards wait for the normal start-of-act encounter.";

  // Explain pool breadth independently from the timing decision.
  const pool = {
    balanced: "Ancient choices use each act's natural pool.",
    chaos: "Ancient choices may come from any appropriate Ancient in that act.",
    true_chaos:
      "Act 2 and Act 3 Ancient pools are combined for both progressive rewards.",
  }[answers.relicPool];

  return `${timing} ${pool}`;
}

/** Summarizes the common Archipelago settings on the Progression step. */
export function summarizeProgressionAnswers(
  answers: ProgressionAnswers,
): string {
  const progression = `Progression balancing is set to ${answers.progressionBalancing}.`;
  const accessibility =
    answers.accessibility === "full"
      ? "All locations must remain reachable."
      : "Only goal-required locations must remain reachable.";
  return `${progression} ${accessibility}`;
}

/**
 * Summarizes enabled check and reward categories.
 *
 * @param answers - Valid current check-type answers.
 * @returns A sentence listing enabled optional location and reward behavior.
 */
export function summarizeCheckAnswers(answers: CheckTypeAnswers): string {
  // Collect only enabled categories using concise review labels.
  const enabled: string[] = [];

  if (answers.neowSanity) {
    enabled.push("Neow's Blessing");
  }

  if (answers.includeFloorChecks) {
    enabled.push("floor checks");
  }

  if (answers.campfireSanity) {
    enabled.push("campfire actions");
  }

  if (answers.goldSanity) {
    enabled.push("gold rewards");
  }

  if (answers.potionSanity) {
    enabled.push("potion drops");
  }

  if (answers.shuffleAllCards) {
    enabled.push("every card reward");
  }

  // Give the all-disabled case an explicit result instead of an empty list.
  if (!enabled.length) {
    return "No optional check or reward categories are enabled.";
  }

  // Join enabled labels in natural language for quick review scanning.
  return `The multiworld also shuffles ${joinNames(enabled)}.`;
}

/**
 * Summarizes Shop Sanity slot counts, removal progression, and pricing.
 *
 * @param answers - Valid current Shop answers.
 * @returns A paragraph describing disabled Shop Sanity or its complete configuration.
 */
export function summarizeShopAnswers(answers: ShopAnswers): string {
  // Dependent fields have no gameplay effect while the controlling toggle is disabled.
  if (!answers.enabled) {
    return "Shop Sanity is disabled; shop inventory and card removal remain vanilla.";
  }

  // Describe all four slot counts explicitly because each produces different items.
  const slots = `${answers.cardSlots} colored card, ${answers.neutralCardSlots} neutral card, ${answers.relicSlots} relic, and ${answers.potionSlots} potion slots are shuffled.`;
  const removal = answers.removeSlots
    ? "Card removal is a three-stage progressive unlock."
    : "Card removal remains available normally.";

  // Convert internal cost-choice names into readable review copy.
  const costs = {
    Fixed: "Every AP shop purchase costs 15 gold.",
    Super_Discount_Tiered:
      "AP shop purchases use super-discount tiered prices.",
    Discount_Tiered: "AP shop purchases use discount tiered prices.",
    Tiered: "AP shop purchases use ordinary tiered prices.",
  }[answers.costs];

  // Join the dependent choices in their UI order.
  return `${slots} ${removal} ${costs}`;
}

/**
 * Summarizes the configured Bonus Items subsection.
 *
 * @param answers - Ordered Bonus Item answers from the Item Pool table.
 * @returns A sentence covering the empty state or the configured item count.
 */
export function summarizeBonusItemAnswers(answers: BonusItemAnswer[]): string {
  // Keep the deliberate empty state explicit in the final review.
  if (!answers.length) {
    return "No Bonus Items are added to the item pool.";
  }

  // Rows are listed individually beneath this summary, so the prose stays compact.
  return `${countWord(answers.length)} Bonus ${
    answers.length === 1 ? "Item is" : "Items are"
  } added to the item pool before any filler.`;
}

/**
 * Builds structured review rows for every configured Bonus Item.
 *
 * @param answers - Ordered Bonus Item answers from the Item Pool table.
 * @returns Image/name/details rows in the player's configured order.
 * @remarks Delegates all relic resolution to the shared display helper so the
 * review list matches the Item Pool table exactly.
 */
export function buildBonusItemReviewRows(
  answers: BonusItemAnswer[],
): BonusItemDisplayRow[] {
  return getBonusItemDisplayRows(answers);
}

/**
 * Summarizes the Checks step.
 *
 * @param answers - Valid Starting Equipment and check-type answers.
 * @returns One paragraph following the step's order.
 */
export function summarizeChecksAnswers(answers: ChecksAnswers): string {
  const startingEquipment = summarizeStartingEquipmentAnswers(
    answers.startingEquipment,
    answers.types.includeFloorChecks,
  );
  const checks = summarizeCheckAnswers(answers.types);
  return `${checks} ${startingEquipment}`;
}

/**
 * Summarizes the Shops step.
 *
 * @param answers - Valid Shop Sanity and shop stock answers.
 * @returns One paragraph following the step's order.
 */
export function summarizeShopsAnswers(answers: ShopsAnswers): string {
  const shop = summarizeShopAnswers(answers.shop);
  const shopCards = summarizeShopCardAnswers(answers.shopCards);
  const shopRelics = summarizeShopRelicAnswers(answers.shopRelics);
  const shopPotions = summarizeShopPotionAnswers(answers.shopPotions);
  return `${shop} ${shopCards} ${shopRelics} ${shopPotions}`;
}

/**
 * Summarizes the Item Pool step.
 *
 * @param answers - Valid Bonus Item, filler and trap answers.
 * @returns One paragraph following the step's order; Bonus Items are listed separately.
 */
export function summarizeItemPoolAnswers(answers: ItemPoolAnswers): string {
  const bonusItems = summarizeBonusItemAnswers(answers.bonusItems);
  const filler = summarizeFillerAnswers(answers.filler);
  const traps =
    answers.traps.chance === 0
      ? "Traps are disabled."
      : `${answers.traps.chance}% of filler slots become traps, with ${Object.values(answers.traps.weights).filter((weight) => weight !== "none").length} trap types enabled. At most one queued trap applies per player per turn.`;
  return `${bonusItems} ${filler} ${traps}`;
}

/**
 * Summarizes Death Link participation and received-link consequences.
 *
 * @param answers - Valid current Death Link answers.
 * @returns A sentence describing disabled Death Link or its damage and curse behavior.
 */
export function summarizeDeathLinkAnswers(answers: DeathLinkAnswers): string {
  // Dependent fields have no gameplay effect while the controlling toggle is disabled.
  if (!answers.enabled) {
    return "Death Link is disabled.";
  }

  // Lethal mode replaces both preserved nonlethal preferences in the compiler.
  if (answers.beKilled) {
    return "A received Death Link kills you by dealing 100% of your maximum health in damage; nonlethal effects are disabled.";
  }

  // Describe only the received effects explicitly selected by the player.
  const effects: string[] = [];

  if (answers.receiveFragment) {
    effects.push("adds a Death Fragment Curse card");
  }

  if (answers.receiveDamage) {
    effects.push(
      `deals ${answers.damagePercent}% of your maximum health in damage`,
    );
  }

  // Death Link can still transmit this player's death without an incoming penalty.
  if (!effects.length) {
    return "Death Link is enabled, but a received death has no additional effect.";
  }

  // Reuse the natural-language list formatter for the one- or two-effect result.
  return `A received Death Link ${joinNames(effects)}.`;
}

/** Summarizes starting items, starting hints, and where items may be placed. */
export function summarizeItemAnswers(answers: ItemAnswers): string {
  const sentences: string[] = [];

  if (answers.startingInventory.length) {
    const stacks = answers.startingInventory.map((stack) => {
      const name = itemName(stack.character, stack.item);
      return stack.count === 1 ? name : `${stack.count}× ${name}`;
    });
    sentences.push(`You start with ${joinNames(stacks)}.`);
  } else {
    sentences.push("You start with no extra items.");
  }
  if (answers.startHints.length) {
    sentences.push(
      `${joinNames(answers.startHints)} ${answers.startHints.length === 1 ? "is" : "are"} hinted from the start.`,
    );
  }
  if (answers.localItems.length) {
    sentences.push(`${joinNames(answers.localItems)} stay in your world.`);
  }
  if (answers.nonLocalItems.length) {
    sentences.push(
      `${joinNames(answers.nonLocalItems)} go to other players' worlds.`,
    );
  }
  return sentences.join(" ");
}

/**
 * Describes settings kept from an imported YAML that the builder doesn't ask about.
 *
 * @param keptOptions - Kept values keyed by option name.
 * @returns A sentence naming them, or null when there are none.
 */
export function summarizeKeptOptions(
  keptOptions: Readonly<Record<string, unknown>>,
): string | null {
  const names = Object.keys(keptOptions).map(
    (key) => optionCatalog.options[key]?.display_name ?? key,
  );
  if (!names.length) {
    return null;
  }
  return `Your imported YAML's ${joinNames(names)} ${names.length === 1 ? "is" : "are"} kept as written. The builder doesn't ask about ${names.length === 1 ? "it" : "them"}; edit the downloaded YAML to change ${names.length === 1 ? "it" : "them"}.`;
}

/**
 * Builds ordered review sections for every implemented wizard step.
 *
 * @param answers - Complete valid player-facing wizard answers.
 * @returns Titled review summaries in navigation order.
 */
export function buildWizardReviewSections(
  answers: WizardAnswers,
): WizardReviewSection[] {
  // Build each section independently so review layout does not understand answer fields.
  const sections: WizardReviewSection[] = [
    {
      title: "Character Setup",
      summary: summarizeCharacterAnswers(
        answers.characters,
        answers.checks.types.includeFloorChecks,
      ),
      warning: describeBetaOnlyCharacters(answers.characters),
    },
    { title: "Run Settings", summary: summarizeRunAnswers(answers.run) },
    { title: "Checks", summary: summarizeChecksAnswers(answers.checks) },
    { title: "Shops", summary: summarizeShopsAnswers(answers.shops) },
    {
      title: "Item Pool",
      summary: summarizeItemPoolAnswers(answers.itemPool),
      items: buildBonusItemReviewRows(answers.itemPool.bonusItems),
    },
    { title: "Boons", summary: summarizeBoonAnswers(answers.boons) },
    {
      title: "Challenges",
      summary: summarizeChallengeAnswers(answers.challenges),
      warning: describeChallengeWarnings(answers.challenges),
    },
    {
      title: "Death Link",
      summary: summarizeDeathLinkAnswers(answers.deathLink),
    },
    {
      title: "Progression",
      summary: summarizeProgressionAnswers(answers.progression),
    },
    { title: "Items & Hints", summary: summarizeItemAnswers(answers.items) },
  ];

  const kept = summarizeKeptOptions(answers.keptOptions);
  if (kept !== null) {
    sections.push({ title: "Kept From Your YAML", summary: kept });
  }

  // Return a fresh presentation model for the review component.
  return sections;
}
