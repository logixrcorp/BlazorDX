# Learn: Phase-1 documents-track components

Three styled (Tier 2) components shipped in Phase 1 of the documents & reporting
track. Each "learn" page below follows one shape — **Concept → Code → Why** — so
you can go from "what is this" to the exact line that implements it to the reason
it was built that way.

**How to read these:** start at the Concept (one paragraph: what the component is
for), skim the Code section (annotated `file:line` deep links into `src/`), and
read the Why (how the decision ties back to a non-negotiable or an ADR). The
`file:line` references point at the current `src/` tree; if a line drifts, search
for the quoted member name.

## Lookup table

| Component | Demo route | Key source (`file:line`) | ADRs |
|---|---|---|---|
| [File manager](file-manager.md) | `/files` | [`DxFileManager.cs:24`](../../src/BlazorDX.Components/DxFileManager.cs) · [`FileManagerPrimitive.cs:39`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs) | [0013](../adr/0013-render-mode-selection.md), [0012](../adr/0012-wcag-conformance-gate.md), [0007](../adr/0007-security-baseline.md), [0001](../adr/0001-two-tier-headless.md) |
| [Scheduler](scheduler.md) | `/scheduler` | [`DxScheduler.cs:24`](../../src/BlazorDX.Components/DxScheduler.cs) · `SchedulerPrimitive.cs` | [0013](../adr/0013-render-mode-selection.md), [0012](../adr/0012-wcag-conformance-gate.md), [0011](../adr/0011-speed-vs-size-rust-profile.md), [0001](../adr/0001-two-tier-headless.md) |
| [Calendar](calendar.md) | `/calendar` | [`DxCalendar.cs`](../../src/BlazorDX.Components/DxCalendar.cs) · [`CalendarPrimitive.cs`](../../src/BlazorDX.Primitives/Inputs/CalendarPrimitive.cs) | [0012](../adr/0012-wcag-conformance-gate.md), [0001](../adr/0001-two-tier-headless.md) |
| [PDF / document viewer](document-viewer.md) | `/docviewer` | [`DxDocumentViewer.cs:59`](../../src/BlazorDX.Components/DxDocumentViewer.cs) | [0013](../adr/0013-render-mode-selection.md), [0012](../adr/0012-wcag-conformance-gate.md), [0007](../adr/0007-security-baseline.md), [0010](../adr/0010-documents-and-reporting-integration.md) |

## The non-negotiables these pages tie back to

- **Two-tier headless** ([ADR 0001](../adr/0001-two-tier-headless.md)): a Tier 1
  headless primitive owns logic/state; the Tier 2 `Dx*` component owns DOM + styling.
- **Render mode is a per-component choice** ([ADR 0013](../adr/0013-render-mode-selection.md)).
- **WCAG 2.2 AA is a gate, not a nice-to-have** ([ADR 0012](../adr/0012-wcag-conformance-gate.md)) —
  notably a drag-free move alternative (2.5.7), correct ARIA roles, and visible focus.
- **Security baseline** ([ADR 0007](../adr/0007-security-baseline.md)) — untrusted
  values never become live `src`/`href` without passing an allowlist.
- **1000-line file cap** ([ADR 0006](../adr/0006-1000-line-file-cap.md)).
