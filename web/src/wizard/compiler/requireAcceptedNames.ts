/**
 * @file Checks names a compiler writes into a generated set option.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";

/**
 * Makes a renamed or removed name fail here rather than in Archipelago's generator.
 *
 * @param catalog - Generated schema holding the option's `valid_keys`.
 * @param optionKey - Generated set option the names are written to.
 * @param names - Canonical names about to be written.
 * @throws When the option no longer accepts one of the names.
 */
export function requireAcceptedNames(
  catalog: OptionCatalog,
  optionKey: string,
  names: readonly string[],
): void {
  const validKeys = catalog.options[optionKey]?.valid_keys ?? [];
  for (const name of names) {
    if (!validKeys.includes(name)) {
      throw new Error(
        `Generated option '${optionKey}' no longer accepts '${name}'.`,
      );
    }
  }
}
