/**
 * @file Row shape and ordering shared by the Run Modifier and Boon checklists.
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

/**
 * Orders selected names for stable state, review, and YAML compilation.
 *
 * @param catalog - Rows in display order, such as `RUN_MODIFIERS` or `BOONS`.
 * @param selected - Canonical names in any order.
 * @returns The names the catalog knows, in catalog order, without duplicates.
 */
export function getOrderedOptionNames(
  catalog: readonly { optionName: string }[],
  selected: Iterable<string>,
): string[] {
  const selectedNames = new Set(selected);

  return catalog
    .filter((row) => selectedNames.has(row.optionName))
    .map((row) => row.optionName);
}
