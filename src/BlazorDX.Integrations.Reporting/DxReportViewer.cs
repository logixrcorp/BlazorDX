using System.Collections.Generic;
using System.Globalization;
using BlazorDX.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Integrations.Reporting;

/// <summary>How a <see cref="DxReportViewer"/> presents an SSRS report.</summary>
public enum ReportViewMode
{
    /// <summary>
    /// Embed the server's own HTML viewer in an <c>&lt;iframe&gt;</c> pointed at the
    /// URL-Access render URL. Microsoft's renderer supplies the toolbar, paging, and
    /// parameter prompts; BlazorDX guarantees only the wrapper and the frame title.
    /// </summary>
    Embed,

    /// <summary>
    /// Render in-app: build a parameter form from <see cref="IReportParameterSource"/>
    /// metadata, then have a host endpoint call <see cref="IReportRenderer.RenderAsync"/>
    /// and swap the rendered output in. HTML5 output is run through the
    /// <see cref="HtmlSanitizer"/> before display; PDF goes into an <c>&lt;embed&gt;</c>.
    /// </summary>
    Render,
}

/// <summary>
/// A <b>static-SSR + HTMX</b> SQL Server Reporting Services viewer (ADR 0013). It
/// has two modes (ADR 0014 level 2):
/// <list type="bullet">
/// <item><b>Embed</b> (the default) drops an <c>&lt;iframe&gt;</c> on the URL-Access
/// render URL, so the server's own toolbar and paging do the work.</item>
/// <item><b>Render</b> builds a <em>parameter form</em> from the report's declared
/// parameters (labels, required markers, prefilled defaults, a <c>&lt;select&gt;</c>
/// for closed valid-value lists — WCAG 3.3.2 / 3.3.7) using the dual
/// <c>href</c> + <c>hx-get</c> pattern, so it works with JavaScript disabled: the
/// form GETs <see cref="RenderEndpoint"/>, the host invokes the renderer, and the
/// result is swapped in (HTML5 <b>sanitized</b> into a container, or PDF into an
/// <c>&lt;embed&gt;</c>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Security.</b> SSRS HTML5 output is untrusted markup; it is <em>never</em>
/// passed to a raw <see cref="MarkupString"/>. It flows through the injected
/// <see cref="HtmlSanitizer"/> (the one sanctioned boundary, ADR 0007). Credentials
/// stay server-side in <see cref="ReportingOptions"/> and never reach the browser.
/// There is no reflection and no <c>[JSImport]</c>; all output is real semantic
/// elements with constant render-tree sequence numbers.
/// </para>
/// <para>
/// <b>What BlazorDX guarantees vs the report author / Microsoft.</b> BlazorDX owns
/// the wrapper, the parameter form (conforming labels, required state, prefilled
/// defaults), the iframe's accessible name (<c>title</c>, WCAG 4.1.2), the
/// sanitization boundary, and the accessible export links. The accessibility of the
/// <em>report body itself</em> — table headers, reading order, colour contrast — is
/// determined by the RDL author and Microsoft's HTML5 renderer, which BlazorDX
/// cannot retrofit. See <c>docs/reporting-accessibility.md</c>.
/// </para>
/// </remarks>
public sealed class DxReportViewer : ComponentBase
{
    /// <summary>The catalog path of the report, e.g. <c>/Sales/Monthly</c>. Required (ADR 0014 level 1).</summary>
    [Parameter, EditorRequired] public string Report { get; set; } = string.Empty;

    /// <summary>Embed (default) or in-app Render. ADR 0014 level 2.</summary>
    [Parameter] public ReportViewMode Mode { get; set; } = ReportViewMode.Embed;

    /// <summary>The output format requested in render mode and offered as the primary export. Defaults to <see cref="ReportFormat.Html5"/>.</summary>
    [Parameter] public ReportFormat Format { get; set; } = ReportFormat.Html5;

    /// <summary>Whether the embedded server viewer shows its toolbar (<c>rc:Toolbar</c>). Embed mode only.</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>
    /// In render mode, the host endpoint the parameter form submits to (both as a
    /// real <c>action</c>/<c>href</c> for no-JS and as <c>hx-get</c> for HTMX). The
    /// report path and parameter values are appended as query keys. Defaults to the
    /// current page (empty string), which re-renders the page server-side.
    /// </summary>
    [Parameter] public string RenderEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The already-rendered report, supplied by the host after it resolved the form
    /// query and called <see cref="IReportRenderer.RenderAsync"/>. When set in render
    /// mode, the output region shows it (HTML5 sanitized, PDF embedded). <c>null</c>
    /// means "no submission yet" — only the form is shown.
    /// </summary>
    [Parameter] public RenderedReport? Result { get; set; }

    /// <summary>An error message to surface in the output region (e.g. an <c>rsParameterError</c>), or <c>null</c>.</summary>
    [Parameter] public string? ErrorMessage { get; set; }

    /// <summary>
    /// The parameter values currently in effect, so the form re-prefills the user's
    /// last submission (WCAG 3.3.7). Keys are parameter names; a multi-value key may
    /// repeat. <c>null</c> falls back to each parameter's declared default.
    /// </summary>
    [Parameter] public IEnumerable<KeyValuePair<string, string>>? Values { get; set; }

    /// <summary>
    /// The base path the browser uses to reach the URL-Access render endpoint for the
    /// embed <c>&lt;iframe&gt;</c> and the export links. A <em>relative</em> path
    /// (e.g. <c>/ReportServer</c>) is recommended so those URLs resolve against the
    /// page's own origin (same-origin: the iframe loads, no scheme/host mismatch).
    /// When empty, the server-side <see cref="ReportingOptions.ServerUrl"/> is used.
    /// Credentials are never part of these URLs; they live only on the server-side
    /// <see cref="IReportRenderer"/> HTTP client.
    /// </summary>
    [Parameter] public string RenderBasePath { get; set; } = string.Empty;

    /// <summary>A stable DOM id for the viewer root so HTMX can target/replace it.</summary>
    [Parameter] public string ElementId { get; set; } = "dx-report";

    /// <summary>Optional extra CSS class on the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Level-3 slot: replaces the embed-mode chrome around the iframe (rarely needed).</summary>
    [Parameter] public RenderFragment? Toolbar { get; set; }

    /// <summary>Level-3 slot: replaces the entire generated parameter form in render mode.</summary>
    [Parameter] public RenderFragment? ParametersTemplate { get; set; }

    /// <summary>Level-3 slot: shown in render mode before any submission, in place of an empty output region.</summary>
    [Parameter] public RenderFragment? LoadingTemplate { get; set; }

    /// <summary>Level-3 slot: replaces the default error panel; receives the message.</summary>
    [Parameter] public RenderFragment<string>? ErrorTemplate { get; set; }

    /// <summary>
    /// The sanitizer applied to SSRS HTML5 output before display. Defaults to an
    /// inert (HTML-encode-all) policy — the safest possible behaviour — so untrusted
    /// report markup renders as text unless a host injects a vetted allow-list policy
    /// (ADR 0007). BlazorDX ships no HTML parser of its own.
    /// </summary>
    [Parameter] public HtmlSanitizer? Sanitizer { get; set; }

    // The safest default: an encode-all sanitizer shared across instances.
    private static readonly HtmlSanitizer InertSanitizer = new();

    private HtmlSanitizer ActiveSanitizer => Sanitizer ?? InertSanitizer;

    // Resolved server-side; credentials never leave this process (ADR 0010).
    [Inject] private ReportingOptions Options { get; set; } = default!;

    [Inject] private IReportParameterSource ParameterSource { get; set; } = default!;

    // Loaded in render mode for the parameter form. Empty until OnParametersSetAsync runs.
    private IReadOnlyList<ReportParameter> _parameters = Array.Empty<ReportParameter>();
    private string? _metadataError;

    protected override async Task OnParametersSetAsync()
    {
        // Defense-in-depth: the browser-facing base and form endpoint must stay
        // same-origin. Reject anything carrying a scheme (http:, https:, javascript:,
        // …) or a protocol-relative //host authority, so a misconfiguration cannot
        // point the iframe/form at a cross-origin or hostile target. Validated on
        // every parameter set, before any mode-specific work, so both Embed and
        // Render are covered.
        EnsureRelative(RenderBasePath, nameof(RenderBasePath));
        EnsureRelative(RenderEndpoint, nameof(RenderEndpoint));

        if (Mode != ReportViewMode.Render || ParametersTemplate is not null)
        {
            return;
        }

        try
        {
            _parameters = await ParameterSource.GetParametersAsync(Report).ConfigureAwait(false);
            _metadataError = null;
        }
        catch (ReportRenderException ex)
        {
            _parameters = Array.Empty<ReportParameter>();
            _metadataError = ex.Message;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ElementId);
        builder.AddAttribute(2, "class", $"dx-report {Class}".TrimEnd());
        builder.AddAttribute(3, "data-report-mode", Mode.ToString());

        if (Mode == ReportViewMode.Embed)
        {
            BuildEmbed(builder);
        }
        else
        {
            BuildRender(builder);
        }

        builder.CloseElement();
    }

    // ----- Embed mode ------------------------------------------------------

    private void BuildEmbed(RenderTreeBuilder builder)
    {
        string? src = TryBuildAbsoluteRenderUrl(Format);

        if (Toolbar is not null)
        {
            builder.AddContent(10, Toolbar);
        }

        if (src is null)
        {
            BuildError(builder, "The report server URL is not configured.");
            return;
        }

        // The server's own HTML viewer. The non-empty title gives the frame an
        // accessible name (WCAG 4.1.2); the server supplies the toolbar/paging.
        builder.OpenElement(11, "iframe");
        builder.AddAttribute(12, "class", "dx-report-frame");
        builder.AddAttribute(13, "src", src);
        builder.AddAttribute(14, "title", FrameTitle(Report));
        builder.AddAttribute(15, "loading", "lazy");
        builder.CloseElement();

        BuildExportLinks(builder, 16);
    }

    // ----- Render mode -----------------------------------------------------

    private void BuildRender(RenderTreeBuilder builder)
    {
        if (_metadataError is not null && ParametersTemplate is null)
        {
            BuildError(builder, _metadataError);
            return;
        }

        BuildParameterForm(builder);
        BuildOutput(builder);
    }

    private void BuildParameterForm(RenderTreeBuilder builder)
    {
        if (ParametersTemplate is not null)
        {
            builder.AddContent(20, ParametersTemplate);
            return;
        }

        // Dual-mode form: a real GET to RenderEndpoint (works with JS off — the host
        // re-renders the page with the result), and hx-get so HTMX swaps just this
        // viewer fragment. The report path travels as a hidden field so the endpoint
        // knows which report to render.
        string action = string.IsNullOrEmpty(RenderEndpoint) ? "" : RenderEndpoint;

        builder.OpenElement(21, "form");
        builder.AddAttribute(22, "class", "dx-report-form");
        builder.AddAttribute(23, "method", "get");
        builder.AddAttribute(24, "action", action);
        builder.AddAttribute(25, "hx-get", action);
        builder.AddAttribute(26, "hx-target", $"#{ElementId}");
        builder.AddAttribute(27, "hx-select", $"#{ElementId}");
        builder.AddAttribute(28, "hx-swap", "outerHTML");
        builder.AddAttribute(29, "hx-push-url", "true");
        builder.AddAttribute(30, "data-enhance-nav", "false");

        builder.OpenElement(31, "input");
        builder.AddAttribute(32, "type", "hidden");
        builder.AddAttribute(33, "name", "report");
        builder.AddAttribute(34, "value", Report);
        builder.CloseElement();

        BuildFieldset(builder);

        builder.OpenElement(60, "div");
        builder.AddAttribute(61, "class", "dx-report-actions");
        builder.OpenElement(62, "button");
        builder.AddAttribute(63, "type", "submit");
        builder.AddAttribute(64, "class", "dx-report-run");
        builder.AddContent(65, "Run report");
        builder.CloseElement();
        builder.CloseElement(); // actions

        builder.CloseElement(); // form
    }

    private void BuildFieldset(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "fieldset");
        builder.AddAttribute(41, "class", "dx-report-fields");

        builder.OpenElement(42, "legend");
        builder.AddContent(43, "Report parameters");
        builder.CloseElement();

        var current = BuildValueLookup();

        foreach (ReportParameter p in _parameters)
        {
            builder.OpenElement(44, "div");
            builder.SetKey(p.Name);
            builder.AddAttribute(45, "class", "dx-report-field");

            string fieldId = $"{ElementId}-{p.Name}";
            string? value = current.TryGetValue(p.Name, out var v) && v.Count > 0
                ? v[0]
                : p.DefaultValue;

            BuildLabel(builder, p, fieldId);
            BuildControl(builder, p, fieldId, value);

            builder.CloseElement(); // field
        }

        builder.CloseElement(); // fieldset
    }

    // A conforming label tied to its control by for/id (WCAG 3.3.2), with a
    // required marker that is also exposed to assistive tech via the control's
    // aria-required (set in BuildControl).
    private static void BuildLabel(RenderTreeBuilder builder, ReportParameter p, string fieldId)
    {
        builder.OpenElement(46, "label");
        builder.AddAttribute(47, "class", "dx-report-label");
        builder.AddAttribute(48, "for", fieldId);
        builder.AddContent(49, string.IsNullOrWhiteSpace(p.Prompt) ? p.Name : p.Prompt);
        if (p.Required)
        {
            builder.OpenElement(50, "span");
            builder.AddAttribute(51, "class", "dx-report-required");
            builder.AddAttribute(52, "aria-hidden", "true");
            builder.AddContent(53, " *");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static void BuildControl(RenderTreeBuilder builder, ReportParameter p, string fieldId, string? value)
    {
        // A closed valid-value list → a <select> so the choice is constrained.
        if (p.ValidValues is { Count: > 0 } options)
        {
            builder.OpenElement(54, "select");
            builder.AddAttribute(55, "id", fieldId);
            builder.AddAttribute(56, "name", p.Name);
            builder.AddAttribute(57, "class", "dx-report-input dx-report-select");
            if (p.MultiValue)
            {
                builder.AddAttribute(58, "multiple", "multiple");
            }

            if (p.Required)
            {
                builder.AddAttribute(59, "required", "required");
                builder.AddAttribute(66, "aria-required", "true");
            }

            foreach (string option in options)
            {
                builder.OpenElement(67, "option");
                builder.SetKey(option);
                builder.AddAttribute(68, "value", option);
                if (string.Equals(option, value, StringComparison.Ordinal))
                {
                    builder.AddAttribute(69, "selected", "selected");
                }

                builder.AddContent(70, option);
                builder.CloseElement();
            }

            builder.CloseElement(); // select
            return;
        }

        // Boolean → a checkbox. SSRS treats "true"/"false"; the checked state mirrors it.
        if (p.Type == ReportParameterDataType.Boolean)
        {
            builder.OpenElement(71, "input");
            builder.AddAttribute(72, "id", fieldId);
            builder.AddAttribute(73, "name", p.Name);
            builder.AddAttribute(74, "type", "checkbox");
            builder.AddAttribute(75, "class", "dx-report-input dx-report-checkbox");
            builder.AddAttribute(76, "value", "true");
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddAttribute(77, "checked", "checked");
            }

            builder.CloseElement();
            return;
        }

        // Otherwise a text or number input.
        bool numeric = p.Type is ReportParameterDataType.Integer or ReportParameterDataType.Float;
        builder.OpenElement(78, "input");
        builder.AddAttribute(79, "id", fieldId);
        builder.AddAttribute(80, "name", p.Name);
        builder.AddAttribute(81, "type", numeric ? "number" : "text");
        builder.AddAttribute(82, "class", "dx-report-input");
        if (p.Type == ReportParameterDataType.Integer)
        {
            builder.AddAttribute(83, "step", "1");
        }

        if (value is not null)
        {
            builder.AddAttribute(84, "value", value);
        }

        if (p.Required)
        {
            builder.AddAttribute(85, "required", "required");
            builder.AddAttribute(86, "aria-required", "true");
        }

        builder.CloseElement();
    }

    private void BuildOutput(RenderTreeBuilder builder)
    {
        builder.OpenElement(90, "div");
        builder.AddAttribute(91, "class", "dx-report-output");
        builder.AddAttribute(92, "role", "region");
        builder.AddAttribute(93, "aria-label", $"{FrameTitle(Report)} output");
        builder.AddAttribute(94, "aria-live", "polite");
        builder.AddAttribute(95, "tabindex", "-1");

        if (ErrorMessage is not null)
        {
            BuildError(builder, ErrorMessage);
        }
        else if (Result is not null)
        {
            BuildResult(builder, Result);
        }
        else if (LoadingTemplate is not null)
        {
            builder.AddContent(96, LoadingTemplate);
        }
        else
        {
            builder.OpenElement(97, "p");
            builder.AddAttribute(98, "class", "dx-report-empty");
            builder.AddContent(99, "Choose parameters and run the report to see results here.");
            builder.CloseElement();
        }

        builder.CloseElement(); // output

        BuildExportLinks(builder, 110);
    }

    private void BuildResult(RenderTreeBuilder builder, RenderedReport result)
    {
        switch (result.Format)
        {
            case ReportFormat.Pdf:
            {
                // PDF render output is offered through a same-origin object URL the
                // host minted; embed it natively with a non-empty title (WCAG 4.1.2).
                string? src = TryBuildAbsoluteRenderUrl(ReportFormat.Pdf);
                if (src is null)
                {
                    BuildError(builder, "The report server URL is not configured.");
                    return;
                }

                builder.OpenElement(111, "embed");
                builder.AddAttribute(112, "class", "dx-report-pdf");
                builder.AddAttribute(113, "type", "application/pdf");
                builder.AddAttribute(114, "src", src);
                builder.AddAttribute(115, "title", FrameTitle(Report));
                builder.CloseElement();
                break;
            }

            case ReportFormat.Html5:
            {
                // SSRS HTML5 is untrusted — run it through the sanitizer (ADR 0007).
                // The sanitizer is the ONLY place a MarkupString is created from
                // runtime HTML; everywhere else it is a DX1001 analyzer error.
                string html = System.Text.Encoding.UTF8.GetString(result.Content);
                builder.OpenElement(116, "div");
                builder.AddAttribute(117, "class", "dx-report-html");
                builder.AddContent(118, ActiveSanitizer.Sanitize(html));
                builder.CloseElement();
                break;
            }

            default:
            {
                // CSV / image: text/binary — show as inert text rather than as markup.
                string text = System.Text.Encoding.UTF8.GetString(result.Content);
                builder.OpenElement(119, "pre");
                builder.AddAttribute(120, "class", "dx-report-raw");
                builder.AddContent(121, text);
                builder.CloseElement();
                break;
            }
        }
    }

    // ----- Shared bits -----------------------------------------------------

    // Accessible export anchors straight to the URL-Access render URL (real links,
    // no JS). PDF and CSV always; HTML when it is not already the embedded format.
    private void BuildExportLinks(RenderTreeBuilder builder, int seq)
    {
        string? pdf = TryBuildAbsoluteRenderUrl(ReportFormat.Pdf);
        string? csv = TryBuildAbsoluteRenderUrl(ReportFormat.Csv);
        if (pdf is null && csv is null)
        {
            return;
        }

        builder.OpenElement(seq, "nav");
        builder.AddAttribute(seq + 1, "class", "dx-report-export");
        builder.AddAttribute(seq + 2, "aria-label", "Export this report");

        if (pdf is not null)
        {
            BuildExportLink(builder, seq + 3, pdf, "Export PDF", "pdf");
        }

        if (csv is not null)
        {
            BuildExportLink(builder, seq + 9, csv, "Export CSV", "csv");
        }

        builder.CloseElement();
    }

    private static void BuildExportLink(RenderTreeBuilder builder, int seq, string url, string label, string kind)
    {
        builder.OpenElement(seq, "a");
        builder.AddAttribute(seq + 1, "class", $"dx-report-export-link dx-report-export-{kind}");
        builder.AddAttribute(seq + 2, "href", url);
        builder.AddAttribute(seq + 3, "rel", "noopener noreferrer");
        builder.AddContent(seq + 4, label);
        builder.CloseElement();
    }

    private void BuildError(RenderTreeBuilder builder, string message)
    {
        if (ErrorTemplate is not null)
        {
            builder.AddContent(130, ErrorTemplate(message));
            return;
        }

        builder.OpenElement(131, "p");
        builder.AddAttribute(132, "class", "dx-report-error");
        builder.AddAttribute(133, "role", "alert");
        builder.AddContent(134, message);
        builder.CloseElement();
    }

    /// <summary>
    /// Builds the absolute URL-Access render URL for <paramref name="format"/> by
    /// joining the configured <see cref="ReportingOptions.ServerUrl"/> with the query
    /// from <see cref="SsrsUrlBuilder"/>. Returns <c>null</c> when no server URL is
    /// configured. The query carries the current parameter values so the embed and
    /// the export links reflect the user's selection.
    /// </summary>
    private string? TryBuildAbsoluteRenderUrl(ReportFormat format)
    {
        // Browser-facing base: a relative RenderBasePath (preferred — same-origin)
        // or the server URL as a fallback. No credentials are ever in this URL.
        string baseUrl = string.IsNullOrWhiteSpace(RenderBasePath)
            ? Options.ServerUrl
            : RenderBasePath;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(Report))
        {
            return null;
        }

        var request = new ReportRequest(
            Report,
            format,
            MaterializeValues(),
            ShowToolbar && format == ReportFormat.Html5);

        string query = SsrsUrlBuilder.BuildRenderQuery(request);
        return baseUrl.TrimEnd('/') + query;
    }

    // The form values projected as the ordered multi-map the request expects, or
    // null when none were supplied (so the URL omits them and SSRS applies defaults).
    private IEnumerable<KeyValuePair<string, string>>? MaterializeValues()
    {
        if (Values is null)
        {
            return null;
        }

        var list = new List<KeyValuePair<string, string>>();
        foreach (var pair in Values)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                list.Add(pair);
            }
        }

        return list.Count == 0 ? null : list;
    }

    private Dictionary<string, List<string>> BuildValueLookup()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (Values is null)
        {
            return map;
        }

        foreach (var pair in Values)
        {
            if (!map.TryGetValue(pair.Key, out var list))
            {
                list = new List<string>();
                map[pair.Key] = list;
            }

            list.Add(pair.Value);
        }

        return map;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if <paramref name="value"/> is a
    /// non-relative URL (carries a scheme like <c>http:</c>/<c>javascript:</c> or a
    /// protocol-relative <c>//host</c> authority). An empty value is allowed (it means
    /// "use the configured server URL" / "post to the current page").
    /// </summary>
    private static void EnsureRelative(string value, string parameterName)
    {
        if (!IsRelativeUrl(value))
        {
            throw new ArgumentException(
                $"'{parameterName}' must be a relative URL so the report viewer stays " +
                "same-origin; a scheme (http:, https:, javascript:, …) or a " +
                $"protocol-relative '//host' value is not allowed. Got: '{value}'.",
                parameterName);
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> is safe to use as a same-origin URL: empty,
    /// or a relative reference with no scheme and no <c>//</c> authority. Rejects
    /// absolute URLs (<c>http:</c>, <c>https:</c>, <c>javascript:</c>, <c>data:</c>, …)
    /// and protocol-relative <c>//host</c> values.
    /// </summary>
    internal static bool IsRelativeUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        // Protocol-relative ("//host/…") borrows the page scheme but a foreign host.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // A leading-colon control char (e.g. ":evil") or a "scheme:" prefix is absolute.
        int colon = trimmed.IndexOf(':');
        if (colon < 0)
        {
            return true;
        }

        if (colon == 0)
        {
            return false;
        }

        // A colon that appears only after a path separator (e.g. "/a/b:c") is part of
        // the path, not a scheme; a scheme cannot contain a slash before its colon.
        int slash = trimmed.IndexOfAny(new[] { '/', '?', '#' });
        if (slash >= 0 && slash < colon)
        {
            return true;
        }

        // Otherwise the leading "token:" is a scheme — reject it.
        string scheme = trimmed[..colon];
        return !IsSchemeToken(scheme);
    }

    private static bool IsSchemeToken(string s)
    {
        if (s.Length == 0 || !char.IsLetter(s[0]))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    // The embedded frame must always carry a non-empty accessible name (WCAG 4.1.2).
    private static string FrameTitle(string report)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            return "Report";
        }

        int slash = report.TrimEnd('/').LastIndexOf('/');
        string leaf = slash >= 0 ? report.TrimEnd('/')[(slash + 1)..] : report;
        return string.IsNullOrWhiteSpace(leaf)
            ? "Report"
            : string.Create(CultureInfo.InvariantCulture, $"{leaf} report");
    }
}
