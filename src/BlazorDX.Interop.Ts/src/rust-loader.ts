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

// Marks an error as "the fetch itself failed" (bad HTTP status) so the instantiateStreaming
// fallback below can tell that apart from "the MIME type just wasn't application/wasm" and
// re-throw instead of silently retrying a request that will only 404 again.
class WasmFetchError extends Error {}

async function fetchWasmOrThrow(url: URL): Promise<Response> {
  const response = await fetch(url);
  if (!response.ok) {
    // A 404/500/etc. response body is very often an HTML error page, which
    // WebAssembly.instantiate would otherwise fail to parse with an opaque
    // "expected magic word" CompileError -- this is the actual, diagnosable cause.
    throw new WasmFetchError(`${url.pathname} request failed: ${response.status} ${response.statusText}`);
  }
  return response;
}

function instantiate(): Promise<GridWasmExports> {
  const wasmUrl = new URL("./dx_grid.wasm", import.meta.url);
  return fetchWasmOrThrow(wasmUrl)
    .then((response) => WebAssembly.instantiateStreaming(response, {}))
    .catch(async (err) => {
      if (err instanceof WasmFetchError) {
        throw err;
      }
      // instantiateStreaming fails if the server's MIME type is not
      // application/wasm; fall back to a plain fetch + instantiate.
      const bytes = await fetchWasmOrThrow(wasmUrl).then((response) => response.arrayBuffer());
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
