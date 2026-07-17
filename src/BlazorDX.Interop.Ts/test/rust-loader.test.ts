// Regression coverage for a real bug: a 404 (e.g. a stale deploy missing dx_security.wasm)
// used to reach WebAssembly.instantiate with an HTML error-page body, failing with an opaque
// "expected magic word ... found <!DO" CompileError instead of a diagnosable message. These
// tests only exercise the fetch-validation path (fetchWasmOrThrow/WasmFetchError) -- they stub
// global fetch and never reach a real WebAssembly.instantiate call, since a 404 must never get
// that far.

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

  it("ensureSecurityWasm rejects with a clear, diagnosable error on a 404 -- no opaque CompileError", async () => {
    global.fetch = vi.fn(async () =>
      new Response("<!DOCTYPE html><title>Not Found</title>", { status: 404, statusText: "Not Found" }),
    ) as unknown as typeof fetch;

    const { ensureSecurityWasm } = await import("../src/rust-loader");

    await expect(ensureSecurityWasm()).rejects.toThrow(/dx_security\.wasm request failed: 404 Not Found/);
  });

  it("ensureSecurityWasm only fetches once on a 404 -- never masks it with a second, doomed-to-fail request", async () => {
    const fetchMock = vi.fn(async () => new Response("", { status: 404, statusText: "Not Found" }));
    global.fetch = fetchMock as unknown as typeof fetch;

    const { ensureSecurityWasm } = await import("../src/rust-loader");

    await expect(ensureSecurityWasm()).rejects.toThrow();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("ensureGridWasm rejects with a clear, diagnosable error on a 404", async () => {
    global.fetch = vi.fn(async () =>
      new Response("<!DOCTYPE html><title>Not Found</title>", { status: 404, statusText: "Not Found" }),
    ) as unknown as typeof fetch;

    const { ensureGridWasm } = await import("../src/rust-loader");

    await expect(ensureGridWasm()).rejects.toThrow(/dx_grid\.wasm request failed: 404 Not Found/);
  });
});
