/**
 * @file Builds Archipelago item names for the Items & Hints questions.
 *
 * The catalog lists each character item once ("Relic"), because every character has its
 * own copy named "<character> <item>" ("Ironclad Relic"). These helpers add the names of
 * the characters in a roster, list what a player may start with, and recognise item and
 * group names typed into a YAML by hand.
 */

import type { CatalogItem, OptionCatalog } from "../generated/optionCatalog";
import type { CharacterAnswers } from "./WizardAnswers";

/**
 * Groups whose items the starting inventory leaves out. Traps only hurt, and each Bonus
 * Item takes its contents from a Bonus Items entry, which a starting copy would not have.
 */
const NOT_STARTABLE_GROUPS: ReadonlySet<string> = new Set([
  "Traps",
  "Bonus Items",
]);

/** An item name split into the character that owns it and the item itself. */
export interface ParsedItemName {
  /** The owning character, or null for an item no character owns. */
  character: string | null;
  item: string;
}

/**
 * Builds the Archipelago name of an item.
 *
 * @param character - The owning character, or null for an item no character owns.
 * @param item - Item name as the catalog lists it.
 * @returns The full item name, such as "Ironclad Relic".
 */
export function itemName(character: string | null, item: string): string {
  return character === null ? item : `${character} ${item}`;
}

/**
 * Lists the roster's characters whose item names are known before generation.
 *
 * @param answers - Character Setup answers.
 * @param catalog - Generated catalog naming built-in and predefined modded characters.
 * @returns Built-in characters, then predefined modded characters by their display name.
 * @remarks Other modded characters are named "Custom Character N" by their position in
 * the roster the APWorld generates, which the builder can't know in advance.
 */
export function getItemCharacters(
  answers: CharacterAnswers,
  catalog: OptionCatalog,
): string[] {
  const characters = [...answers.selectedCharacters];

  for (const modded of answers.moddedCharacters) {
    const key = modded.name.trim().toLowerCase();
    const predefined = catalog.characters.predefined.find(
      (character) =>
        character.name.toLowerCase() === key ||
        character.internal_id.toLowerCase() === key,
    );
    if (predefined && !characters.includes(predefined.name)) {
      characters.push(predefined.name);
    }
  }

  return characters;
}

/**
 * Lists the items a player may start with.
 *
 * @param character - The owning character, or null for items no character owns.
 * @param catalog - Generated item catalog.
 * @returns Items in catalog order, without traps and Bonus Items.
 */
export function getStartingItemChoices(
  character: string | null,
  catalog: OptionCatalog,
): CatalogItem[] {
  const items =
    character === null
      ? catalog.items.universal_items
      : catalog.items.character_items;
  return items.filter(
    (item) => !item.groups.some((group) => NOT_STARTABLE_GROUPS.has(group)),
  );
}

/**
 * Lists every character name an item name may start with, including the generic slots.
 *
 * @param catalog - Generated catalog naming built-in and predefined modded characters.
 * @returns Built-in, predefined and "Custom Character N" names.
 */
function getAllCharacterNames(catalog: OptionCatalog): string[] {
  const generic = Array.from(
    { length: catalog.characters.generic_character_limit },
    (_, index) => `Custom Character ${index + 1}`,
  );
  return [
    ...(catalog.options.characters?.valid_keys ?? []),
    ...catalog.characters.predefined.map((character) => character.name),
    ...generic,
  ];
}

/**
 * Recognises an item name whatever its capitalisation, and returns the exact spelling
 * Archipelago requires, so a hand-written YAML still loads.
 *
 * @param text - A name from a YAML.
 * @param catalog - Generated item catalog.
 * @returns The owning character and item, or null when no item has this name.
 */
export function parseItemName(
  text: string,
  catalog: OptionCatalog,
): ParsedItemName | null {
  const key = text.trim().toLowerCase();

  const universal = catalog.items.universal_items.find(
    (item) => item.name.toLowerCase() === key,
  );
  if (universal) {
    return { character: null, item: universal.name };
  }

  for (const character of getAllCharacterNames(catalog)) {
    const prefix = `${character.toLowerCase()} `;
    if (!key.startsWith(prefix)) {
      continue;
    }
    const rest = key.slice(prefix.length);
    const item = catalog.items.character_items.find(
      (candidate) => candidate.name.toLowerCase() === rest,
    );
    if (item) {
      return { character, item: item.name };
    }
  }

  return null;
}

/**
 * Recognises an item group name, ignoring case.
 *
 * @param text - A name from a YAML.
 * @param catalog - Generated item catalog.
 * @returns The group's canonical name, or null when no group has this name.
 */
export function findItemGroup(
  text: string,
  catalog: OptionCatalog,
): string | null {
  const key = text.trim().toLowerCase();
  return (
    catalog.items.groups.find((group) => group.toLowerCase() === key) ?? null
  );
}

/**
 * Checks whether an entry of a hint or placement list covers an item.
 *
 * @param entry - A group name or a full item name.
 * @param character - The item's owning character, or null.
 * @param item - The item.
 * @returns Whether Archipelago would apply the entry to the item.
 */
function entryCovers(
  entry: string,
  character: string | null,
  item: CatalogItem,
): boolean {
  return (
    entry === "Everything" ||
    item.groups.includes(entry) ||
    entry === itemName(character, item.name)
  );
}

/**
 * Finds an item two hint or placement lists both cover, through a group or by name.
 *
 * @param first - Group and item names.
 * @param second - Group and item names.
 * @param characters - Characters whose items to check, besides those the lists name.
 * @param catalog - Generated item catalog.
 * @returns The full name of one item both lists cover, or null when they share none.
 * @remarks Archipelago applies Local Items and Non-local Items together, so an item in
 * both may go nowhere and generation fails.
 */
export function findSharedItem(
  first: readonly string[],
  second: readonly string[],
  characters: readonly string[],
  catalog: OptionCatalog,
): string | null {
  const owners = new Set<string>(characters);
  for (const entry of [...first, ...second]) {
    const parsed = parseItemName(entry, catalog);
    if (parsed?.character) {
      owners.add(parsed.character);
    }
  }

  const candidates: [string | null, CatalogItem][] = [
    ...catalog.items.universal_items.map((item): [null, CatalogItem] => [
      null,
      item,
    ]),
    ...[...owners].flatMap((character) =>
      catalog.items.character_items.map((item): [string, CatalogItem] => [
        character,
        item,
      ]),
    ),
  ];

  for (const [character, item] of candidates) {
    if (
      first.some((entry) => entryCovers(entry, character, item)) &&
      second.some((entry) => entryCovers(entry, character, item))
    ) {
      return itemName(character, item.name);
    }
  }
  return null;
}
