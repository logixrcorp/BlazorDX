# ADR 0003 — Rust + WASM for heavy compute

**Status:** Accepted

## Context

Blazor WebAssembly runs single-threaded on the UI thread. Heavy work (sorting or
filtering tens of thousands of rows) done in managed C# on that thread freezes
the DOM. The .NET WASM runtime is also not the fastest option for tight numeric
loops.

## Decision

Write CPU-heavy algorithms in Rust, compiled to `wasm32-unknown-unknown`, loaded
as a sidecar module beside the .NET WASM runtime. The first such module is
`dx_grid` (sort/filter). C# calls it through `BlazorDX.Compute`, which marshals
only compact `int[]` row-index permutations across the boundary, never row
objects.

`BlazorDX.Compute` also ships a **managed C# fallback** for every routine, so the
grid works in static SSR and Interactive Server where the Rust module is absent.

## Consequences

- Near-native speed for the heaviest operations, off the slowest path.
- A second toolchain (cargo) is required; the build degrades gracefully without it.
- Marshaling is index-only, keeping the boundary cost flat regardless of row size.
