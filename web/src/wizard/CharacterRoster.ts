/**
 * @file Provides semantic helpers for Character Setup's combined roster.
 *
 * Built-in portraits and player-entered modded character IDs are stored separately
 * because Archipelago's standard YAML uses two option arrays. The wizard treats them
 * as one roster for random selection, unlocks, and goal counts. These helpers keep that
 * player-facing behavior consistent across Vue components, review copy, and compilers
 * without exposing generated option keys.
 */

import { optionCatalog, type OptionCatalog } from "../generated/optionCatalog";
import type {
  AscensionConfigurationAnswers,
  CharacterAnswers,
} from "./WizardAnswers";

/**
 * Maximum characters without a predefined name in one generated roster.
 *
 * @remarks Built-in and predefined modded characters have their own Archipelago ID
 * blocks; only the remaining characters use the limited generic Custom Character slots.
 */
export const MAX_GENERIC_CHARACTERS =
  optionCatalog.characters.generic_character_limit;

/**
 * Checks whether a character needs one of the limited generic Custom Character slots.
 *
 * @param name - Built-in name, predefined display name, or modded internal ID.
 * @param catalog - Generated schema supplying built-in and predefined characters.
 * @returns Whether the APWorld will number this character as a Custom Character.
 * @remarks Matches the APWorld's case-insensitive alias lookup, so a predefined
 * character's display name and full internal ID are both recognised.
 */
export function usesGenericCharacterSlot(
  name: string,
  catalog: OptionCatalog,
): boolean {
  const key = name.trim().toLowerCase();
  const builtIns = catalog.options.characters?.valid_keys ?? [];

  if (builtIns.some((builtIn) => builtIn.toLowerCase() === key)) {
    return false;
  }

  return !catalog.characters.predefined.some(
    (character) =>
      character.name.toLowerCase() === key ||
      character.internal_id.toLowerCase() === key,
  );
}

/**
 * Copies an Ascension configuration for immutable answer-model updates.
 *
 * @param configuration - Existing Ascension, Ascension Down mode, and selections.
 * @returns A deep-enough copy with the mode preserved and new checkbox arrays.
 */
export function copyAscensionConfiguration(
  configuration: AscensionConfigurationAnswers,
): AscensionConfigurationAnswers {
  // Clone both arrays because Vue controls replace, sort, and filter them independently.
  return {
    enabled: [...configuration.enabled],
    ascensionDownsEnabled: configuration.ascensionDownsEnabled,
    downs: [...configuration.downs],
  };
}

/**
 * Lists complete modded character IDs currently entered by the player.
 *
 * @param answers - Current Character Setup answer model.
 * @returns Trimmed non-empty IDs in their visible table order.
 * @remarks Empty rows remain in persistent form state but are excluded from derived
 * dropdowns until the player supplies the required internal character ID.
 */
export function getNamedModdedCharacters(answers: CharacterAnswers): string[] {
  // Preserve row order while excluding incomplete names from dependent controls.
  const names: string[] = [];

  for (const moddedCharacter of answers.moddedCharacters) {
    const name = moddedCharacter.name.trim();

    if (name) {
      names.push(name);
    }
  }

  return names;
}

/**
 * Builds the unified built-in and modded roster used by shared character questions.
 *
 * @param answers - Current Character Setup answer model.
 * @returns Built-in display names followed by complete modded character IDs.
 */
export function getConfiguredCharacterNames(
  answers: CharacterAnswers,
): string[] {
  // Begin with built-in portraits in generated schema order.
  const names = [...answers.selectedCharacters];

  // Append named modded rows so they participate in selection and unlock behavior.
  names.push(...getNamedModdedCharacters(answers));

  return names;
}

/**
 * Checks whether one built-in portrait may be deselected without removing every slot.
 *
 * @param answers - Current Character Setup answer model.
 * @param character - Built-in character the player is attempting to deselect.
 * @returns Whether another built-in selection or allocated modded-character slot remains.
 * @remarks A blank modded row counts as an allocated slot here because its table is
 * already mounted and can be completed immediately after the built-in is removed.
 * Compilation continues to require every allocated modded row to have a valid name.
 */
export function canDeselectBuiltInCharacter(
  answers: CharacterAnswers,
  character: string,
): boolean {
  // Count selected built-ins other than the portrait being removed.
  let remainingBuiltInCount = 0;

  for (const selectedCharacter of answers.selectedCharacters) {
    if (selectedCharacter !== character) {
      remainingBuiltInCount += 1;
    }
  }

  // Any allocated modded row is a viable replacement even before its name is entered.
  return remainingBuiltInCount > 0 || answers.moddedCharacters.length > 0;
}
