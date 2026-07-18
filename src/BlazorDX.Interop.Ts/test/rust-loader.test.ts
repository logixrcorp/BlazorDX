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

describe("rust-loader property access hardening (whitepaper §3.2)", () => {
  const originalFetch = global.fetch;
  const originalWebAssembly = global.WebAssembly;

  beforeEach(() => {
    vi.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    global.WebAssembly = originalWebAssembly;
    vi.unstubAllGlobals();
  });

  it("securityWasm() returns a frozen exports object -- page-realm code cannot reassign e.g. decrypt_payload", async () => {
    global.fetch = vi.fn(async () => new Response(new Uint8Array(), { status: 200 })) as unknown as typeof fetch;
    const fakeExports = {
      memory: new WebAssembly.Memory({ initial: 1 }),
      alloc: vi.fn(),
      dealloc: vi.fn(),
      begin_session: vi.fn(),
      complete_session: vi.fn(),
      decrypt_payload: vi.fn(),
      clear_payload: vi.fn(),
      end_session: vi.fn(),
      sign: vi.fn(),
      verify_signal: vi.fn(),
      verify_and_end_session: vi.fn(),
      end_with_receipt: vi.fn(),
    };
    global.WebAssembly = {
      ...originalWebAssembly,
      instantiateStreaming: vi.fn(async () => ({ instance: { exports: fakeExports }, module: {} }) as never),
    };

    const { ensureSecurityWasm, securityWasm } = await import("../src/rust-loader");
    await ensureSecurityWasm();
    const wasm = securityWasm();

    expect(Object.isFrozen(wasm)).toBe(true);
    // ES module code is always strict mode, so assigning to a frozen property throws (rather
    // than silently no-op-ing, as it would in sloppy mode).
    expect(() => {
      // @ts-expect-error -- intentionally assigning to a readonly export to prove it is rejected
      wasm.decrypt_payload = () => 0;
    }).toThrow(TypeError);
    expect(wasm.decrypt_payload).toBe(fakeExports.decrypt_payload); // unchanged
  });

  it("gridWasm() is NOT frozen -- hardening is scoped to the module that carries key material", async () => {
    global.fetch = vi.fn(async () => new Response(new Uint8Array(), { status: 200 })) as unknown as typeof fetch;
    const fakeGridExports = {
      memory: new WebAssembly.Memory({ initial: 1 }),
      alloc: vi.fn(),
      dealloc: vi.fn(),
      sort_indices_f64: vi.fn(),
      filter_indices_gte_f64: vi.fn(),
      aggregate_f64: vi.fn(),
      histogram_f64: vi.fn(),
      downsample_lttb_f64: vi.fn(),
    };
    global.WebAssembly = {
      ...originalWebAssembly,
      instantiateStreaming: vi.fn(async () => ({ instance: { exports: fakeGridExports }, module: {} }) as never),
    };

    const { ensureGridWasm, gridWasm } = await import("../src/rust-loader");
    await ensureGridWasm();

    expect(Object.isFrozen(gridWasm())).toBe(false);
  });
});
