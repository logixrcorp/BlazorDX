# ADR 0011 — Speed-vs-size Rust build profile

**Status:** Accepted

## Context

The `dx_grid` Rust kernels compile to `wasm32-unknown-unknown` and are **downloaded
by the browser**, so `Cargo.toml`'s `[profile.release]` is tuned for size
(`opt-level = "z"`, `lto`, `panic = "abort"`, `codegen-units = 1`). The documents
track set "extremely fast at runtime" as the priority, which wants higher
optimization (opt-level 3) and wasm SIMD for the heavy parse/recalc kernels — but
those increase the wasm payload. Size and speed are in genuine tension, and the
right default differs per deployment.

## Decision

Keep the **default build size-tuned and byte-for-byte unchanged**. Add an **opt-in**
speed build, never the default:

- A custom `release-speed` cargo profile (`inherits = "release"`, `opt-level = 3`).
- wasm SIMD via `-C target-feature=+simd128`.
- Selected with `dotnet build … -p:RustSpeed=true`; `build/Rust.targets` sets
  `RUSTFLAGS` only on that path, cross-platform (`set` on Windows, `VAR=… cmd` on
  POSIX). When unset, the cargo invocation is identical to before this existed.
- The size/speed delta is **measured** (`benches/kernels.rs`, recorded in the crate
  README), with the explicit caveat that Criterion runs **native**, not wasm — so it
  reflects the opt-level delta, not the wasm-SIMD runtime gain.

## Consequences

- Consumers who need raw throughput opt in; everyone else keeps the small module.
- Measured: ~1.85×–3× on the hottest kernels (native opt-level delta); **+15.7%**
  wasm size for the speed build. CI builds both paths so neither rots.
- **Deferred:** true in-browser wasm-SIMD runtime validation (a Playwright/E2E item);
  no in-browser SIMD speedup is *claimed* until then.
