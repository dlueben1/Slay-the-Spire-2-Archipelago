/**
 * @file Enchantments that ENCHANTMENT and ENCHANTED_CARDS Bonus Items can give.
 *
 * The tiers and amounts mirror `BONUS_ENCHANTMENT_TIERS` in the APWorld's items.py
 * (which validates YAML entries) and `BonusEnchantmentCatalog` in the client (which
 * grants them). Names, descriptions and icons come from Spire Codex through
 * `web/src/generated/enchantments.json`; regenerate it with
 * `python scripts/download_latest_enchantments.py`.
 */

import enchantmentsJson from "../generated/enchantments.json";

/** Tiers a random enchantment bonus rolls from, as the YAML `Pools` names them. */
export const BONUS_ENCHANTMENT_TIERS = ["Common", "Uncommon", "Rare"] as const;
export type BonusEnchantmentTier = (typeof BONUS_ENCHANTMENT_TIERS)[number];

/** One enchantment record from the generated Spire Codex export. */
interface GeneratedEnchantment {
  id: string;
  name: string;
  description: string;
  image_url: string;
}

/** Player-facing enchantment model used by the picker, table, and review list. */
export interface BonusEnchantmentInfo {
  /** The YAML name, the game's class name (for example `RoyallyApproved`). */
  name: string;
  displayName: string;
  /** Plain description with the bonus amount filled in. */
  description: string;
  imageUrl: string;
  /** The tier that rolls it, or `null` when it can only be chosen by name. */
  tier: BonusEnchantmentTier | null;
  amount: number;
}

/** Text and icon for the mod's own enchantments, which Spire Codex does not list. */
interface CustomEnchantment {
  displayName: string;
  description: string;
  imageUrl: string;
}

/**
 * Every enchantment a bonus may give. Tezcatara's Ember is deliberately absent. Those
 * without a tier have a drawback or need a rest site, so only a named choice gives them.
 * Entries with `custom` are the mod's own enchantments; their text matches the client's
 * enchantments.json.
 */
const BONUS_ENCHANTMENTS: readonly {
  name: string;
  tier: BonusEnchantmentTier | null;
  amount: number;
  custom?: CustomEnchantment;
}[] = [
  { name: "Sharp", tier: "Common", amount: 2 },
  { name: "Nimble", tier: "Common", amount: 3 },
  { name: "Adroit", tier: "Common", amount: 3 },
  { name: "Steady", tier: "Common", amount: 1 },
  { name: "Swift", tier: "Common", amount: 2 },
  { name: "Vigorous", tier: "Common", amount: 5 },
  { name: "Inky", tier: "Uncommon", amount: 1 },
  { name: "Momentum", tier: "Uncommon", amount: 2 },
  { name: "RoyallyApproved", tier: "Uncommon", amount: 1 },
  { name: "Goopy", tier: "Uncommon", amount: 1 },
  {
    name: "Eager",
    tier: "Uncommon",
    amount: 1,
    custom: {
      displayName: "Eager",
      description:
        "The first time you draw this card each combat, it costs 1 less until played.",
      imageUrl: "/static/images/enchantments/ap_eager.png",
    },
  },
  {
    name: "Exposing",
    tier: "Uncommon",
    amount: 1,
    custom: {
      displayName: "Exposing",
      description: "This card applies 1 Vulnerable. Attacks only.",
      imageUrl: "/static/images/enchantments/ap_exposing.png",
    },
  },
  { name: "Sown", tier: "Rare", amount: 1 },
  { name: "Glam", tier: "Rare", amount: 1 },
  { name: "Imbued", tier: "Rare", amount: 1 },
  { name: "SoulsPower", tier: "Rare", amount: 1 },
  { name: "Instinct", tier: "Rare", amount: 1 },
  { name: "Spiral", tier: "Rare", amount: 1 },
  {
    name: "Checkbound",
    tier: "Rare",
    amount: 1,
    custom: {
      displayName: "Checkbound",
      description:
        "+1 damage and Block each time this character's checks sent pass 5%, 12%, 22%, 36%, 55% and 80% (up to +6). Singleplayer only.",
      imageUrl: "/static/images/enchantments/ap_checkbound.png",
    },
  },
  { name: "Corrupted", tier: null, amount: 1 },
  { name: "Slither", tier: null, amount: 1 },
  { name: "SlumberingEssence", tier: null, amount: 1 },
  { name: "PerfectFit", tier: null, amount: 1 },
  { name: "Clone", tier: null, amount: 4 },
];

/** Short descriptions for each tier in the random picker. */
export const BONUS_ENCHANTMENT_TIER_DESCRIPTIONS: Record<
  BonusEnchantmentTier,
  string
> = {
  Common: "Small, steady boosts such as Sharp, Nimble and Swift.",
  Uncommon: "Stronger or odder effects such as Momentum and Royally Approved.",
  Rare: "Run-shaping effects such as Glam, Sown and Instinct.",
};

/** Enchantment whose icon stands for a random enchantment row. */
const RANDOM_ENCHANTMENT_IMAGE_NAME = "Glam";

/** Matches `RoyallyApproved`, `royally approved` and `ROYALLY_APPROVED` alike. */
export function normalizeEnchantmentName(name: string): string {
  return name.replace(/[^a-z0-9]/gi, "").toLowerCase();
}

/** Turns the game's text markup into plain text: `[energy:2]` reads "2 Energy". */
function stripTags(text: string): string {
  return text
    .replace(/\[energy:([^\]]+)\]/gi, "$1 Energy")
    .replace(/\[\/?[a-z_]+\]/gi, "");
}

const generatedByKey = new Map<string, GeneratedEnchantment>(
  (enchantmentsJson as GeneratedEnchantment[]).map((record) => [
    normalizeEnchantmentName(record.id),
    record,
  ]),
);

const enchantments: readonly BonusEnchantmentInfo[] = BONUS_ENCHANTMENTS.map(
  (entry) => {
    if (entry.custom) {
      return {
        name: entry.name,
        ...entry.custom,
        tier: entry.tier,
        amount: entry.amount,
      };
    }
    const generated = generatedByKey.get(normalizeEnchantmentName(entry.name));
    if (!generated) {
      throw new Error(
        `Bonus enchantment source data is invalid: enchantments.json has no '${entry.name}'.`,
      );
    }
    return {
      name: entry.name,
      displayName: generated.name,
      description: stripTags(generated.description).replace(
        /\bX\b/g,
        String(entry.amount),
      ),
      imageUrl: generated.image_url,
      tier: entry.tier,
      amount: entry.amount,
    };
  },
);

/** Every enchantment a bonus may give, in table order. */
export function getBonusEnchantments(): readonly BonusEnchantmentInfo[] {
  return enchantments;
}

/** Finds an enchantment by any spelling of its name, or `undefined`. */
export function findBonusEnchantment(
  name: string,
): BonusEnchantmentInfo | undefined {
  const key = normalizeEnchantmentName(name);
  return enchantments.find(
    (entry) =>
      normalizeEnchantmentName(entry.name) === key ||
      normalizeEnchantmentName(entry.displayName) === key,
  );
}

export function isBonusEnchantmentTier(
  value: unknown,
): value is BonusEnchantmentTier {
  return (
    typeof value === "string" &&
    (BONUS_ENCHANTMENT_TIERS as readonly string[]).includes(value)
  );
}

/** The icon shown for a random enchantment row. */
export function getRandomEnchantmentImageUrl(): string {
  return findBonusEnchantment(RANDOM_ENCHANTMENT_IMAGE_NAME)!.imageUrl;
}
