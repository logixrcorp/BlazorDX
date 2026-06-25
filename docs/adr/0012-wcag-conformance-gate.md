# ADR 0012 — WCAG 2.2 AA conformance gate (documents/reporting track)

**Status:** Accepted

## Context

Accessibility on the documents track must be enforced, not aspirational —
especially for the hard cases (drag-and-drop, calendars, spreadsheets, embedded
PDF/report iframes). The repo already runs axe-core in CI; an earlier audit showed
that automated scanning alone misses target size and missing-drag-alternative
failures, and that real ARIA-structure violations slip past bUnit entirely.

## Decision

WCAG 2.2 **Level AA** is a **done-gate per component**, equal in standing to
trim-clean publish and the 1000-line cap. A component is not "done" until:

- **axe-core E2E** runs against its route (serious/critical = build fail).
- **Tests for what axe can't see** exist: target-size (≥24×24) and a keyboard +
  single-pointer drag alternative wherever a drag gesture exists.
- A **manual screen-reader pass** (NVDA / JAWS / VoiceOver) is recorded — the half
  axe cannot cover.
- The established patterns are reused: 2.5.7 single-pointer alternative, 2.5.8 24×24,
  3.3.1 `aria-invalid`/`aria-describedby`, focus-visible/trap, `prefers-reduced-motion`.
- For **embedded/integration** components (PDF, SSRS, Power BI), an **accessibility
  statement** delineates what BlazorDX guarantees (the wrapper, toolbar, parameter
  form, names) vs what depends on the renderer or the document/report author, and an
  **accessible alternative** (download, accessible export, "show as table") is shipped.

## Consequences

- Real violations are caught before merge — wiring the gate for the new routes
  immediately surfaced and fixed **5** serious/critical issues (ARIA role hierarchy
  on the scheduler grid and file-manager table, plus colour contrast) that bUnit
  could not see.
- Embedded content is scoped honestly: we conform the wrapper and prefer accessible
  render formats; we do not claim to fix an untagged PDF or a poorly-authored report.
- The **manual SR pass remains a human step** and is the gate's standing open item
  for already-merged components until performed and recorded.
