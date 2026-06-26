# Changelog

All notable changes to BlazorDX are documented here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/) once it reaches 1.0.

> **Beta.** BlazorDX is pre-1.0 and built with substantial AI assistance. Breaking
> changes can land in any minor release until 1.0.

## [0.3.0] — 2026-06-26

### Added — Extended Document Handling track

- **`BlazorDX.Documents`** — `DxSpreadsheetViewer` (Excel `.xlsx` viewer **and**
  editor with a live formula recalculation graph) and `DxWordViewer` / `DxWordEditor`
  (Word `.docx` over a sanitized OOXML↔HTML round-trip). Heavy parsers, quarantined
  in an opt-in package.
- **`BlazorDX.Documents.Parsing`** — UI-free OOXML readers/writers (`XlsxReader`/
  `XlsxWorkbookWriter`, `DocxReader`/`DocxWriter`) and the spreadsheet formula engine
  (tokenizer, parser, evaluator, function library, dependency-graph recalc). Hand-rolled
  on `System.IO.Compression` + `System.Xml` — **no external NuGet dependencies**.
- **`DxDocumentViewer`** (core) — native-embed viewer for PDF and other
  browser-renderable documents; a toolbar + iframe shell, no parser.
- **`BlazorDX.Integrations.Reporting`** — `DxReportViewer`: server-side **SSRS**
  rendering through Microsoft's own URL-access engine, delivered over HTMX (parameter
  forms + fragment swaps, no WASM payload).
- **`BlazorDX.Integrations.PowerBI`** — `DxPowerBiReport`: a thin, lazy-loaded wrapper
  over Microsoft's `powerbi-client` SDK. The embed token is minted server-side and
  fetched from your endpoint, so AAD credentials never reach the browser.
- **`BlazorDX.Htmx`** — `DxHtmxDocumentViewer`: a static-SSR, read-only PDF/Excel/Word
  viewer with a no-JS `href` fallback (zero circuit, zero WASM).
- **`samples/BlazorDX.MockReportServer`** — emulates the documented SSRS URL-access +
  REST and Power BI REST (`GenerateToken`) contracts, so the integrations are verified
  end-to-end against the protocol without live infrastructure.
- Demo pages and accessibility (axe) E2E coverage for every new route; docs:
  `roadmap-documents-and-reporting.md`, ADRs 0010–0014, reporting/Power BI a11y
  statements, and `learn/` entries.

### Changed

- **WCAG 2.2:** every new route is gated by the axe-core E2E suite; reporting/Power BI
  accessibility responsibilities documented (wrapper vs. renderer).
- **CI:** the unit step now also runs the Reporting, Power BI, and MockReportServer
  suites; the AOT publish + smoke job runs on PRs (not only `main`) so a WASM-AOT/trim
  break is caught before merge, not after.
- **Release:** the `Release` workflow no longer publishes to nuget.org with a stored
  API key — it packs and uploads artifacts for **manual** publishing; no release
  credential lives on CI.

### Decided

- **RDLC viewer declined** — we will not ship an unmanaged, vendor-bound renderer; the
  SSRS server-render path covers the reporting need (recorded in ADR-0010).

## [0.2.0] — 2026-06-24

### Added

- WCAG 2.2 **Level A** gap closure across the catalog.
- Versioned release tooling (`Build-Release.ps1`): per-version NuGet packages +
  symbols, a source-snapshot zip, and a SHA-256 manifest.

### Changed

- WCAG 2.2 **AA** hardening: single-pointer + keyboard alternatives for drag
  (2.5.7), 24×24 target sizes (2.5.8), and related fixes extended to sortable/tiles.

## [0.1.0] — 2026-06-21

- Initial beta: the headless two-tier engine; the DataGrid family (flat / tree /
  pivot, server-side data, grouping); data visualization; scheduling; editors; file
  management; forms (one model that doubles as an AI/MCP tool); AI chat; and
  standards-verified barcodes/QR — built to publish trim-clean, zero runtime reflection.

[0.3.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.3.0
[0.2.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.2.0
[0.1.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.1.0
