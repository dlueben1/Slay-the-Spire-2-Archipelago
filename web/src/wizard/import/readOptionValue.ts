/**
 * @file Reads one Archipelago YAML option value the way Archipelago's generator would.
 *
 * Archipelago accepts far more spellings than the builder writes: weighted mappings,
 * `random`, `on`/`off`, choice numbers, range keywords such as `high`, and comma-separated
 * sets. This module resolves a raw parsed value to the canonical shape the section
 * compilers produce (choice names from the catalog, whole numbers, string lists), or
 * reports why it cannot: a random or weighted value the builder cannot show, or a value
 * Archipelago itself would reject. It mirrors `Generate.get_choice` and the `from_any`
 * methods of the Option classes in Archipelago's `Options.py`.
 */

import type {
  GeneratedOption,
  OptionChoice,
  OptionValue,
} from "../../generated/optionCatalog";

/** Generated metadata the TypeScript catalog type leaves out but import needs. */
interface ImportableOption extends GeneratedOption {
  /** False for collection options, which Archipelago never treats as weighted. */
  supports_weighting?: boolean;
  /** Named Range keywords such as `normal: 50`; keys are lowercase. */
  special_values?: Record<string, number>;
  /** Alternative choice names, such as Accessibility's `items`. */
  aliases?: OptionChoice[];
}

/** Outcome of reading one option value. */
export type OptionReadResult =
  /** A canonical value; `ignoredEntries` lists set members that were dropped. */
  | { status: "value"; value: OptionValue; ignoredEntries?: string[] }
  /** A random keyword or a weighting between several values. */
  | { status: "random" }
  /** A value Archipelago would reject; `detail` quotes it for the player. */
  | { status: "invalid"; detail: string };

/** Strings `Toggle.from_text` reads as off and on. */
const TOGGLE_OFF = new Set([
  "off",
  "0",
  "false",
  "none",
  "null",
  "no",
  "disabled",
]);
const TOGGLE_ON = new Set(["on", "1", "true", "yes", "enabled"]);

/** Whole numbers as Python's `int()` reads them from text. */
const INTEGER_TEXT = /^\s*[+-]?\d+\s*$/;

/**
 * Checks whether a parsed YAML value is a mapping.
 *
 * @param value - Any parsed YAML value.
 * @returns Whether the value is a plain object rather than a list or scalar.
 */
export function isMapping(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Quotes a raw value for a warning message.
 *
 * @param value - Parsed YAML value as the player wrote it.
 * @returns Short readable text, such as `'maybe'` or `{"a":1}`.
 */
export function describeValue(value: unknown): string {
  return typeof value === "string" ? `'${value}'` : JSON.stringify(value);
}

/**
 * Converts a weight the way Python's `int()` does in `Generate.get_choice`.
 *
 * @param weight - Raw weight from a weighted mapping.
 * @returns The integer weight, or `null` when Python would raise.
 */
function readWeight(weight: unknown): number | null {
  if (typeof weight === "boolean") {
    return weight ? 1 : 0;
  }
  if (typeof weight === "number" && Number.isFinite(weight)) {
    return Math.trunc(weight);
  }
  if (typeof weight === "string" && INTEGER_TEXT.test(weight)) {
    return Number.parseInt(weight, 10);
  }
  return null;
}

/**
 * Resolves Archipelago weighting to the single value it would always pick.
 *
 * @param raw - Raw option value, possibly a weighted mapping or a list to pick from.
 * @returns The only possible value, or why there is none.
 * @remarks A mapping with exactly one non-zero weight, or a list whose entries are all
 * the same, always produces that value, so the builder can show it.
 */
export function pickCertainValue(
  raw: unknown,
):
  | { status: "value"; value: unknown }
  | Exclude<OptionReadResult, { status: "value" }> {
  if (Array.isArray(raw)) {
    if (!raw.length) {
      return { status: "invalid", detail: "an empty list" };
    }
    const distinct = new Set(raw.map((entry) => JSON.stringify(entry)));
    return distinct.size === 1
      ? { status: "value", value: raw[0] }
      : { status: "random" };
  }

  if (!isMapping(raw)) {
    return { status: "value", value: raw };
  }

  const weighted: string[] = [];
  for (const [value, weight] of Object.entries(raw)) {
    const parsedWeight = readWeight(weight);
    if (parsedWeight === null || parsedWeight < 0) {
      return { status: "invalid", detail: describeValue(raw) };
    }
    if (parsedWeight > 0) {
      weighted.push(value);
    }
  }

  // Archipelago rejects an empty mapping and one whose weights are all zero.
  if (!weighted.length) {
    return { status: "invalid", detail: describeValue(raw) };
  }

  return weighted.length === 1
    ? { status: "value", value: weighted[0] }
    : { status: "random" };
}

/**
 * Reads a Toggle as `Toggle.from_any` does.
 *
 * @param value - Unweighted raw value.
 * @returns The boolean, or why it cannot be read.
 */
function readToggle(value: unknown): OptionReadResult {
  if (typeof value === "boolean") {
    return { status: "value", value };
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return { status: "value", value: Math.trunc(value) !== 0 };
  }
  if (typeof value === "string") {
    const text = value.toLowerCase();
    if (text === "random") {
      return { status: "random" };
    }
    if (TOGGLE_OFF.has(text)) {
      return { status: "value", value: false };
    }
    if (TOGGLE_ON.has(text)) {
      return { status: "value", value: true };
    }
  }
  return { status: "invalid", detail: describeValue(value) };
}

/**
 * Finds the canonical choice for a name or alias, ignoring case like `Choice.from_text`.
 *
 * @param option - Generated choice option.
 * @param text - Name the player wrote.
 * @returns The canonical choice, or `undefined` when nothing matches.
 */
function findChoiceByName(
  option: ImportableOption,
  text: string,
): OptionChoice | undefined {
  const key = text.toLowerCase();
  const choices = option.choices ?? [];
  const direct = choices.find((choice) => choice.name.toLowerCase() === key);
  if (direct) {
    return direct;
  }

  // An alias names a value; report the choice that owns that value.
  const alias = option.aliases?.find(
    (entry) => entry.name.toLowerCase() === key,
  );
  return alias && choices.find((choice) => choice.value === alias.value);
}

/**
 * Reads a Choice or TextChoice as `Choice.from_any` and `TextChoice.from_text` do.
 *
 * @param option - Generated choice metadata.
 * @param value - Unweighted raw value.
 * @returns The canonical choice name, custom text for a TextChoice, or a failure.
 */
function readChoice(
  option: ImportableOption,
  value: unknown,
): OptionReadResult {
  const allowsText = option.kind === "text_choice";

  // Integers select by value; Python's `type(data) == int` excludes booleans.
  if (typeof value === "number" && Number.isInteger(value)) {
    const choice = option.choices?.find((entry) => entry.value === value);
    if (choice) {
      return { status: "value", value: choice.name };
    }
  }

  if (value === null && !allowsText) {
    // Python reads YAML null as the text "None", which some aliases accept.
    value = "None";
  }

  if (
    typeof value === "string" ||
    typeof value === "number" ||
    typeof value === "boolean"
  ) {
    // Python's `str()` writes booleans capitalised; matching ignores case anyway.
    const text = String(value);
    if (text.toLowerCase() === "random") {
      return { status: "random" };
    }
    const choice = findChoiceByName(option, text);
    if (choice) {
      return { status: "value", value: choice.name };
    }
    if (allowsText) {
      return { status: "value", value: text };
    }

    // Weighted mapping keys reach us as text even when the player wrote a number.
    if (INTEGER_TEXT.test(text)) {
      const number = Number.parseInt(text, 10);
      const byValue = option.choices?.find((entry) => entry.value === number);
      if (byValue) {
        return { status: "value", value: byValue.name };
      }
    }
  }

  return { status: "invalid", detail: describeValue(value) };
}

/**
 * Checks a number against a Range's bounds and Named Range keywords.
 *
 * @param option - Generated range metadata.
 * @param number - Whole number to check.
 * @returns The number, or an invalid result when it is out of range.
 */
function checkRangeBounds(
  option: ImportableOption,
  number: number,
): OptionReadResult {
  const isSpecial = Object.values(option.special_values ?? {}).includes(number);
  const inRange =
    (option.minimum === undefined || number >= option.minimum) &&
    (option.maximum === undefined || number <= option.maximum);
  return inRange || isSpecial
    ? { status: "value", value: number }
    : { status: "invalid", detail: describeValue(number) };
}

/**
 * Reads a Range or Named Range as `Range.from_any` and `NamedRange.from_text` do.
 *
 * @param option - Generated range metadata.
 * @param value - Unweighted raw value.
 * @returns The whole number, or why it cannot be read.
 */
function readRange(option: ImportableOption, value: unknown): OptionReadResult {
  if (typeof value === "number") {
    return Number.isInteger(value)
      ? checkRangeBounds(option, value)
      : { status: "invalid", detail: describeValue(value) };
  }

  if (typeof value !== "string" && typeof value !== "boolean") {
    return { status: "invalid", detail: describeValue(value) };
  }

  const text = String(value).toLowerCase();
  const special = option.special_values?.[text];
  if (special !== undefined) {
    return { status: "value", value: special };
  }
  if (text.startsWith("random")) {
    return { status: "random" };
  }

  const defaultValue = option.default as number;
  if (text === "default") {
    return { status: "value", value: defaultValue };
  }
  if (text === "high" && option.maximum !== undefined) {
    return { status: "value", value: option.maximum };
  }
  if (text === "low" && option.minimum !== undefined) {
    return { status: "value", value: option.minimum };
  }

  // Ranges starting at zero with a non-zero default read true/false as default/off.
  if (
    option.minimum === 0 &&
    defaultValue !== 0 &&
    (text === "true" || text === "false")
  ) {
    return { status: "value", value: text === "true" ? defaultValue : 0 };
  }

  return INTEGER_TEXT.test(text)
    ? checkRangeBounds(option, Number.parseInt(text, 10))
    : { status: "invalid", detail: describeValue(value) };
}

/**
 * Reads an OptionSet as `OptionSet.from_any` does, matching valid keys without case.
 *
 * @param option - Generated set metadata.
 * @param value - Raw value; sets are never weighted.
 * @returns Canonical entries in the order written, plus any unknown entries dropped.
 */
function readSet(option: ImportableOption, value: unknown): OptionReadResult {
  let entries: unknown[];

  if (Array.isArray(value)) {
    entries = value;
  } else if (isMapping(value)) {
    // Python iterates a mapping's keys when it builds a set from one.
    entries = Object.keys(value);
  } else if (typeof value === "string") {
    if (option.valid_keys?.length && value.toLowerCase().startsWith("random")) {
      return { status: "random" };
    }
    entries = value.split(",");
  } else {
    return { status: "invalid", detail: describeValue(value) };
  }

  const validKeys = option.valid_keys ?? [];
  const canonical: string[] = [];
  const ignoredEntries: string[] = [];

  for (const entry of entries) {
    if (typeof entry !== "string" && typeof entry !== "number") {
      ignoredEntries.push(describeValue(entry));
      continue;
    }
    const text = String(entry).trim();
    if (!text) {
      continue;
    }

    const validKey = validKeys.find(
      (key) => key.toLowerCase() === text.toLowerCase(),
    );
    if (
      validKey === undefined &&
      validKeys.length &&
      !option.allow_custom_values
    ) {
      ignoredEntries.push(text);
      continue;
    }

    const key = validKey ?? text;
    if (!canonical.includes(key)) {
      canonical.push(key);
    }
  }

  return {
    status: "value",
    value: canonical,
    ...(ignoredEntries.length ? { ignoredEntries } : {}),
  };
}

/**
 * Reads an OptionList as `OptionList.from_any` does.
 *
 * @param value - Raw value; lists are never weighted.
 * @returns The list entries, or an invalid result.
 */
function readList(value: unknown): OptionReadResult {
  if (Array.isArray(value)) {
    return { status: "value", value };
  }
  if (isMapping(value)) {
    return { status: "value", value: Object.keys(value) };
  }
  if (typeof value === "string" || typeof value === "number") {
    return {
      status: "value",
      value: String(value)
        .split(",")
        .map((entry) => entry.trim()),
    };
  }
  return { status: "invalid", detail: describeValue(value) };
}

/**
 * Reads one game option value as Archipelago's generator would.
 *
 * @param option - Generated metadata for the option.
 * @param raw - Value parsed from the YAML game section.
 * @returns The canonical value, or why the builder cannot use it.
 */
export function readOptionValue(
  option: GeneratedOption,
  raw: unknown,
): OptionReadResult {
  const importable = option as ImportableOption;

  // Collection options take their value literally; everything else may be weighted.
  let value = raw;
  if (importable.supports_weighting !== false) {
    const picked = pickCertainValue(raw);
    if (picked.status !== "value") {
      return picked;
    }
    value = picked.value;
  }

  switch (importable.kind) {
    case "toggle":
      return readToggle(value);
    case "choice":
    case "text_choice":
      return readChoice(importable, value);
    case "range":
    case "named_range":
      return readRange(importable, value);
    case "set":
      return readSet(importable, value);
    case "list":
      return readList(value);
    case "dictionary":
    case "counter":
      return isMapping(value)
        ? { status: "value", value }
        : { status: "invalid", detail: describeValue(value) };
    default:
      return { status: "value", value: value as OptionValue };
  }
}
