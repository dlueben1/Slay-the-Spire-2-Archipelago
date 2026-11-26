/**
 * @file Defines the player-facing Boon catalog for the Boons step.
 *
 * Each row pairs the canonical `boons` name accepted by the generated schema with short
 * display copy, whether it works in multiplayer, and the table group it is listed under.
 * The effect summaries condense the option description in `world/spire2/options.py`;
 * the generated `valid_keys` remain authoritative, and the Boons compiler rejects any
 * name the regenerated schema no longer accepts.
 */

import type { ModifierTableRow } from "./ModifierTableRow";

export type BoonGroup = "travel" | "relics" | "campfires" | "gold" | "deck";

export interface Boon {
  optionName: string;
  name: string;
  effect: string;
  /** Whether multiplayer runs keep this boon; the others only apply to solo runs. */
  worksInMultiplayer: boolean;
  group: BoonGroup;
}

/** Table group headings in display order. */
export const BOON_GROUPS: readonly { id: BoonGroup; title: string }[] = [
  { id: "travel", title: "Map and combat" },
  { id: "relics", title: "Relics from the first game" },
  { id: "campfires", title: "Campfires" },
  { id: "gold", title: "Gold and shops" },
  { id: "deck", title: "Deck" },
];

/** Ordered Boon rows, grouped by `BOON_GROUPS`. */
export const BOONS: readonly Boon[] = [
  {
    optionName: "Wayfinder",
    name: "Wayfinder",
    effect:
      "Three times per run, travel to any room in the next row, ignoring the paths.",
    worksInMultiplayer: false,
    group: "travel",
  },
  {
    optionName: "SecondWind",
    name: "Second Wind",
    effect: "Once per run, survive a killing blow and heal 35% of your max HP.",
    worksInMultiplayer: false,
    group: "travel",
  },
  {
    optionName: "Amphorae",
    name: "Amphorae",
    effect:
      "At the start of each act, fill all empty potion slots with random potions.",
    worksInMultiplayer: true,
    group: "travel",
  },
  {
    optionName: "CursedKettle",
    name: "Cursed Kettle",
    effect: "The first time you draw a Curse each turn, draw 1 card.",
    worksInMultiplayer: true,
    group: "travel",
  },
  {
    optionName: "InkBottle",
    name: "Ink Bottle",
    effect: "Every time you play 10 cards, draw 1 card.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "MedicalKit",
    name: "Medical Kit",
    effect: "Status cards can be played for 0 energy and Exhaust when played.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "BlueCandle",
    name: "Blue Candle",
    effect:
      "Curses can be played for 0 energy; playing one costs 1 HP and Exhausts it.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "PreservedInsect",
    name: "Preserved Insect",
    effect: "Enemies in Elite fights have 25% less HP.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "Torii",
    name: "Torii",
    effect: "Unblocked attack damage of 5 or less is reduced to 1.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "Turnip",
    name: "Turnip",
    effect: "You can't become Frail.",
    worksInMultiplayer: true,
    group: "relics",
  },
  {
    optionName: "EmberEnchanting",
    name: "Ember Enchanting",
    effect:
      "Campfires can enchant a card with a random enchantment instead. This needs the same Progressive Smith as upgrading.",
    worksInMultiplayer: false,
    group: "campfires",
  },
  {
    optionName: "CampfireBrewing",
    name: "Campfire Brewing",
    effect:
      "Once per campfire, brew a random potion. You can still rest or upgrade afterwards.",
    worksInMultiplayer: false,
    group: "campfires",
  },
  {
    optionName: "CompoundInterest",
    name: "Compound Interest",
    effect:
      "At the start of Act 2 and of Act 3, gain 20% of the gold you hold, up to 100.",
    worksInMultiplayer: true,
    group: "gold",
  },
  {
    optionName: "Gilded",
    name: "Gilded",
    effect: "Gain 30% more gold, including gold from Archipelago items.",
    worksInMultiplayer: true,
    group: "gold",
  },
  {
    optionName: "LoyalCustomer",
    name: "Loyal Customer",
    effect:
      "Each purchase makes the rest of that shop 15% cheaper, up to 45%. Archipelago checks in the shop count too.",
    worksInMultiplayer: true,
    group: "gold",
  },
  {
    optionName: "SpringCleaning",
    name: "Spring Cleaning",
    effect:
      "After the Act 1 and Act 2 bosses, get one at random: remove a card, transform a card, or upgrade 2 random cards.",
    worksInMultiplayer: false,
    group: "deck",
  },
  {
    optionName: "EndlessUpgrades",
    name: "Endless Upgrades",
    effect:
      "Cards can be upgraded any number of times. Each upgrade adds the card's upgrade bonus again.",
    worksInMultiplayer: true,
    group: "deck",
  },
];

/** Boon rows for `ModifierTable.vue`, badged with where they work. */
export const BOON_TABLE_ROWS: readonly ModifierTableRow<BoonGroup>[] =
  BOONS.map((boon) => ({
    optionName: boon.optionName,
    name: boon.name,
    effect: boon.effect,
    badge: boon.worksInMultiplayer ? "Multiplayer" : "Solo only",
    group: boon.group,
  }));
