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
  CharacterAnswers,
  CheckAnswers,
  ChecksAndRewardsAnswers,
  DeathLinkAnswers,
  FillerAnswers,
  RunAnswers,
  ShopAnswers,
  WizardAnswers,
} from "./WizardAnswers";
import { getConfiguredCharacterNames } from "./CharacterRoster";

export interface WizardReviewSection {
  title: string;
  summary: string;
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
 * Summarizes Character Setup choices in gameplay language.
 *
 * @param answers - Valid current Character Setup answers.
 * @returns A short paragraph covering selection, unlocking, and completion goal.
 * @remarks Compile and validate before displaying this summary so impossible answer
 * combinations cannot be presented as if they were valid.
 */
export function summarizeCharacterAnswers(answers: CharacterAnswers): string {
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
  return `${selection} ${availability} ${goal} ${ascensions}`;
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
 * Summarizes Ancient reward, Neow, relic-choice, and seed behavior.
 *
 * @param answers - Valid current Run Rules answers.
 * @returns A compact paragraph describing the resulting climb rules.
 */
export function summarizeRunAnswers(answers: RunAnswers): string {
  // Translate Ancient timing into language about how the player claims rewards.
  const timing =
    answers.ancientRelicLocation === "anytime"
      ? "Progressive Ancient rewards can be claimed as soon as they arrive."
      : "Progressive Ancient rewards wait for the normal start-of-act encounter.";

  // Explain pool breadth independently from the timing decision.
  const pool = {
    balanced: "Ancient choices use each act's natural pool.",
    chaos: "Ancient choices may come from any appropriate Ancient in that act.",
    true_chaos:
      "Act 2 and Act 3 Ancient pools are combined for both progressive rewards.",
  }[answers.ancientRelicPool];

  // Summarize the remaining scalar and binary run choices.
  const relicChoices = `Archipelago Relic items offer ${countWord(answers.relicChoiceCount)} ${answers.relicChoiceCount === 1 ? "choice" : "choices"}.`;
  const neow = answers.neowSanity
    ? "Neow's blessing is shuffled."
    : "Neow's blessing remains vanilla.";
  const seeded = answers.seeded
    ? "Each character uses a fixed seed."
    : "Runs are not assigned fixed seeds.";
  const progression = `Progression balancing is set to ${answers.progressionBalancing}.`;
  const accessibility =
    answers.accessibility === "full"
      ? "All locations must remain reachable."
      : "Only goal-required locations must remain reachable.";

  // Preserve question order so the paragraph matches the step the player completed.
  return `${timing} ${pool} ${relicChoices} ${neow} ${seeded} ${progression} ${accessibility}`;
}

/**
 * Summarizes enabled check and reward categories.
 *
 * @param answers - Valid current Checks & Rewards answers.
 * @returns A sentence listing enabled optional location and reward behavior.
 */
export function summarizeCheckAnswers(answers: CheckAnswers): string {
  // Collect only enabled categories using concise review labels.
  const enabled: string[] = [];

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
 * Summarizes the three option families in the combined Checks & Rewards step.
 *
 * @param answers - Valid ordinary-check, Shop Sanity, and filler subsection answers.
 * @returns One paragraph following the same order as the combined step UI.
 * @remarks The focused summary functions remain reusable while this facade mirrors
 * the consolidated answer model and visible review section.
 */
export function summarizeChecksAndRewardsAnswers(
  answers: ChecksAndRewardsAnswers,
): string {
  // Summarize each independently compiled family through its focused prose helper.
  const checks = summarizeCheckAnswers(answers.checks);
  const shop = summarizeShopAnswers(answers.shop);
  const filler = summarizeFillerAnswers(answers.filler);

  // Preserve the visible subsection order in the final combined review paragraph.
  return `${checks} ${shop} ${filler}`;
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
      summary: summarizeCharacterAnswers(answers.characters),
    },
    { title: "Run Rules", summary: summarizeRunAnswers(answers.run) },
    {
      title: "Checks & Rewards",
      summary: summarizeChecksAndRewardsAnswers(answers.checksAndRewards),
    },
    {
      title: "Death Link",
      summary: summarizeDeathLinkAnswers(answers.deathLink),
    },
  ];

  // Return a fresh presentation model for the review component.
  return sections;
}
