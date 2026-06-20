# ADR 0005 — Intercepted unmounting for declarative motion

**Status:** Accepted

## Context

When `@if` turns false, Blazor destroys the DOM node and disposes the component
immediately. There is no native hook to delay removal, so exit animations are
impossible without `Task.Delay` hacks and manual `isClosing` state machines.

## Decision

`PresenceBoundary` (Tier 1) wraps children and intercepts the disposal lifecycle.
When a child is toggled off it keeps the node alive, signals the CSS/TypeScript
exit transition, waits for completion, then releases the node and disposes. This
mirrors React's `AnimatePresence`.

## Consequences

- Components can animate out, not just in, without per-component hacks.
- The boundary owns a small state machine; misuse (forgetting to wrap) simply
  falls back to instant removal — no crash, just no exit animation.
