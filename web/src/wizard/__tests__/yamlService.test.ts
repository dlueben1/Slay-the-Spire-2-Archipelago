/**
 * @file Protects final wizard metadata validation and Archipelago YAML structure.
 *
 * Compiler suites verify individual generated option values. This suite begins at the
 * next pipeline boundary and proves that Review receives one complete document with a
 * normalized player name, stable builder metadata, the correct game identifier, and
 * every guided option nested below the matching game mapping.
 */

import { describe, expect, it } from "vitest";
import {
  buildWizardYaml,
  WIZARD_GAME_NAME,
  WIZARD_YAML_DESCRIPTION,
} from "../../services/YamlService";

/**
 * Verifies metadata and game options serialize into a complete Archipelago document.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function serializesCompleteWizardYaml(): void {
  // Use representative scalar and list options to verify indentation and ordering.
  const yaml = buildWizardYaml("  Spire Climber  ", {
    characters: ["Ironclad"],
    progression_balancing: 50,
  });

  // Metadata must precede the exact game-name mapping requested by the builder.
  expect(yaml).toBe(
    [
      "name: Spire Climber",
      `description: ${JSON.stringify(WIZARD_YAML_DESCRIPTION)}`,
      `game: ${JSON.stringify(WIZARD_GAME_NAME)}`,
      `${WIZARD_GAME_NAME}:`,
      "  characters:",
      '    - "Ironclad"',
      "  progression_balancing: 50",
      "",
    ].join("\n"),
  );
}

/**
 * Verifies blank and multiline player names cannot reach preview or download output.
 *
 * @returns Nothing; Vitest records assertion failures.
 */
function rejectsInvalidPlayerNames(): void {
  /** Builds YAML with an empty player name. */
  function buildBlankNameYaml(): void {
    // Use an empty option record because metadata validation should fail first.
    buildWizardYaml("   ", {});
  }

  /** Builds YAML with a player name spanning multiple lines. */
  function buildMultilineNameYaml(): void {
    // Quoting could serialize this, but the wizard deliberately accepts one line only.
    buildWizardYaml("Player One\nPlayer Two", {});
  }

  // Both failures should explain the player-facing correction needed.
  expect(buildBlankNameYaml).toThrow("Enter a player name");
  expect(buildMultilineNameYaml).toThrow("must fit on one line");
}

/**
 * Registers complete-document YAML cases with Vitest.
 *
 * @returns Nothing; test registration occurs as a module-load side effect.
 */
function registerYamlServiceTests(): void {
  // Cover the successful document shape and both metadata failure modes.
  it("serializes complete Archipelago YAML", serializesCompleteWizardYaml);
  it("rejects invalid player names", rejectsInvalidPlayerNames);
}

// Register the service boundary as one focused wizard-output suite.
describe("wizard YAML service", registerYamlServiceTests);
