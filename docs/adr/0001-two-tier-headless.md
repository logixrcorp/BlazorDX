# ADR 0001 — Two-tier headless model

**Status:** Accepted

## Context

Enterprise Blazor suites bake CSS deep into their components. Matching a bespoke
design means fighting nested selectors, `!important`, and Shadow DOM. The React
world solved this with headless primitives (Radix) plus styled layers (shadcn/ui).

## Decision

Split every component into two tiers:

- **Tier 1 — Primitives (`BlazorDX.Primitives`):** unstyled C# that owns state,
  keyboard navigation, focus management, and WAI-ARIA. Ships no CSS.
- **Tier 2 — Styled (`BlazorDX.Components`):** wraps a primitive and supplies
  looks via CSS variables and utility-class-friendly markup.

## Consequences

- Teams can adopt Tier 1 for total design control or Tier 2 for batteries-included.
- Accessibility lives in one place (Tier 1) and is inherited by every skin.
- Two projects per concern is more ceremony, accepted for the separation it buys.
