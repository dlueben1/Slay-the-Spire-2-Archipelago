/**
 * @file Narrows loose Nuxt UI control events to supported answer values.
 *
 * Nuxt UI controls emit permissive types: checkboxes may report an indeterminate
 * string, numeric inputs may emit `null` while temporarily empty, and sliders share a
 * generic scalar-or-array API. Step components previously repeated these guards in
 * every setter; the generic control renderer now narrows once through this module.
 * Each adapter returns `null` when the event should be ignored so callers can retain
 * the last complete answer.
 */

import type { GeneratedNumberRange } from "./WizardOptionKey";

/**
 * Narrows a checkbox event to a concrete boolean.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns The boolean, or `null` for the unsupported indeterminate state.
 */
export function narrowBooleanValue(value: unknown): boolean | null {
  // Binary answers never store Nuxt UI's optional indeterminate state.
  return typeof value === "boolean" ? value : null;
}

/**
 * Narrows a radio or select event to one declared choice value.
 *
 * @param value - Value emitted by Nuxt UI's generic choice controls.
 * @param allowed - Choice values declared by the question's control descriptor.
 * @returns The matching declared value, or `null` for anything undeclared.
 */
export function narrowChoiceValue(
  value: unknown,
  allowed: readonly string[],
): string | null {
  // Only values declared by the descriptor may reach the persistent answer model.
  if (typeof value !== "string" || !allowed.includes(value)) {
    return null;
  }

  return value;
}

/**
 * Narrows a numeric input or slider event to a whole number inside its range.
 *
 * @param value - Scalar, array, or empty value emitted by Nuxt UI's numeric controls.
 * @returns The integral value, or `null` for empty, array, or out-of-range states.
 */
export function narrowIntegerValue(
  value: unknown,
  range: GeneratedNumberRange,
): number | null {
  // Single-thumb controls cannot accept arrays or temporarily empty states.
  if (
    typeof value !== "number" ||
    !Number.isInteger(value) ||
    value < range.minimum ||
    value > range.maximum
  ) {
    return null;
  }

  return value;
}
