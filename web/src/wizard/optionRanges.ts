/**
 * @file Resolves semantic range names to generated numeric bounds.
 *
 * Control descriptors in `WizardStep.ts` reference ranges by semantic name only, so
 * that file keeps its contract of never touching Archipelago option keys. This module
 * owns the name-to-key mapping (mirroring how `FillerItem.ts` maps semantic filler
 * IDs) and reads authoritative bounds from the generated catalog on demand, which
 * also removes the range prop drilling previously done by `WizardView.vue`.
 */

import { optionCatalog } from "../generated/optionCatalog";
import type { WizardControlRange } from "./QuestionControl";
import {
  DEATH_LINK_OPTION_KEYS,
  getGeneratedNumberRange,
  PROGRESSION_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_OPTION_KEYS,
} from "./WizardOptionKey";

/** Semantic name to generated option key for every range-backed guided control. */
const WIZARD_RANGE_SOURCES = {
  relicRewardsAvailableAnytime: RUN_OPTION_KEYS.relicRewardsAvailableAnytime,
  progressionBalancing: PROGRESSION_OPTION_KEYS.progressionBalancing,
  deathLinkDamagePercent: DEATH_LINK_OPTION_KEYS.damagePercent,
  shopCardSlots: SHOP_OPTION_KEYS.cardSlots,
  shopNeutralCardSlots: SHOP_OPTION_KEYS.neutralCardSlots,
  shopRelicSlots: SHOP_OPTION_KEYS.relicSlots,
  shopPotionSlots: SHOP_OPTION_KEYS.potionSlots,
} as const;

/** Every semantic range name resolvable by `generatedRange`. */
export type WizardRangeName = keyof typeof WIZARD_RANGE_SOURCES;

/** Optional presentation bounds layered inside the generated schema bounds. */
export interface WizardRangeClamp {
  minimum?: number;
  maximum?: number;
}

/**
 * Builds a control range resolver backed by one generated option.
 *
 * @param name - Semantic range name owned by this module's source mapping.
 * @param clamp - Optional UX bounds applied inside the generated schema bounds,
 * such as Death Link damage presenting 1-100 even if the schema allowed 0.
 * @returns A resolver matching the descriptor contract; it ignores current answers
 * because generated bounds are static.
 * @throws When resolved while the generated option lacks numeric bounds.
 */
export function generatedRange(
  name: WizardRangeName,
  clamp?: WizardRangeClamp,
): WizardControlRange {
  return () => {
    // Read lazily so module import order cannot observe a partially built catalog.
    const range = getGeneratedNumberRange(
      optionCatalog,
      WIZARD_RANGE_SOURCES[name],
    );

    // Clamps tighten presentation only; schema validation still owns the real bounds.
    return {
      minimum:
        clamp?.minimum !== undefined
          ? Math.max(clamp.minimum, range.minimum)
          : range.minimum,
      maximum:
        clamp?.maximum !== undefined
          ? Math.min(clamp.maximum, range.maximum)
          : range.maximum,
    };
  };
}
