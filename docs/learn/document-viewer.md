# PDF / document viewer — Concept → Code → Why

**Demo:** `/docviewer` · **Source:** [`DxDocumentViewer.cs`](../../src/BlazorDX.Components/DxDocumentViewer.cs)

## Concept

A multi-format viewer that dispatches on each document's kind: images render in an
`<img>` with zoom, PDFs in the browser's **native** `<embed>` viewer (no bundled PDF
engine), Markdown through the safe `DxMarkdown` renderer, and text/code in a `<pre>`.
With more than one document a sidebar switches between them. It is a leaf component —
all of its risk is in what it puts into a `src`/`href`.

## Code

- **Kind dispatch** is in [`DxDocumentViewer.cs:357`](../../src/BlazorDX.Components/DxDocumentViewer.cs)
  (`BuildBody`); kinds are inferred from the file name at
  [`:36`](../../src/BlazorDX.Components/DxDocumentViewer.cs) (`KindFromName`).
- **PDF** uses a native `<embed type=application/pdf>` —
  [`DxDocumentViewer.cs:380`](../../src/BlazorDX.Components/DxDocumentViewer.cs) — plus an
  always-present download fallback link so no-plugin / AT users can still get the file
  ([`:395`](../../src/BlazorDX.Components/DxDocumentViewer.cs)).
- **Markdown** is delegated to the safe `DxMarkdown` renderer rather than raw HTML:
  [`DxDocumentViewer.cs:412`](../../src/BlazorDX.Components/DxDocumentViewer.cs).
- **Print** targets the embedded frame via interop, no-op off-browser:
  [`DxDocumentViewer.cs:262`](../../src/BlazorDX.Components/DxDocumentViewer.cs).

## Why (security + accessibility + non-negotiables)

- **XSS scheme allowlist (ADR 0007) is the headline decision.** No source becomes a
  live `src`/`href` without passing `IsSafeSource` —
  [`DxDocumentViewer.cs:282`](../../src/BlazorDX.Components/DxDocumentViewer.cs). It allows
  relative URLs, `http`/`https`/`blob`, and only `data:image/*` / `data:application/pdf`;
  it rejects `javascript:`, `vbscript:`, `file:`, and HTML data URLs. Every emission
  site is gated by it: the `<img>` ([`:365`](../../src/BlazorDX.Components/DxDocumentViewer.cs)),
  the `<embed>` ([`:381`](../../src/BlazorDX.Components/DxDocumentViewer.cs)), the
  download/open links ([`:218`/`:244`](../../src/BlazorDX.Components/DxDocumentViewer.cs)),
  and the unknown-kind download ([`:431`](../../src/BlazorDX.Components/DxDocumentViewer.cs)).
  A rejected source renders a non-clickable "unavailable" placeholder
  ([`:448`](../../src/BlazorDX.Components/DxDocumentViewer.cs)) — never a broken or
  exploitable link.
- **4.1.2 accessible name.** The embedded PDF frame always carries a non-empty
  `title`, guaranteed by [`FrameTitle` at `:271`](../../src/BlazorDX.Components/DxDocumentViewer.cs).
- **2.5.8 target size + focus.** Toolbar actions are a labelled `role=group` of
  keyboard-operable 24×24 controls — [`DxDocumentViewer.cs:211`](../../src/BlazorDX.Components/DxDocumentViewer.cs).
  The sidebar uses `aria-current` ([`:157`](../../src/BlazorDX.Components/DxDocumentViewer.cs))
  and the text `<pre>` is focusable/scrollable ([`:419`](../../src/BlazorDX.Components/DxDocumentViewer.cs)).
- **Render mode (ADR 0013): static-SSR-friendly.** The viewer is read-only /
  server-driven — ADR 0013 lists "the read-only document viewer" under the static-SSR
  tier; the native `<embed>` and download links work without WASM. (The demo route
  hosts it as `InteractiveWebAssembly` only to share the demo shell; the component
  itself needs no client runtime for its core display.)
- **No bundled PDF engine** keeps payload down and offloads rendering to the
  browser's hardened viewer — consistent with the integration policy in
  [ADR 0010](../adr/0010-documents-and-reporting-integration.md).

### Verified by

bUnit unit tests cover kind dispatch, the `IsSafeSource` allowlist (including
`javascript:`/empty rejection), and the PDF fallback link; axe checks the route
([`AccessibilityE2ETests.cs`](../../tests/BlazorDX.E2E.Tests/AccessibilityE2ETests.cs)).
