# ADR 0009 — Source-generated binding for data-bound components

**Status:** Accepted

## Context

Several wishlist components bind to arbitrary user models: schema-driven dynamic
forms, charts (series over a model), and the pivot grid. The obvious
implementation — reflect over the model at runtime — is exactly what BlazorDX's
zero-reflection policy ([ADR 0002](0002-zero-reflection-source-generation.md))
forbids, because it breaks under Native AOT and trimming. This is a latent
contradiction in the wishlist that must be resolved before those components land.

## Decision

Reuse the binding pattern already proven by the DataGrid's `[GridRow]` generator.

- **Compile-time-known models:** a generator emits typed accessors for each
  annotated model — `[FormModel]` for forms, `[ChartSeries]` for charts —
  analogous to `IGridRowAccessor`. No `PropertyInfo` at runtime.
- **Genuinely runtime-defined schemas** (user-authored fields with no compile-time
  type): expressed through an explicit typed descriptor model
  (field name, kind, getter/setter delegates supplied by the caller), never via
  reflection over an arbitrary CLR type.

The descriptor abstraction and the generated accessors share one interface shape so
components consume both uniformly.

## Consequences

- Dynamic forms and charts stay AOT/trim-safe; binding errors surface at build time.
- Authors annotate a model (the common case) or build a descriptor (the rare truly
  dynamic case) instead of getting free reflection.
- One generator pattern (already validated for grids) extends across forms and
  charts, keeping the mental model small.
