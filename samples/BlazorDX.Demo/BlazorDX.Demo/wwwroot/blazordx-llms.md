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
```razor
<DxBarChart Bars="bars" Width="420" Height="220" />     @* IReadOnlyList<ChartBar>: new ChartBar("Label", 18) *@
<DxPieChart Slices="bars" Donut="true" />
<DxLineChart X="xs" Y="ys" Threshold="400" Width="720" Height="240" />
<DxRadialGauge Value="72" Label="CPU %" Size="120" Color="#16a34a" Format="0" />
<DxStackedBarChart Categories="quarters" Series="series" />  @* ChartSeries(name, double[]) *@
<DxSparkline Values="@(new double[]{4,7,5,9,6})" />
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
  DxCandlestickChart
- **Scheduling:** DxScheduler, DxGantt
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
