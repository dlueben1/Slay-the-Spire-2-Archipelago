/**
 * @file Translates Items & Hints answers into Archipelago's common item options.
 *
 * Starting items become the `start_inventory` counter, with stacks of the same item
 * added together. Hint and placement lists pass through as item group and item names.
 * The compiler refuses what generation would reject or fail on: items the roster has no
 * character for, unknown names, and items both kept in and kept out of the player's world.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import {
  findItemGroup,
  findSharedItem,
  getItemCharacters,
  getStartingItemChoices,
  itemName,
  parseItemName,
} from "../ItemNames";
import type { CharacterAnswers, ItemAnswers } from "../WizardAnswers";
import { ITEM_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/** The most copies of one item the builder lets a player start with. */
export const STARTING_ITEM_MAX_COUNT = 99;

/**
 * Builds the starting inventory counter.
 *
 * @param answers - Starting item stacks.
 * @param characters - Characters whose items the roster can start with.
 * @param catalog - Generated item catalog.
 * @returns Counts keyed by full item name.
 * @throws When a stack names a character outside the roster, an item that character
 * can't start with, or a count out of range.
 */
function compileStartingInventory(
  answers: ItemAnswers,
  characters: readonly string[],
  catalog: OptionCatalog,
): Record<string, number> {
  const inventory: Record<string, number> = {};

  for (const stack of answers.startingInventory) {
    if (stack.character !== null && !characters.includes(stack.character)) {
      throw new Error(
        `Starting Inventory gives ${stack.character} items, but ${stack.character} isn't in your roster. Remove that row or add the character.`,
      );
    }
    const choices = getStartingItemChoices(stack.character, catalog);
    if (!choices.some((choice) => choice.name === stack.item)) {
      throw new Error(
        `Starting Inventory can't include '${itemName(stack.character, stack.item)}'.`,
      );
    }
    if (
      !Number.isInteger(stack.count) ||
      stack.count < 1 ||
      stack.count > STARTING_ITEM_MAX_COUNT
    ) {
      throw new Error(
        `Starting Inventory needs a whole number from 1 to ${STARTING_ITEM_MAX_COUNT} for '${itemName(stack.character, stack.item)}'.`,
      );
    }

    const name = itemName(stack.character, stack.item);
    inventory[name] = (inventory[name] ?? 0) + stack.count;
  }

  return inventory;
}

/**
 * Checks that every entry of a hint or placement list is a group or item name.
 *
 * @param entries - Names from the answers.
 * @param label - The question's name, for the error message.
 * @param catalog - Generated item catalog.
 * @throws When an entry names no item group and no item.
 */
function checkItemNames(
  entries: readonly string[],
  label: string,
  catalog: OptionCatalog,
): void {
  for (const entry of entries) {
    if (
      findItemGroup(entry, catalog) === null &&
      parseItemName(entry, catalog) === null
    ) {
      throw new Error(
        `${label} lists '${entry}', which isn't an item or item group.`,
      );
    }
  }
}

/**
 * Applies the Items & Hints step to compiled options.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Starting items, starting hints, and placement lists.
 * @param characterAnswers - Character Setup answers, which decide whose items exist.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `ITEM_OPTION_KEYS`.
 * @throws When the catalog lacks an owned option or the answers can't generate.
 */
export function applyItemOptions(
  target: CompiledOptions,
  answers: ItemAnswers,
  characterAnswers: CharacterAnswers,
  catalog: OptionCatalog,
): void {
  for (const optionKey of Object.values(ITEM_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  const characters = getItemCharacters(characterAnswers, catalog);

  checkItemNames(answers.startHints, "Starting Hints", catalog);
  checkItemNames(answers.localItems, "Local Items", catalog);
  checkItemNames(answers.nonLocalItems, "Non-local Items", catalog);

  const shared = findSharedItem(
    answers.localItems,
    answers.nonLocalItems,
    characters,
    catalog,
  );
  if (shared !== null) {
    throw new Error(
      `'${shared}' is in both Local Items and Non-local Items, so it could go nowhere. Remove it from one of them.`,
    );
  }

  target[ITEM_OPTION_KEYS.startInventory] = compileStartingInventory(
    answers,
    characters,
    catalog,
  );
  target[ITEM_OPTION_KEYS.startHints] = [...answers.startHints];
  target[ITEM_OPTION_KEYS.localItems] = [...answers.localItems];
  target[ITEM_OPTION_KEYS.nonLocalItems] = [...answers.nonLocalItems];
}
