# ADR 0004 — HTMX as the static-SSR forms tier

**Status:** Accepted

## Context

Not every part of an app needs interactivity. Sign-in, settings, and CRUD forms
are better served fast and resilient, without paying for a SignalR circuit or a
WASM download. Blazor's static SSR plus enhanced forms gets close; HTMX adds
declarative partial updates over hypermedia.

## Decision

`BlazorDX.Htmx` provides a tier of static-server-rendered components annotated
with HTMX attributes. They run with **no circuit and no WASM payload**. The
interactive tiers (Primitives/Components) remain pure Blazor and are unaffected;
HTMX is scoped to this forms tier rather than threaded through the whole library.

## Consequences

- Forms-heavy pages ship near-zero client runtime.
- HTMX is an additive tier, not a dependency of interactive components.
- Two interaction models coexist in one app; the boundary is explicit (which tier
  a component comes from).
