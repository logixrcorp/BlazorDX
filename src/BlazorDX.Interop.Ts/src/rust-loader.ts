// Loads and caches the `dx_grid` Rust wasm module. Loading is asynchronous
// (a network fetch + instantiate), but once cached the exports are available
// synchronously via `gridWasm()`. The .NET side awaits `ensureGridWasm()` once,
// then calls the synchronous kernels — this keeps the [JSImport] signatures off
// the unsupported "Promise of array" marshalling path.

export interface GridWasmExports {
  memory: WebAssembly.Memory;
  alloc(byteLength: number): number;
  dealloc(pointer: number, byteLength: number): void;
  sort_indices_f64(
    valuesPointer: number,
    length: number,
    descending: number,
    outPointer: number,
  ): void;
  filter_indices_gte_f64(
    valuesPointer: number,
    length: number,
    threshold: number,
    outPointer: number,
  ): number;
  aggregate_f64(valuesPointer: number, length: number, outPointer: number): void;
  histogram_f64(
    valuesPointer: number,
    length: number,
    bins: number,
    min: number,
    max: number,
    outPointer: number,
  ): number;
  downsample_lttb_f64(
    xPointer: number,
    yPointer: number,
    length: number,
    threshold: number,
    outPointer: number,
  ): number;
}

let cached: GridWasmExports | null = null;
let loading: Promise<GridWasmExports> | null = null;

function instantiate(): Promise<GridWasmExports> {
  const wasmUrl = new URL("./dx_grid.wasm", import.meta.url);
  return WebAssembly.instantiateStreaming(fetch(wasmUrl), {})
    .catch(async () => {
      // instantiateStreaming fails if the server's MIME type is not
      // application/wasm; fall back to a plain fetch + instantiate.
      const bytes = await fetch(wasmUrl).then((response) => response.arrayBuffer());
      return WebAssembly.instantiate(bytes, {});
    })
    .then((result) => result.instance.exports as unknown as GridWasmExports);
}

// Awaited once by the host before any kernel call.
export async function ensureGridWasm(): Promise<void> {
  if (cached !== null) {
    return;
  }
  if (loading === null) {
    loading = instantiate();
  }
  cached = await loading;
}

// Synchronous accessor used by the kernels after loading has completed.
export function gridWasm(): GridWasmExports {
  if (cached === null) {
    throw new Error("dx_grid wasm is not loaded yet; await ensureGridWasm() first.");
  }
  return cached;
}

// --- dx_security (BlazorDX.Security.Rust) -----------------------------------
// Same load-once/cache pattern as dx_grid above, for the zero-trust ephemeral
// chat crypto core. Kept in this file because it is the same Rust-wasm loading
// convention, not because the two modules are related -- ephemeral-chat.ts is
// the only consumer of the exports below.

export interface SecurityWasmExports {
  memory: WebAssembly.Memory;
  alloc(byteLength: number): number;
  dealloc(pointer: number, byteLength: number): void;
  begin_session(
    sessionIdPointer: number,
    sessionIdLength: number,
    seedPointer: number,
    outPublicKeyPointer: number,
  ): number;
  complete_session(
    sessionIdPointer: number,
    sessionIdLength: number,
    serverPublicKeyPointer: number,
    serverPublicKeyLength: number,
  ): number;
  decrypt_payload(
    sessionIdPointer: number,
    sessionIdLength: number,
    noncePointer: number,
    ciphertextPointer: number,
    ciphertextLength: number,
    outLengthPointer: number,
  ): number;
  clear_payload(pointer: number, byteLength: number): void;
  end_session(sessionIdPointer: number, sessionIdLength: number): void;
}

let securityCached: SecurityWasmExports | null = null;
let securityLoading: Promise<SecurityWasmExports> | null = null;

function instantiateSecurity(): Promise<SecurityWasmExports> {
  const wasmUrl = new URL("./dx_security.wasm", import.meta.url);
  return WebAssembly.instantiateStreaming(fetch(wasmUrl), {})
    .catch(async () => {
      // Same MIME-type fallback as instantiate() above.
      const bytes = await fetch(wasmUrl).then((response) => response.arrayBuffer());
      return WebAssembly.instantiate(bytes, {});
    })
    .then((result) => result.instance.exports as unknown as SecurityWasmExports);
}

// Awaited once by the host before any session/decrypt call.
export async function ensureSecurityWasm(): Promise<void> {
  if (securityCached !== null) {
    return;
  }
  if (securityLoading === null) {
    securityLoading = instantiateSecurity();
  }
  securityCached = await securityLoading;
}

// Synchronous accessor used after loading has completed.
export function securityWasm(): SecurityWasmExports {
  if (securityCached === null) {
    throw new Error("dx_security wasm is not loaded yet; await ensureSecurityWasm() first.");
  }
  return securityCached;
}
