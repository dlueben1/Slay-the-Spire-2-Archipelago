/**
 * @file Declares the control descriptors that make wizard questions renderable data.
 *
 * A question that only needs a standard control — radio group, checkbox card,
 * checkbox grid, number input, number grid, or value slider — declares one of these
 * descriptors in `WizardStep.ts` and is rendered generically by
 * `components/wizard/core/WizardControl.vue`. Questions without a descriptor remain
 * bespoke: their step component renders custom markup (portrait grids, relic tables,
 * Ascension editors) while still sourcing copy from the shared question definition.
 *
 * Descriptors bind to answers through section-relative `field` paths and stay free of
 * Archipelago option keys; section compilers remain the only translation layer.
 */

import type { GeneratedNumberRange } from "./WizardOptionKey";
import type { WizardAnswers } from "./WizardAnswers";

/** Every scalar value a generic control can write into the answer model. */
export type WizardControlValue = boolean | number | string;

/**
 * Pure cross-field rule invoked instead of the default single-field write.
 *
 * @remarks Transitions modules (for example `deathLinkTransitions.ts`) export these
 * wrappers around strongly typed functions. The descriptor binds a transition to the
 * section owned by its step, and descriptor integrity tests exercise that binding.
 */
export type WizardSectionTransition = (
  section: object,
  value: WizardControlValue,
) => object;

/** Numeric bounds resolved against current answers just before rendering. */
export type WizardControlRange = (
  answers: WizardAnswers,
) => GeneratedNumberRange;

/** One selectable option shown by a radio control. */
export interface WizardControlChoice {
  value: string;
  label: string;
  description: string;
}

/** One boolean card inside a checkbox-grid control. */
export interface WizardCheckboxGridItem {
  field: string;
  label: string;
  description: string;
  /** Grays the card out while its prerequisite answers are unmet. */
  isEnabled?: (answers: WizardAnswers) => boolean;
  /** Cross-field rule for this card only, such as clearing dependent toggles. */
  applyChange?: WizardSectionTransition;
}

/** One labeled numeric field inside a number-grid control. */
export interface WizardNumberGridField {
  field: string;
  label: string;
  range: WizardControlRange;
}

/** One named shortcut value rendered as a button under a slider. */
export interface WizardSliderPreset {
  label: string;
  value: number;
}

/** A table-variant radio group bound to one string answer field. */
export interface WizardRadioControl {
  kind: "radio";
  field: string;
  choices: readonly WizardControlChoice[];
  applyChange?: WizardSectionTransition;
}

/** A single card-variant checkbox bound to one boolean answer field. */
export interface WizardCheckboxControl {
  kind: "checkbox";
  field: string;
  label: string;
  description: string;
  applyChange?: WizardSectionTransition;
}

/** A collection of related checkbox cards bound to sibling boolean fields. */
export interface WizardCheckboxGridControl {
  kind: "checkbox-grid";
  items: readonly WizardCheckboxGridItem[];
  /** `grid` uses the shared two-column layout; `stack` is one vertical list. */
  layout?: "grid" | "stack";
}

/** A single bounded integer input bound to one numeric answer field. */
export interface WizardNumberControl {
  kind: "number";
  field: string;
  range: WizardControlRange;
}

/** A grid of labeled bounded integer inputs bound to sibling numeric fields. */
export interface WizardNumberGridControl {
  kind: "number-grid";
  fields: readonly WizardNumberGridField[];
}

/** A whole-number slider with a live value readout and optional presets. */
export interface WizardSliderControl {
  kind: "slider";
  field: string;
  range: WizardControlRange;
  /** Named shortcuts; their presence also switches to the badge-header layout. */
  presets?: readonly WizardSliderPreset[];
  /** Suffix such as `%` appended to the value readout. */
  unit?: string;
  ariaLabel: string;
}

/** Discriminated union of every generically renderable control. */
export type WizardControlDescriptor =
  | WizardRadioControl
  | WizardCheckboxControl
  | WizardCheckboxGridControl
  | WizardNumberControl
  | WizardNumberGridControl
  | WizardSliderControl;
