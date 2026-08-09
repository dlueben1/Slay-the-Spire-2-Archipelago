/**
 * @file Defines the semantic filler-item roster used by the guided wizard.
 *
 * The generated option catalog remains authoritative for option defaults, labels,
 * descriptions, choices, and accepted values. This file supplies the hand-authored
 * bridge between stable player-facing filler IDs and generated Archipelago option
 * keys. Both the Filler UI and its section compiler consume this same mapping.
 */

import type { OptionCatalog } from "../generated/optionCatalog";
import type {
  FillerAnswers,
  FillerItemId,
  FillerWeightLevel,
} from "./WizardAnswers";

export const FILLER_WEIGHT_NAMES = ["none", "low", "medium", "high"] as const;

export type FillerWeightName = (typeof FILLER_WEIGHT_NAMES)[number];

export interface FillerItemDefinition {
  id: FillerItemId;
  optionKey: string;
  imageSource: string;
}

export interface FillerDisplayItem extends FillerItemDefinition {
  name: string;
  description: string;
  imageSource: string;
}

/**
 * Stable semantic-to-technical mapping for every guided filler item.
 *
 * @remarks Add a new generated filler option here only after assigning it a
 * player-facing `FillerItemId` in `WizardAnswers.ts`.
 */
export const FILLER_ITEM_DEFINITIONS: readonly FillerItemDefinition[] = [
  {
    id: "oneGold",
    optionKey: "one_gold_filler_weight",
    imageSource: "/icons/thievery_power.webp",
  },
  {
    id: "fiveGold",
    optionKey: "five_gold_filler_weight",
    imageSource: "/icons/royalties_power.webp",
  },
  {
    id: "freeAttack",
    optionKey: "free_attack_filler_weight",
    imageSource: "/icons/free_attack_power.webp",
  },
  {
    id: "freePower",
    optionKey: "free_power_filler_weight",
    imageSource: "/icons/free_power_power.webp",
  },
  {
    id: "freeSkill",
    optionKey: "free_skill_filler_weight",
    imageSource: "/icons/free_skill_power.webp",
  },
  {
    id: "vigor",
    optionKey: "vigor_filler_weight",
    imageSource: "/icons/vigor_power.webp",
  },
  {
    id: "artifact",
    optionKey: "artifact_filler_weight",
    imageSource: "/icons/artifact_power.webp",
  },
  {
    id: "thorns",
    optionKey: "thorns_filler_weight",
    imageSource: "/icons/thorns_power.webp",
  },
  {
    id: "buffer",
    optionKey: "buffer_filler_weight",
    imageSource: "/icons/buffer_power.webp",
  },
  {
    id: "dexterity",
    optionKey: "dexterity_filler_weight",
    imageSource: "/icons/dexterity_power.webp",
  },
  {
    id: "strength",
    optionKey: "strength_filler_weight",
    imageSource: "/icons/strength_power.webp",
  },
  {
    id: "plating",
    optionKey: "plating_filler_weight",
    imageSource: "/icons/plating_power.webp",
  },
  {
    id: "friendship",
    optionKey: "friendship_filler_weight",
    imageSource: "/icons/friendship_power.webp",
  },
  {
    id: "postCombatCardUpgrade",
    optionKey: "post_combat_card_upgrade_filler_weight",
    imageSource: "/icons/improvement_power.webp",
  },
  {
    id: "postCombatCardRemoval",
    optionKey: "post_combat_card_removal_filler_weight",
    imageSource: "/icons/forbidden_grimoire_power.webp",
  },
  {
    id: "additionalCardReward",
    optionKey: "additional_card_reward_filler_weight",
    imageSource: "/icons/the_hunt_power.webp",
  },
];

/**
 * Converts a generated canonical weight name into the wizard's slider level.
 *
 * @param value - Normalized generated option default.
 * @param optionKey - Option key included in errors for schema-drift diagnosis.
 * @returns A slider level from zero through three.
 * @throws When the generated default is not one of the four supported weight names.
 */
function weightNameToLevel(
  value: unknown,
  optionKey: string,
): FillerWeightLevel {
  // Search the ordered names because their positions are the slider's semantic values.
  const level = FILLER_WEIGHT_NAMES.indexOf(value as FillerWeightName);

  // A missing name indicates that the generated schema changed its filler contract.
  if (level < 0) {
    throw new Error(
      `Generated filler option '${optionKey}' has unsupported default '${String(value)}'.`,
    );
  }

  // The guarded array index is necessarily one of 0, 1, 2, or 3.
  return level as FillerWeightLevel;
}

/**
 * Creates schema-derived initial answers for the Filler Setup step.
 *
 * @param catalog - Generated option catalog containing filler defaults.
 * @returns A complete semantic filler answer model keyed by stable filler IDs.
 * @throws When an expected filler option is missing or has an unsupported default.
 */
export function createDefaultFillerAnswers(
  catalog: OptionCatalog,
): FillerAnswers {
  // Build every answer from generated defaults rather than duplicating defaults here.
  const weights = {} as Record<FillerItemId, FillerWeightLevel>;

  for (const definition of FILLER_ITEM_DEFINITIONS) {
    const option = catalog.options[definition.optionKey];

    if (!option) {
      throw new Error(
        `Generated option catalog is missing '${definition.optionKey}'.`,
      );
    }

    weights[definition.id] = weightNameToLevel(
      option.default,
      definition.optionKey,
    );
  }

  // Wrap weights in a section object so future filler answers have a clear home.
  return { weights };
}

/**
 * Builds schema-backed display data for the Filler Setup component.
 *
 * @param catalog - Generated option catalog containing labels and descriptions.
 * @returns Ordered filler rows with semantic IDs and their supplied icon sources.
 * @throws When an expected generated filler option is missing.
 */
export function createFillerDisplayItems(
  catalog: OptionCatalog,
): FillerDisplayItem[] {
  // Preserve mapping order so the UI and generated review output remain predictable.
  const items: FillerDisplayItem[] = [];

  for (const definition of FILLER_ITEM_DEFINITIONS) {
    const option = catalog.options[definition.optionKey];

    if (!option) {
      throw new Error(
        `Generated option catalog is missing '${definition.optionKey}'.`,
      );
    }

    // Remove only the repetitive technical suffix from the generated display name.
    const name = option.display_name.replace(/ Filler Weight$/, "");

    items.push({
      ...definition,
      name,
      description: option.description,
      imageSource: definition.imageSource,
    });
  }

  // Return presentation data without exposing mutable catalog entries to the component.
  return items;
}
