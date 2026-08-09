/**
 * @file Validates completed compiler output against the generated option catalog.
 *
 * Validation answers "does Archipelago's generated schema accept this final object?"
 * It intentionally does not interpret player intent or decide how answers map to keys;
 * those responsibilities belong to `wizard/compiler`. Keeping this as a final boundary
 * catches schema drift, missing options, illegal choices, and out-of-range values after
 * every section compiler has contributed to the same complete configuration.
 */

import type {
  GeneratedOption,
  OptionCatalog,
  OptionValue,
} from "../../generated/optionCatalog";

/**
 * Checks one compiled value against the metadata for its generated option.
 *
 * @param option - Generated schema entry describing the accepted value.
 * @param value - Compiled value to check against that entry.
 * @returns Human-readable error fragments; an empty array means the value is valid.
 * @remarks Errors omit the option key because `validateOptions` adds that context.
 * This is structural schema validation, not cross-answer business-rule validation.
 */
function validateValue(option: GeneratedOption, value: OptionValue): string[] {
  // Gather all applicable problems so callers receive one useful validation report.
  const errors: string[] = [];

  // Range-like values must stay within generated numeric boundaries.
  if (
    (option.kind === "range" || option.kind === "named_range") &&
    typeof value === "number"
  ) {
    // Python Range options accept integral values even when JavaScript can store decimals.
    if (!Number.isInteger(value)) {
      errors.push("must be a whole number");
    }

    if (option.minimum !== undefined && value < option.minimum) {
      errors.push(`must be at least ${option.minimum}`);
    }

    if (option.maximum !== undefined && value > option.maximum) {
      errors.push(`must be at most ${option.maximum}`);
    }
  } else if (option.kind === "choice" && typeof value === "string") {
    // Choice output uses generated canonical names rather than numeric raw values.
    const choiceNames = new Set<string>();

    for (const choice of option.choices ?? []) {
      choiceNames.add(choice.name);
    }

    if (!choiceNames.has(value)) {
      errors.push(`has unknown choice '${value}'`);
    }
  } else if (option.kind === "toggle" && typeof value !== "boolean") {
    // Normalized toggle defaults and compiler output are always booleans.
    errors.push("must be true or false");
  } else if (option.kind === "set") {
    // Sets serialize as arrays and may optionally restrict their members.
    if (!Array.isArray(value)) {
      errors.push("must be a list");
    } else if (option.valid_keys && !option.allow_custom_values) {
      const invalid: unknown[] = [];

      for (const entry of value) {
        if (typeof entry !== "string" || !option.valid_keys.includes(entry)) {
          invalid.push(entry);
        }
      }

      if (invalid.length) {
        errors.push(`contains invalid values: ${invalid.join(", ")}`);
      }
    }
  } else if (option.kind === "text_choice" && typeof value !== "string") {
    // Text choices accept canonical choice names or custom strings, but never numbers.
    errors.push("must be text");
  }

  // Return fragments instead of throwing so the outer pass can aggregate failures.
  return errors;
}

/**
 * Validates a complete compiled option configuration against its generated catalog.
 *
 * @param options - Complete options snapshot produced by `compileWizardAnswers`.
 * @param catalog - Generated metadata defining accepted keys and values.
 * @returns Nothing when every compiled key and value is valid.
 * @throws A single aggregated error containing every detected schema violation.
 * @remarks Run once after all section compilers. Do not use this function to validate
 * partially completed forms because it requires every generated option to be present.
 */
export function validateOptions(
  options: Record<string, OptionValue>,
  catalog: OptionCatalog,
): void {
  // Accumulate errors so schema drift is diagnosable in one test or development run.
  const errors: string[] = [];

  // Reject unknown keys and validate each known value against its own schema entry.
  for (const [key, value] of Object.entries(options)) {
    const option = catalog.options[key];

    if (!option) {
      errors.push(`Unknown generated option '${key}'.`);
    } else {
      const valueErrors = validateValue(option, value);

      for (const valueError of valueErrors) {
        errors.push(`${key} ${valueError}.`);
      }
    }
  }

  // A canonical result is complete, so every ordered catalog key must be represented.
  for (const key of catalog.option_order) {
    if (!(key in options)) {
      errors.push(`Missing generated option '${key}'.`);
    }
  }

  // Throw only after the full pass so callers see every inconsistency together.
  if (errors.length) {
    throw new Error(`Compiled options are invalid:\n${errors.join("\n")}`);
  }
}
