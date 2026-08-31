# BlazorDX API Reference

A secure-by-default, headless, AOT-safe Blazor component system for .NET 10 — generated
directly from the source's XML doc comments, so it never drifts from the shipped code.

> **Beta.** BlazorDX is pre-1.0 and built with substantial AI assistance. See the
> [main documentation site](https://blazordx.com/docs) for install instructions, a live demo of
> every component, and the [component parameter reference](https://blazordx.com/docs) with
> interactive examples. This site is the *full* API surface — every public type and member
> across every package, not just component parameters.

## Packages

| Package | What it covers |
| --- | --- |
| `BlazorDX.Components` | The component library itself — DataGrid, forms, charts, dialogs, and the rest of the 100+ component catalog. |
| `BlazorDX.Primitives` | Headless Tier-1 behavior/accessibility primitives the styled Tier-2 components in `BlazorDX.Components` build on. |
| `BlazorDX.Compute` | The managed-C# compute fallback and the `IGridDataSource`/`IGridCompute` contracts the Rust/wasm grid kernel implements. |
| `BlazorDX.Documents` | In-browser Excel/Word/PDF viewers and editors. |
| `BlazorDX.Documents.Parsing` | The `.xlsx`/`.docx` reader, writer, and formula engine `BlazorDX.Documents` is built on. |
| `BlazorDX.Htmx` | The static-SSR + HTMX tier: forms and progressive enhancement with zero WASM payload. |
| `BlazorDX.Integrations.Reporting` | Server-side SSRS report-viewer integration. |
| `BlazorDX.Integrations.PowerBI` | Server-side Power BI embedding integration. |
| `BlazorDX.Interop` | The C# `[JSImport]` browser-bridge surface (DOM, overlay, hotkeys, file drag-and-drop, and the rest). |
| `BlazorDX.Security` | Safe-action dispatch, the sanctioned HTML sanitizer boundary, and scoped-state registration. |

## See also

- [Architecture](https://github.com/logixrcorp/BlazorDX/blob/main/docs/ARCHITECTURE.md)
- [Component catalog](https://github.com/logixrcorp/BlazorDX/blob/main/docs/COMPONENTS.md)
- [Architecture decision records](https://github.com/logixrcorp/BlazorDX/tree/main/docs/adr)
- [Roadmap](https://github.com/logixrcorp/BlazorDX/blob/main/docs/ROADMAP.md)
