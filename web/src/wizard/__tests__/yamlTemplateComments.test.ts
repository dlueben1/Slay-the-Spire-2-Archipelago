/**
 * @file Protects how template comments are read and written into builder output.
 *
 * The hand-written release template is the only source of the builder's option
 * comments, so these cases cover which comments attach to an option, how they appear
 * in the generated document, and that the bundled template names real options.
 */

import { describe, expect, it } from "vitest";
import spire2Template from "../../../../world/spire2/docs/Spire2-template.yaml?raw";
import { optionCatalog } from "../../generated/optionCatalog";
import { buildWizardYaml, WIZARD_GAME_NAME } from "../../services/YamlService";
import {
  parseTemplateComments,
  type YamlComments,
} from "../../services/YamlTemplateComments";

const FIXTURE = [
  "# File header, outside the game mapping.",
  "name: Player",
  "requires:",
  "  version: 0.6.7 # not a game option",
  "Slay the Spire II:",
  "  ###########",
  "  # Section #",
  "  ###########",
  "",
  "  # Section introduction, separated by a blank line.",
  "",
  "  # Characters to play.",
  "  #   Ironclad, Silent",
  "  characters:",
  '    - "Ironclad"',
  "  uncommented: 1",
  "  shop_card_slots: 2          # 0-5 card slots",
  "  shop_remove_slots: false    # card removal",
  "  # Two lines above",
  "  # and a trailing one.",
  '  weak_trap_weight: "high"  # 1 Weak',
  "Other Game:",
  "  # Belongs to another game.",
  "  characters: []",
].join("\n");

function readsOnlyCommentsTouchingAnOption(): void {
  const comments = parseTemplateComments(FIXTURE, WIZARD_GAME_NAME);

  expect([...comments.keys()]).toEqual([
    "characters",
    "shop_card_slots",
    "shop_remove_slots",
    "weak_trap_weight",
  ]);
  expect(comments.get("characters")).toEqual({
    above: ["Characters to play.", "  Ironclad, Silent"],
  });
  expect(comments.get("shop_card_slots")).toEqual({
    above: [],
    trailing: "0-5 card slots",
  });
  expect(comments.get("weak_trap_weight")).toEqual({
    above: ["Two lines above", "and a trailing one."],
    trailing: "1 Weak",
  });
}

function writesCommentsIntoTheDocument(): void {
  const comments: YamlComments = {
    header: ["Made with the builder.", "", "Second paragraph."],
    options: parseTemplateComments(FIXTURE, WIZARD_GAME_NAME),
  };

  const yaml = buildWizardYaml(
    "Spire2Player",
    {
      characters: ["Ironclad"],
      uncommented: 1,
      shop_card_slots: 2,
      shop_remove_slots: false,
      weak_trap_weight: "high",
    },
    comments,
  );
  const lines = yaml.split("\n");

  expect(lines.slice(0, 5)).toEqual([
    "# Made with the builder.",
    "#",
    "# Second paragraph.",
    "",
    "name: Spire2Player",
  ]);
  expect(lines.slice(lines.indexOf(`${WIZARD_GAME_NAME}:`))).toEqual([
    `${WIZARD_GAME_NAME}:`,
    "  # Characters to play.",
    "  #   Ironclad, Silent",
    "  characters:",
    '    - "Ironclad"',
    "  uncommented: 1",
    "  shop_card_slots: 2        # 0-5 card slots",
    "  shop_remove_slots: false  # card removal",
    "",
    "  # Two lines above",
    "  # and a trailing one.",
    '  weak_trap_weight: "high"  # 1 Weak',
    "",
  ]);
}

function templateCommentsNameRealOptions(): void {
  const commented = parseTemplateComments(spire2Template, WIZARD_GAME_NAME);
  const unknown = [...commented.keys()].filter(
    (key) => !(key in optionCatalog.options),
  );

  expect(commented.size).toBeGreaterThan(0);
  expect(unknown).toEqual([]);
}

describe("YAML template comments", () => {
  it(
    "reads only comments touching an option",
    readsOnlyCommentsTouchingAnOption,
  );
  it("writes comments into the document", writesCommentsIntoTheDocument);
  it(
    "names only real options in the bundled template",
    templateCommentsNameRealOptions,
  );
});
