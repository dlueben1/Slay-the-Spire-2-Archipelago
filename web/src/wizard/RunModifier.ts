/**
 * @file Defines the player-facing Run Modifier catalog for the Challenges step.
 *
 * Each row pairs the canonical `run_modifiers` name accepted by the generated schema
 * with short display copy, the act in which the modifier first changes a run, and the
 * table group it is listed under. The effect summaries condense the option description
 * in `world/spire2/options.py`; the generated `valid_keys` remain authoritative, and the
 * Challenges compiler rejects any name the regenerated schema no longer accepts.
 */

export type RunModifierGroup = "combat" | "deck" | "resources";

export interface RunModifier {
  optionName: string;
  name: string;
  effect: string;
  /** First act whose runs this modifier changes. */
  startsInAct: 1 | 2;
  group: RunModifierGroup;
}

/** Table group headings in display order. */
export const RUN_MODIFIER_GROUPS: readonly {
  id: RunModifierGroup;
  title: string;
}[] = [
  { id: "combat", title: "Combat" },
  { id: "deck", title: "Deck" },
  { id: "resources", title: "Resources and map" },
];

/** Ordered Run Modifier rows; within each group, Act 2 modifiers come first. */
export const RUN_MODIFIERS: readonly RunModifier[] = [
  {
    optionName: "RisingStrength",
    name: "Rising Strength",
    effect:
      "Elites and bosses start each combat with 1 Strength in Act 2 and 2 in Act 3.",
    startsInAct: 2,
    group: "combat",
  },
  {
    optionName: "EscalatingBuffs",
    name: "Escalating Buffs",
    effect:
      "Enemies start combat with one random buff in Act 2 and two different ones in Act 3.",
    startsInAct: 2,
    group: "combat",
  },
  {
    optionName: "SlowStart",
    name: "Slow Start",
    effect: "Draw 1 fewer card on the first turn of each combat.",
    startsInAct: 1,
    group: "combat",
  },
  {
    optionName: "MountingCurses",
    name: "Mounting Curses",
    effect: "Gain a random curse at the start of Act 2 and of Act 3.",
    startsInAct: 2,
    group: "deck",
  },
  {
    optionName: "MountingBanes",
    name: "Mounting Banes",
    effect:
      "Gain an Ascender's Bane, which cannot be removed, at the start of Act 2 and of Act 3.",
    startsInAct: 2,
    group: "deck",
  },
  {
    optionName: "CursedRun",
    name: "Cursed Run",
    effect:
      "Gain a random curse at the start of every act, including Act 1. Stacks with Mounting Curses.",
    startsInAct: 1,
    group: "deck",
  },
  {
    optionName: "ExtraAscendersBane",
    name: "Extra Ascender's Bane",
    effect:
      "Start each run with an extra Ascender's Bane. Stacks with Mounting Banes.",
    startsInAct: 1,
    group: "deck",
  },
  {
    optionName: "ShrinkingRests",
    name: "Shrinking Rests",
    effect:
      "Resting heals 25% of max HP in Act 2 and 20% in Act 3, instead of 30%.",
    startsInAct: 2,
    group: "resources",
  },
  {
    optionName: "FrailStart",
    name: "Frail Start",
    effect: "Start each run with 10% less max HP.",
    startsInAct: 1,
    group: "resources",
  },
  {
    optionName: "DangerousUnknowns",
    name: "Dangerous Unknowns",
    effect:
      "? rooms lean towards fights and elites, more so each time one is not a fight. Resets each act.",
    startsInAct: 1,
    group: "resources",
  },
];

/**
 * Orders selected Run Modifiers for stable state, review, and YAML compilation.
 *
 * @param selected - Canonical modifier names in any order.
 * @returns The known names in catalog order, without duplicates.
 */
export function getOrderedRunModifiers(selected: Iterable<string>): string[] {
  const selectedNames = new Set(selected);

  return RUN_MODIFIERS.filter((modifier) =>
    selectedNames.has(modifier.optionName),
  ).map((modifier) => modifier.optionName);
}
