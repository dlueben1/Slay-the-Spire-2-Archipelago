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
}

export interface OptionCatalog {
  schema_version: number;
  game: string;
  option_order: string[];
  options: Record<string, GeneratedOption>;
}

export const optionCatalog = catalogJson as OptionCatalog;
