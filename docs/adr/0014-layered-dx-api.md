# ADR 0014 — Layered DX / progressive-disclosure API for integrations

**Status:** Accepted

## Context

The Microsoft integrations (SSRS / report viewer, Power BI) and the document
viewers must be **trivial for the common case** — a junior developer should get a
working, secure, accessible viewer in one line — yet **fully customizable** for
senior developers who need to swap the rendering engine, auth, caching, or the
entire chrome. A single flat API can satisfy only one of those audiences.

## Decision

Apply the existing two-tier headless philosophy as **four opt-in levels of control**;
you only reach for the next when you need it:

1. **Zero-config** — one DI call (`AddBlazorDXReporting(…)`) + one attribute
   (`<DxReportViewer Report="…" />`). Secure and WCAG-AA defaults are built in.
2. **Configure** — per-instance parameters over startup options (mode, format,
   caching, toolbar, callbacks).
3. **Template** — `RenderFragment` slots (`Toolbar`, `ParametersTemplate`,
   `LoadingTemplate`, `ErrorTemplate`) replace markup without touching behavior.
4. **Extend / headless** — every moving part is a **DI-swappable interface, no
   reflection** (`IReportRenderer`, `IReportAuthProvider`/`ITokenProvider`,
   `IReportCache`, request interceptors), and the Tier-1 primitive exposes the raw
   rendered state for a bespoke shell.

**The load-bearing rule:** defaults are secure and WCAG-AA *by construction*, and
customization is *additive* — overriding the toolbar, parameter UI, or renderer
cannot remove the accessibility/security floor (sanitization, iframe `title`, auth,
keyboard + labels), because that floor is enforced by the **primitive**, not the
swappable template.

## Consequences

- One line yields a conformant viewer; a senior dev can replace the engine; neither
  can accidentally ship something insecure or inaccessible.
- The pattern is uniform with the rest of the catalog's Tier-1/Tier-2 split, keeping
  the mental model small.
- Slightly more interface surface to design and maintain per integration; justified
  by serving both audiences from one component.
