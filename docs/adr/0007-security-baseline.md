# ADR 0007 — Security baseline

**Status:** Accepted

## Context

Two security failures are endemic to Blazor apps: cross-user data leakage from
Singleton-scoped UI state on the server, and stale/unauthorized UI from
out-of-order async responses. XSS via raw HTML injection is a third.

## Decision

Bake the mitigations into the component lifecycle and enforce them at build time:

1. **Component-scoped state only.** `BlazorDX.Security` provides scoped state
   helpers; the analyzer flags `AddSingleton` of a state type as an error.
2. **`ISafeAction` cancellation.** Async work runs under a cancellation token; a
   newer dispatch cancels the pending one, so a slow earlier response cannot
   overwrite newer state.
3. **Sanitizer-only raw HTML.** `MarkupString` from runtime data is an analyzer
   error; HTML must pass the strict sanitizer in `BlazorDX.Security`.

## Consequences

- The most common Blazor security mistakes become build failures, not incidents.
- Authors give up some convenience (free Singletons, raw `MarkupString`) by design.
