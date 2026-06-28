# From 0.2 to 0.4.4: documents, a model-driven editor, and an accessibility gate that never blinked

*BlazorDX release recap · covers v0.2.0 → v0.4.4*

When 0.2.0 shipped, BlazorDX was a broad, headless component library that took accessibility
seriously. Twenty-odd tagged releases later, at 0.4.4, it has grown a whole **documents and
reporting** stack — Excel and Word viewers *and* editors, a PDF viewer, SSRS and Power BI
integrations — and the Word editor has been rebuilt from the inside out onto a **model-driven core**.
None of it cost the things that make BlazorDX what it is: zero runtime reflection, AOT/trim-clean
output, no external NuGet dependencies in the document parsers, and a WCAG axe gate on every route.

Here's the whole arc.

> **Beta.** BlazorDX is pre-1.0 and built with substantial AI assistance. Breaking changes can land
> in any minor release until 1.0. Not yet intended for production use.

---

## TL;DR

- **0.2.0** — WCAG 2.2 **Level A → AA** gap closure across the catalog, plus versioned release tooling.
- **0.3.0** — the **Documents & Reporting track**: `BlazorDX.Documents` (Excel/Word viewer + editor),
  `DxDocumentViewer` (PDF), SSRS (`DxReportViewer`), Power BI (`DxPowerBiReport`), an HTMX no-JS
  viewer, and a mock report server — all with **no external NuGet dependencies** in the parsers.
- **0.3.1 – 0.3.8** — the document **editors mature**: a real Excel editor, a Word editor that
  round-trips underline/strike/links/alignment/color, nested lists, images, undo/redo, and tables.
- **0.3.9 – 0.3.19** — **ADR-0015**, the model-driven editing core: `WordDocument` becomes the source
  of truth and `execCommand` is retired, phase by phase — *plus* spreadsheet performance work
  (incremental recalc, 2-D virtualization).
- **0.4.0 – 0.4.4** — Word editor **typography and layout depth**: fonts, super/subscript, paragraph
  styles, spacing, indentation, and table cell shading + merge.

---

## 0.2.0 — Accessibility, all the way to AA

0.2.0 was about finishing what "accessible by default" actually requires. It closed the remaining
**WCAG 2.2 Level A** gaps across the catalog, then hardened to **AA**: a single-pointer + keyboard
alternative for every drag interaction (2.5.7), 24×24 minimum target sizes (2.5.8), and the related
fixes carried through to the sortable list and tile layout.

It also introduced **versioned release tooling** (`Build-Release.ps1`): per-version NuGet packages
with symbols, a source-snapshot zip, and a SHA-256 manifest — the plumbing every later release rode on.

## 0.3.0 — The Documents & Reporting track

The biggest single release in this range. 0.3.0 added a whole opt-in stack for the file formats
enterprise apps actually live in — and did it without dragging in a single third-party parser:

- **`BlazorDX.Documents`** — `DxSpreadsheetViewer` (Excel `.xlsx` viewer **and** editor with a live
  formula recalculation graph) and `DxWordViewer` / `DxWordEditor` (Word `.docx` over a sanitized
  OOXML↔HTML round-trip).
- **`BlazorDX.Documents.Parsing`** — UI-free OOXML readers/writers and the spreadsheet formula engine
  (tokenizer, parser, evaluator, function library, dependency-graph recalc), hand-rolled on
  `System.IO.Compression` + `System.Xml`. **No external NuGet dependencies.**
- **`DxDocumentViewer`** — a native-embed PDF/document viewer (toolbar + iframe shell, no parser).
- **`BlazorDX.Integrations.Reporting`** — `DxReportViewer`: server-side **SSRS** rendering through
  Microsoft's URL-access engine, delivered over **HTMX** (parameter forms + fragment swaps, no WASM payload).
- **`BlazorDX.Integrations.PowerBI`** — `DxPowerBiReport`: a thin, lazy-loaded wrapper over the
  `powerbi-client` SDK, with the embed token minted **server-side** so AAD credentials never reach the browser.
- **`BlazorDX.Htmx`** — `DxHtmxDocumentViewer`: a static-SSR, read-only viewer with a no-JS `href`
  fallback (zero circuit, zero WASM).
- **`samples/BlazorDX.MockReportServer`** — emulates the SSRS URL-access/REST and Power BI
  `GenerateToken` contracts so the integrations are verified end-to-end without live infrastructure.

Alongside the features, 0.3.0 hardened the pipeline: every new route is gated by the **axe-core E2E
suite**, the AOT publish + smoke job runs on PRs (so a trim/AOT break is caught before merge), and the
release workflow stopped publishing to nuget.org with a stored key — it now **packs artifacts for
manual publishing**, so no release credential lives on CI. We also formally **declined an RDLC viewer**
(no unmanaged, vendor-bound renderer) — the SSRS server-render path covers the need (ADR-0010).

## 0.3.1 – 0.3.8 — The editors grow up

With the formats in place, the editors got real:

- **0.3.1** — the **editable spreadsheet** fleshed out: a formula bar (A1 address + raw content), a
  toolbar (insert/delete row & column, download `.xlsx`), and Excel-style keyboard entry — plus three
  fixes (an active-cell crash, a footer-overlap layout bug, and trailing-blank trimming on real files).
- **0.3.2** — **Word editor expansion**: built-in Download `.docx`, a live word/character/paragraph
  stats line, a model-based find & replace bar, and formatting that now round-trips to `.docx` —
  underline, strikethrough, hyperlinks (URL-sanitized), paragraph alignment, and text color/highlight.
  The viewer learned to render all of them, and **scheme-guards `.docx` hyperlink hrefs** (a malicious
  `javascript:` link in an untrusted file is dropped, not rendered clickable).
- **0.3.3** — in-editor **text color + highlight** swatches, with selection save/restore so the native
  color picker can't steal the contentEditable selection.
- **0.3.4** — **nested lists** round-trip through HTML, `.docx`, and the viewer.
- **0.3.5** — **embedded images** round-trip (a `WordImage` block surviving HTML/`.docx`/viewer, with
  `data:`-URL-only parsing and always-present `alt` text).
- **0.3.6** — **find-next** navigation in the find bar, and **Phase A** of the model-driven core: an
  owned-selection primitive that selects over the editor's live text nodes — no `execCommand`.
- **0.3.7** — **undo / redo** in the Word editor, backed by a model-snapshot history (so a find/replace
  is undoable too).
- **0.3.8** — **table editing** lands, closing the last of the original Word-editor feature gaps.

## 0.3.9 – 0.3.19 — ADR-0015: inverting the editor

This is the through-line of the whole 0.3.x series. **ADR-0015** decided to invert the Word editor so
that `WordDocument` is the source of truth and `contentEditable` is just an I/O surface (the
ProseMirror/Lexical pattern) — reusing the model and round-trip we already owned, with **no
`execCommand` and no third-party editor JS**. It shipped in flag-gated phases:

- **0.3.9 (Phase B)** — model-driven **inline formatting**.
- **0.3.10 (Phase C)** — **model-state undo/redo** that restores in place, with **no component re-mount**.
- **0.3.11 – 0.3.14 (Phase D)** — execCommand parity, one command at a time: alignment + clear
  formatting, headings + color, lists, and links.
- **0.3.18** — Word editor **keyboard shortcuts** (Ctrl/Cmd+B/I/U/K, undo/redo).
- **0.3.19** — the model-driven core becomes the **default**, `execCommand` retired to legacy, plus
  **in-editor image insertion**.

Interleaved with the editor work, the spreadsheet got faster: an **incremental recalc engine**
(AST cache + dirty propagation) in **0.3.15**, wired into the editor in **0.3.16**, and **2-D column
virtualization** in **0.3.17** — so large workbooks stay responsive on both axes.

## 0.4.0 – 0.4.4 — Typography and layout depth

With the core solid, 0.4.x pushed the Word editor toward everyday-document fidelity:

- **0.4.0** — **typography**: font family, font size, superscript/subscript.
- **0.4.1** — a **paragraph style dropdown** (Normal / Heading 1–3).
- **0.4.2** — paragraph **line spacing + indentation**.
- **0.4.3** — **table cell shading**.
- **0.4.4** — **horizontal table cell merge** (merge right / split).

## The things that didn't change

Across every one of these releases, the non-negotiables held:

- **Zero runtime reflection** — binding and serialization go through source generators.
- **AOT / trim-clean** — the AOT publish + smoke job gates PRs, not just `main`.
- **No external NuGet dependencies** in the document parsers — OOXML is hand-rolled on the BCL.
- **Two-tier headless** (ADR-0001) — a headless primitive owns logic/state; the `Dx*` component owns DOM + styling.
- **WCAG 2.2 AA as a gate** — every new route is axe-tested in E2E; warnings are errors; files stay under the 1000-line cap.
- **No release credential on CI** — packages are built as artifacts and published by hand.

## Already on `main` since 0.4.4

If you build from source, three things have already landed past this release and will ship next:
a standalone **`DxCalendar`** (inline month calendar, single + range), **scheduler recurrence and
drag-to-move/create**, and **two-sided SHA-256 upload integrity** in the file manager.

---

*Source & releases: [github.com/logixrcorp/BlazorDX](https://github.com/logixrcorp/BlazorDX) ·
Docs & live demos: [blazordx.com](https://blazordx.com). MIT-licensed. Requires .NET 10.*
