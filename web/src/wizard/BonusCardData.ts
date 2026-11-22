/**
 * @file The pool words of a Bonus Cards (`CARDS`) row.
 *
 * A pool such as "Regent Attack" or "Colorless Rare Upgraded" names at most one source,
 * card type and rarity, plus Upgraded; with no source the cards come from the run's own
 * character. "Random" mixes sources card by card. This mirrors `parse_bonus_card_pool` in
 * the APWorld's items.py and `BonusCardPool` in the client.
 */

export const BONUS_CARD_CHARACTERS = [
  "Ironclad",
  "Silent",
  "Defect",
  "Necrobinder",
  "Regent",
  "Fool",
] as const;
export const BONUS_CARD_OTHER_SOURCES = [
  "Other",
  "Colorless",
  "Event",
  "Ancient",
] as const;
export const BONUS_CARD_SOURCES = [
  ...BONUS_CARD_CHARACTERS,
  ...BONUS_CARD_OTHER_SOURCES,
] as const;
export const BONUS_CARD_TYPES = ["Attack", "Skill", "Power"] as const;
export const BONUS_CARD_RARITIES = ["Common", "Uncommon", "Rare"] as const;
export const BONUS_CARD_RANDOM = "Random";
export const BONUS_CARD_UPGRADED = "Upgraded";

export type BonusCardSource = (typeof BONUS_CARD_SOURCES)[number];
export type BonusCardType = (typeof BONUS_CARD_TYPES)[number];
export type BonusCardRarity = (typeof BONUS_CARD_RARITIES)[number];

/** One pool split into its parts. A Random pool has no source, type or rarity. */
export interface BonusCardPoolParts {
  random: boolean;
  source: BonusCardSource | null;
  type: BonusCardType | null;
  rarity: BonusCardRarity | null;
  upgraded: boolean;
}

/** What each source offers, for the dialog. */
export const BONUS_CARD_SOURCE_DESCRIPTIONS: Readonly<Record<BonusCardSource, string>> = {
  Ironclad: "Ironclad's cards.",
  Silent: "Silent's cards.",
  Defect: "Defect's cards.",
  Necrobinder: "Necrobinder's cards.",
  Regent: "Regent's cards.",
  Fool: "Fool's cards. Needs game version 0.113; skipped on older ones.",
  Other: "A random character other than the one you're playing, picked for each card.",
  Colorless: "Colorless cards, half Uncommon and half Rare.",
  Event: "Cards that events give.",
  Ancient: "Cards that Ancients give. Very strong.",
};

/** Relic art standing for a Bonus Cards row: Prismatic Gem, the game's "cards from anywhere". */
export const BONUS_CARDS_IMAGE_RELIC_ID = "PRISMATIC_GEM";

/**
 * Splits a pool into its parts, matching words in any case.
 *
 * @returns The parts, or a player-facing reason the pool can't be used.
 */
export function parseBonusCardPool(
  text: string,
): BonusCardPoolParts | { error: string } {
  const parts: BonusCardPoolParts = {
    random: false,
    source: null,
    type: null,
    rarity: null,
    upgraded: false,
  };
  const words = text.trim().split(/\s+/).filter(Boolean);
  if (!words.length) {
    return { error: "a pool can't be empty" };
  }
  const find = <T extends string>(names: readonly T[], word: string) =>
    names.find((name) => name.toLowerCase() === word.toLowerCase());
  for (const word of words) {
    const source =
      word.toLowerCase() === "colourless"
        ? "Colorless"
        : find(BONUS_CARD_SOURCES, word);
    const type = find(BONUS_CARD_TYPES, word);
    const rarity = find(BONUS_CARD_RARITIES, word);
    if (source) {
      if (parts.source) return { error: "it names more than one source" };
      parts.source = source;
    } else if (type) {
      if (parts.type) return { error: "it names more than one type" };
      parts.type = type;
    } else if (rarity) {
      if (parts.rarity) return { error: "it names more than one rarity" };
      parts.rarity = rarity;
    } else if (word.toLowerCase() === "random") {
      if (parts.random) return { error: "it says Random twice" };
      parts.random = true;
    } else if (word.toLowerCase() === "upgraded") {
      if (parts.upgraded) return { error: "it says Upgraded twice" };
      parts.upgraded = true;
    } else {
      return {
        error: `'${word}' is not a card source, type, rarity, Random or Upgraded`,
      };
    }
  }
  if (parts.random && (parts.source || parts.type || parts.rarity)) {
    return { error: "Random can only be combined with Upgraded" };
  }
  if ((parts.source === "Event" || parts.source === "Ancient") && parts.rarity) {
    return { error: `${parts.source} cards have no Common, Uncommon or Rare` };
  }
  if (parts.source === "Colorless" && parts.rarity === "Common") {
    return { error: "there are no Common colorless cards" };
  }
  return parts;
}

/** Writes a pool in its canonical order: Upgraded, source, rarity, type. */
export function formatBonusCardPool(parts: BonusCardPoolParts): string {
  if (parts.random) {
    return parts.upgraded
      ? `${BONUS_CARD_RANDOM} ${BONUS_CARD_UPGRADED}`
      : BONUS_CARD_RANDOM;
  }
  return [
    parts.upgraded ? BONUS_CARD_UPGRADED : null,
    parts.source,
    parts.rarity,
    parts.type,
  ]
    .filter(Boolean)
    .join(" ");
}

/** Whether a pool can name a rarity: Event, Ancient and Random pools can't. */
export function bonusCardPoolTakesRarity(parts: BonusCardPoolParts): boolean {
  return !parts.random && parts.source !== "Event" && parts.source !== "Ancient";
}

/** A short player-facing description of a pool, such as "Upgraded Regent Rare Attack". */
export function describeBonusCardPool(parts: BonusCardPoolParts): string {
  if (parts.random) {
    return parts.upgraded ? "Random (upgraded)" : "Random";
  }
  return formatBonusCardPool(parts) || "Your character";
}
