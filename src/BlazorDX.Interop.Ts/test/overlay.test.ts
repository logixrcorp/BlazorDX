// Regression coverage for a real bug: every overlay (Dialog, Sheet, CommandPalette,
// ContextMenu) used to register its own independent `document`-level Escape listener with no
// stacking concept, so nesting one overlay inside another (e.g. a context menu opened from a
// dialog) closed both on a single Escape press. Only the topmost (most-recently-opened,
// still-open) overlay should react.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("overlay Escape stacking", () => {
  beforeEach(() => {
    vi.resetModules();
    document.body.innerHTML = "";
  });

  afterEach(() => {
    document.body.innerHTML = "";
  });

  function addPanel(id: string): HTMLElement {
    const el = document.createElement("div");
    el.id = id;
    document.body.appendChild(el);
    return el;
  }

  it("Escape dismisses only the topmost of two nested overlays", async () => {
    const overlay = await import("../src/overlay");
    addPanel("outer");
    addPanel("inner");

    const outerDismiss = vi.fn();
    const innerDismiss = vi.fn();

    overlay.open("outer", "", false, false, true, false, outerDismiss);
    overlay.open("inner", "", false, false, true, false, innerDismiss);

    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));

    expect(innerDismiss).toHaveBeenCalledTimes(1);
    expect(outerDismiss).not.toHaveBeenCalled();
  });

  it("closing the topmost overlay lets Escape reach the next one down", async () => {
    const overlay = await import("../src/overlay");
    addPanel("outer");
    addPanel("inner");

    const outerDismiss = vi.fn();
    const innerDismiss = vi.fn();

    overlay.open("outer", "", false, false, true, false, outerDismiss);
    overlay.open("inner", "", false, false, true, false, innerDismiss);
    overlay.close("inner");

    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));

    expect(outerDismiss).toHaveBeenCalledTimes(1);
  });

  it("a topmost overlay opened with closeOnEsc:false swallows Escape instead of falling through", async () => {
    const overlay = await import("../src/overlay");
    addPanel("outer");
    addPanel("inner");

    const outerDismiss = vi.fn();
    const innerDismiss = vi.fn();

    overlay.open("outer", "", false, false, true, false, outerDismiss);
    overlay.open("inner", "", false, false, false, false, innerDismiss); // closeOnEsc: false

    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));

    expect(innerDismiss).not.toHaveBeenCalled();
    expect(outerDismiss).not.toHaveBeenCalled();
  });

  it("a single overlay still closes on Escape (no regression for the common case)", async () => {
    const overlay = await import("../src/overlay");
    addPanel("solo");

    const dismiss = vi.fn();
    overlay.open("solo", "", false, false, true, false, dismiss);

    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));

    expect(dismiss).toHaveBeenCalledTimes(1);
  });
});
