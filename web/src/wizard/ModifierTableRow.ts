/**
 * @file Row shape shared by the Run Modifier and Boon checklists.
 *
 * `ModifierTable.vue` renders any catalog mapped to these rows; each catalog keeps its
 * own fields and maps them here, such as a Run Modifier's starting act to its badge.
 */

export interface ModifierTableRow<Group extends string = string> {
  /** Canonical name written to the YAML. */
  optionName: string;
  name: string;
  effect: string;
  /** Short label shown before the name, such as `Act 2+` or `Solo only`. */
  badge: string;
  group: Group;
}

/** A table group heading. */
export interface ModifierTableGroup<Group extends string = string> {
  id: Group;
  title: string;
}
