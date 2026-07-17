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
- **Catalog**: overlays, inputs, navigation/layout, 21 chart/gauge/sparkline types,
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
- **Packaging & delivery**: eight NuGet packages (incl. analyzer/source-gen) packed clean
  and published to a feed; containerized demo deployment behind a Cloudflare tunnel.
- **Automated checks**: ~460 automated tests (bUnit + compute + analyzer + Playwright E2E)
  and Rust `cargo test` currently pass; **trim-clean publish** under
  `IsTrimmable`/`IsAotCompatible` + warnings-as-errors; 1000-line cap holding via DX1000.
  This is automated coverage only — with limited real-world use, treat the green suite as a
  starting point, not evidence of production readiness.
- **Showcase**: the demo home page is now a developer landing — live previews,
  getting-started code, and a categorized catalog with copy-pasteable examples per
  component, behind a categorized nav.

---

## Remaining toward 1.0

Breadth is wide and several high-value depth items are in place (server-side grid binding,
`.xlsx` / PDF export, an AOT pass). What's left is substantial: **trust and real-world
hardening** — the binding constraint on any adoption — plus a number of targeted
enhancements. None of this should be read as "ready"; it is a beta with work ahead.

### Trust (the actual binding constraint)

- **Localization & RTL** — `IStringLocalizer` integration for component-supplied strings
  and a right-to-left layout pass. *Not yet started; a hard requirement for many
  enterprise and international buyers.*
- **Formal accessibility audit + VPAT** — automated **axe-core checks now run in CI**
  (`AccessibilityE2ETests`, across Chromium/Firefox/WebKit) over the showcase and the
  TicketDesk demo app, with zero serious/critical violations; wiring this up already caught
  and fixed real form-labeling and contrast gaps. The remaining work is to lift this to a
  screen-reader audit and an attested **WCAG / VPAT** statement procurement can cite.
- **Hosted docs site + API reference** — the in-app docs and the
  COMPONENTS/ARCHITECTURE/ADRs are the seed; publish a generated API reference.
- **Independent senior review** — proof of the differentiating claims; see
  [docs/REVIEW.md](REVIEW.md).
- **Production track record** — none yet. The deployed showcase is only a demo; the library
  has no production use, and real-world adoption and hardening would have to be earned over time.

### Depth & breadth enhancements

- **AI access** — the secured tool core, the stdio transport, and an HTTP (request/response)
  endpoint are done; next are HTTP+SSE/sessions for server-initiated streaming, the DataGrid as
  a read tool over `IGridDataSource`, and the wider MCP surface (resources / prompts). See
  [docs/ai-integration.md](ai-integration.md).
- **Chart interactivity** — tooltips, legend toggling, zoom/pan over the SVG charts.
- **Forms depth** — array / nested / conditional fields.
- **Breadth tail** — a handful of leaves still worth adding (FAB/SpeedDial, Mention,
  standalone AutoComplete; heatmap / treemap / sankey; month-view Calendar).

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

With breadth and depth largely in place, the path to 1.0 is **trust-first**:
localization/RTL and the accessibility audit/VPAT are the highest-leverage items for
adoption, run alongside hosted docs and an independent review. Raw component count is
explicitly **not** the target — only the trust work moves *adoption*.
