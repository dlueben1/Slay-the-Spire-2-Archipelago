/**
 * @file Validates metadata that surrounds compiled game options in the final YAML.
 *
 * Game-option validation belongs to `validateOptions.ts`, while this module owns the
 * small piece of player input that Archipelago expects outside the game mapping. The
 * YAML service calls this boundary immediately before serialization so copied,
 * previewed, and downloaded documents always enforce the same player-name rules.
 */

/**
 * Validates and normalizes the player name written to generated YAML.
 *
 * @param playerName - Raw text entered above the wizard navigation tabs.
 * @returns The trimmed non-empty name suitable for YAML serialization.
 * @throws When the name is blank or contains a line break.
 * @remarks Spaces inside a name are preserved; only surrounding whitespace is removed.
 */
export function validateWizardPlayerName(playerName: string): string {
  // Remove accidental whitespace around an otherwise meaningful player name.
  const normalizedName = playerName.trim();

  if (!normalizedName) {
    throw new Error("Enter a player name before reviewing the generated YAML.");
  }

  // Keep one logical YAML metadata value on one line even though quoting is safe.
  if (/\r|\n/.test(normalizedName)) {
    throw new Error("The player name must fit on one line.");
  }

  // Return the exact internal spacing the player supplied.
  return normalizedName;
}
