# BlazorDX Architecture

This document is the authoritative blueprint. Each major decision also has a
short Architecture Decision Record under [docs/adr](docs/adr); this file ties
them together into one picture.

## 1. The problem we are solving

Blazor in .NET 10 is a capable runtime, but its component ecosystem is dominated
by monolithic suites that share three structural weaknesses:

1. **They crash under AOT/trimming** because they lean on runtime reflection for
   data binding and serialization.
2. **They leak state on the server** because UI state is commonly parked in
   Singleton services shared across every connected user.
3. **They are hard to style** because their CSS is baked deep into the component
   and resists bespoke design.

BlazorDX treats all three as architectural constraints to be designed out, not
documentation footnotes to warn about.

## 2. Two-tier headless model

```
┌─────────────────────────────────────────────────────────┐
│ Tier 2 — BlazorDX.Components  (styled, themeable)         │
│   DxDataGrid, ...   CSS variables, Tailwind-friendly      │
├─────────────────────────────────────────────────────────┤
│ Tier 1 — BlazorDX.Primitives  (headless behavior + a11y)  │
│   DataGridPrimitive, PresenceBoundary                     │
│   state · keyboard nav · focus · WAI-ARIA · NO css        │
└─────────────────────────────────────────────────────────┘
```

A team that wants total design control consumes Tier 1 and styles it themselves.
A team that wants batteries-included consumes Tier 2. Neither path fights the
other. See [ADR 0001](docs/adr/0001-two-tier-headless.md).

## 3. Language boundaries

BlazorDX restricts itself to the fastest primitives the browser offers and uses
each language only for what it is best at:

| Language | Compiles to | Responsibility |
| --- | --- | --- |
| **C#** | IL / WASM (AOT) | Render tree, diffing, component state, the public API. |
| **Rust** | `wasm32-unknown-unknown` | CPU-heavy work in the `dx_grid` crate: `sort_indices`, `filter_indices_gte`, `aggregate` (count/sum/min/max/mean), `histogram` (binning), `downsample_lttb`. Runs at near-native speed beside the .NET WASM runtime; each kernel has a managed C# twin. |
| **TypeScript** | minified ESM | The thin DOM bridge WASM cannot do itself: viewport metrics, `ResizeObserver`, focus trap, click-outside, loading the Rust module. |

The C# ↔ JS boundary uses **`[JSImport]` / `[JSExport]`**
(`System.Runtime.InteropServices.JavaScript`) exclusively — compile-time,
AOT-compatible bindings, never the reflection-based `IJSRuntime` path.
See [ADR 0003](docs/adr/0003-rust-wasm-heavy-compute.md).

### Data flow for a DataGrid sort

```
DxDataGrid (Tier 2)
  -> DataGridPrimitive raises "sort column 3 ascending" (Tier 1)
     -> BlazorDX.Compute.SortAsync(columnBuffer)
        -> [JSImport] -> rust-loader.ts -> dx_grid.wasm  (returns int[] row order)
        -> (managed C# fallback if wasm/JS unavailable, e.g. static SSR)
     <- new row-index permutation
  <- grid re-renders only the visible window via RenderTreeBuilder
```

Only a compact `int[]` row-index permutation crosses the boundary, never the row
objects themselves — this keeps marshaling cheap.

## 4. Zero-reflection policy

Reflection is the root cause of "compiles fine, crashes in the browser." BlazorDX
forbids it for anything on a hot or trimmable path:

- **JSON**: `System.Text.Json` source generators (`JsonSerializerContext`).
- **Grid binding**: `BlazorDX.SourceGen` reads `[GridColumn]` attributes on a row
  type and emits a strongly-typed `IGridRowAccessor<TRow>` at build time — typed
  `switch` expressions for reading cell text/values **and** writing them back
  (inline edit parses through the generated `SetCellText`). No `PropertyInfo`
  anywhere. The same accessor serves the flat grid, the tree grid, and the pivot.
- **Interop**: `[JSImport]`/`[JSExport]` generate their bindings at compile time.

Every library is marked `IsTrimmable` and `IsAotCompatible`, so the trimmer
analyzes our code and the build fails on a trim warning rather than the user's
browser failing at runtime. See [ADR 0002](docs/adr/0002-zero-reflection-source-generation.md).

## 5. Rendering strategy

- **`RenderTreeBuilder` with static sequence numbers** for repetitive nodes
  (grid cells/rows), avoiding a C# component object per cell and preserving
  Blazor's linear-time diff. Sequence numbers are hardcoded constants, never
  generated in a loop.
- **Immutable parameters + explicit `ShouldRender`** so unchanged subtrees are
  skipped automatically.
- **Virtualization**: only the rows intersecting the viewport are in the DOM. The
  TypeScript DOM bridge (`grid-dom.ts`) reports scroll position (throttled via
  `requestAnimationFrame`, with a `setTimeout` fallback for background tabs where
  rAF never fires), and the primitive recomputes the visible window. The same
  windowing is extracted into `DxVirtualize<T>` and reused by the tree grid.

## 6. Security model

Security is part of the component lifecycle, not a deployment afterthought.
See [ADR 0007](docs/adr/0007-security-baseline.md).

- **Component-scoped state only.** UI state is never stored in a Singleton.
  `BlazorDX.Security` provides scoped state helpers, and the analyzer flags
  Singleton-of-state registrations as a build error to prevent the classic
  Blazor Server cross-user data leak.
- **Race-condition mitigation.** `ISafeAction` wraps async work in a
  cancellation token; dispatching a newer action cancels the pending one, so a
  slow first response can never overwrite the UI with stale data.
- **XSS prevention.** Components render through Blazor's auto-encoded render tree.
  `MarkupString` (raw HTML) is banned by the analyzer unless the content passes
  through the configurable strict sanitizer in `BlazorDX.Security`.

## 7. Static-SSR + HTMX tier

`BlazorDX.Htmx` provides a hypermedia tier for forms and progressive enhancement.
Its `DxHtmxForm` renders as static server HTML enhanced with HTMX attributes —
no SignalR circuit, no WASM payload — for the parts of an app (sign-in, settings
forms) that should be fast and resilient without interactivity cost; the demo's
`/htmx/echo` endpoint shows the swap round-trip. Interactive tiers remain pure
Blazor. The broader form suite is the main area still to be filled out.
See [ADR 0004](docs/adr/0004-htmx-static-ssr-tier.md).

## 8. Declarative motion

Blazor destroys a DOM node the instant `@if` turns false, which makes exit
animations impossible without hacks. `PresenceBoundary` (Tier 1) intercepts the
disposal lifecycle: when a child is toggled off it delays destruction, plays the
CSS/TS exit transition, then releases the node. This is the AnimatePresence
equivalent for .NET. See [ADR 0005](docs/adr/0005-intercepted-unmounting-motion.md).

## 9. Governance: readability and the 1000-line cap

Every source file is capped at 1000 lines:

- **C# and `.razor`**: the `DX1000` Roslyn analyzer reports an error at build.
- **Rust, TypeScript, CSS**: the `FileLength` MSBuild target fails the build.

The cap is a forcing function for single-responsibility files and reviewable
diffs. Combined with the readability rules in [CONTRIBUTING.md](CONTRIBUTING.md),
it keeps the codebase navigable by humans as it grows. See
[ADR 0006](docs/adr/0006-1000-line-file-cap.md).

## 10. Project map

| Project | Tier / role | Target |
| --- | --- | --- |
| `BlazorDX.Primitives` | Tier 1 headless | net10.0 (Razor) |
| `BlazorDX.Components` | Tier 2 styled | net10.0 (Razor) |
| `BlazorDX.Interop` | `[JSImport]` bindings + module manager | net10.0 |
| `BlazorDX.Interop.Ts` | TypeScript source → ESM | (build only) |
| `BlazorDX.Compute` | C# façade over Rust + managed fallback | net10.0 |
| `BlazorDX.Compute.Rust` | Rust `dx_grid` crate → wasm | (cargo) |
| `BlazorDX.Security` | sanitizer, `ISafeAction`, scoped state | net10.0 |
| `BlazorDX.SourceGen` | Roslyn generators | netstandard2.0 |
| `BlazorDX.Htmx` | static-SSR forms tier | net10.0 (Razor) |
| `BlazorDX.Analyzers` | DX1000 + security bans | netstandard2.0 |
| `BlazorDX.Demo` | sample app (a demo page per component) | net10.0 (Blazor Web App) |

## 11. The engine, and what falls out of it

The thesis behind the breadth of the catalog is that **most components are
compositions of a small set of headless primitives**, not bespoke widgets. A
handful of engine pieces — anchored positioning (flip/shift), a dismiss layer,
focus trapping, roving-tabindex, selection state, a virtualizer, drag-reorder,
theme tokens, and `PresenceBoundary` motion — combine into the ~55 styled
components. A Sheet *is* a Dialog anchored to an edge; a Command Palette *is* a
Dialog plus a typeahead plus roving; a Pivot *is* the grid's Rust aggregation in a
cross-tab; a tree grid *is* the grid's accessor and virtualizer over a flattened
hierarchy. Building the engine well is what made the catalog cheap and consistent.
See [ADR 0008](docs/adr/0008-shared-primitive-engine.md) and
[ADR 0009](docs/adr/0009-source-generated-binding.md); the full list of components
and their demos is in [COMPONENTS.md](COMPONENTS.md).
