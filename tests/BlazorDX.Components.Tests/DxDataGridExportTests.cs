using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>CSV export: content, ordering, escaping, and respecting active filters.</summary>
public sealed class DxDataGridExportTests : TestContext
{
    // Records the last download so the test can assert the generated CSV.
    private sealed class RecordingDom : IGridDomInterop
    {
        public string? File { get; private set; }
        public string? Mime { get; private set; }
        public string? Content { get; private set; }
        public byte[]? Bytes { get; private set; }
        public string? Clipboard { get; private set; }

        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask<(double, double, double)> MeasureViewportAsync(string id) =>
            ValueTask.FromResult<(double, double, double)>((0, 0, 0));
        public ValueTask<(double, double, double, double)> MeasureViewport2dAsync(string id) =>
            ValueTask.FromResult<(double, double, double, double)>((0, 0, 0, 0));
        public ValueTask SubscribeScrollAsync(string id, Action onScroll) => ValueTask.CompletedTask;
        public ValueTask FocusFirstAsync(string id) => ValueTask.CompletedTask;
        public ValueTask DownloadTextAsync(string filename, string mime, string content)
        {
            File = filename;
            Mime = mime;
            Content = content;
            return ValueTask.CompletedTask;
        }

        public ValueTask DownloadBytesAsync(string filename, string mime, byte[] content)
        {
            File = filename;
            Mime = mime;
            Bytes = content;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> WriteClipboardAsync(string text)
        {
            Clipboard = text;
            return ValueTask.FromResult(true);
        }

        public ValueTask ScrollToAsync(string elementId, double top) => ValueTask.CompletedTask;

        public ValueTask SuppressArrowKeysAsync(string elementId) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Copy_writes_selected_rows_as_tsv_to_the_clipboard()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Selectable, true)
            .Add(g => g.ShowClipboard, true));

        // Select the first row, then copy.
        grid.FindAll(".dx-grid-row .dx-grid-select")[0].Change(true);
        grid.Find(".dx-grid-export").Click();   // the Copy button shares the toolbar button style

        // Header + the one selected row, tab-separated.
        Assert.Equal("Name\tQuantity\nAlpha\t10\n", dom.Clipboard);
    }

    [Fact]
    public void Copy_without_a_selection_copies_all_visible_rows()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowClipboard, true));

        grid.Find(".dx-grid-export").Click();

        Assert.Equal("Name\tQuantity\nAlpha\t10\nBeta\t20\nGamma\t30\n", dom.Clipboard);
    }

    private readonly RecordingDom dom = new();

    public DxDataGridExportTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop>(_ => dom);
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Alpha", Quantity = 10 },
        new() { Name = "Beta", Quantity = 20 },
        new() { Name = "Gamma", Quantity = 30 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render(IEnumerable<WidgetRow> rows, bool export = true) =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, rows.ToList())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowExport, export)
            .Add(g => g.ExportFileName, "out.csv"));

    [Fact]
    public void Export_button_renders_only_when_enabled()
    {
        Assert.Single(Render(Rows()).FindAll(".dx-grid-export"));
        Assert.Empty(Render(Rows(), export: false).FindAll(".dx-grid-export"));
    }

    [Fact]
    public void Clicking_export_downloads_csv_with_header_and_rows()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(Rows());

        grid.Find(".dx-grid-export").Click();

        Assert.Equal("out.csv", dom.File);
        Assert.StartsWith("text/csv", dom.Mime);
        Assert.Equal("Name,Quantity\r\nAlpha,10\r\nBeta,20\r\nGamma,30\r\n", dom.Content);
    }

    [Fact]
    public void Csv_quotes_fields_with_commas_and_quotes()
    {
        var rows = new List<WidgetRow> { new() { Name = "A,\"B\"", Quantity = 1 } };
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(rows);

        grid.Find(".dx-grid-export").Click();

        // Comma + embedded quotes -> field is quoted, inner quotes doubled.
        Assert.Equal("Name,Quantity\r\n\"A,\"\"B\"\"\",1\r\n", dom.Content);
    }

    [Fact]
    public void Export_respects_the_active_filter()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Filterable, true)
            .Add(g => g.ShowExport, true));

        // Filter the Name column to "et" -> only "Beta".
        grid.FindAll(".dx-grid-filter-input")[0].Input("et");
        grid.Find(".dx-grid-export").Click();

        Assert.Equal("Name,Quantity\r\nBeta,20\r\n", dom.Content);
    }

    private static string SheetXml(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Excel_export_button_renders_only_when_enabled()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> on = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowExcelExport, true));
        Assert.Contains(on.FindAll(".dx-grid-export"), b => b.TextContent.Contains("Excel"));

        IRenderedComponent<DxDataGrid<WidgetRow>> off = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor()));
        Assert.Empty(off.FindAll(".dx-grid-export"));
    }

    [Fact]
    public void Clicking_excel_export_downloads_a_valid_xlsx_with_the_data()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowExcelExport, true)
            .Add(g => g.ExcelFileName, "widgets.xlsx"));

        grid.Find(".dx-grid-export").Click();

        Assert.Equal("widgets.xlsx", dom.File);
        Assert.StartsWith("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", dom.Mime);
        Assert.NotNull(dom.Bytes);

        string sheet = SheetXml(dom.Bytes!);
        // Bold header row + a numeric Quantity cell prove styling and number typing.
        Assert.Contains("<is><t xml:space=\"preserve\">Name</t></is>", sheet);
        Assert.Contains("<c r=\"A1\" s=\"1\" t=\"inlineStr\">", sheet);
        Assert.Contains("<c r=\"B2\"><v>10</v></c>", sheet);
        Assert.Contains("<is><t xml:space=\"preserve\">Gamma</t></is>", sheet);
    }

    [Fact]
    public void Excel_export_respects_the_active_filter()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Filterable, true)
            .Add(g => g.ShowExcelExport, true));

        grid.FindAll(".dx-grid-filter-input")[0].Input("et");   // -> only "Beta"
        grid.Find(".dx-grid-export").Click();

        string sheet = SheetXml(dom.Bytes!);
        Assert.Contains("Beta", sheet);
        Assert.DoesNotContain("Alpha", sheet);
        Assert.DoesNotContain("Gamma", sheet);
    }

    private static string PdfText(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Pdf_export_button_renders_only_when_enabled()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> on = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowPdfExport, true));
        Assert.Contains(on.FindAll(".dx-grid-export"), b => b.TextContent.Contains("PDF"));

        IRenderedComponent<DxDataGrid<WidgetRow>> off = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor()));
        Assert.Empty(off.FindAll(".dx-grid-export"));
    }

    [Fact]
    public void Clicking_pdf_export_downloads_a_valid_pdf_with_the_data()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowPdfExport, true)
            .Add(g => g.PdfFileName, "widgets.pdf"));

        grid.Find(".dx-grid-export").Click();

        Assert.Equal("widgets.pdf", dom.File);
        Assert.StartsWith("application/pdf", dom.Mime);
        Assert.NotNull(dom.Bytes);

        string pdf = PdfText(dom.Bytes!);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("(Name) Tj", pdf);
        Assert.Contains("(Alpha) Tj", pdf);
    }

    [Fact]
    public void Pdf_export_respects_the_active_filter()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Filterable, true)
            .Add(g => g.ShowPdfExport, true));

        grid.FindAll(".dx-grid-filter-input")[0].Input("et");   // -> only "Beta"
        grid.Find(".dx-grid-export").Click();

        string pdf = PdfText(dom.Bytes!);
        Assert.Contains("(Beta) Tj", pdf);
        Assert.DoesNotContain("(Alpha) Tj", pdf);
        Assert.DoesNotContain("(Gamma) Tj", pdf);
    }
}
