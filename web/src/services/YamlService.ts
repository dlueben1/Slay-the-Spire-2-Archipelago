/**
 * @file Serializes compiled Archipelago options into a readable YAML representation.
 *
 * This service sits after wizard compilation and validation. It knows nothing about
 * questions or player intent and must only receive compiled values. The first vertical
 * slice uses a deliberately small serializer suitable for the generated scalar, list,
 * and simple mapping defaults shown in review output.
 */

import type { OptionValue } from "../generated/optionCatalog";

/**
 * Formats a primitive option value as a YAML-safe scalar.
 *
 * @param value - Boolean, number, or string value to serialize.
 * @returns YAML-safe scalar text; strings use JSON quoting, which YAML accepts.
 */
function scalar(value: boolean | number | string): string {
  // Quote strings to preserve empty strings and characters significant to YAML.
  return typeof value === "string" ? JSON.stringify(value) : String(value);
}

/**
 * Serializes compiled options as deterministic, human-readable YAML.
 *
 * @param options - Ordered compiled option entries to serialize.
 * @returns YAML text ending with one newline.
 * @remarks Call this after compilation. This lightweight serializer supports the value
 * shapes in the generated catalog but is not intended as a general-purpose YAML library.
 */
export function optionsToYaml(options: Record<string, OptionValue>): string {
  // Build line-by-line so arrays and dictionaries receive the correct indentation.
  const lines: string[] = [];

  for (const [key, value] of Object.entries(options)) {
    // Render list-like options in block sequence form.
    if (Array.isArray(value)) {
      lines.push(`${key}:`);

      for (const entry of value) {
        lines.push(
          `  - ${typeof entry === "object" ? JSON.stringify(entry) : scalar(entry as boolean | number | string)}`,
        );
      }
    } else if (value !== null && typeof value === "object") {
      // Render dictionary-like options as an empty mapping or indented entries.
      const entries = Object.entries(value);

      if (!entries.length) {
        lines.push(`${key}: {}`);
      } else {
        lines.push(`${key}:`);

        for (const [childKey, childValue] of entries) {
          lines.push(
            `  ${JSON.stringify(childKey)}: ${JSON.stringify(childValue)}`,
          );
        }
      }
    } else {
      // Primitive options fit on the same line as their key.
      lines.push(`${key}: ${scalar(value as boolean | number | string)}`);
    }
  }

  // POSIX-style trailing newlines make copied and downloaded YAML cleaner.
  return `${lines.join("\n")}\n`;
}
