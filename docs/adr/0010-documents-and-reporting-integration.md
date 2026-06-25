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

Layer by package so the blast radius is contained:

- **`BlazorDX.Documents`** — MIT, interactive (WASM) in-browser viewers/editors.
  Same rules as core: zero reflection, AOT/trim-clean, `[JSImport]` only, 1000-line cap.
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

- The core stays trim-clean; external/paid dependencies are quarantined behind opt-in
  integration packages.
- **Known divergence (to reconcile):** Phase-1 components (file manager, scheduler,
  PDF viewer) currently ship inside `BlazorDX.Components`, not yet a separate
  `BlazorDX.Documents`. Either re-home them or amend this ADR to declare core the home.
- Reporting reuses Microsoft's supported rendering rather than a reimplementation,
  which is the only viable path on .NET 10 (no official Blazor ReportViewer exists).
