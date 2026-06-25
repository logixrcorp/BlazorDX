# ADR 0010 — Documents & reporting: integration policy

**Status:** Accepted

## Context

The "Extended document type handling" track (PDF / Excel / Word viewers and
editors, a drag-and-drop file manager, scheduler depth, and a functional SSRS /
Power BI report viewer) deliberately brings into scope work the main
[ROADMAP](../ROADMAP.md) previously listed as *out of scope by design*. Some of it
needs heavy parsing engines, large third-party JavaScript SDKs, or external/paid
services (a report server, Azure + Power BI Embedded). None of that may dilute the
trim-clean, zero-reflection, MIT WASM **core**. See
[the track roadmap](../roadmap-documents-and-reporting.md).

## Decision

Layer by package so the blast radius is contained. The boundary is **weight and
dependencies, not topic**: a component earns its own package only when it carries a
heavy parsing/compute engine, an external/paid SDK, or a server integration.
Lightweight, dependency-free UI components stay in core `BlazorDX.Components` alongside
the DataGrid, Dialog, etc.

- **Core `BlazorDX.Components`** — the file manager, scheduler, and the **native-embed**
  PDF/document viewer. These are pure C# + a thin TS bridge, no heavy engine, no external
  SDK; they are ordinary UI components and belong with the rest of the catalog.
- **`BlazorDX.Documents`** — MIT, interactive (WASM) viewers/editors that **carry a heavy
  engine**: the Excel and Word viewers/editors (Rust `.xlsx`/OOXML parsers + the formula
  recalc graph). Same rules as core: zero reflection, AOT/trim-clean, `[JSImport]` only,
  1000-line cap. This is the package whose absence would otherwise bloat core.
- **`BlazorDX.Htmx`** (existing static-SSR tier) — the server-rendered, read-only
  document viewer and the report viewer's parameter forms + fragment swaps. No WASM
  payload, no circuit.
- **`BlazorDX.Integrations.Reporting`** — server-side SSRS/RDLC rendering via
  Microsoft's own components, delivered over HTMX. Explicitly an integration, not core.
- **`BlazorDX.Integrations.PowerBI`** — thin, lazy-loaded wrapper over Microsoft's
  `powerbi-client` SDK + MSAL.

Two invariants: **credentials and large JS never reach the browser core**, and we
**defer to native/remote engines** (browser PDF, SSRS, Power BI) rather than
reimplement them. The report *designer*, mapping/GIS, and diagram engines remain
out of scope.

## Consequences

- The core stays trim-clean; heavy engines and external/paid dependencies are
  quarantined behind opt-in packages, while lightweight UI components stay where authors
  expect them.
- **Resolved:** the Phase-1 components (file manager, scheduler, native-embed PDF viewer)
  correctly live in `BlazorDX.Components` — they carry no heavy engine or external
  dependency, so they are not "bloat." `BlazorDX.Documents` is created when the first
  heavy component (the Excel viewer, Phase 2) needs it, and the Excel/Word components land
  there. This supersedes the earlier "all doc components go in a separate package" framing.
- Reporting reuses Microsoft's supported rendering rather than a reimplementation,
  which is the only viable path on .NET 10 (no official Blazor ReportViewer exists).
