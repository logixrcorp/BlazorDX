# ADR 0013 — Render-mode selection (HTMX/static-SSR vs interactive WASM)

**Status:** Accepted

## Context

BlazorDX supports per-component render modes (static SSR + the `BlazorDX.Htmx`
tier, vs `InteractiveWebAssembly`). The documents track spans both extremes:
report viewers are server-driven request→render→display flows, while spreadsheet
editors and the scheduler are compute-heavy, fine-grained client interactions.
Picking one render mode for the whole track would be wrong for half of it — HTMX
adds a network round-trip per interaction (bad for live editing), while shipping
WASM for a server-rendered report is pure overhead.

## Decision

Choose the render tier **per component, by interaction shape**:

- **Static-SSR + HTMX** for server-driven / read-only / coarse-interaction
  components: the SSRS/report viewer, the read-only document viewer, and the file
  manager's navigation/listing. Fast first paint, no WASM payload, no SignalR
  circuit, progressive-enhancement (works without JS). HTMX adds *no* interaction
  penalty here because the heavy work is server-side regardless.
- **InteractiveWebAssembly** for compute-heavy / fine-grained components: Excel/Word
  editors, the interactive viewers, the scheduler, the DataGrid — where client-side
  Rust kernels and DOM virtualization deliver the runtime goal and a round-trip per
  keystroke would not.
- **Hybrid** where both apply: the file manager uses HTMX for nav/listing and
  interactive WASM/JS for drag-and-drop, upload, and preview.

## Consequences

- Each component's tier is a documented, deliberate choice (a checklist item).
- HTMX is used where it genuinely wins (no per-interaction latency tax) and avoided
  where it would regress fine-grained interactivity.
- The static-SSR choices are progressive-enhancement-friendly, which directly
  strengthens the WCAG gate ([ADR 0012](0012-wcag-conformance-gate.md)) via no-JS
  fallbacks rather than trading against it.
