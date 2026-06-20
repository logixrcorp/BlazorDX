# ADR 0002 — Zero-reflection via source generation

**Status:** Accepted

## Context

Reflection-based data binding and JSON serialization are the primary reason
Blazor component libraries compile cleanly but crash in the browser under Native
AOT and IL trimming: the trimmer removes types it cannot see being used, then a
runtime `MissingMethodException` follows.

## Decision

Forbid runtime reflection on any hot or trimmable path. Generate the equivalent
at build time:

- JSON through `System.Text.Json` source generators (`JsonSerializerContext`).
- Grid column binding through `BlazorDX.SourceGen`, which reads `[GridColumn]`
  attributes and emits strongly-typed cell accessors.
- C# ↔ JS interop through `[JSImport]`/`[JSExport]`.

Every library sets `IsTrimmable` and `IsAotCompatible`, and the build treats trim
warnings as errors.

## Consequences

- AOT/trimming failures surface at build time, in our CI, not in users' browsers.
- Authors write attributes instead of reflection; the generator does the rest.
- A custom generator is a maintenance surface, accepted for the safety it gives.
