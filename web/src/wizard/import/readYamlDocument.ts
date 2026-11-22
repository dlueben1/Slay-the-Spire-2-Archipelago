/**
 * @file Finds the Slay the Spire II player in an Archipelago YAML file.
 *
 * One YAML file may hold several players separated by `---`, and each player's `game`
 * may be a weighted mapping between games. This module parses the text the way
 * Archipelago's PyYAML loader does (YAML 1.1, so `on`/`off` and `yes`/`no` are booleans,
 * and repeated keys keep the last value), picks the first document that always plays
 * Slay the Spire II, and returns its name and game section. Problems that leave nothing
 * to import throw an `Error` whose message is written for players.
 */

import { parseAllDocuments } from "yaml";
import { WIZARD_GAME_NAME } from "../../services/YamlService";
import { isMapping, pickCertainValue } from "./readOptionValue";

/** The Slay the Spire II player found in a YAML file. */
export interface Spire2YamlDocument {
  /** The raw `name` value, which may itself be weighted. */
  name: unknown;
  /** Options under the `Slay the Spire II:` mapping. */
  section: Record<string, unknown>;
  /** Top-level keys of the document, such as `triggers`, besides the game section. */
  rootKeys: string[];
  /** How many documents in the file play Slay the Spire II. */
  matchingDocuments: number;
}

/** How a document's `game` relates to Slay the Spire II. */
type GameMatch = "always" | "sometimes" | "never";

/**
 * Decides whether a document always plays Slay the Spire II.
 *
 * @param game - The document's raw `game` value.
 * @returns `always` when every roll picks this game, `sometimes` when it is one of
 * several weighted games, and `never` otherwise.
 */
function matchGame(game: unknown): GameMatch {
  const picked = pickCertainValue(game);
  if (picked.status === "value") {
    return picked.value === WIZARD_GAME_NAME ? "always" : "never";
  }

  // A weighting that includes this game with a non-zero weight only sometimes picks it.
  if (picked.status === "random") {
    const candidates = Array.isArray(game)
      ? game
      : Object.entries(game as Record<string, unknown>)
          .filter(([, weight]) => Number(weight) > 0)
          .map(([name]) => name);
    if (candidates.includes(WIZARD_GAME_NAME)) {
      return "sometimes";
    }
  }
  return "never";
}

/**
 * Parses YAML text and selects the first player that plays Slay the Spire II.
 *
 * @param text - Contents of the player's YAML file.
 * @returns The player's name, game section, and document facts used for warnings.
 * @throws When the text is not valid YAML or has no usable Slay the Spire II player.
 */
export function readSpire2Document(text: string): Spire2YamlDocument {
  const documents = parseAllDocuments(text, {
    version: "1.1",
    uniqueKeys: false,
    merge: true,
  });

  for (const document of documents) {
    const [error] = document.errors;
    if (error) {
      throw new Error(`This file is not valid YAML: ${error.message.trim()}`);
    }
  }

  let selected: Record<string, unknown> | undefined;
  let matchingDocuments = 0;
  let hasWeightedMatch = false;

  for (const document of documents) {
    const content: unknown = document.toJS();
    if (!isMapping(content)) {
      continue;
    }

    const match = matchGame(content.game);
    hasWeightedMatch ||= match === "sometimes";
    if (match === "always") {
      matchingDocuments += 1;
      selected ??= content;
    }
  }

  if (!selected) {
    throw new Error(
      hasWeightedMatch
        ? `This file only plays ${WIZARD_GAME_NAME} some of the time. The builder can load a YAML whose game is always ${WIZARD_GAME_NAME}.`
        : `This file has no ${WIZARD_GAME_NAME} settings. Check that it says "game: ${WIZARD_GAME_NAME}".`,
    );
  }

  if (!(WIZARD_GAME_NAME in selected)) {
    throw new Error(
      `This file has no "${WIZARD_GAME_NAME}:" section for its options.`,
    );
  }

  // An empty game section is a player who kept every default.
  const section = selected[WIZARD_GAME_NAME] ?? {};
  if (!isMapping(section)) {
    throw new Error(
      `The "${WIZARD_GAME_NAME}:" section must hold the game's options, one per line.`,
    );
  }

  return {
    name: selected.name,
    section,
    rootKeys: Object.keys(selected).filter((key) => key !== WIZARD_GAME_NAME),
    matchingDocuments,
  };
}
