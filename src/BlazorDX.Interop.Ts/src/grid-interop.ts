// The bridge the .NET side calls via [JSImport]. `ensureLoaded` is the single
// asynchronous step; the kernels are synchronous and assume the wasm is cached.
// Each kernel copies a column of values into the Rust wasm heap, runs the
// algorithm, and copies the resulting row-index buffer back out. Only indices
// cross back, never row data.

import { ensureGridWasm, gridWasm, type GridWasmExports } from "./rust-loader";

const F64_BYTES = 8;
const U32_BYTES = 4;

// Awaited once by the host before any sort/filter call.
export function ensureLoaded(): Promise<void> {
  return ensureGridWasm();
}

// Writes `values` into a freshly allocated f64 buffer and returns its pointer.
// The caller owns the buffer and must free it.
function writeValues(wasm: GridWasmExports, values: number[]): number {
  const pointer = wasm.alloc(values.length * F64_BYTES);
  new Float64Array(wasm.memory.buffer, pointer, values.length).set(values);
  return pointer;
}

export function sortIndices(values: number[], descending: boolean): number[] {
  const wasm = gridWasm();
  const length = values.length;
  const valuesPointer = writeValues(wasm, values);
  const outPointer = wasm.alloc(length * U32_BYTES);
  try {
    wasm.sort_indices_f64(valuesPointer, length, descending ? 1 : 0, outPointer);
    return Array.from(new Uint32Array(wasm.memory.buffer, outPointer, length));
  } finally {
    wasm.dealloc(valuesPointer, length * F64_BYTES);
    wasm.dealloc(outPointer, length * U32_BYTES);
  }
}

export function aggregate(values: number[]): number[] {
  const wasm = gridWasm();
  const length = values.length;
  const valuesPointer = writeValues(wasm, values);
  const outPointer = wasm.alloc(5 * F64_BYTES);
  try {
    wasm.aggregate_f64(valuesPointer, length, outPointer);
    // [count, sum, min, max, mean]
    return Array.from(new Float64Array(wasm.memory.buffer, outPointer, 5));
  } finally {
    wasm.dealloc(valuesPointer, length * F64_BYTES);
    wasm.dealloc(outPointer, 5 * F64_BYTES);
  }
}

export function histogram(
  values: number[],
  bins: number,
  min: number,
  max: number,
): number[] {
  const wasm = gridWasm();
  const length = values.length;
  const valuesPointer = writeValues(wasm, values);
  const outPointer = wasm.alloc(bins * U32_BYTES);
  try {
    const count = wasm.histogram_f64(valuesPointer, length, bins, min, max, outPointer);
    return Array.from(new Uint32Array(wasm.memory.buffer, outPointer, count));
  } finally {
    wasm.dealloc(valuesPointer, length * F64_BYTES);
    wasm.dealloc(outPointer, bins * U32_BYTES);
  }
}

export function downsampleLttb(xs: number[], ys: number[], threshold: number): number[] {
  const wasm = gridWasm();
  const length = xs.length;
  const xPointer = writeValues(wasm, xs);
  const yPointer = writeValues(wasm, ys);
  const outPointer = wasm.alloc(length * U32_BYTES);
  try {
    const count = wasm.downsample_lttb_f64(xPointer, yPointer, length, threshold, outPointer);
    return Array.from(new Uint32Array(wasm.memory.buffer, outPointer, count));
  } finally {
    wasm.dealloc(xPointer, length * F64_BYTES);
    wasm.dealloc(yPointer, length * F64_BYTES);
    wasm.dealloc(outPointer, length * U32_BYTES);
  }
}

export function filterIndicesGte(values: number[], threshold: number): number[] {
  const wasm = gridWasm();
  const length = values.length;
  const valuesPointer = writeValues(wasm, values);
  const outPointer = wasm.alloc(length * U32_BYTES);
  try {
    const matched = wasm.filter_indices_gte_f64(
      valuesPointer,
      length,
      threshold,
      outPointer,
    );
    return Array.from(new Uint32Array(wasm.memory.buffer, outPointer, matched));
  } finally {
    wasm.dealloc(valuesPointer, length * F64_BYTES);
    wasm.dealloc(outPointer, length * U32_BYTES);
  }
}
