/**
 * @file Loads an existing Slay the Spire II YAML back into the builder's answers.
 *
 * This is the reverse of the builder pipeline (answers → compiler → guided options →
 * YAML). It reads the YAML the way Archipelago would, overlays its values on the
 * generated defaults (an option the YAML leaves out keeps its default, as in
 * Archipelago), and then inverts each section compiler. The result compiles back to
 * options equivalent to the YAML's.
 *
 * Problems that leave nothing to load throw an `Error` with a player-readable message.
 * Everything else becomes a warning: random or weighted values, values Archipelago
 * would reject, options the builder does not cover, unknown keys, and answers that
 * still need a change before the builder can make a YAML from them.
 */

import type { OptionCatalog, OptionValue } from "../../generated/optionCatalog";
import { createDefaultFillerAnswers } from "../FillerItem";
import { getGuidedOptionKeys } from "../GuidedOption";
import {
  createDefaultWizardAnswers,
  type WizardAnswers,
} from "../WizardAnswers";
import type { CompiledOptions } from "../compiler/applyCharacterOptions";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import { readCharacterAnswers } from "./readCharacterAnswers";
import {
  isMapping,
  pickCertainValue,
  readOptionValue,
} from "./readOptionValue";
import {
  readChallengeAnswers,
  readChecksAndRewardsAnswers,
  readDeathLinkAnswers,
  readProgressionAnswers,
  readRunAnswers,
} from "./readSectionAnswers";
import { readSpire2Document } from "./readYamlDocument";

/** Answers loaded from a YAML file, plus what the builder could not keep. */
export interface WizardYamlImport {
  answers: WizardAnswers;
  /** Player-readable notes about settings that were changed or left out. */
  warnings: string[];
}

/** Archipelago features that rewrite options, which the builder cannot show. */
const OPTION_REWRITING_KEYS = ["triggers", "linked_options"];

/**
 * Checks whether a value the builder drops is empty or its default anyway.
 *
 * @param value - Raw YAML value of an option the builder does not cover.
 * @param defaultValue - The option's generated default.
 * @returns Whether dropping the value changes nothing.
 */
function isDefaultOrEmpty(value: unknown, defaultValue: unknown): boolean {
  if (value === null || value === undefined) {
    return true;
  }
  if (Array.isArray(value) && !value.length) {
    return true;
  }
  if (isMapping(value) && !Object.keys(value).length) {
    return true;
  }
  return JSON.stringify(value) === JSON.stringify(defaultValue);
}

/**
 * Reads the player name, which Archipelago also allows to be weighted.
 *
 * @param raw - The document's `name` value.
 * @param warn - Receives a warning when the name is random.
 * @returns The name, or an empty string when there is none to show.
 */
function readPlayerName(raw: unknown, warn: (message: string) => void): string {
  if (raw === undefined || raw === null) {
    return "";
  }
  const picked = pickCertainValue(raw);
  if (
    picked.status === "value" &&
    (typeof picked.value === "string" || typeof picked.value === "number")
  ) {
    return String(picked.value).trim();
  }
  warn(
    "The player name is random or not plain text, so enter a name in the builder.",
  );
  return "";
}

/**
 * Builds builder answers from the text of a Slay the Spire II Archipelago YAML.
 *
 * @param text - Contents of the player's YAML file.
 * @param catalog - Generated option catalog the builder compiles against.
 * @returns Answers to load into the builder, and warnings to show the player.
 * @throws When the text is not YAML, has no Slay the Spire II player, or its game
 * section is not a mapping of options.
 */
export function importWizardYaml(
  text: string,
  catalog: OptionCatalog,
): WizardYamlImport {
  const document = readSpire2Document(text);
  const warnings: string[] = [];
  const warn = (message: string): void => {
    warnings.push(message);
  };

  if (document.matchingDocuments > 1) {
    warn(
      `This file has ${document.matchingDocuments} Slay the Spire II players; the builder loaded the first one.`,
    );
  }

  // Start from every generated default, exactly as Archipelago does.
  const options: CompiledOptions = {};
  for (const key of catalog.option_order) {
    options[key] = structuredClone(
      catalog.options[key]!.default as OptionValue,
    );
  }

  const guidedKeys = new Set(getGuidedOptionKeys());
  const randomOptions: string[] = [];
  const invalidOptions: string[] = [];
  const droppedOptions = OPTION_REWRITING_KEYS.filter((key) =>
    document.rootKeys.includes(key),
  );
  const unknownKeys: string[] = [];

  for (const [key, raw] of Object.entries(document.section)) {
    const option = catalog.options[key];
    if (!option) {
      if (OPTION_REWRITING_KEYS.includes(key)) {
        droppedOptions.push(key);
      } else {
        unknownKeys.push(key);
      }
      continue;
    }

    if (!guidedKeys.has(key)) {
      if (!isDefaultOrEmpty(raw, option.default)) {
        droppedOptions.push(option.display_name);
      }
      continue;
    }

    const result = readOptionValue(option, raw);
    if (result.status === "random") {
      randomOptions.push(option.display_name);
    } else if (result.status === "invalid") {
      invalidOptions.push(`${option.display_name} (${result.detail})`);
    } else {
      options[key] = result.value;
      if (result.ignoredEntries) {
        warn(
          `${option.display_name}: ignored ${result.ignoredEntries.map((entry) => `'${entry}'`).join(", ")}, which ${result.ignoredEntries.length === 1 ? "is" : "are"} not valid here.`,
        );
      }
    }
  }

  if (randomOptions.length) {
    warn(
      `The builder can't show random or weighted values, so these options use their defaults: ${randomOptions.join(", ")}.`,
    );
  }
  if (invalidOptions.length) {
    warn(
      `These values aren't valid, so the options use their defaults: ${invalidOptions.join(", ")}.`,
    );
  }
  if (droppedOptions.length) {
    warn(
      `The builder doesn't cover these settings, so they were left out: ${droppedOptions.join(", ")}.`,
    );
  }
  if (unknownKeys.length) {
    warn(
      `These keys aren't Slay the Spire II options and were ignored: ${unknownKeys.join(", ")}.`,
    );
  }

  const defaults = createDefaultWizardAnswers(
    catalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(catalog),
    catalog,
  );
  const answers: WizardAnswers = {
    playerName: readPlayerName(document.name, warn),
    characters: readCharacterAnswers(
      options,
      defaults.characters,
      catalog,
      warn,
    ),
    run: readRunAnswers(options),
    challenges: readChallengeAnswers(options, defaults.challenges, catalog),
    checksAndRewards: readChecksAndRewardsAnswers(options, warn),
    deathLink: readDeathLinkAnswers(options, defaults.deathLink),
    progression: readProgressionAnswers(options),
  };

  // Settings Archipelago would reject, such as a goal above the roster size, still
  // load so the player can fix them in the builder; say what needs fixing first.
  try {
    compileWizardAnswers(answers, catalog);
  } catch (error) {
    warn(
      `Change this before the builder can make a YAML from these settings: ${error instanceof Error ? error.message : String(error)}`,
    );
  }

  return { answers, warnings };
}
