/**
 * @file Defines the TypeScript view of the generated Archipelago option catalog.
 *
 * The JSON file remains the technical source of truth. This module only describes
 * its shape and exposes one typed import for the wizard. Wizard questions must read
 * valid keys, defaults, choices, and ranges here instead of duplicating those facts.
 */

import catalogJson from "./options_compiled.json";

export type OptionKind =
  | "toggle"
  | "choice"
  | "text_choice"
  | "range"
  | "named_range"
  | "set"
  | "counter"
  | "list"
  | "dictionary"
  | "option";

export type OptionValue =
  boolean | number | string | string[] | Record<string, unknown> | unknown[];

export interface OptionChoice {
  name: string;
  display_name: string;
  value: number;
}

export interface GeneratedOption {
  key: string;
  kind: OptionKind;
  display_name: string;
  description: string;
  default: OptionValue;
  choices?: OptionChoice[];
  minimum?: number;
  maximum?: number;
  valid_keys?: string[];
  allow_custom_values?: boolean;
  group?: string;
  /** A game feature the option needs that only the game's beta branch has. */
  required_game_feature?: string;
}

export interface PredefinedCharacter {
  name: string;
  internal_id: string;
}

/** Character registry facts the APWorld uses to limit generated rosters. */
export interface CharacterRegistry {
  /** Maximum selected characters without a predefined name (generic Custom Character slots). */
  generic_character_limit: number;
  /** Modded characters with their own ID blocks; they do not use a generic slot. */
  predefined: PredefinedCharacter[];
  /** Built-in characters that only the game's beta branch has. */
  beta_only: string[];
}

/** One Archipelago item, with the item groups it belongs to besides "Everything". */
export interface CatalogItem {
  name: string;
  groups: string[];
}

/** Item names and groups for the starting inventory, hint and locality questions. */
export interface ItemCatalog {
  /** Items every character has its own copy of, named "<character> <item>". */
  character_items: CatalogItem[];
  /** Items no character owns. */
  universal_items: CatalogItem[];
  /** Item group names, including Archipelago's automatic "Everything". */
  groups: string[];
}

export interface OptionCatalog {
  schema_version: number;
  game: string;
  characters: CharacterRegistry;
  items: ItemCatalog;
  option_order: string[];
  options: Record<string, GeneratedOption>;
}

export const optionCatalog = catalogJson as OptionCatalog;
