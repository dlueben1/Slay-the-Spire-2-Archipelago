import type { OptionCatalog } from "../generated/optionCatalog";
import { TRAP_CHANCE_OPTION_KEY } from "./WizardOptionKey";

export const TRAP_WEIGHTS = ["none", "low", "medium", "high"] as const;
export type TrapWeight = (typeof TRAP_WEIGHTS)[number];

export const TRAP_ITEM_DEFINITIONS = [
  {
    id: "weak",
    optionKey: "weak_trap_weight",
    imageSource: "/icons/weak_power.webp",
  },
  {
    id: "frail",
    optionKey: "frail_trap_weight",
    imageSource: "/icons/frail_power.webp",
  },
  {
    id: "vulnerable",
    optionKey: "vulnerable_trap_weight",
    imageSource: "/icons/vulnerable_power.webp",
  },
  {
    id: "noDraw",
    optionKey: "no_draw_trap_weight",
    imageSource: "/icons/no_draw_power.webp",
  },
  {
    id: "tangled",
    optionKey: "tangled_trap_weight",
    imageSource: "/icons/tangled_power.webp",
  },
  { id: "vakuu", optionKey: "vakuu_trap_weight" },
  {
    id: "confused",
    optionKey: "confused_trap_weight",
    imageSource: "/icons/confused_power.webp",
  },
  { id: "sloth", optionKey: "sloth_trap_weight" },
  { id: "dazed", optionKey: "dazed_trap_weight" },
] as const;

export type TrapItemId = (typeof TRAP_ITEM_DEFINITIONS)[number]["id"];
export interface TrapAnswers {
  chance: number;
  weights: Record<TrapItemId, TrapWeight>;
}

export function createTrapDisplayItems(catalog: OptionCatalog) {
  return TRAP_ITEM_DEFINITIONS.map((item) => {
    const option = catalog.options[item.optionKey];
    if (!option) throw new Error(`Missing trap option '${item.optionKey}'.`);
    return {
      ...item,
      name: option.display_name.replace(/ Weight$/, ""),
      description: option.description,
    };
  });
}

export function createDefaultTrapAnswers(catalog: OptionCatalog): TrapAnswers {
  const chance = catalog.options[TRAP_CHANCE_OPTION_KEY]?.default;
  if (typeof chance !== "number")
    throw new Error("Missing trap chance default.");
  const weights = {} as TrapAnswers["weights"];
  for (const item of TRAP_ITEM_DEFINITIONS) {
    const value = catalog.options[item.optionKey]?.default;
    if (!TRAP_WEIGHTS.includes(value as TrapWeight)) {
      throw new Error(`Invalid default for '${item.optionKey}'.`);
    }
    weights[item.id] = value as TrapWeight;
  }
  return { chance, weights };
}
