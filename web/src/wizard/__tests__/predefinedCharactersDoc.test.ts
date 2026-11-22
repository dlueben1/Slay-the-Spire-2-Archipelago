/**
 * @file Keeps the published predefined-character list in step with the APWorld registry.
 *
 * `docs/predefined-characters.md` is written by hand for GitHub and the wizard, while
 * the generated catalog exports the registry from `world/spire2/characters.py`. Update
 * the document whenever a predefined character is added.
 */

import { describe, expect, it } from "vitest";
import predefinedCharactersDoc from "../../../../docs/predefined-characters.md?raw";
import { optionCatalog } from "../../generated/optionCatalog";

/** Table rows that name a character, excluding the header and separator rows. */
const characterRows = predefinedCharactersDoc
  .split("\n")
  .filter((line) => line.startsWith("| ") && line.includes("`"));

describe("predefined character list", () => {
  it("lists every registered character and internal ID in registry order", () => {
    const listed = characterRows.map((row) => {
      const [name = "", internalId = ""] = row
        .split("|")
        .slice(1, 3)
        .map((cell) => cell.trim());
      return { name, internal_id: internalId.replace(/`/g, "") };
    });

    expect(listed).toEqual(optionCatalog.characters.predefined);
  });

  it("links every character's mod on Steam Workshop", () => {
    for (const row of characterRows) {
      expect(row).toMatch(
        /\(https:\/\/steamcommunity\.com\/sharedfiles\/filedetails\/\?id=\d+\)/,
      );
    }
  });
});
