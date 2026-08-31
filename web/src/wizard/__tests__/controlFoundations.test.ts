/**
 * @file Protects the shared foundations of the declarative control renderer.
 *
 * These tests cover the immutable dot-path answer accessors and the Nuxt UI event
 * adapters. Both are consumed by every generically rendered question, so failures
 * here would silently corrupt answers across all wizard sections.
 */

import { describe, expect, it } from "vitest";
import { getAnswerAtPath, setAnswerAtPath } from "../answerPath";
import {
  narrowBooleanValue,
  narrowChoiceValue,
  narrowIntegerValue,
} from "../controlAdapters";

/** Representative nested section shaped like the Checks & Rewards answers. */
function createSampleSection() {
  return {
    seeded: true,
    checks: { neowSanity: false, includeFloorChecks: true },
    shop: { cardSlots: 3 },
  };
}

/** Verifies both direct and nested reads resolve their stored values. */
function readsValuesThroughPaths(): void {
  const section = createSampleSection();

  expect(getAnswerAtPath(section, "seeded")).toBe(true);
  expect(getAnswerAtPath(section, "checks.neowSanity")).toBe(false);
  expect(getAnswerAtPath(section, "shop.cardSlots")).toBe(3);
}

/** Verifies unknown segments fail loudly instead of returning undefined. */
function rejectsUnknownPaths(): void {
  const section = createSampleSection();

  expect(() => getAnswerAtPath(section, "missing")).toThrow("missing");
  expect(() => getAnswerAtPath(section, "checks.unknownField")).toThrow(
    "checks.unknownField",
  );
  expect(() => setAnswerAtPath(section, "missing", 1)).toThrow("missing");
  expect(() => setAnswerAtPath(section, "seeded.nested", 1)).toThrow(
    "seeded.nested",
  );
}

/** Verifies writes copy only the objects along the written path. */
function writesImmutablyAlongThePath(): void {
  const section = createSampleSection();
  const next = setAnswerAtPath(section, "checks.neowSanity", true);

  // The original object and its nested sections must remain untouched.
  expect(section.checks.neowSanity).toBe(false);
  expect(next.checks.neowSanity).toBe(true);
  expect(next).not.toBe(section);
  expect(next.checks).not.toBe(section.checks);

  // Sibling sections keep their identity so Vue re-renders stay minimal.
  expect(next.shop).toBe(section.shop);
}

/** Verifies boolean narrowing ignores Nuxt UI's indeterminate state. */
function narrowsBooleanEvents(): void {
  expect(narrowBooleanValue(true)).toBe(true);
  expect(narrowBooleanValue(false)).toBe(false);
  expect(narrowBooleanValue("indeterminate")).toBeNull();
  expect(narrowBooleanValue(undefined)).toBeNull();
}

/** Verifies choice narrowing only admits declared descriptor values. */
function narrowsChoiceEvents(): void {
  const allowed = ["full", "minimal"];

  expect(narrowChoiceValue("full", allowed)).toBe("full");
  expect(narrowChoiceValue("unknown", allowed)).toBeNull();
  expect(narrowChoiceValue(3, allowed)).toBeNull();
  expect(narrowChoiceValue(undefined, allowed)).toBeNull();
}

/** Verifies integer narrowing enforces wholeness and inclusive bounds. */
function narrowsIntegerEvents(): void {
  const range = { minimum: 1, maximum: 10 };

  expect(narrowIntegerValue(1, range)).toBe(1);
  expect(narrowIntegerValue(10, range)).toBe(10);
  expect(narrowIntegerValue(0, range)).toBeNull();
  expect(narrowIntegerValue(11, range)).toBeNull();
  expect(narrowIntegerValue(2.5, range)).toBeNull();

  // Sliders share a scalar-or-array API and inputs may report empty states.
  expect(narrowIntegerValue([3], range)).toBeNull();
  expect(narrowIntegerValue(null, range)).toBeNull();
  expect(narrowIntegerValue(undefined, range)).toBeNull();
}

describe("answerPath", () => {
  it("reads values through paths", readsValuesThroughPaths);
  it("rejects unknown paths", rejectsUnknownPaths);
  it("writes immutably along the path", writesImmutablyAlongThePath);
});

describe("controlAdapters", () => {
  it("narrows boolean events", narrowsBooleanEvents);
  it("narrows choice events", narrowsChoiceEvents);
  it("narrows integer events", narrowsIntegerEvents);
});
