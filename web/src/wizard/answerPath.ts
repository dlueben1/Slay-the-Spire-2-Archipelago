/**
 * @file Immutable dot-path access into section answer objects.
 *
 * Declarative control descriptors in `WizardStep.ts` bind to answer fields with
 * section-relative paths such as `"seeded"` or `"shop.cardSlots"`. These helpers give
 * the generic control renderer one well-tested way to read and replace those fields
 * without mutating the reactive answer model owned by the wizard view.
 */

/**
 * Reads one answer field through a section-relative dot path.
 *
 * @param section - Section answer object owned by the active wizard step.
 * @param path - Dot-separated field path such as `checks.neowSanity`.
 * @returns The value stored at that path.
 * @throws When any path segment does not exist, so descriptor typos fail loudly.
 */
export function getAnswerAtPath(section: object, path: string): unknown {
  let current: unknown = section;

  for (const segment of path.split(".")) {
    // Descriptor paths must resolve fully; a miss means the descriptor drifted.
    if (
      current === null ||
      typeof current !== "object" ||
      !(segment in current)
    ) {
      throw new Error(`Answer path '${path}' does not exist on this section.`);
    }

    current = (current as Record<string, unknown>)[segment];
  }

  return current;
}

/**
 * Replaces one answer field through a section-relative dot path.
 *
 * @param section - Section answer object owned by the active wizard step.
 * @param path - Dot-separated field path such as `checks.neowSanity`.
 * @param value - Narrowed control value to store at that path.
 * @returns A new section object with every object along the path shallow-copied.
 * @throws When any path segment does not exist, so descriptor typos fail loudly.
 * @remarks Only objects along the written path are copied; sibling sections keep
 * their identity so Vue re-renders stay minimal.
 */
export function setAnswerAtPath<T extends object>(
  section: T,
  path: string,
  value: unknown,
): T {
  const segments = path.split(".");
  const [head, ...rest] = segments;

  if (head === undefined || !(head in section)) {
    throw new Error(`Answer path '${path}' does not exist on this section.`);
  }

  // The final segment is replaced directly inside one shallow copy.
  if (rest.length === 0) {
    return { ...section, [head]: value };
  }

  const child = (section as Record<string, unknown>)[head];

  if (child === null || typeof child !== "object") {
    throw new Error(`Answer path '${path}' does not exist on this section.`);
  }

  // Recurse so each nested level is copied instead of mutated in place.
  return {
    ...section,
    [head]: setAnswerAtPath(child as object, rest.join("."), value),
  };
}
