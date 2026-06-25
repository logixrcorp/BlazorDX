using BlazorDX.Integrations.Reporting;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// bUnit coverage for <see cref="DxReportViewer"/>: embed-mode iframe (accessible
/// title + URL-Access URL), render-mode parameter form built from metadata (labeled
/// inputs, prefilled defaults, a select for valid-value lists, required markers),
/// the accessible export links, and the no-JS form controls (real action/href).
/// </summary>
public sealed class DxReportViewerTests : TestContext
{
    // A canned parameter source mirroring the mock catalog's /Sales/Monthly:
    // Region (multi-value, closed valid-value list, required) + Year (default 2026).
    private sealed class FakeParameterSource : IReportParameterSource
    {
        public Task<IReadOnlyList<ReportParameter>> GetParametersAsync(
            string reportPath, CancellationToken ct = default)
        {
            IReadOnlyList<ReportParameter> parameters = new[]
            {
                new ReportParameter(
                    "Region", ReportParameterDataType.String, "Sales region",
                    Nullable: false, MultiValue: true, Required: true,
                    DefaultValue: null,
                    ValidValues: new[] { "West", "East", "North", "South" }),
                new ReportParameter(
                    "Year", ReportParameterDataType.Integer, "Fiscal year",
                    Nullable: false, MultiValue: false, Required: false,
                    DefaultValue: "2026", ValidValues: null),
            };

            return Task.FromResult(parameters);
        }
    }

    public DxReportViewerTests()
    {
        Services.AddSingleton(new ReportingOptions { ServerUrl = "https://rs.example.com/ReportServer" });
        Services.AddSingleton<IReportParameterSource>(new FakeParameterSource());
    }

    private IRenderedComponent<DxReportViewer> RenderEmbed() =>
        RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Embed)
            .Add(c => c.RenderBasePath, "/ReportServer"));

    private IRenderedComponent<DxReportViewer> RenderForm() =>
        RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Render)
            .Add(c => c.RenderEndpoint, "/reports")
            .Add(c => c.RenderBasePath, "/ReportServer"));

    // ---- Embed mode ----

    [Fact]
    public void Embed_renders_an_iframe_with_a_non_empty_title()
    {
        var v = RenderEmbed();

        var frame = v.Find("iframe.dx-report-frame");
        string? title = frame.GetAttribute("title");
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.Equal("Monthly report", title);
    }

    [Fact]
    public void Embed_iframe_src_is_the_url_access_render_url()
    {
        var v = RenderEmbed();

        string src = v.Find("iframe.dx-report-frame").GetAttribute("src")!;
        Assert.StartsWith("/ReportServer?", src);
        Assert.Contains("%2fSales%2fMonthly", src);
        Assert.Contains("rs:Command=Render", src);
        Assert.Contains("rs:Format=HTML5", src);
        Assert.Contains("rc:Toolbar=true", src);
    }

    [Fact]
    public void Embed_toolbar_can_be_turned_off()
    {
        var v = RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Embed)
            .Add(c => c.RenderBasePath, "/ReportServer")
            .Add(c => c.ShowToolbar, false));

        Assert.Contains("rc:Toolbar=false", v.Find("iframe.dx-report-frame").GetAttribute("src"));
    }

    [Fact]
    public void Embed_offers_accessible_export_links_to_the_render_url()
    {
        var v = RenderEmbed();

        var pdf = v.Find("a.dx-report-export-pdf");
        Assert.Contains("rs:Format=PDF", pdf.GetAttribute("href"));
        Assert.Contains("noopener", pdf.GetAttribute("rel"));

        var csv = v.Find("a.dx-report-export-csv");
        Assert.Contains("rs:Format=CSV", csv.GetAttribute("href"));
    }

    // ---- Render mode: the parameter form ----

    [Fact]
    public void Render_builds_a_labeled_input_per_parameter()
    {
        var v = RenderForm();

        // Each parameter has a label tied to its control by for/id (WCAG 3.3.2).
        var labels = v.FindAll("label.dx-report-label");
        Assert.Equal(2, labels.Count);
        Assert.Contains(labels, l => l.TextContent.Contains("Sales region"));
        Assert.Contains(labels, l => l.TextContent.Contains("Fiscal year"));

        // The Region label points at an element that exists.
        var regionLabel = labels.First(l => l.TextContent.Contains("Sales region"));
        string forId = regionLabel.GetAttribute("for")!;
        Assert.NotNull(v.Find($"#{forId}"));
    }

    [Fact]
    public void Render_uses_a_select_for_a_valid_values_list()
    {
        var v = RenderForm();

        var select = v.Find("select[name='Region']");
        Assert.Equal("multiple", select.GetAttribute("multiple"));
        var options = select.QuerySelectorAll("option");
        Assert.Equal(4, options.Length);
        Assert.Contains(options, o => o.TextContent == "West");
        Assert.Contains(options, o => o.TextContent == "South");
    }

    [Fact]
    public void Render_prefills_a_parameter_default()
    {
        var v = RenderForm();

        // Year has no valid-value list and an Integer type → a number input prefilled
        // with its declared default (WCAG 3.3.7).
        var year = v.Find("input[name='Year']");
        Assert.Equal("number", year.GetAttribute("type"));
        Assert.Equal("2026", year.GetAttribute("value"));
    }

    [Fact]
    public void Render_marks_a_required_parameter()
    {
        var v = RenderForm();

        var region = v.Find("select[name='Region']");
        Assert.Equal("required", region.GetAttribute("required"));
        Assert.Equal("true", region.GetAttribute("aria-required"));

        // The optional Year is not required.
        var year = v.Find("input[name='Year']");
        Assert.Null(year.GetAttribute("required"));
    }

    [Fact]
    public void Render_form_is_a_real_get_to_the_endpoint_for_no_js()
    {
        var v = RenderForm();

        var form = v.Find("form.dx-report-form");
        // No-JS: a real GET to the host endpoint.
        Assert.Equal("get", form.GetAttribute("method"));
        Assert.Equal("/reports", form.GetAttribute("action"));
        // HTMX: the same endpoint, swapping just this fragment.
        Assert.Equal("/reports", form.GetAttribute("hx-get"));
        Assert.Equal("#dx-report", form.GetAttribute("hx-target"));

        // The report path travels as a hidden field so the endpoint knows which report.
        var hidden = v.Find("input[type='hidden'][name='report']");
        Assert.Equal("/Sales/Monthly", hidden.GetAttribute("value"));

        // A real submit control.
        Assert.Equal("submit", v.Find("button.dx-report-run").GetAttribute("type"));
    }

    [Fact]
    public void Render_prefills_submitted_values_over_defaults()
    {
        var v = RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Render)
            .Add(c => c.RenderEndpoint, "/reports")
            .Add(c => c.RenderBasePath, "/ReportServer")
            .Add(c => c.Values, new[]
            {
                new KeyValuePair<string, string>("Region", "East"),
                new KeyValuePair<string, string>("Year", "2024"),
            }));

        // The user's submitted value wins over the declared default.
        Assert.Equal("2024", v.Find("input[name='Year']").GetAttribute("value"));
        var selected = v.Find("select[name='Region'] option[selected]");
        Assert.Equal("East", selected.GetAttribute("value"));
    }

    // ---- Render mode: the output region ----

    [Fact]
    public void Render_html_result_is_sanitized_not_raw_markup()
    {
        // A hostile <script> in the SSRS HTML must not survive into the DOM as markup.
        byte[] html = System.Text.Encoding.UTF8.GetBytes(
            "<table><tr><td>West</td></tr></table><script>alert(1)</script>");

        var v = RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Render)
            .Add(c => c.RenderEndpoint, "/reports")
            .Add(c => c.RenderBasePath, "/ReportServer")
            .Add(c => c.Result, new RenderedReport(html, "text/html", ReportFormat.Html5)));

        // The default sanitizer is inert (encode-all): no live <script> or <table>.
        Assert.Empty(v.FindAll("div.dx-report-html script"));
        Assert.Empty(v.FindAll("div.dx-report-html table"));
        // The text content is shown verbatim (encoded), so nothing is silently dropped.
        Assert.Contains("alert(1)", v.Find("div.dx-report-html").TextContent);
    }

    [Fact]
    public void Render_error_is_surfaced_in_an_alert()
    {
        var v = RenderComponent<DxReportViewer>(p => p
            .Add(c => c.Report, "/Sales/Monthly")
            .Add(c => c.Mode, ReportViewMode.Render)
            .Add(c => c.RenderEndpoint, "/reports")
            .Add(c => c.RenderBasePath, "/ReportServer")
            .Add(c => c.ErrorMessage, "rsParameterError: Region is required."));

        var alert = v.Find("p.dx-report-error");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("rsParameterError", alert.TextContent);
    }

    [Fact]
    public void Render_shows_an_empty_prompt_before_any_submission()
    {
        var v = RenderForm();

        Assert.Single(v.FindAll("p.dx-report-empty"));
        Assert.Empty(v.FindAll("div.dx-report-html"));
    }
}
