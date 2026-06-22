# BlazorDX Roadmap

The foundation goal — *prove a secure-by-default, AOT-safe, headless component
system is possible* — is met, and the catalog has grown well past the original plan:
**~95 components** on a shared headless engine, a deep DataGrid family (flat / tree /
pivot, with server-side data and grouping), data visualization, scheduling, editors,
file management, forms (one model that doubles as an AI/MCP tool), AI chat, and
standards-verified barcodes/QR — all trim-clean. See [COMPONENTS.md](COMPONENTS.md)
and [ARCHITECTURE.md](ARCHITECTURE.md).

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
- **Catalog**: overlays, inputs, navigation/layout, ~11 chart/gauge/sparkline types,
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
- **Proof**: ~460 tests green (bUnit + compute + analyzer + Playwright E2E) + Rust
  `cargo test`; **trim-clean publish** under `IsTrimmable`/`IsAotCompatible` +
  warnings-as-errors; 1000-line cap holding via DX1000.
- **Showcase**: the demo home page is now a developer landing — live previews,
  getting-started code, and a categorized catalog with copy-pasteable examples per
  component, behind a categorized nav.

---

## Remaining toward 1.0

Breadth is essentially line-of-business-complete and the high-value depth items have
landed (server-side grid binding, `.xlsx` / PDF export, full AOT pass). What's left is
mostly **trust** — the binding constraint on adoption — plus a few targeted enhancements.

### Trust (the actual binding constraint)

- **Localization & RTL** — `IStringLocalizer` integration for component-supplied strings
  and a right-to-left layout pass. *Not yet started; a hard requirement for many
  enterprise and international buyers.*
- **Formal accessibility audit + VPAT** — automated axe checks pass today; lift this to a
  screen-reader audit and an attested **WCAG / VPAT** statement procurement can cite.
- **Hosted docs site + API reference** — the in-app docs and the
  COMPONENTS/ARCHITECTURE/ADRs are the seed; publish a generated API reference.
- **Independent senior review** — proof of the differentiating claims; see
  [docs/REVIEW.md](REVIEW.md).
- **Production track record** — the deployed showcase is a start; grow real adoption.

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

## Out of scope by design

These are separate products (server-side document engines, not UI components), and
pursuing them would dilute the headless/auditable identity that *is* the
differentiator. Where it makes sense, we expose clean integration seams instead:

- Document processing (PDF/Word/Excel generation & parsing)
- Report designer
- Full in-browser spreadsheet
- PDF viewer
- Mapping / GIS
- Outlook-depth recurring scheduler, diagram/flowchart engine

## Sequencing

With breadth and depth largely in place, the path to 1.0 is **trust-first**:
localization/RTL and the accessibility audit/VPAT are the highest-leverage items for
adoption, run alongside hosted docs and an independent review. Raw component count is
explicitly **not** the target — only the trust work moves *adoption*.
