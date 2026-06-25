using System.IO.Compression;
using System.Text;
using AngleSharp.Dom;
using BlazorDX.Htmx;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The static-SSR + HTMX read-only document viewer. These tests assert the
/// <b>no-JS-critical</b> contract — sheet tabs and the pager are real anchors carrying
/// both an <c>href</c> (full-page navigation when JS is off) and matching
/// <c>hx-get</c>/<c>hx-select</c> attributes (htmx enhancement) — plus the semantic
/// structure axe cannot fully cover: <c>th[scope]</c> + <c>td</c> grids, real heading
/// tags out of OOXML, and a PDF <c>&lt;embed title&gt;</c> with a download link.
/// </summary>
public sealed class DxHtmxDocumentViewerTests : TestContext
{
    // ---- Excel ----------------------------------------------------------

    [Fact]
    public void Excel_renders_a_grid_with_th_scope_col_and_td_cells()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Excel)
            .Add(v => v.Bytes, Xlsx(("Sheet1", [["Name", "Role"], ["Ada", "Analyst"]]))));

        IElement table = viewer.Find("table.dx-htmxdoc-grid");
        Assert.Equal("grid", table.GetAttribute("role"));

        IHtmlCollection<IElement> colHeaders = table.QuerySelectorAll("thead th");
        Assert.Equal(["Name", "Role"], colHeaders.Select(h => h.TextContent).ToArray());
        Assert.All(colHeaders, th => Assert.Equal("col", th.GetAttribute("scope")));

        // Data cells are <td>; each data row carries a <th scope="row"> gutter label.
        IElement row = table.QuerySelector("tbody tr")!;
        Assert.Equal("row", row.QuerySelector("th")!.GetAttribute("scope"));
        Assert.Equal(["Ada", "Analyst"], row.QuerySelectorAll("td").Select(c => c.TextContent).ToArray());
    }

    [Fact]
    public void Excel_sheet_tabs_are_real_anchors_with_both_href_and_hx_get()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Excel)
            .Add(v => v.Endpoint, "/htmx/doc?kind=excel")
            .Add(v => v.Bytes, Xlsx(
                ("Ledger", [["A"], ["1"]]),
                ("Summary", [["B"], ["2"]]))));

        IElement[] tabs = viewer.FindAll("nav.dx-htmxdoc-tabs a").ToArray();
        Assert.Equal(2, tabs.Length);

        foreach (IElement tab in tabs)
        {
            // The no-JS floor: a real, navigable href.
            string href = tab.GetAttribute("href")!;
            Assert.StartsWith("/htmx/doc?kind=excel", href, StringComparison.Ordinal);
            Assert.Contains("sheet=", href, StringComparison.Ordinal);

            // And the htmx enhancement: same URL, swap just this fragment.
            Assert.Equal(href, tab.GetAttribute("hx-get"));
            Assert.Equal("#dx-htmxdoc", tab.GetAttribute("hx-target"));
            Assert.Equal("#dx-htmxdoc", tab.GetAttribute("hx-select"));
            Assert.Equal("outerHTML", tab.GetAttribute("hx-swap"));
        }

        // The active sheet is marked for AT without JS.
        Assert.Equal("true", tabs[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Excel_honours_the_active_sheet_index()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Excel)
            .Add(v => v.SheetIndex, 1)
            .Add(v => v.Bytes, Xlsx(
                ("Ledger", [["A"], ["1"]]),
                ("Summary", [["B"], ["2"]]))));

        // Sheet 1 ("Summary") is active: its header "B" shows, the other sheet's does not.
        Assert.Equal("B", viewer.Find("thead th").TextContent);
        IElement[] tabs = viewer.FindAll("nav.dx-htmxdoc-tabs a").ToArray();
        Assert.Null(tabs[0].GetAttribute("aria-current"));
        Assert.Equal("true", tabs[1].GetAttribute("aria-current"));
    }

    [Fact]
    public void Excel_pager_is_a_real_link_when_rows_exceed_the_page_size()
    {
        List<IReadOnlyList<string>> rows = [["N"]];
        for (int i = 0; i < 10; i++)
        {
            rows.Add([i.ToString()]);
        }

        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Excel)
            .Add(v => v.Endpoint, "/htmx/doc?kind=excel")
            .Add(v => v.PageSize, 4)
            .Add(v => v.Bytes, Xlsx(("Big", rows))));

        IElement next = viewer.Find("nav.dx-htmxdoc-pager a.dx-htmxdoc-page");
        string href = next.GetAttribute("href")!;
        Assert.Contains("page=1", href, StringComparison.Ordinal);
        Assert.Equal(href, next.GetAttribute("hx-get"));

        // Page 1 (the default) shows 4 data rows, not all 10.
        Assert.Equal(4, viewer.FindAll("tbody tr").Count);
    }

    // ---- Word -----------------------------------------------------------

    [Fact]
    public void Word_renders_semantic_headings_emphasis_lists_and_table()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Word)
            .Add(v => v.Bytes, Docx()));

        Assert.Equal("Quarterly Report", viewer.Find("h1").TextContent);
        Assert.Equal("Overview", viewer.Find("h2").TextContent);

        IElement para = viewer.Find("p.dx-htmxdoc-para");
        Assert.Equal("strong", para.QuerySelector("strong")!.TextContent);
        Assert.Equal("steady", para.QuerySelector("em")!.TextContent);

        Assert.Equal(2, viewer.Find("ul.dx-htmxdoc-list").QuerySelectorAll("li").Length);

        IElement wordTable = viewer.Find("table.dx-htmxdoc-wordtable");
        Assert.All(wordTable.QuerySelectorAll("thead th"),
            th => Assert.Equal("col", th.GetAttribute("scope")));
    }

    [Fact]
    public void Word_region_is_a_focusable_labelled_document()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Word)
            .Add(v => v.Name, "Report")
            .Add(v => v.Bytes, Docx()));

        IElement region = viewer.Find(".dx-htmxdoc-word");
        Assert.Equal("document", region.GetAttribute("role"));
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("Report", region.GetAttribute("aria-label"));
    }

    [Fact]
    public void Word_text_is_html_encoded_not_raw_markup()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Word)
            .Add(v => v.Bytes, DocxParagraph("<script>alert(1)</script>")));

        Assert.Empty(viewer.FindAll("script"));
        Assert.Contains("&lt;script&gt;", viewer.Markup, StringComparison.Ordinal);
    }

    // ---- PDF ------------------------------------------------------------

    [Fact]
    public void Pdf_renders_an_embed_with_a_title_and_a_download_link()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Pdf)
            .Add(v => v.Name, "report.pdf")
            .Add(v => v.Source, "/files/report.pdf"));

        IElement embed = viewer.Find("embed.dx-htmxdoc-pdf");
        Assert.Equal("application/pdf", embed.GetAttribute("type"));
        Assert.Equal("report.pdf", embed.GetAttribute("title"));
        Assert.Equal("/files/report.pdf", embed.GetAttribute("src"));

        IElement download = viewer.Find("a.dx-htmxdoc-download");
        Assert.Equal("/files/report.pdf", download.GetAttribute("href"));
        Assert.Equal("report.pdf", download.GetAttribute("download"));
    }

    [Fact]
    public void Pdf_rejects_an_unsafe_source()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Pdf)
            .Add(v => v.Source, "javascript:alert(1)"));

        Assert.Empty(viewer.FindAll("embed"));
        Assert.Contains("unavailable", viewer.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/files/a.pdf", true)]
    [InlineData("https://example.com/a.pdf", true)]
    [InlineData("data:application/pdf;base64,AAAA", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>", false)]
    [InlineData("", false)]
    public void IsSafeSource_enforces_the_scheme_allowlist(string source, bool expected) =>
        Assert.Equal(expected, DxHtmxDocumentViewer.IsSafeSource(source));

    // ---- Endpoint scheme guard (defense-in-depth, Fix 2) ----

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("vbscript:msgbox(1)")]
    public void Endpoint_rejects_a_dangerous_scheme(string hostile)
    {
        // Endpoint becomes the href / hx-get on every tab and pager link; a dangerous
        // scheme there is a script-injection vector, so it must be rejected at set-time.
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            RenderComponent<DxHtmxDocumentViewer>(p => p
                .Add(v => v.Kind, HtmxDocumentKind.Excel)
                .Add(v => v.Endpoint, hostile)
                .Add(v => v.Bytes, Xlsx(
                    ("A", [["x"], ["1"]]),
                    ("B", [["y"], ["2"]])))));

        Assert.Equal("Endpoint", ex.ParamName);
    }

    [Theory]
    [InlineData("/htmx/doc?kind=excel")]
    [InlineData("htmx/doc")]
    [InlineData("https://reports.example.com/doc")]
    [InlineData("")]
    public void Endpoint_accepts_relative_and_http_values(string ok)
    {
        // The demo's own relative value, a bare relative path, an http URL, and the
        // empty fall-back-to-current-path all pass — links stay same-origin where given.
        Exception? ex = Record.Exception(() =>
            RenderComponent<DxHtmxDocumentViewer>(p => p
                .Add(v => v.Kind, HtmxDocumentKind.Excel)
                .Add(v => v.Endpoint, ok)
                .Add(v => v.Bytes, Xlsx(
                    ("A", [["x"], ["1"]]),
                    ("B", [["y"], ["2"]])))));

        Assert.Null(ex);
    }

    [Fact]
    public void Tab_and_form_links_stay_same_origin_for_a_relative_endpoint()
    {
        IRenderedComponent<DxHtmxDocumentViewer> viewer = RenderComponent<DxHtmxDocumentViewer>(p => p
            .Add(v => v.Kind, HtmxDocumentKind.Excel)
            .Add(v => v.Endpoint, "/htmx/doc?kind=excel")
            .Add(v => v.Bytes, Xlsx(
                ("A", [["x"], ["1"]]),
                ("B", [["y"], ["2"]]))));

        foreach (IElement tab in viewer.FindAll("nav.dx-htmxdoc-tabs a"))
        {
            string href = tab.GetAttribute("href")!;
            Assert.DoesNotContain("://", href);
            Assert.False(href.StartsWith("//", StringComparison.Ordinal));
            Assert.Equal(href, tab.GetAttribute("hx-get"));
        }
    }

    // ---- Minimal OOXML byte builders (kept tiny; exercise the real parsers) ----

    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string WordprocessingMl =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Rels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Xlsx(params (string Name, IReadOnlyList<IReadOnlyList<string>> Rows)[] sheets)
    {
        StringBuilder workbook = new();
        StringBuilder relsXml = new();
        workbook.Append($"<workbook xmlns=\"{SpreadsheetMl}\" xmlns:r=\"{Rels}\"><sheets>");
        relsXml.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder types = new();
            types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

            for (int s = 0; s < sheets.Length; s++)
            {
                string part = $"worksheets/sheet{s + 1}.xml";
                workbook.Append($"<sheet name=\"{Esc(sheets[s].Name)}\" sheetId=\"{s + 1}\" r:id=\"rId{s + 1}\"/>");
                relsXml.Append($"<Relationship Id=\"rId{s + 1}\" Type=\"{Rels}/worksheet\" Target=\"{part}\"/>");
                types.Append($"<Override PartName=\"/xl/{part}\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                Add(zip, $"xl/{part}", Sheet(sheets[s].Rows));
            }

            types.Append("</Types>");
            workbook.Append("</sheets></workbook>");
            relsXml.Append("</Relationships>");

            Add(zip, "[Content_Types].xml", types.ToString());
            Add(zip, "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rIdW\" Type=\"{Rels}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", workbook.ToString());
            Add(zip, "xl/_rels/workbook.xml.rels", relsXml.ToString());
        }

        return stream.ToArray();
    }

    private static string Sheet(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        StringBuilder sb = new();
        sb.Append($"<worksheet xmlns=\"{SpreadsheetMl}\"><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            foreach (string cell in rows[r])
            {
                sb.Append($"<c t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(cell)}</t></is></c>");
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static byte[] Docx()
    {
        string body =
            Heading(1, "Quarterly Report") +
            Heading(2, "Overview") +
            "<w:p><w:r><w:t xml:space=\"preserve\">This was </w:t></w:r>" +
            "<w:r><w:rPr><w:b/></w:rPr><w:t>strong</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\"> and </w:t></w:r>" +
            "<w:r><w:rPr><w:i/></w:rPr><w:t>steady</w:t></w:r><w:r><w:t>.</w:t></w:r></w:p>" +
            ListItem("First") + ListItem("Second") +
            "<w:tbl>" + Tr("Name", "Role") + Tr("Ada", "Analyst") + "</w:tbl>";
        return DocxBody(body);
    }

    private static byte[] DocxParagraph(string text) =>
        DocxBody($"<w:p><w:r><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>");

    private static byte[] DocxBody(string body)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rels}/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml",
                $"<w:document xmlns:w=\"{WordprocessingMl}\"><w:body>{body}</w:body></w:document>");
        }

        return stream.ToArray();
    }

    private static string Heading(int level, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr><w:r><w:t>{Esc(text)}</w:t></w:r></w:p>";

    private static string ListItem(string text) =>
        $"<w:p><w:pPr><w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr><w:r><w:t>{Esc(text)}</w:t></w:r></w:p>";

    private static string Tr(string a, string b) =>
        $"<w:tr><w:tc><w:p><w:r><w:t>{Esc(a)}</w:t></w:r></w:p></w:tc>" +
        $"<w:tc><w:p><w:r><w:t>{Esc(b)}</w:t></w:r></w:p></w:tc></w:tr>";

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static void Add(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }
}
