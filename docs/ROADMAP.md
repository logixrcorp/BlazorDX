# BlazorDX Roadmap

> **Status: early beta.** BlazorDX was built with substantial AI assistance and has had
> only limited real-world testing. This roadmap describes direction and intent, not a
> finished or production-ready product. Dates, scope, and claims below are aspirational and
> subject to change. It is not intended for production use.

The original foundation goal — *explore whether a secure-by-default, AOT-safe, headless
component system is possible* — is largely met as a proof of concept, and the catalog has
grown well past the original plan: **100+ components** on a shared headless engine, a
DataGrid family (flat / tree / pivot, with server-side data and grouping), data
visualization, scheduling, editors, file management, forms (one model that doubles as an
AI/MCP tool), AI chat, standards-verified barcodes/QR, and an **Extended Document Handling**
track (Excel/Word viewers & editors, native PDF, SSRS & Power BI reporting; see
[roadmap-documents-and-reporting.md](roadmap-documents-and-reporting.md)) — built to publish
trim-clean. See [COMPONENTS.md](COMPONENTS.md) and [ARCHITECTURE.md](ARCHITECTURE.md).

This roadmap is now about **finishing the library to a coherent 1.0** and **earning
adoption**, deliberately scoped so we deepen our identity instead of chasing a
commercial suite feature-for-feature.

## What "complete" means here

Complete = **line-of-business-complete + hardened + trusted**, *not* "matches
Syncfusion." Concretely:

- the components a typical app reaches for (Bucket 1 below) exist,
- the flagship DataGrid can bind to a server,
- the differentiating claims are independently verified,
- and there is enough documentation, accessibility evidence, and review for someone
  to bet on it.

Raw component count is explicitly **not** the target — see *Out of scope*.

## Done

- **Engine**: anchored positioning (flip/shift), dismiss layer, focus trap,
  roving-tabindex, selection state, generalized `DxVirtualize<T>`, drag-reorder,
  theme tokens, `PresenceBoundary` motion.
- **DataGrid**: sort, multi-column sort, per-column filter, Excel-style value filter,
  column chooser, group + per-group aggregates (Rust), row selection, inline edit
  (zero-reflection write-back), column reorder, column resize, pinned columns,
  master/detail rows, CSV / Excel (`.xlsx`) / PDF export, clipboard copy, keyboard cell
  navigation, saved layout/state, tree data, pivot, and **server-side data binding with
  server-side grouping + aggregation** (`IGridDataSource` / `IGridGroupDataSource`).
- **Catalog**: overlays, inputs, navigation/layout, 25 chart/gauge/sparkline types,
  scheduling (Scheduler + Gantt), Kanban, editors (Markdown + WYSIWYG + QueryBuilder),
  file manager, AI chat, hotkeys, and pure-C# **EAN-13 / Code 128 / QR** verified against
  published reference vectors. Multiple `[JSImport]` TS bridges; five Rust kernels with
  managed parity.
- **Forms as AI tools**: one source-generated model renders a `DxForm` *and* projects a
  JSON-Schema tool definition served over the **Model Context Protocol** (incl. interop with
  standard `System.ComponentModel.DataAnnotations` models), with a runnable stdio server
  ([`samples/BlazorDX.McpServer`](../samples/BlazorDX.McpServer)). The tool surface is **secured**:
  per-tool authorization, audit via the diagnostics sink, cancellation, and `[AiHidden]` /
  `[DxField(Sensitive)]` redaction of PII. See [docs/ai-integration.md](ai-integration.md).
- **Packaging & delivery**: twelve NuGet packages (incl. analyzer/source-gen, and the opt-in
  `Documents`/`Documents.Parsing`/`Integrations.PowerBI`/`Integrations.Reporting` packages) packed
  clean and published to a feed; containerized demo deployment behind a Cloudflare tunnel.
- **Automated checks**: ~950 automated tests (bUnit + compute + analyzer + Playwright E2E)
  and Rust `cargo test` currently pass; **trim-clean publish** under
  `IsTrimmable`/`IsAotCompatible` + warnings-as-errors; 1000-line cap holding via DX1000.
  This is automated coverage only — with limited real-world use, treat the green suite as a
  starting point, not evidence of production readiness.
- **Showcase**: the demo home page is now a developer landing — live previews,
  getting-started code, and a categorized catalog with copy-pasteable examples per
  component, behind a categorized nav.
- **Hosted docs site + API reference**: a full, generated API reference
  (`docs/apidocs`, [DocFX](https://dotnet.github.io/docfx/)) — every public type and
  member across all ten packages, built directly from XML doc comments, rebuilt from
  source on every change rather than hand-maintained. `.github/workflows/docs.yml`
  builds and deploys it to GitHub Pages on every push to `main`; live at
  [logixrcorp.github.io/BlazorDX](https://logixrcorp.github.io/BlazorDX/).

---

## Remaining toward 1.0

Breadth is wide and several high-value depth items are in place (server-side grid binding,
`.xlsx` / PDF export, an AOT pass). What's left is substantial: **trust and real-world
hardening** — the binding constraint on any adoption — plus a number of targeted
enhancements. None of this should be read as "ready"; it is a beta with work ahead.

### Trust (the actual binding constraint)

- **Localization & RTL** — `IStringLocalizer` integration for component-supplied strings
  and a right-to-left layout pass. **Phase 0 done**: the `IStringLocalizer<T>` +
  root-level `.resx` mechanism is proven end to end against the real AOT-publish CI gate
  (not assumed), piloted on `DxAlert` and `DxDataGrid`, with a working RTL CSS pilot
  (`dx-overlay.css` converted to logical properties) and a `?dir=rtl` demo toggle — see
  [ADR 0016](adr/0016-localization-rtl-strategy.md). **Foundation done**: localization is
  now **opt-in for consumers** (`AddLocalization()` is no longer required to render a
  BlazorDX component — the pilot's `[Inject] IStringLocalizer<T>` would have made it
  mandatory library-wide), a broken resource lookup falls back to English instead of
  showing the raw key, and two ratchets keep the rollout from regressing: analyzer DX1003
  bans hardcoded user-facing strings in any component already localized, and
  `RtlLogicalPropertyTests` bans physical directional CSS in any stylesheet marked
  converted — see [ADR 0021](adr/0021-optional-localization-and-rollout-guardrails.md) and
  the runbook at [docs/localization.md](localization.md). **RTL done**: all 25 stylesheets
  are converted to logical properties, and the guard's marker is now **mandatory** rather
  than opt-in, so a new stylesheet cannot skip the check by omitting it. The physical usages
  that remain each carry a written reason — a screen-edge API (`DxSheet`'s `Side`), boxes
  pinned to both edges, and the two places CSS has no logical form (`transform` and
  `transform-origin`, handled with explicit `[dir="rtl"]` rules). *Remaining: the string
  rollout — **43 of 83 components** still to localize, ~60–80 strings; 40 are done, carrying
  205 externalized strings (the "~130 components" here previously counted every file; 57
  have no user-facing text at all). A hard requirement for many enterprise and international
  buyers.*
- **Formal accessibility audit + VPAT** — automated **axe-core checks now run in CI**
  (`AccessibilityE2ETests`, across Chromium/Firefox/WebKit) over the showcase and the
  TicketDesk demo app, with zero serious/critical violations; wiring this up already caught
  and fixed real form-labeling and contrast gaps. The remaining work is to lift this to a
  screen-reader audit and an attested **WCAG / VPAT** statement procurement can cite.
- **Independent senior review** — proof of the differentiating claims; see
  [docs/REVIEW.md](REVIEW.md).
- **Production track record** — none yet. The deployed showcase is only a demo; the library
  has no production use, and real-world adoption and hardening would have to be earned over time.

### Depth & breadth enhancements

- **AI access** — the secured tool core, the stdio transport, and an HTTP (request/response)
  endpoint are done; next are HTTP+SSE/sessions for server-initiated streaming, the DataGrid as
  a read tool over `IGridDataSource`, and the wider MCP surface (resources / prompts). See
  [docs/ai-integration.md](ai-integration.md).
- **Chart interactivity** — shipped in full. Point selection, hover, and legend toggling
  (title-tag tooltips only, not a rich hover card). Zoom/pan for `DxLineChart`/`DxAreaChart`
  (X-only, opt-in via `Zoomable`): see [ADR 0017](adr/0017-chart-zoom-pan-strategy.md).
  Rectangular (both-axes) zoom/pan for `DxScatterChart`/`DxBubbleChart` — wheel to zoom
  uniformly, drag to brush-zoom into a region, Shift+drag to pan, keyboard alternative: see
  [ADR 0020](adr/0020-scatter-bubble-2d-zoom-strategy.md). Every chart kind
  `ChartSelectionPrimitive`/`ChartZoomPrimitive`'s own doc comments ever named as an intended
  target now has the interactivity they described.
- **Forms depth** — shipped in full. Conditional fields (a field gating on another
  field's live value, governing visibility/requiredness/the AI-MCP schema together —
  [ADR 0018](adr/0018-conditional-form-fields.md)), and array + nested-object fields
  (`[DxField]` on a `List<T>` or a `[DxFormModel]`-typed property — recursive rendering,
  validation, and JSON-Schema/`ApplyArguments`, via a new non-generic `IFormModelUntyped`
  face on the generated descriptor — [ADR 0019](adr/0019-array-and-nested-form-fields.md)).
- **Breadth tail** — a handful of leaves still worth adding (FAB/SpeedDial, Mention,
  standalone AutoComplete). The chart-family tail (heatmap, treemap, sankey, and beyond) and
  the month-view Calendar have both shipped since this was written.

---

## Extended document type handling

The **viewer/editor** side of documents and reporting is now a planned track — PDF /
Excel / Word viewers, a drag-and-drop file manager, scheduler depth, and a **functional
SSRS report viewer built on Microsoft's own server components**. It is scoped to protect
the core's identity: heavy parsing runs in **Rust → wasm**, in-browser viewers ship in a
separate MIT `BlazorDX.Documents` package, and external-service/paid integrations (SSRS,
Power BI) live in **server-side `BlazorDX.Integrations.*` packages** so the trim-clean
WASM core is never weighed down. **WCAG 2.2 AA is a per-component done-gate** for the whole
track (axe in CI + target-size/drag-alternative E2E + manual screen-reader pass), with a
documented accessibility statement for the embedded report/PDF integrations. Full plan,
phases, per-component language allocation, and the accessibility gate:
**[roadmap-documents-and-reporting.md](roadmap-documents-and-reporting.md)**.

## Out of scope by design

These remain separate products that would dilute the headless/auditable identity that *is*
the differentiator. Where it makes sense, we expose clean integration seams instead:

- Report **designer** (authoring RDL/RDLC), not just viewing
- Document **generation engines** beyond the existing export (`.xlsx` / PDF / CSV)
- Mapping / GIS
- *Outlook-depth* recurring scheduler (full RRULE: exceptions/EXDATE, per-occurrence edits,
  timezone rules). `DxScheduler` ships core recurrence — daily/weekly/monthly with
  interval/count/until and weekly by-weekday — plus drag-to-move/create; the deep cases above
  stay out of scope. Diagram/flowchart engine.

## Sequencing

With breadth and depth largely in place and hosted docs now live, the path to 1.0 is
**trust-first**: the localization/RTL rollout and the accessibility audit/VPAT are the
highest-leverage remaining items for adoption, alongside an independent review. Raw
component count is explicitly **not** the target — only the trust work moves *adoption*.
