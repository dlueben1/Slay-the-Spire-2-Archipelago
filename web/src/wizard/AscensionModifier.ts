/**
 * @file Defines the player-facing Slay the Spire II Ascension catalog.
 *
 * Character Setup stores Ascensions as stable numeric levels because that is easier
 * for checkboxes and per-character state than Archipelago's mixed string-or-number
 * syntax. This module is the documented bridge from those levels to the canonical
 * option names accepted by the generated schema. Display names and concise effect
 * summaries follow the Ascension reference at
 * https://spire-codex.com/mechanics/ascension-modifiers; generated option keys remain
 * authoritative for compilation.
 */

import type { OptionCatalog } from "../generated/optionCatalog";

export type AscensionLevel = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10;

export interface AscensionModifier {
  level: AscensionLevel;
  optionName: string;
  name: string;
  effect: string;
}

/** Ordered Ascension rows shared by standard and per-character configuration. */
export const ASCENSION_MODIFIERS: readonly AscensionModifier[] = [
  {
    level: 1,
    optionName: "SwarmingElites",
    name: "Swarming Elites",
    effect: "Adds more elite encounters to the map.",
  },
  {
    level: 2,
    optionName: "WearyTraveler",
    name: "Weary Traveler",
    effect: "Reduces the healing received from Ancients.",
  },
  {
    level: 3,
    optionName: "Poverty",
    name: "Poverty",
    effect: "Reduces gold earned from rewards.",
  },
  {
    level: 4,
    optionName: "TightBelt",
    name: "Tight Belt",
    effect: "Reduces the potion belt from three slots to two.",
  },
  {
    level: 5,
    optionName: "AscendersBane",
    name: "Ascender's Bane",
    effect: "Adds the Ascender's Bane curse to the starting deck.",
  },
  {
    level: 6,
    optionName: "Inflation",
    name: "Inflation",
    effect: "Makes repeated Merchant card removals more expensive.",
  },
  {
    level: 7,
    optionName: "Scarcity",
    name: "Scarcity",
    effect: "Makes rare card rewards less common.",
  },
  {
    level: 8,
    optionName: "ToughEnemies",
    name: "Tough Enemies",
    effect: "Increases enemy health.",
  },
  {
    level: 9,
    optionName: "DeadlyEnemies",
    name: "Deadly Enemies",
    effect: "Increases enemy damage.",
  },
  {
    level: 10,
    optionName: "DoubleBoss",
    name: "Double Boss",
    effect: "Adds a second boss to the end of the final act.",
  },
];

/**
 * Checks whether an unknown value is one of the ten supported Ascension levels.
 *
 * @param value - Candidate numeric value to narrow.
 * @returns Whether the value is an integer from 1 through 10.
 */
export function isAscensionLevel(value: unknown): value is AscensionLevel {
  // The game currently exposes exactly ten numbered Ascension modifiers.
  return Number.isInteger(value) && Number(value) >= 1 && Number(value) <= 10;
}

/**
 * Resolves a supported Ascension level to its display and generated-option metadata.
 *
 * @param level - Stable player-facing Ascension level.
 * @returns The matching ordered Ascension definition.
 * @throws When a manually constructed value falls outside the supported catalog.
 */
export function getAscensionModifier(level: AscensionLevel): AscensionModifier {
  // Use the fixed one-based level to index the ordered immutable definition list.
  const modifier = ASCENSION_MODIFIERS[level - 1];

  if (!modifier) {
    throw new Error(`Unknown Ascension level '${level}'.`);
  }

  // Return shared immutable metadata rather than duplicating names across callers.
  return modifier;
}

/**
 * Converts one generated Ascension entry to its stable numeric level.
 *
 * @param entry - Generated string or numeric set member to interpret.
 * @returns The corresponding Ascension level.
 * @throws When the generated default contains an unsupported name or number.
 */
function parseGeneratedAscensionEntry(entry: unknown): AscensionLevel {
  // Numeric set members may arrive as numbers or numeric strings.
  const numericLevel = typeof entry === "number" ? entry : Number(entry);

  if (isAscensionLevel(numericLevel)) {
    return numericLevel;
  }

  // Named entries must match the exact generated spelling recorded above.
  for (const modifier of ASCENSION_MODIFIERS) {
    if (modifier.optionName === entry) {
      return modifier.level;
    }
  }

  throw new Error(`Unknown generated Ascension value '${String(entry)}'.`);
}

/**
 * Reads a generated Ascension-set default into explicit checkbox levels.
 *
 * @param catalog - Generated option catalog containing the authoritative default.
 * @param optionKey - Generated `ascension` or `ascension_down` option key.
 * @returns Ordered explicit levels suitable for persistent checkbox state.
 * @throws When the option is absent, its default is not a list, or a value is unknown.
 * @remarks A lone numeric value uses Archipelago's shorthand and expands to every
 * level from one through that number. Wizard compilation always writes explicit names
 * afterward, avoiding ambiguity between shorthand and individually selected levels.
 */
export function getDefaultAscensionLevels(
  catalog: OptionCatalog,
  optionKey: string,
): AscensionLevel[] {
  // Resolve the generated option before interpreting its normalized default.
  const option = catalog.options[optionKey];

  if (!option || !Array.isArray(option.default)) {
    throw new Error(`Generated option '${optionKey}' has no list default.`);
  }

  // Expand a lone numeric shorthand into the checkbox levels it represents.
  if (option.default.length === 1) {
    const onlyEntry = option.default[0];
    const isNumericEntry =
      typeof onlyEntry === "number" ||
      (typeof onlyEntry === "string" && /^\d+$/.test(onlyEntry));

    if (isNumericEntry) {
      const maximumLevel = parseGeneratedAscensionEntry(onlyEntry);
      const expandedLevels: AscensionLevel[] = [];

      for (const modifier of ASCENSION_MODIFIERS) {
        if (modifier.level <= maximumLevel) {
          expandedLevels.push(modifier.level);
        }
      }

      return expandedLevels;
    }
  }

  // Convert named or individually numbered entries and remove accidental duplicates.
  const selectedLevels = new Set<AscensionLevel>();

  for (const entry of option.default) {
    selectedLevels.add(parseGeneratedAscensionEntry(entry));
  }

  // Restore canonical display order rather than relying on generated set ordering.
  const orderedLevels: AscensionLevel[] = [];

  for (const modifier of ASCENSION_MODIFIERS) {
    if (selectedLevels.has(modifier.level)) {
      orderedLevels.push(modifier.level);
    }
  }

  return orderedLevels;
}
