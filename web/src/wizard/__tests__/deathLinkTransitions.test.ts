/**
 * @file Protects the pure Death Link cross-field rules.
 *
 * The step's two invariants — an enabled Death Link always keeps one received effect
 * selected, and Die is mutually exclusive with both nonlethal effects — used to live
 * in the Vue component's setters. These tests exercise the extracted transitions
 * directly so the rules stay verifiable without mounting components.
 */

import { describe, expect, it } from "vitest";
import {
  setBeKilled,
  setDeathLinkEnabled,
  setReceiveDamage,
  setReceiveFragment,
} from "../deathLinkTransitions";
import type { DeathLinkAnswers } from "../WizardAnswers";

/** Baseline answers matching the wizard's disabled Death Link defaults. */
function createAnswers(patch?: Partial<DeathLinkAnswers>): DeathLinkAnswers {
  return {
    enabled: false,
    receiveFragment: false,
    receiveDamage: false,
    damagePercent: 25,
    beKilled: false,
    ...patch,
  };
}

/** Verifies enabling with no selected effect auto-selects the fragment default. */
function enablingSelectsDefaultEffect(): void {
  const next = setDeathLinkEnabled(createAnswers(), true);

  expect(next.enabled).toBe(true);
  expect(next.receiveFragment).toBe(true);
}

/** Verifies enabling and disabling preserve an existing effect selection. */
function togglingEnabledPreservesEffects(): void {
  const configured = createAnswers({ receiveDamage: true });

  const enabled = setDeathLinkEnabled(configured, true);
  expect(enabled.receiveDamage).toBe(true);
  expect(enabled.receiveFragment).toBe(false);

  // Disabling stores preferences losslessly for later re-enabling.
  const disabled = setDeathLinkEnabled(enabled, false);
  expect(disabled.enabled).toBe(false);
  expect(disabled.receiveDamage).toBe(true);
}

/** Verifies the last selected received-link effect can never be cleared. */
function keepsAtLeastOneEffectSelected(): void {
  const fragmentOnly = createAnswers({ enabled: true, receiveFragment: true });
  expect(setReceiveFragment(fragmentOnly, false)).toBe(fragmentOnly);

  const damageOnly = createAnswers({ enabled: true, receiveDamage: true });
  expect(setReceiveDamage(damageOnly, false)).toBe(damageOnly);

  // With both nonlethal effects active, either one may still be cleared.
  const both = createAnswers({
    enabled: true,
    receiveFragment: true,
    receiveDamage: true,
  });
  expect(setReceiveFragment(both, false).receiveFragment).toBe(false);
  expect(setReceiveDamage(both, false).receiveDamage).toBe(false);
}

/** Verifies Die replaces the nonlethal effects and restores a default when off. */
function dieIsMutuallyExclusive(): void {
  const both = createAnswers({
    enabled: true,
    receiveFragment: true,
    receiveDamage: true,
  });

  const lethal = setBeKilled(both, true);
  expect(lethal.beKilled).toBe(true);
  expect(lethal.receiveFragment).toBe(false);
  expect(lethal.receiveDamage).toBe(false);

  // Turning Die off must leave a valid selection rather than none.
  const restored = setBeKilled(lethal, false);
  expect(restored.beKilled).toBe(false);
  expect(restored.receiveFragment).toBe(true);
}

/** Verifies selecting a nonlethal effect replaces an active Die mode. */
function nonlethalEffectsReplaceDie(): void {
  const lethal = createAnswers({ enabled: true, beKilled: true });

  const fragment = setReceiveFragment(lethal, true);
  expect(fragment.beKilled).toBe(false);
  expect(fragment.receiveFragment).toBe(true);

  const damage = setReceiveDamage(lethal, true);
  expect(damage.beKilled).toBe(false);
  expect(damage.receiveDamage).toBe(true);

  // Clearing a nonlethal effect while Die is active is ignored as meaningless.
  expect(setReceiveFragment(lethal, false)).toBe(lethal);
  expect(setReceiveDamage(lethal, false)).toBe(lethal);
}

describe("deathLinkTransitions", () => {
  it("enabling selects a default effect", enablingSelectsDefaultEffect);
  it("toggling enabled preserves effects", togglingEnabledPreservesEffects);
  it("keeps at least one effect selected", keepsAtLeastOneEffectSelected);
  it("die is mutually exclusive", dieIsMutuallyExclusive);
  it("nonlethal effects replace die", nonlethalEffectsReplaceDie);
});
