/**
 * @file Reads Character Setup answers back from Archipelago character options.
 *
 * This is the inverse of `compiler/applyCharacterOptions.ts`. Archipelago has two
 * character systems: the standard one (`characters`, `modded_characters`, and shared
 * `ascension`/`ascension_down`) and the advanced dictionary used when
 * `use_advanced_characters` is on. Both become one roster in the builder. Ascension
 * entries are interpreted like the Python world's `_to_ascensions` and
 * `_to_ascension_downs`, so numeric shorthand and names give the same checkbox levels.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import {
  ASCENSION_MODIFIERS,
  isAscensionLevel,
  type AscensionLevel,
} from "../AscensionModifier";
import { copyAscensionConfiguration } from "../CharacterRoster";
import type {
  AscensionConfigurationAnswers,
  CharacterAnswers,
  CharacterStarterOverrides,
  ModdedCharacterAnswers,
} from "../WizardAnswers";
import { CHARACTER_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "../compiler/applyCharacterOptions";
import { describeValue, isMapping } from "./readOptionValue";

/** Adds one player-readable warning. */
type Warn = (message: string) => void;

/** Ascension settings and starter overrides read for one character. */
interface CharacterEntry {
  name: string;
  ascensions: AscensionConfigurationAnswers;
  starterOverrides: CharacterStarterOverrides;
}

/** Advanced entry keys the Python schema accepts. */
const ADVANCED_ENTRY_KEYS = new Set([
  "ascension",
  "ascension_down",
  "progressive_starter_card",
  "progressive_starter_relic",
]);

/** Per-character starter override keys and their answer fields. */
const STARTER_OVERRIDE_FIELDS = [
  ["progressive_starter_card", "progressiveStarterCard"],
  ["progressive_starter_relic", "progressiveStarterRelic"],
] as const;

/**
 * Compares two characters the way the Python world's `character_identity` does.
 *
 * @param name - Built-in name, predefined display name, or modded internal ID.
 * @param catalog - Generated registry of predefined modded characters.
 * @returns A key shared by a predefined character's display name and internal ID.
 */
function characterIdentity(name: string, catalog: OptionCatalog): string {
  const key = name.trim().toLowerCase();
  const predefined = catalog.characters.predefined.find(
    (character) =>
      character.name.toLowerCase() === key ||
      character.internal_id.toLowerCase() === key,
  );
  return predefined ? predefined.internal_id.toLowerCase() : key;
}

/**
 * Reads one Ascension entry as a level.
 *
 * @param entry - Number or canonical name, in any letter case.
 * @returns The level, or `null` when the entry names no Ascension.
 */
function readAscensionEntry(entry: unknown): AscensionLevel | null {
  const text = String(entry).trim();
  if (/^\d+$/.test(text)) {
    const level = Number(text);
    return isAscensionLevel(level) ? level : null;
  }
  const modifier = ASCENSION_MODIFIERS.find(
    (candidate) => candidate.optionName.toLowerCase() === text.toLowerCase(),
  );
  return modifier?.level ?? null;
}

/**
 * Reads the single-number shorthand that both Ascension lists accept.
 *
 * @param entries - Raw Ascension entries.
 * @returns The number when the list is exactly one whole number, otherwise `null`.
 */
function readNumericShorthand(entries: readonly unknown[]): number | null {
  if (entries.length !== 1) {
    return null;
  }
  const text = String(entries[0]).trim();
  return /^\d+$/.test(text) ? Number(text) : null;
}

/**
 * Converts listed Ascension entries to levels, warning about unknown ones.
 *
 * @param entries - Raw entries other than the numeric shorthand.
 * @param context - Option or character named in warnings.
 * @param warn - Receives a warning for each unknown entry.
 * @returns The recognised levels.
 */
function readListedLevels(
  entries: readonly unknown[],
  context: string,
  warn: Warn,
): Set<AscensionLevel> {
  const levels = new Set<AscensionLevel>();
  for (const entry of entries) {
    const level = readAscensionEntry(entry);
    if (level === null) {
      warn(
        `${context}: ignored ${describeValue(entry)}, which is not an Ascension.`,
      );
    } else {
      levels.add(level);
    }
  }
  return levels;
}

/**
 * Lists levels in ascending order.
 *
 * @param levels - Levels in any order.
 * @returns Sorted levels for checkbox state.
 */
function sortLevels(levels: Iterable<AscensionLevel>): AscensionLevel[] {
  return [...levels].sort((left, right) => left - right);
}

/**
 * Reads Ascension and Ascension Down entries into checkbox answers.
 *
 * @param ascension - Raw `ascension` entries.
 * @param ascensionDown - Raw `ascension_down` entries.
 * @param context - Option or character named in warnings.
 * @param warn - Receives warnings for unknown entries.
 * @returns Explicit enabled and Down levels.
 * @remarks A lone number N enables Ascensions 1 to N, while a lone Down number N
 * selects the highest N enabled Ascensions. Downs without their Ascension are dropped,
 * as the Python world drops them.
 */
function readAscensionConfiguration(
  ascension: readonly unknown[],
  ascensionDown: readonly unknown[],
  context: string,
  warn: Warn,
): AscensionConfigurationAnswers {
  let enabled: AscensionLevel[];
  const ascensionCount = readNumericShorthand(ascension);
  if (ascensionCount !== null && ascensionCount <= ASCENSION_MODIFIERS.length) {
    enabled = ASCENSION_MODIFIERS.filter(
      (modifier) => modifier.level <= ascensionCount,
    ).map((modifier) => modifier.level);
  } else {
    enabled = sortLevels(readListedLevels(ascension, context, warn));
  }

  let downs: AscensionLevel[];
  const downCount = readNumericShorthand(ascensionDown);
  if (downCount !== null && downCount <= ASCENSION_MODIFIERS.length) {
    downs = sortLevels(enabled.slice(Math.max(0, enabled.length - downCount)));
  } else {
    const listed = readListedLevels(ascensionDown, `${context} Downs`, warn);
    downs = enabled.filter((level) => listed.has(level));
  }
  if (downCount === 0) {
    downs = [];
  }

  return { enabled, ascensionDownsEnabled: downs.length > 0, downs };
}

/**
 * Checks whether two Ascension setups compile identically.
 *
 * @param left - First setup.
 * @param right - Second setup.
 * @returns Whether both enable the same Ascensions and Downs.
 */
function sameAscensions(
  left: AscensionConfigurationAnswers,
  right: AscensionConfigurationAnswers,
): boolean {
  return (
    left.enabled.join() === right.enabled.join() &&
    left.downs.join() === right.downs.join()
  );
}

/**
 * Reads a list-shaped field of an advanced character entry.
 *
 * @param value - Raw field value; a lone scalar is read as a one-entry list.
 * @param fallback - Python schema default when the field is absent.
 * @returns The entries.
 */
function readEntryList(value: unknown, fallback: unknown[]): unknown[] {
  if (value === undefined) {
    return fallback;
  }
  if (value === null) {
    return [];
  }
  return Array.isArray(value) ? value : [value];
}

/**
 * Reads one advanced character entry.
 *
 * @param name - Character key from the advanced dictionary.
 * @param raw - Its settings mapping.
 * @param warn - Receives warnings for malformed settings.
 * @returns The character's Ascensions and starter overrides.
 */
function readAdvancedEntry(
  name: string,
  raw: unknown,
  warn: Warn,
): CharacterEntry {
  const context = `Advanced Characters (${name})`;
  let settings: Record<string, unknown> = {};
  if (isMapping(raw)) {
    settings = raw;
  } else if (raw !== null && raw !== undefined) {
    warn(
      `${context}: ignored ${describeValue(raw)}; expected Ascension settings.`,
    );
  }

  const unknownKeys = Object.keys(settings).filter(
    (key) => !ADVANCED_ENTRY_KEYS.has(key),
  );
  if (unknownKeys.length) {
    warn(`${context}: ignored unknown settings ${unknownKeys.join(", ")}.`);
  }

  const ascensions = readAscensionConfiguration(
    readEntryList(settings.ascension, [1]),
    readEntryList(settings.ascension_down, []),
    context,
    warn,
  );

  // Omitted starter settings inherit the global options.
  const starterOverrides: CharacterStarterOverrides = {};
  for (const [key, field] of STARTER_OVERRIDE_FIELDS) {
    const value = settings[key];
    if (typeof value === "boolean") {
      starterOverrides[field] = value ? "enabled" : "disabled";
    } else if (value !== undefined) {
      warn(
        `${context}: ignored ${key} ${describeValue(value)}; expected true or false.`,
      );
    }
  }

  return { name, ascensions, starterOverrides };
}

/**
 * Lists the characters of the active character system.
 *
 * @param options - Resolved option values.
 * @param warn - Receives warnings for malformed entries.
 * @returns Every character, with its own Ascensions and starter overrides.
 */
function readCharacterEntries(
  options: CompiledOptions,
  warn: Warn,
): CharacterEntry[] {
  const entries: CharacterEntry[] = [];

  if (options[CHARACTER_OPTION_KEYS.useAdvancedCharacters] === true) {
    const advanced = options[CHARACTER_OPTION_KEYS.advancedCharacters];
    for (const [name, settings] of Object.entries(
      isMapping(advanced) ? advanced : {},
    )) {
      entries.push(readAdvancedEntry(name.trim(), settings, warn));
    }
    return entries;
  }

  // The standard system shares one Ascension setup across every character.
  const shared = readAscensionConfiguration(
    options[CHARACTER_OPTION_KEYS.ascension] as unknown[],
    options[CHARACTER_OPTION_KEYS.ascensionDown] as unknown[],
    "Ascension",
    warn,
  );
  const moddedNames: string[] = [];
  for (const entry of options[
    CHARACTER_OPTION_KEYS.moddedCharacters
  ] as unknown[]) {
    const name =
      typeof entry === "string" || typeof entry === "number"
        ? String(entry).trim()
        : "";
    if (name) {
      moddedNames.push(name);
    } else {
      warn(
        `Modded Characters: ignored ${describeValue(entry)}, which is not a character ID.`,
      );
    }
  }

  for (const name of [
    ...(options[CHARACTER_OPTION_KEYS.characters] as string[]),
    ...moddedNames,
  ]) {
    entries.push({
      name,
      ascensions: copyAscensionConfiguration(shared),
      starterOverrides: {},
    });
  }
  return entries;
}

/**
 * Removes repeated characters, keeping the first, as `unique_character_ids` does.
 *
 * @param entries - Characters in YAML order.
 * @param catalog - Generated registry used to match aliases with full IDs.
 * @param warn - Receives one warning naming the repeated characters.
 * @returns Each character once.
 */
function removeRepeatedCharacters(
  entries: CharacterEntry[],
  catalog: OptionCatalog,
  warn: Warn,
): CharacterEntry[] {
  const seen = new Set<string>();
  const repeated: string[] = [];
  const unique = entries.filter((entry) => {
    const identity = characterIdentity(entry.name, catalog);
    if (seen.has(identity)) {
      repeated.push(entry.name);
      return false;
    }
    seen.add(identity);
    return true;
  });
  if (repeated.length) {
    warn(
      `Characters listed more than once were loaded once: ${repeated.join(", ")}.`,
    );
  }
  return unique;
}

/**
 * Reads Character Setup answers from resolved option values.
 *
 * @param options - Complete option values: generated defaults overlaid with the YAML.
 * @param defaults - Fresh default Character Setup answers.
 * @param catalog - Generated catalog with built-in and predefined characters.
 * @param warn - Receives player-readable warnings.
 * @returns Answers that compile back to equivalent character options.
 */
export function readCharacterAnswers(
  options: CompiledOptions,
  defaults: CharacterAnswers,
  catalog: OptionCatalog,
  warn: Warn,
): CharacterAnswers {
  const builtIns = catalog.options.characters?.valid_keys ?? [];
  const entries = removeRepeatedCharacters(
    readCharacterEntries(options, warn),
    catalog,
    warn,
  );

  // Built-ins become portraits; every other name becomes a modded character row.
  const selectedCharacters: string[] = [];
  const builtInEntries: CharacterEntry[] = [];
  const moddedEntries: CharacterEntry[] = [];
  for (const entry of entries) {
    const builtIn = builtIns.find(
      (name) => name.toLowerCase() === entry.name.toLowerCase(),
    );
    if (builtIn) {
      selectedCharacters.push(builtIn);
      builtInEntries.push({ ...entry, name: builtIn });
    } else {
      moddedEntries.push(entry);
    }
  }

  // Shared Ascensions fit when every character has the same setup. The compiler only
  // writes advanced YAML without starter overrides for individual Ascensions, so that
  // case stays individual and compiles back to the same YAML.
  const hasStarterOverrides = entries.some(
    (entry) => Object.keys(entry.starterOverrides).length > 0,
  );
  const [first] = entries;
  const allSame = entries.every(
    (entry) => first && sameAscensions(entry.ascensions, first.ascensions),
  );
  const usesAdvanced =
    options[CHARACTER_OPTION_KEYS.useAdvancedCharacters] === true;
  const ascensionMode =
    !first || !usesAdvanced || (allSame && hasStarterOverrides)
      ? "shared"
      : "individual";
  const sharedAscensions =
    first && allSame
      ? copyAscensionConfiguration(first.ascensions)
      : copyAscensionConfiguration(defaults.sharedAscensions);

  // Unselected portraits start from the shared setup if the player switches modes.
  const individualAscensions: Record<string, AscensionConfigurationAnswers> =
    {};
  for (const name of Object.keys(defaults.individualAscensions)) {
    individualAscensions[name] = copyAscensionConfiguration(sharedAscensions);
  }
  const individualStarterOverrides: Record<string, CharacterStarterOverrides> =
    {};
  for (const entry of builtInEntries) {
    individualAscensions[entry.name] = entry.ascensions;
    if (Object.keys(entry.starterOverrides).length) {
      individualStarterOverrides[entry.name] = entry.starterOverrides;
    }
  }

  const moddedCharacters: ModdedCharacterAnswers[] = moddedEntries.map(
    (entry) => ({
      name: entry.name,
      ascensions: entry.ascensions,
      ...(Object.keys(entry.starterOverrides).length
        ? { starterOverrides: entry.starterOverrides }
        : {}),
    }),
  );

  const roster = [
    ...selectedCharacters,
    ...moddedEntries.map((entry) => entry.name),
  ];

  // Archipelago uses every character when asked to pick at least as many as exist.
  const pickCount = options[CHARACTER_OPTION_KEYS.pickNumCharacters] as number;
  const picksSubset = pickCount > 0 && pickCount <= roster.length;
  const goal = options[CHARACTER_OPTION_KEYS.numCharactersGoal] as number;

  // A fixed start with an empty unlocked character picks a random one, like locked_random.
  const lockCharacters = options[CHARACTER_OPTION_KEYS.lockCharacters];
  const unlockedCharacter = String(
    options[CHARACTER_OPTION_KEYS.unlockedCharacter] ?? "",
  );
  let availability: CharacterAnswers["availability"] =
    lockCharacters === "unlocked"
      ? "all"
      : lockCharacters === "locked_fixed" && unlockedCharacter.trim()
        ? "fixed"
        : "random";

  let startingCharacter: string | null = roster[0] ?? null;
  if (availability === "fixed") {
    // Built-ins arrive as lowercase choice names; modded starts use any alias.
    const identity = characterIdentity(unlockedCharacter, catalog);
    const match = roster.find(
      (name) => characterIdentity(name, catalog) === identity,
    );
    if (match) {
      startingCharacter = match;
    } else {
      warn(
        `Unlocked Character '${unlockedCharacter}' is not one of your characters, so the builder starts with a random character instead.`,
      );
      availability = "random";
    }
  }

  return {
    ...defaults,
    selectedCharacters,
    moddedCharacters,
    ascensionMode,
    sharedAscensions,
    individualAscensions,
    individualStarterOverrides,
    selectionMode: picksSubset ? "random" : "all",
    randomCharacterCount: picksSubset
      ? pickCount
      : defaults.randomCharacterCount,
    availability,
    startingCharacter,
    goal: goal === 0 ? "all" : goal,
  };
}
