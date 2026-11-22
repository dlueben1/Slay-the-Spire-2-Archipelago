/**
 * @file Checks the Items & Hints compiler: starting items, hints and placement lists.
 */

import { describe, expect, it } from "vitest";
import { optionCatalog } from "../../generated/optionCatalog";
import { createDefaultFillerAnswers } from "../FillerItem";
import { selectGuidedOptions } from "../GuidedOption";
import { findSharedItem, getItemCharacters, parseItemName } from "../ItemNames";
import { compileWizardAnswers } from "../compiler/compileWizardAnswers";
import {
  createDefaultWizardAnswers,
  type WizardAnswers,
} from "../WizardAnswers";

function createAnswers(): WizardAnswers {
  const answers = createDefaultWizardAnswers(
    optionCatalog.options.characters?.valid_keys ?? [],
    createDefaultFillerAnswers(optionCatalog),
    optionCatalog,
  );
  answers.characters.selectedCharacters = ["Ironclad", "Silent"];
  return answers;
}

describe("Items & Hints compiler", () => {
  it("adds up stacks of the same item", () => {
    const answers = createAnswers();
    answers.items.startingInventory = [
      { character: "Ironclad", item: "Relic", count: 2 },
      { character: "Ironclad", item: "Relic", count: 1 },
      { character: null, item: "Strength", count: 1 },
    ];

    const compiled = compileWizardAnswers(answers, optionCatalog);

    expect(compiled.start_inventory).toEqual({
      "Ironclad Relic": 3,
      Strength: 1,
    });
  });

  it("leaves empty item options out of the YAML", () => {
    const guided = selectGuidedOptions(
      compileWizardAnswers(createAnswers(), optionCatalog),
    );

    expect(guided).not.toHaveProperty("start_inventory");
    expect(guided).not.toHaveProperty("local_items");
  });

  it("refuses items for a character outside the roster", () => {
    const answers = createAnswers();
    answers.items.startingInventory = [
      { character: "Regent", item: "Relic", count: 1 },
    ];

    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /Regent isn't in your roster/,
    );
  });

  it("refuses traps and Bonus Items as starting items", () => {
    const answers = createAnswers();
    answers.items.startingInventory = [
      { character: null, item: "Weak Trap", count: 1 },
    ];

    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /can't include 'Weak Trap'/,
    );
  });

  it("refuses an item in both placement lists, through groups too", () => {
    const answers = createAnswers();
    answers.items.localItems = ["Everything"];
    answers.items.nonLocalItems = ["Traps"];

    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /in both Local Items and Non-local Items/,
    );
  });

  it("refuses names that are neither items nor groups", () => {
    const answers = createAnswers();
    answers.items.startHints = ["Ironclad Lunch"];

    expect(() => compileWizardAnswers(answers, optionCatalog)).toThrow(
      /isn't an item or item group/,
    );
  });
});

describe("item names", () => {
  it("lists predefined modded characters by name and skips generic ones", () => {
    const answers = createAnswers();
    answers.characters.moddedCharacters = [
      {
        name: "INTOTHESPIREVERSE-SHADOW_IRONCLAD",
        ascensions: answers.characters.sharedAscensions,
      },
      {
        name: "SOME_MOD-HERO",
        ascensions: answers.characters.sharedAscensions,
      },
    ];

    expect(getItemCharacters(answers.characters, optionCatalog)).toEqual([
      "Ironclad",
      "Silent",
      "Tectonic",
    ]);
  });

  it("reads character, universal and generic slot items", () => {
    expect(parseItemName("silent progressive smith", optionCatalog)).toEqual({
      character: "Silent",
      item: "Progressive Smith",
    });
    expect(parseItemName("Card Sight", optionCatalog)).toEqual({
      character: null,
      item: "Card Sight",
    });
    expect(parseItemName("Custom Character 3 Relic", optionCatalog)).toEqual({
      character: "Custom Character 3",
      item: "Relic",
    });
    expect(parseItemName("Ironclad Lunch", optionCatalog)).toBeNull();
  });

  it("finds overlaps through nested groups and names", () => {
    expect(
      findSharedItem(
        ["Card Rewards"],
        ["Silent Rare Card Reward"],
        [],
        optionCatalog,
      ),
    ).toBe("Silent Rare Card Reward");
    expect(
      findSharedItem(
        ["Rare Card Rewards"],
        ["Relics"],
        ["Ironclad"],
        optionCatalog,
      ),
    ).toBeNull();
  });
});
