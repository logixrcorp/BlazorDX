// Regression coverage for a real bug: a 404 (e.g. a stale deploy missing dx_grid.wasm) used to
// reach WebAssembly.instantiate with an HTML error-page body, failing with an opaque "expected
// magic word ... found <!DO" CompileError instead of a diagnosable message. This test only
// exercises the fetch-validation path (fetchWasmOrThrow/WasmFetchError) -- it stubs global fetch
// and never reaches a real WebAssembly.instantiate call, since a 404 must never get that far.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("rust-loader wasm fetch validation", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    vi.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    vi.unstubAllGlobals();
  });

  it("ensureGridWasm rejects with a clear, diagnosable error on a 404", async () => {
    global.fetch = vi.fn(async () =>
      new Response("<!DOCTYPE html><title>Not Found</title>", { status: 404, statusText: "Not Found" }),
    ) as unknown as typeof fetch;

    const { ensureGridWasm } = await import("../src/rust-loader");

    await expect(ensureGridWasm()).rejects.toThrow(/dx_grid\.wasm request failed: 404 Not Found/);
  });
});
