# dx_grid

Heavy-compute kernels for BlazorDX, compiled to `wasm32-unknown-unknown` and
loaded as a sidecar module beside the .NET WASM runtime. Five kernels are
exported over a small pointer/length C-ABI (no wasm-bindgen):

- `sort_indices` — index permutation that orders an `f64` column
- `filter_indices_gte` — indices of rows `>= threshold`
- `aggregate` — `[count, sum, min, max, mean]` in one pass
- `histogram` — equal-width bin counts
- `downsample_lttb` — Largest-Triangle-Three-Buckets series reduction

A managed C# twin lives in `src/BlazorDX.Compute` and is the graceful fallback
when cargo/Rust is unavailable.

## Builds: size (default) vs speed (opt-in)

The crate ships a **size-tuned** wasm by default. `Cargo.toml`'s
`[profile.release]` is tuned for size (`opt-level = "z"`, `lto`, `panic=abort`,
`codegen-units=1`) because the module is downloaded by the browser.

An **opt-in speed build** is available via the custom `release-speed` cargo
profile (`opt-level = 3`, inherits `release`) plus wasm SIMD
(`-C target-feature=+simd128`). Select it from MSBuild:

```
dotnet build src/BlazorDX.Interop/BlazorDX.Interop.csproj -p:RustSpeed=true
```

When `RustSpeed` is unset the cargo invocation, profile, and RUSTFLAGS are
exactly the historical ones, so the shipped `.wasm` is **byte-for-byte** the
size build. The wiring lives in `build/Rust.targets`.

Build the speed wasm directly:

```
RUSTFLAGS="-C target-feature=+simd128" \
  cargo build --profile release-speed --target wasm32-unknown-unknown
```

## Phase 0 measurements

### wasm binary size (the real, measured cost)

`wasm32-unknown-unknown`, release vs release-speed+simd128:

| build                         | dx_grid.wasm | delta            |
| ----------------------------- | ------------ | ---------------- |
| size (`release`, opt-level z) | 24,754 bytes | —                |
| speed (`release-speed` + SIMD)| 28,637 bytes | +3,883 (+15.7 %) |

### host benchmark deltas (Criterion, native — see caveat)

Run with `cargo bench` (see `benches/kernels.rs`), ~100k `f64` elements. Median
times below were captured with the bench compiled at `opt-level=z` vs
`opt-level=3` to isolate the optimization-level delta:

| kernel             | opt-level z | opt-level 3 | speedup |
| ------------------ | ----------- | ----------- | ------- |
| sort_indices       | 21.18 ms    | 11.43 ms    | ~1.85x  |
| filter_indices_gte | 369.3 µs    | 121.2 µs    | ~3.0x   |
| aggregate          | 219.1 µs    | 153.8 µs    | ~1.4x   |
| histogram          | 403.1 µs    | 444.3 µs    | ~1.0x (within noise) |
| downsample_lttb    | 341.9 µs    | 364.9 µs    | ~1.0x (within noise) |

(Absolute numbers are machine-dependent; the ratios are the takeaway.
`sort_indices` and `filter_indices_gte` benefit most from higher opt-level;
`histogram`/`downsample_lttb` are effectively flat at this size.)

### Caveat: Criterion is NATIVE, not wasm SIMD

Criterion runs on the **host** target (x86_64), so the numbers above measure the
**opt-level / algorithm delta only** — they do **NOT** reflect the wasm
`simd128` runtime speedup. No wasm-SIMD runtime gain is claimed here.

True in-browser wasm-SIMD runtime validation is a **follow-up Playwright / E2E
item** and is intentionally **not done in Phase 0**.
