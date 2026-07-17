# BlazorDX — LLM context & usage guide

> A single-file reference for AI assistants writing applications with **BlazorDX**, a
> secure-by-default, headless, **AOT-safe**, zero-runtime-reflection Blazor component
> system for **.NET 10**. MIT-licensed. Beta — not for production.
>
> Source: https://github.com/logixrcorp/BlazorDX · Docs/demo: https://blazordx.com

When generating BlazorDX code, follow the **Hard rules** below — they are enforced by
Roslyn analyzers and the build fails (warnings-as-errors) if you violate them.

---

## 1. Hard rules (the build enforces these)

1. **No raw `MarkupString`** for untrusted/dynamic HTML (analyzer **DX1001**). Render text
   normally, or route HTML through `BlazorDX.Security` sanitization. Prefer `DxMarkdown`.
2. **No Singleton holding UI/user state** (analyzer **DX1002**). App/data services that back
   components must be registered **`Scoped`**, never `Singleton`.
3. **Zero runtime reflection on binding/serialization.** Never use `PropertyInfo`/reflection
   to read or write model values. Use the **source generators** (`[GridRow]`, `[DxFormModel]`).
4. **1000-line file cap** per source file (analyzer **DX1000** + MSBuild target).
5. Everything must compile under **`TreatWarningsAsErrors`** and stay **trim/AOT-clean**.

`DateTime.Now`, `Math.Random`, LINQ, generics, etc. are all fine in application code.

---

## 2. Setup

### Packages
```bash
dotnet add package BlazorDX.Components   # styled components (pulls Primitives, Compute, Interop, SourceGen)
```

### Services — register in BOTH Program.cs files (server host + WASM client), so prerender works
```csharp
builder.Services.AddBlazorDXCompute();                        // grid compute (Rust in browser / managed fallback) + DOM interop
builder.Services.AddScoped<BlazorDX.Components.ToastService>(); // only if you use DxToast
// Your own data services: ALWAYS Scoped (never Singleton — DX1002)
builder.Services.AddScoped<MyAppStore>();
```

### Styles — link the sheets you use (App.razor `<head>`)
```html
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-theme.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-datagrid.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-overlay.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-input.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-form.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-chart.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-display.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-scheduler.css" />   <!-- DxScheduler / DxGantt -->
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-filemanager.css" /> <!-- DxFileManager -->
```

### Render modes & usings
Interactive pages start with `@rendermode InteractiveWebAssembly` (or `InteractiveServer`)
and `@using BlazorDX.Components`. Static-SSR content pages need no render mode.

---

## 3. Source generators (this is how you bind data — no reflection)

### `[GridRow]` → a `<TypeName>GridAccessor` for `DxDataGrid`
Decorate a **flat** row type (simple types: `string`, `int`, `double`, `bool`) with
`[GridRow]` and each column with `[GridColumn]`. The generator emits a zero-reflection
accessor named `<TypeName>GridAccessor`.

```csharp
using BlazorDX.Primitives.Grid;

[GridRow]
public sealed class PersonRow
{
    [GridColumn("ID", Order = 0)] public int Id { get; set; }
    [GridColumn("Name", Order = 1)] public string Name { get; set; } = "";
    [GridColumn("City", Order = 2)] public string City { get; set; } = "";
    [GridColumn("Score", Order = 3)] public double Score { get; set; }
}
// usage: private readonly PersonRowGridAccessor accessor = new();
```
For rich domain objects, project them to a flat `[GridRow]` row (string/int columns) and
keep the domain model separate.

### `[DxFormModel]` + DataAnnotations → a `<TypeName>FormModel` descriptor for `DxForm`
One model becomes a rendered form, a validation contract, **and** an MCP/AI tool. **Enum
fields render as `<select>` dropdowns automatically.** Validation comes from standard
`System.ComponentModel.DataAnnotations`.

```csharp
using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;

public enum Priority { Low, Medium, High }

[DxFormModel(Name = "create_ticket", Description = "Open a ticket.")]
public sealed class NewTicket
{
    [Required, StringLength(80, MinimumLength = 4)]
    [Display(Name = "Title", Order = 0, Prompt = "Short summary")]
    public string Title { get; set; } = "";

    [Required, StringLength(2000, MinimumLength = 10)]
    [Display(Name = "Description", Order = 1)]
    public string Description { get; set; } = "";

    [Display(Name = "Priority", Order = 2)]
    public Priority Priority { get; set; } = Priority.Medium;   // -> dropdown
}
// usage: private readonly NewTicketFormModel descriptor = new();
```

---

## 4. Key components — exact usage

### DxDataGrid<TRow>
```razor
@using BlazorDX.Primitives.Grid
<DxDataGrid TRow="PersonRow"
            Items="rows"
            Accessor="accessor"
            Selectable="true" Filterable="true"
            ShowColumnChooser="true" ShowFilterMenu="true"
            ShowExport="true" ExportFileName="rows.csv"
            ShowExcelExport="true" ExcelFileName="rows.xlsx"
            ShowPdfExport="true" PdfFileName="rows.pdf"
            ShowClipboard="true" KeyboardNavigation="true"
            PinnedColumns="1"
            SelectionChanged="OnSel"
            RowHeight="34" ViewportHeight="540">
    <DetailTemplate Context="r"><div>@r.Name — @r.City</div></DetailTemplate>
</DxDataGrid>
@code {
    private readonly PersonRowGridAccessor accessor = new();
    private IReadOnlyList<PersonRow> rows = Load();
    // open a row on selection:
    private void OnSel(IReadOnlyList<PersonRow> sel) { if (sel.Count > 0) Nav.NavigateTo($"/item/{sel[^1].Id}"); }
}
```
Related: `DxTreeGrid<TRow>` (hierarchy), `DxPivotGrid<TRow>` (cross-tab). Optional:
`GroupByColumn` (int), `Aggregations` (`Dictionary<int, GridAggregateKind>`), `Editable="true"`
with `RowEdited`, `@ref` + `CaptureState()`/`ApplyStateAsync()` for saved layouts.

### DxForm<TModel>
```razor
<DxForm TModel="NewTicket" Model="model" Descriptor="descriptor"
        ValidateOnChange="true" OnValidSubmit="Submit" SubmitText="Create">
    <DxFormSection Title="Details">
        <DxFormGrid Columns="2">
            <DxFormField Name="Title" />
            <DxFormField Name="Priority" />
        </DxFormGrid>
        <DxFormField Name="Description" />
    </DxFormSection>
</DxForm>
@code {
    private readonly NewTicket model = new();
    private readonly NewTicketFormModel descriptor = new();
    private void Submit(NewTicket form) { /* model is populated & validated */ }
}
```

### DxKanban (drag/drop board)
`KanbanColumn(title, cards)`, `KanbanCard(title, tag?)` — cards are reference types, so map
each card back to your entity by identity. `OnChange` fires (parameterless) after any move;
read the columns to persist.
```razor
<DxKanban Columns="columns" OnChange="OnChange" />
@code {
    private readonly List<KanbanColumn> columns = new();
    private readonly Dictionary<KanbanCard, int> cardToId = new();
    // build columns + cardToId in OnInitialized; in OnChange, for each column i and card,
    // store.SetStatus(cardToId[card], statusForColumn[i]);
}
```

### Charts (SVG, Rust-downsampled where relevant)
Every chart accepts one shared, generic data shape — `IReadOnlyList<ChartPoint>` — instead of a
bespoke type per chart (`ChartPoint(X, Y, Category, Y2, Y3, Y4, Series, Color)`; unused fields are
simply ignored). A bar/pie/funnel/sparkline reads `Category` + `Y`; a line/area/scatter chart reads
`X` + `Y`; a stacked-bar/radar chart also reads `Series`; a candlestick reads `Y`..`Y4` as
Open/High/Low/Close; a waterfall reads `Y` as a delta unless `Y2` is set (an absolute total that
resets the running total); a bubble chart reads `Y2` as radius; a heatmap reads `Series` as the row
key and `Category` as the column key. Every mark animates in (staggered fade/rise; line/area wipe
in left-to-right), and `DxBarChart`/`DxWaterfallChart` take an opt-in `Gradient` bool.
```razor
<DxBarChart Points="bars" Width="420" Height="220" Gradient="true" />   @* new ChartPoint(Category: "Label", Y: 18) *@
<DxPieChart Points="bars" Donut="true" />
<DxLineChart Points="series" Threshold="400" Width="720" Height="240" />   @* new ChartPoint(X: i, Y: v) *@
<DxRadialGauge Value="72" Label="CPU %" Size="120" Color="#16a34a" Format="0" />
<DxStackedBarChart Categories="quarters" Points="points" />   @* new ChartPoint(Category: "Q1", Y: 18, Series: "Direct") *@
<DxSparkline Points="@(new[]{ new ChartPoint(Y: 4), new ChartPoint(Y: 7) })" />
<DxWaterfallChart Points="points" />   @* new ChartPoint(Category: "Start", Y2: 100) then new ChartPoint(Category: "Gain", Y: 30) *@
<DxBubbleChart Points="points" MinRadius="6" MaxRadius="32" />   @* new ChartPoint(X: 1, Y: 2, Y2: 40) — Y2 sizes the bubble *@
<DxHeatmap Points="points" ShowValues="true" />   @* new ChartPoint(Category: "Mon", Series: "Team A", Y: 3) *@
<DxBulletChart Points="rows" />   @* new BulletPoint("Revenue", Value: 84, Target: 90, Max: 100, Ranges: [40, 75]) — not a ChartPoint *@
```

Four more don't fit the flat `ChartPoint` shape at all — a tree, a raw-sample group, and a node/link
graph each get their own type, but reuse `OnPointSelected`-style opt-in selection where it applies:
```razor
<DxTreemap Root="tree" Width="640" Height="280" OnNodeSelected="OnNode" />
<DxSunburst Root="tree" Size="320" />
@* tree: new ChartTreeNode("All", Children: [new("Web", Children: [new("Search", Value: 40)]), new("Mobile", Value: 30)]) *@

<DxBoxPlot Groups="groups" Width="640" Height="280" Violin="true" />
@* groups: new BoxPlotGroup("Control", rawSamples) — Q1/median/Q3/outliers computed from raw values, not pre-aggregated *@

<DxSankeyChart Nodes="nodes" Links="links" Width="640" Height="280" />
@* nodes: new SankeyNode("a", "Visited"); links: new SankeyLink("a", "b", Value: 1000) — layered layout, longest-path-from-source *@

<DxNetworkGraph Nodes="graphNodes" Edges="graphEdges" Width="640" Height="360" OnNodeSelected="OnNode" />
@* graphNodes: new GraphNode("api", "API"); graphEdges: new GraphEdge("api", "auth") — force-directed, plain C#, no Rust *@

<DxParallelCoordinates Axes="@(new[]{"Speed","Power"})" Rows="rows" Width="640" Height="280" />
@* rows: new ParallelCoordinateRow("Model A", [80, 55]) — per-axis min/max normalized *@

<DxWordCloud Words="words" Width="640" Height="300" />
@* words: new WordCloudEntry("Blazor", Weight: 100) — spiral-packed, largest first *@

<DxChordDiagram Nodes="chordNodes" Links="chordLinks" Size="380" />
@* chordNodes: new ChordNode("US"); chordLinks: new ChordLink(From: 0, To: 1, Value: 40) — indices into Nodes, not ids *@
```

### Overlays, inputs, feedback
```razor
<DxDialog @bind-Open="open" Title="Confirm">…</DxDialog>
<DxSelect TValue="string" @bind-Value="choice" />
<DxTabs> … </DxTabs>
@inject ToastService Toasts            @* Toasts.Show("Saved.", "success" | "error" | "warning"); *@
<DxToastHost />                          @* place once per page that shows toasts *@
<DxAlert Severity="warning">Heads up.</DxAlert>
<DxSpinner /> <DxProgress Value="65" /> <DxSkeleton Width="70%" Height="1rem" />
```

### DxCalendar (inline month calendar)
A standalone, always-visible month calendar (distinct from `DxDatePicker`, which is a popup input).
`SelectionMode` is `Single` (`@bind-Value`) or `Range` (`@bind-RangeStart` / `@bind-RangeEnd`). Optional
`Min`/`Max`, an `IsDateDisabled` predicate, a `MarkedDates` set (dot decoration), and a `DayTemplate`
fragment. Week starts on the culture's first day; full ARIA-grid keyboard nav.
```razor
@using BlazorDX.Primitives.Inputs
<DxCalendar @bind-Value="picked" MarkedDates="marked" />

<DxCalendar SelectionMode="CalendarSelectionMode.Range"
            @bind-RangeStart="from" @bind-RangeEnd="to"
            Min="@DateOnly.FromDateTime(DateTime.Today)"
            IsDateDisabled="@(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)"
            OnRangeSelected="r => { /* r.Start, r.End */ }" />
@code {
    private DateOnly? picked = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly? from, to;
    private readonly IReadOnlyCollection<DateOnly> marked = new[] { DateOnly.FromDateTime(DateTime.Today) };
}
```

### DxScheduler (Week / Month / Day calendar)
`SchedulerEvent(Title, Start, End, Color?, Category?, Recurrence?)`. Bind `WeekStart` and `View`.
Set a `Recurrence` to repeat an event; the scheduler expands it for the visible window. On the
time grid, drag an event to move it and drag empty grid to create — handle the results and mutate
your own event list (a recurrence occurrence is not directly draggable). Category renders as text,
never colour alone.
```razor
@using BlazorDX.Primitives.Scheduling
<DxScheduler Events="events" @bind-WeekStart="weekStart" @bind-View="view"
             StartHour="8" EndHour="18"
             OnEventMoved="OnMoved" OnRangeCreated="OnCreated" OnEventSelected="OnSel" />
@code {
    private DateOnly weekStart = DateOnly.FromDateTime(DateTime.Today);
    private SchedulerView view = SchedulerView.Week;
    // The seed's time-of-day + duration anchor every occurrence; ByWeekday picks the days.
    private readonly List<SchedulerEvent> events = new()
    {
        new SchedulerEvent("Standup",
            DateTime.Today.AddHours(9), DateTime.Today.AddHours(9.5), Category: "Meeting",
            Recurrence: new Recurrence(RecurrenceFrequency.Weekly,
                ByWeekday: new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday })),
    };
    private void OnMoved(SchedulerEventMove m)   // move an existing (non-recurring) event
    {
        int i = events.IndexOf(m.Original);
        if (i >= 0) events[i] = m.Original with { Start = m.NewStart, End = m.NewEnd };
    }
    private void OnCreated(SchedulerRange r) => events.Add(new SchedulerEvent("New", r.Start, r.End));
    private void OnSel(SchedulerEvent e) { /* open/edit */ }
}
```

### DxFileManager (two-pane tree + upload, optional integrity)
Drag-and-drop is a hybrid enhancement with keyboard-equivalent paths. `OnUpload` gives real
`IBrowserFile` streams. Set `VerifyIntegrity="true"` to hash each upload in the browser (Web
Crypto, SHA-256 default) and re-hash it server-side, reporting a per-file `FileIntegrityResult`
through `OnUploadVerified` — refuse to write a file whose `Verified` is false.
```razor
<DxFileManager Roots="roots" VerifyIntegrity="true"
               OnUpload="OnUpload" OnUploadVerified="OnVerified" OnItemMove="OnMove" />
@code {
    private void OnVerified(IReadOnlyList<FileIntegrityResult> results)
    {
        foreach (var r in results.Where(x => !x.Verified)) { /* reject r.Name; do not persist */ }
    }
}
```
Server-side, re-hash any stream with the same primitive: `BlazorDX.Primitives.Files.FileHasher`
(`ComputeHexAsync` / `VerifyAsync`) — streaming `IncrementalHash`, constant-time compare.

---

## 5. Full component catalog

- **Overlays:** DxDialog, DxSheet, DxPopover, DxTooltip, DxMenu, DxContextMenu, DxCommandPalette
- **Inputs:** DxSelect<T>, DxListbox<T>, DxComboBox<T>, DxTransferList, DxCheckbox, DxSwitch,
  DxRadioGroup<T>, DxTextBox, DxTextArea, DxPassword, DxNumeric<T>, DxRating, DxDatePicker
- **Nav/layout:** DxTabs, DxAccordion, DxBreadcrumbs, DxDivider, DxDrawer, DxTimeline,
  DxCarousel, DxPager, DxStepper, DxTileLayout, DxSortableList, DxVirtualize<T>, DxThemeProvider
- **Grids:** DxDataGrid<TRow>, DxTreeGrid<TRow>, DxPivotGrid<TRow>
- **Charts:** DxLineChart, DxAreaChart, DxBarChart, DxPieChart, DxHistogram, DxSparkline,
  DxRadialGauge, DxLinearGauge, DxStackedBarChart, DxScatterChart, DxRadarChart, DxFunnelChart,
  DxCandlestickChart, DxWaterfallChart, DxBubbleChart, DxHeatmap, DxBulletChart, DxTreemap,
  DxSunburst, DxBoxPlot, DxSankeyChart, DxNetworkGraph, DxParallelCoordinates, DxWordCloud,
  DxChordDiagram
- **Scheduling:** DxCalendar, DxScheduler, DxGantt
- **Editors/files/AI:** DxMarkdown, DxMarkdownEditor, DxRichTextEditor, DxChat, DxFileManager
- **Documents & reporting:** DxDocumentViewer (core; PDF/embed), DxSpreadsheetViewer (Excel
  viewer/editor, `BlazorDX.Documents`), DxWordViewer/DxWordEditor (`BlazorDX.Documents`),
  DxReportViewer (SSRS over HTMX, `BlazorDX.Integrations.Reporting`), DxPowerBiReport
  (`BlazorDX.Integrations.PowerBI`), DxHtmxDocumentViewer (static-SSR, `BlazorDX.Htmx`)
- **Feedback:** DxToastHost, DxAlert, DxSpinner, DxProgress, DxSkeleton

---

## 6. Patterns & gotchas (write code that compiles & behaves)

- **State store = Scoped service**, injected with `@inject`. Never `Singleton` (DX1002).
- **Grid rows are flat projections.** Don't put enums/objects in `[GridColumn]`s — expose
  `string`/`int`. Keep the rich model separate and `.ToRow()` into the grid row.
- **DxForm enum fields → dropdowns** automatically; don't build manual selects for enums.
- **Open a record from the grid** via `SelectionChanged` → `NavigationManager.NavigateTo`.
- **DxKanban** mutates the column/card lists in place and raises parameterless `OnChange`;
  reconcile state by mapping cards to ids via reference identity.
- **`<DxToastHost />`** must be present on the page for `ToastService.Show(...)` to render.
- **Derive, don't store, computed values** (e.g. a priority from impact×urgency) as `=>` props.
- **Prefer `DxMarkdown`** over `MarkupString` for any rich text (DX1001).
- Reference a record route as a clickable id; the grid/accessor reads cells with no reflection.

---

## 7. Minimal end-to-end example

```razor
@page "/people"
@rendermode InteractiveWebAssembly
@using BlazorDX.Components
@using BlazorDX.Primitives.Grid
@inject NavigationManager Nav

<DxDataGrid TRow="PersonRow" Items="rows" Accessor="accessor"
            Selectable="true" Filterable="true" ShowExport="true"
            SelectionChanged="OnSel" ViewportHeight="480" />

@code {
    private readonly PersonRowGridAccessor accessor = new();
    private IReadOnlyList<PersonRow> rows = Seed();
    private void OnSel(IReadOnlyList<PersonRow> s) { if (s.Count > 0) Nav.NavigateTo($"/person/{s[^1].Id}"); }
    private static IReadOnlyList<PersonRow> Seed() =>
        new List<PersonRow> { new() { Id = 1, Name = "Ada", City = "London", Score = 9.5 } };
}
```

For a full real-world example, see the **TicketDesk** ITIL service-desk app under
`samples/BlazorDX.Demo/BlazorDX.Demo.Client/TicketDesk` (DataGrid, Kanban, charts, source-gen
forms, an in-memory Scoped store) — live at `/app`.
