/**
 * @file Declares wizard navigation, question copy, and standard control bindings.
 *
 * Add flow-level questions here, their persisted values in `WizardAnswers.ts`, and —
 * for standard controls — a declarative `control` descriptor rendered generically by
 * `components/wizard/core/WizardControl.vue`. Only bespoke interfaces (portrait grids,
 * relic tables, Ascension editors) still add custom markup in a step component.
 * This file must not know Archipelago option keys; `wizard/compiler` owns that
 * translation, and numeric bounds arrive through semantic names in `optionRanges.ts`.
 */

import { MAX_MODDED_CHARACTERS } from "./CharacterRoster";
import { includeFloorChecksTransition } from "./checksTransitions";
import {
  beKilledTransition,
  deathLinkEnabledTransition,
  receiveDamageTransition,
  receiveFragmentTransition,
} from "./deathLinkTransitions";
import { generatedRange } from "./optionRanges";
import type { WizardControlDescriptor } from "./QuestionControl";
import type { WizardAnswers } from "./WizardAnswers";

export interface WizardQuestion {
  id: string;
  title?: string;
  description?: string;
  /** Extra guidance shown in the question's help area; resolved per render. */
  help?: string | ((answers: WizardAnswers) => string | null);
  isVisible?: (answers: WizardAnswers) => boolean;
  /** Keeps the question visible but grayed out until prerequisites are met. */
  isEnabled?: (answers: WizardAnswers) => boolean;
  /** Standard control rendered generically; omit for bespoke step markup. */
  control?: WizardControlDescriptor;
}

/** Answer sections addressable by wizard steps; the player name is document metadata. */
export type WizardSectionKey = Exclude<keyof WizardAnswers, "playerName">;

export interface WizardStep {
  id: string;
  title: string;
  description?: string;
  /** Answer section owned by this step's questions; absent for Review. */
  sectionKey?: WizardSectionKey;
  questions: WizardQuestion[];
}

/**
 * Checks whether random character selection needs its count prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the random-count question should be shown.
 */
function usesRandomCharacterSelection(answers: WizardAnswers): boolean {
  // A count is irrelevant when every selected character is used.
  return answers.characters.selectionMode === "random";
}

/**
 * Checks whether character unlocking needs a fixed-start prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the starting-character question should be shown.
 */
function usesFixedStartingCharacter(answers: WizardAnswers): boolean {
  // A named character is irrelevant for all-at-start and random-start modes.
  return answers.characters.availability === "fixed";
}

/**
 * Checks whether entered modded characters need their name-and-help table.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether at least one modded character row exists.
 */
function hasModdedCharacters(answers: WizardAnswers): boolean {
  // The table is mounted only after the Modded Characters portrait is toggled on.
  return answers.characters.moddedCharacters.length > 0;
}

/**
 * Checks whether one Ascension configuration applies to the complete roster.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the shared Ascension checklist should be shown.
 */
function usesSharedAscensions(answers: WizardAnswers): boolean {
  // Shared mode compiles through the standard `ascension` option arrays.
  return answers.characters.ascensionMode === "shared";
}

/**
 * Checks whether each configured character needs independent Ascension settings.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the per-character advanced editor should be shown.
 */
function usesIndividualAscensions(answers: WizardAnswers): boolean {
  // Individual mode compiles through the `advanced_characters` dictionary.
  return answers.characters.ascensionMode === "individual";
}

/**
 * Checks whether Shop Sanity's dependent configuration is meaningful.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether shop slot and cost questions should be shown.
 */
function usesShopSanity(answers: WizardAnswers): boolean {
  // Slot counts and pricing have no effect while Shop Sanity is disabled.
  return answers.checksAndRewards.shop.enabled;
}

/**
 * Checks whether progressive Starting Equipment has its Floor Check prerequisite.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the Starting Equipment toggles should accept input.
 */
function usesFloorChecks(answers: WizardAnswers): boolean {
  // Progressive starter items occupy filler slots that only Floor Checks create.
  return answers.checksAndRewards.checks.includeFloorChecks;
}

/**
 * Provides the Starting Equipment prerequisite hint while it applies.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Guidance for enabling Floor Checks, or `null` once satisfied.
 */
function startingEquipmentHelp(answers: WizardAnswers): string | null {
  if (usesFloorChecks(answers)) {
    return null;
  }

  return "Enable Floor Checks in the additional checks section below to use progressive Starting Equipment.";
}

/**
 * Checks whether Death Link's dependent behavior is meaningful.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether damage and fragment questions should be shown.
 */
function usesDeathLink(answers: WizardAnswers): boolean {
  // Received-link behavior has no effect while Death Link is disabled.
  return answers.deathLink.enabled;
}

/**
 * Checks whether received Death Link damage needs its percentage slider.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether nonlethal maximum-health damage is enabled and editable.
 */
function usesDeathLinkDamage(answers: WizardAnswers): boolean {
  // Lethal mode owns the generated 100% value and disables nonlethal controls.
  return (
    answers.deathLink.enabled &&
    answers.deathLink.receiveDamage &&
    !answers.deathLink.beKilled
  );
}

/** Character Setup definition consumed by its component and wizard navigation. */
export const characterSetupStep: WizardStep = {
  id: "characters",
  title: "Character Setup",
  description:
    "Select which characters you want to play as, how they become available, and their Ascension settings.",
  sectionKey: "characters",
  questions: [
    {
      id: "characters",
      title: "Which characters do you want to play as?",
      description: `Choose at least one character. You can have a mix of Vanilla and Modded characters.`,
    },
    {
      id: "modded-characters",
      title: "Setup Modded Characters",
      description: `Enter each modded character's unique ID so that the Archipelago mod can find them and use them properly in-game. You can have up to ${MAX_MODDED_CHARACTERS} modded characters.`,
      isVisible: hasModdedCharacters,
    },
    {
      id: "ascension-mode",
      title: "Should characters have different Ascension settings?",
      control: {
        kind: "radio",
        field: "ascensionMode",
        choices: [
          {
            label: "Use one setup for every character",
            description:
              "Every built-in and modded character uses the same Ascensions and Ascension Downs.",
            value: "shared",
          },
          {
            label: "Configure each character separately",
            description:
              "Give every selected character its own Ascensions and Ascension Down item pool.",
            value: "individual",
          },
        ],
      },
    },
    {
      id: "shared-ascensions",
      title: "Select your desired Ascensions",
      isVisible: usesSharedAscensions,
    },
    {
      id: "individual-ascensions",
      title: "Configure each character's Ascensions",
      isVisible: usesIndividualAscensions,
    },
    {
      id: "selection",
      title: "Should every selected character be used?",
      control: {
        kind: "radio",
        field: "selectionMode",
        choices: [
          {
            label: "Use all selected characters",
            description:
              "Every character configured above will be included in your world.",
            value: "all",
          },
          {
            label: "Randomly select some",
            description:
              "The Archipelago Randomizer will choose a smaller roster from the characters you selected above.",
            value: "random",
          },
        ],
      },
    },
    {
      id: "random-count",
      title: "How many characters should be randomly selected?",
      isVisible: usesRandomCharacterSelection,
    },
    {
      id: "availability",
      title: "How should characters become available?",
      control: {
        kind: "radio",
        field: "availability",
        choices: [
          {
            label: "Start with all characters",
            description:
              "Your entire generated roster is immediately playable.",
            value: "all",
          },
          {
            label: "Start with one random character",
            description:
              "Begin with a random member of the roster and find unlocks for the rest.",
            value: "random",
          },
          {
            label: "Choose a starting character",
            description:
              "Pick who begins unlocked, then find the remaining character unlocks.",
            value: "fixed",
          },
        ],
      },
    },
    {
      id: "starting-character",
      title: "Which character should start unlocked?",
      isVisible: usesFixedStartingCharacter,
    },
    {
      id: "goal",
      title: "How many characters must complete a run to finish your goal?",
    },
  ],
};

/** Gameplay Modifiers definition consumed by its component and wizard navigation. */
export const runSetupStep: WizardStep = {
  id: "run",
  title: "Gameplay Modifiers",
  description:
    "Configure your experience, how Archipelago Rewards are handled, and what gets randomized.",
  sectionKey: "run",
  questions: [
    {
      id: "seeded",
      title: "Should each run be unique or use a fixed seed?",
      description: "In the Vanilla game, runs are randomized.",
      control: {
        kind: "checkbox",
        field: "seeded",
        label: "Use fixed seeds",
        description:
          "Each generated character receives a repeatable seed for climbing the Spire.",
      },
    },
    {
      id: "release-on-victory",
      title: "Should undiscovered checks be released when a character wins?",
      description:
        "When enabled, beating the final boss with a character releases any remaining checks they had not discovered.",
      control: {
        kind: "checkbox",
        field: "releaseOnVictory",
        label: "Release remaining checks on victory",
        description:
          "Release the winning character's unfinished checks as soon as their goal is recorded.",
      },
    },
    {
      id: "relic-rewards-available-anytime",
      title: "How many Relic rewards should be available immediately?",
      description:
        "This number controls how many Relic Rewards can be claimed in the Archipelago Loot menu at anytime (including the beginning of the run). Subsequent Relic Rewards must be found in their original locations in the Spire once unlocked.",
      help: "Note: Relics will always be shuffled into the Multiworld, this setting just controls how many Relics can be claimed from the Loot Menu versus a Vanilla location.",
      control: {
        kind: "number",
        field: "relicRewardsAvailableAnytime",
        range: generatedRange("relicRewardsAvailableAnytime"),
      },
    },
  ],
};

/** Checks & Rewards definition consumed by its component and navigation. */
export const checkSetupStep: WizardStep = {
  id: "checks",
  title: "Checks & Rewards",
  description:
    "Select what locations (checks) and items are enabled and available in your world.",
  sectionKey: "checksAndRewards",
  questions: [
    {
      id: "starting-equipment",
      title: "Which starting equipment should be progressive?",
      description:
        "These options require Floor Checks and add two progressive items per configured character.",
      help: startingEquipmentHelp,
      isEnabled: usesFloorChecks,
      control: {
        kind: "checkbox-grid",
        items: [
          {
            field: "startingEquipment.progressiveStarterCard",
            label: "Progressive Starter Card",
            description:
              "Add two items per character that restore, then transform, their compatible special starter card(s). For example, the Ironclad would first unlock Bash, then transform it into Break.",
          },
          {
            field: "startingEquipment.progressiveStarterRelic",
            label: "Progressive Starter Relic",
            description:
              "Add two items per character that restore, then upgrade, their compatible starter relic. For example, the Ironclad would first unlock Burning Blood, then upgrade it to Black Blood.",
          },
        ],
      },
    },
    {
      id: "ancient-location",
      title: "When should Progressive Ancient rewards be available?",
      control: {
        kind: "radio",
        field: "ancients.relicLocation",
        choices: [
          {
            label: "At the start of each act",
            description:
              "Claim the reward through that act's normal Ancient encounter.",
            value: "start_of_act",
          },
          {
            label: "As soon as it arrives",
            description:
              "Claim linked choices from the Archipelago reward menu at any time.",
            value: "anytime",
          },
        ],
      },
    },
    {
      id: "ancient-pool",
      title: "Which Ancient relics may appear in each reward?",
      control: {
        kind: "radio",
        field: "ancients.relicPool",
        choices: [
          {
            label: "Vanilla",
            description:
              "Use the base game behavior: Visiting an Ancient provides relics from their own relic pool.",
            value: "balanced",
          },
          {
            label: "Chaos",
            description:
              "Ancient Relic Rewards are comprised of any relic an Ancient from that Act can give you.",
            value: "chaos",
          },
          {
            label: "True Chaos",
            description:
              "Ancient Relic Rewards are comprised of any relic from any Ancient in the game, regardless of Act (except Neow)",
            value: "true_chaos",
          },
        ],
      },
    },
    {
      id: "check-types",
      title: "Which additional checks and rewards should be shuffled?",
      help: "Each enabled option adds or changes locations and items for every generated character.",
      control: {
        kind: "checkbox-grid",
        // Add a new card here only after its semantic answer, generated-key registry,
        // compiler assignment, and generated schema entry have also been added.
        items: [
          {
            field: "checks.neowSanity",
            label: "Neow's Blessing",
            description:
              "Include Neow's Blessing as a third Progressive Ancient reward.",
          },
          {
            field: "checks.includeFloorChecks",
            label: "Floor Checks",
            description:
              "Make reaching new floors into locations and add helpful filler items to fill them.",
            // Disabling Floor Checks also clears dependent Starting Equipment.
            applyChange: includeFloorChecksTransition,
          },
          {
            field: "checks.campfireSanity",
            label: "Campfire Actions",
            description:
              "Shuffle Progressive Rest and Smith items into the Multiworld, per character, per Act.",
          },
          {
            field: "checks.goldSanity",
            label: "Gold Rewards",
            description:
              "Move combat, elite, and boss gold rewards into the Multiworld.",
          },
          {
            field: "checks.potionSanity",
            label: "Potion Drops",
            description: "Move potion rewards into the Multiworld.",
          },
          {
            field: "checks.shuffleAllCards",
            label: "All Card Rewards",
            description:
              "Shuffle every card reward instead of the default behavior of shuffling every other reward.",
          },
          {
            field: "shop.enabled",
            label: "Shop Slots",
            description:
              "Parts of the Shop can be shuffled into the Multiworld.",
          },
        ],
      },
    },
    {
      id: "shop-slots",
      title: "How many slots of each type should be shuffled?",
      help: "Each count controls how many slots of that type are unavailable until their corresponding AP items arrive.",
      isVisible: usesShopSanity,
      control: {
        kind: "number-grid",
        fields: [
          {
            field: "shop.cardSlots",
            label: "Colored card slots",
            range: generatedRange("shopCardSlots"),
          },
          {
            field: "shop.neutralCardSlots",
            label: "Neutral card slots",
            range: generatedRange("shopNeutralCardSlots"),
          },
          {
            field: "shop.relicSlots",
            label: "Relic slots",
            range: generatedRange("shopRelicSlots"),
          },
          {
            field: "shop.potionSlots",
            label: "Potion slots",
            range: generatedRange("shopPotionSlots"),
          },
        ],
      },
    },
    {
      id: "shop-removal",
      title: "Should card removal become a progressive unlock?",
      isVisible: usesShopSanity,
      control: {
        kind: "checkbox",
        field: "shop.removeSlots",
        label: "Shuffle Card Removal",
        description:
          "If enabled, you can only remove a card at a shop if you've unlocked enough Progressive Card Removal Unlocks for that Act.",
      },
    },
    {
      id: "shop-costs",
      title: "How expensive should shuffled shop slots be?",
      description:
        "Logic does not account for these prices, so high costs can make an unlucky shop less convenient.",
      isVisible: usesShopSanity,
      control: {
        kind: "radio",
        field: "shop.costs",
        choices: [
          {
            label: "Fixed",
            description: "Every shuffled shop purchase costs 15 gold.",
            value: "Fixed",
          },
          {
            label: "Super-discount tiered",
            description: "Use 20% of the usual rarity-based shop price.",
            value: "Super_Discount_Tiered",
          },
          {
            label: "Discount tiered",
            description: "Use 50% of the usual rarity-based shop price.",
            value: "Discount_Tiered",
          },
          {
            label: "Tiered",
            description: "Use the ordinary rarity-based price for each slot.",
            value: "Tiered",
          },
        ],
      },
    },
    {
      id: "bonus-items",
      title: "Which Bonus Items should be added to the item pool?",
      description:
        "Bonus Items are guaranteed additions placed before any filler items. Generation fails if there are not enough filler slots for them.",
    },
    {
      id: "filler-weights",
      title: "How often should each filler item appear?",
    },
  ],
};

/** Death Link definition consumed by its component and wizard navigation. */
export const deathLinkSetupStep: WizardStep = {
  id: "death-link",
  title: "Death Link",
  description:
    "Choose whether deaths are shared and what happens when another player dies.",
  sectionKey: "deathLink",
  questions: [
    {
      id: "death-link-enabled",
      control: {
        kind: "checkbox",
        field: "enabled",
        label: "Enable Death Link",
        description:
          "Your deaths affect other participating players, and their deaths affect you.",
        // Enabling must also leave one received effect selected; the rule is pure.
        applyChange: deathLinkEnabledTransition,
      },
    },
    {
      id: "death-link-effects",
      title: "What should happen when a Death Link is received?",
      help: "Select one or both nonlethal effects, or choose Die to replace them.",
      isVisible: usesDeathLink,
      control: {
        kind: "checkbox-grid",
        layout: "stack",
        items: [
          {
            field: "receiveFragment",
            label: "Receive a Death Fragment",
            description:
              "Add a Curse card to your run when another linked player dies.",
            applyChange: receiveFragmentTransition,
          },
          {
            field: "receiveDamage",
            label: "Take Max HP damage",
            description:
              "Lose a configurable percentage of your maximum health.",
            applyChange: receiveDamageTransition,
          },
          {
            field: "beKilled",
            label: "Die",
            description:
              "Die immediately when another linked player dies. (Not Recommended)",
            applyChange: beKilledTransition,
          },
        ],
      },
    },
    {
      id: "death-link-damage",
      title:
        "How much maximum-health damage should a received Death Link deal?",
      help: "Choose a whole percentage from 1% through 100%. This damage is not the same as the explicit Be killed mode above.",
      isVisible: usesDeathLinkDamage,
      control: {
        kind: "slider",
        field: "damagePercent",
        // Keep nonlethal damage inside the 1-100 UX range within schema bounds.
        range: generatedRange("deathLinkDamagePercent", {
          minimum: 1,
          maximum: 100,
        }),
        unit: "%",
        ariaLabel: "Received Death Link maximum-health damage percentage",
      },
    },
  ],
};

/** Progression definition consumed by its component and wizard navigation. */
export const progressionSetupStep: WizardStep = {
  id: "progression",
  title: "Progression",
  description:
    "Configure Archipelago's progression-balancing and accessibility settings.",
  sectionKey: "progression",
  questions: [
    {
      id: "progression-balancing",
      title: "How strongly should Archipelago balance progression items?",
      description:
        "Lower values permit more early-game droughts. Higher values move progression earlier; 0 disables balancing, 50 is normal, and 99 is extreme.",
      control: {
        kind: "slider",
        field: "progressionBalancing",
        // Present the documented 0-99 scale even if schema bounds ever widen.
        range: generatedRange("progressionBalancing", {
          minimum: 0,
          maximum: 99,
        }),
        // Named shortcuts are presentation only; any whole value stays valid.
        presets: [
          { label: "Extreme", value: 99 },
          { label: "Normal", value: 50 },
          { label: "Disabled", value: 0 },
        ],
        ariaLabel: "Progression balancing value",
      },
    },
    {
      id: "accessibility",
      title: "Which locations must be reachable?",
      control: {
        kind: "radio",
        field: "accessibility",
        choices: [
          {
            label: "Full",
            description:
              "Require every location and item in the world to remain reachable.",
            value: "full",
          },
          {
            label: "Minimal",
            description:
              "Require only the items and locations needed to complete the goal to remain reachable.",
            value: "minimal",
          },
        ],
      },
    },
  ],
};

/** Declarative step and question ordering for the guided wizard. */
export const wizardSteps: WizardStep[] = [
  characterSetupStep,
  runSetupStep,
  checkSetupStep,
  deathLinkSetupStep,
  progressionSetupStep,
  {
    id: "review",
    title: "Review",
    questions: [],
  },
];

/**
 * Resolves one declarative question definition by its stable identifier.
 *
 * @param step - Declarative step that owns the question.
 * @param questionId - Stable identifier declared in this file.
 * @returns The matching question definition.
 * @throws When the identifier is unknown, so template typos fail loudly in tests.
 */
export function getQuestionById(
  step: WizardStep,
  questionId: string,
): WizardQuestion {
  const question = step.questions.find(
    (candidate) => candidate.id === questionId,
  );

  if (!question) {
    throw new Error(
      `Step '${step.id}' does not declare question '${questionId}'.`,
    );
  }

  return question;
}

/**
 * Checks whether one question applies to the current answers.
 *
 * @param question - Declarative question definition under consideration.
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the question should be rendered.
 * @remarks Questions without an explicit predicate are always visible. This evaluates
 * presentation flow only; it does not validate or compile.
 */
export function isQuestionVisible(
  question: WizardQuestion,
  answers: WizardAnswers,
): boolean {
  return question.isVisible?.(answers) ?? true;
}

/**
 * Checks whether one visible question's controls should accept input.
 *
 * @param question - Declarative question definition under consideration.
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the question's controls are interactive.
 * @remarks Distinct from visibility: a disabled question stays on screen, grayed out,
 * so players can see what a prerequisite (such as Floor Checks) would unlock.
 */
export function isQuestionEnabled(
  question: WizardQuestion,
  answers: WizardAnswers,
): boolean {
  return question.isEnabled?.(answers) ?? true;
}

/**
 * Resolves a question's optional help copy for the current answers.
 *
 * @param question - Declarative question definition under consideration.
 * @param answers - Current player-facing wizard answers.
 * @returns Help text to render, or `null` when none applies right now.
 */
export function resolveQuestionHelp(
  question: WizardQuestion,
  answers: WizardAnswers,
): string | null {
  // Conditional help (such as an unmet-prerequisite hint) is a function of answers.
  if (typeof question.help === "function") {
    return question.help(answers);
  }

  return question.help ?? null;
}

/**
 * Lists one step's questions applicable to the current answers.
 *
 * @param step - Declarative step whose questions should be filtered.
 * @param answers - Current player-facing wizard answers.
 * @returns Visible question definitions in display order.
 */
export function getVisibleQuestions(
  step: WizardStep,
  answers: WizardAnswers,
): WizardQuestion[] {
  return step.questions.filter((question) =>
    isQuestionVisible(question, answers),
  );
}

/**
 * Lists one step's visible question identifiers for flow assertions.
 *
 * @param step - Declarative step whose questions should be filtered.
 * @param answers - Current player-facing wizard answers.
 * @returns Visible question identifiers in display order.
 * @remarks Exposes stable identifiers instead of mutable definition objects.
 */
export function getVisibleQuestionIds(
  step: WizardStep,
  answers: WizardAnswers,
): string[] {
  return getVisibleQuestions(step, answers).map((question) => question.id);
}
