// Regression coverage for a real bug: the global hotkey registry matched purely on
// key/ctrlKey/altKey/shiftKey with no check of what actually had focus, so a bound combo (e.g.
// a plain letter, a common command-palette convention) hijacked that keystroke everywhere -
// including while the user was typing into a text input, textarea, or contenteditable region.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("hotkeys text-entry exemption", () => {
  beforeEach(() => {
    vi.resetModules();
    document.body.innerHTML = "";
  });

  afterEach(() => {
    document.body.innerHTML = "";
  });

  it("does not fire while an <input> has focus", async () => {
    const hotkeys = await import("../src/hotkeys");
    const input = document.createElement("input");
    document.body.appendChild(input);

    const onMatch = vi.fn();
    hotkeys.subscribe(onMatch);
    hotkeys.setBindings(["k"]);

    input.dispatchEvent(new KeyboardEvent("keydown", { key: "k", bubbles: true }));

    expect(onMatch).not.toHaveBeenCalled();
  });

  it("does not fire while a <textarea> has focus", async () => {
    const hotkeys = await import("../src/hotkeys");
    const textarea = document.createElement("textarea");
    document.body.appendChild(textarea);

    const onMatch = vi.fn();
    hotkeys.subscribe(onMatch);
    hotkeys.setBindings(["k"]);

    textarea.dispatchEvent(new KeyboardEvent("keydown", { key: "k", bubbles: true }));

    expect(onMatch).not.toHaveBeenCalled();
  });

  it("does not fire while a contenteditable element has focus", async () => {
    const hotkeys = await import("../src/hotkeys");
    const editable = document.createElement("div");
    editable.contentEditable = "true";
    // jsdom does not implement the isContentEditable computed getter (it's always undefined
    // regardless of the contentEditable attribute) - stub it so this test exercises the same
    // branch a real browser would take, rather than skipping coverage of it entirely.
    Object.defineProperty(editable, "isContentEditable", { value: true });
    document.body.appendChild(editable);

    const onMatch = vi.fn();
    hotkeys.subscribe(onMatch);
    hotkeys.setBindings(["k"]);

    editable.dispatchEvent(new KeyboardEvent("keydown", { key: "k", bubbles: true }));

    expect(onMatch).not.toHaveBeenCalled();
  });

  it("still fires for a plain, non-text-entry target (no regression)", async () => {
    const hotkeys = await import("../src/hotkeys");
    const button = document.createElement("button");
    document.body.appendChild(button);

    const onMatch = vi.fn();
    hotkeys.subscribe(onMatch);
    hotkeys.setBindings(["k"]);

    button.dispatchEvent(new KeyboardEvent("keydown", { key: "k", bubbles: true }));

    expect(onMatch).toHaveBeenCalledWith("k");
  });
});
