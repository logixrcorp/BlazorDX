using System.Net;
using BlazorDX.Integrations.Reporting;
using Xunit;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// Adversarial hardening coverage for <see cref="SsrsUrlBuilder"/>: the REST
/// parameter-definitions path must percent-encode the report path so delimiter
/// characters cannot break out of the parenthesised path token, and the URL-Access
/// render query must keep encoding parameter keys/values so a value cannot inject
/// extra query parameters or rewrite the <c>rs:</c>/<c>rc:</c> control directives.
/// </summary>
public sealed class SsrsUrlBuilderHardeningTests
{
    // ---- Fix 1: REST parameter-definitions path injection ----

    [Theory]
    [InlineData("/Sales/Monthly)/parameterdefinitions;DROP", "%29")] // a closing paren must not survive raw
    [InlineData("/Sales/(Evil)", "%28")]
    [InlineData("/Sales/O'Brien", "%27")]
    [InlineData("/Sales/Drop;Table", "%3B")]
    [InlineData("/Sales/With Space", "%20")]
    [InlineData("/Sales/%2fEncoded", "%252f")] // an already-%2f path is re-encoded, not passed through
    public void BuildParameterDefinitionsPath_EncodesDelimiters_SoTheyCannotEscapeTheToken(
        string reportPath, string expectedEncodedFragment)
    {
        string path = SsrsUrlBuilder.BuildParameterDefinitionsPath(reportPath);

        // Shape is preserved: exactly one opening + closing paren of the wrapper.
        Assert.StartsWith("api/v2.0/reports(", path);
        Assert.EndsWith(")/parameterdefinitions", path);

        // The interior (between the wrapper parens) is the encoded path. Strip the
        // wrapper and assert no raw delimiter leaked through.
        string interior = path["api/v2.0/reports(".Length..^")/parameterdefinitions".Length];
        Assert.DoesNotContain(")", interior);
        Assert.DoesNotContain("(", interior);
        Assert.DoesNotContain("'", interior);
        Assert.DoesNotContain(";", interior);
        Assert.DoesNotContain(" ", interior);
        Assert.DoesNotContain("/", interior); // slashes become %2F

        // The specific dangerous char is present only in its encoded form.
        Assert.Contains(expectedEncodedFragment, interior, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildParameterDefinitionsPath_NormalPath_EncodesSlashesAsPercent2f()
    {
        string path = SsrsUrlBuilder.BuildParameterDefinitionsPath("/Sales/Monthly");

        Assert.Equal("api/v2.0/reports(%2FSales%2FMonthly)/parameterdefinitions", path);
    }

    [Fact]
    public async Task GetParameters_EncodedRealPath_StillFetchesAgainstTheMock()
    {
        using var host = ReportingTestHost.Create();

        // The builder now %2F-encodes the path; this proves the encoded token still
        // round-trips through the mock (and SSRS) to the real catalog entry.
        var parameters = await host.ParameterSource.GetParametersAsync("/Sales/Monthly");

        Assert.Equal(2, parameters.Count);
        Assert.Contains(parameters, p => p.Name == "Region");
    }

    [Fact]
    public async Task GetParameters_InjectionLadenPath_IsSafelyEncoded_MockReturnsCleanNotFound()
    {
        using var host = ReportingTestHost.Create();

        // A hostile path with parens, a quote, a semicolon, and a space. Because the
        // builder encodes it, the parenthesised token cannot be broken and the mock
        // parses it as one (non-existent) report path: a clean rsItemNotFound, never a
        // malformed-request crash or a 500.
        var ex = await Assert.ThrowsAsync<ReportRenderException>(
            () => host.ParameterSource.GetParametersAsync("/Sales/Monthly)';DROP (x"));

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("rsItemNotFound", ex.SsrsErrorCode);
    }

    [Fact]
    public void BuildParameterDefinitionsPath_BlankPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => SsrsUrlBuilder.BuildParameterDefinitionsPath("   "));
    }

    // ---- Fix 3: param key/value encoding locks in (do NOT change the builder) ----

    [Fact]
    public void BuildRenderQuery_ParameterValueWithAmpersandAndEquals_CannotInjectExtraParams()
    {
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Html5,
            new[]
            {
                new KeyValuePair<string, string>("Region", "West&rs:Command=Delete=evil"),
            });

        string query = SsrsUrlBuilder.BuildRenderQuery(request);

        // The literal & and = are encoded, so they cannot start a new query token.
        Assert.Contains("Region=West%26rs%3ACommand%3DDelete%3Devil", query);
        // Exactly one rs:Command — the injected one did not take.
        Assert.Single(Occurrences(query, "rs:Command="));
        Assert.Contains("rs:Command=Render", query);
        Assert.DoesNotContain("rs:Command=Delete", query);
    }

    [Fact]
    public void BuildRenderQuery_ParameterValueWithHash_IsEncoded_NoFragment()
    {
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Html5,
            new[] { new KeyValuePair<string, string>("Region", "We#st") });

        string query = SsrsUrlBuilder.BuildRenderQuery(request);

        Assert.Contains("Region=We%23st", query);
        Assert.DoesNotContain("#", query);
    }

    [Fact]
    public void BuildRenderQuery_ParameterValueWithCrlf_IsEncoded_NoHeaderSplitting()
    {
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Html5,
            new[] { new KeyValuePair<string, string>("Region", "West\r\nrc:Toolbar=true") });

        string query = SsrsUrlBuilder.BuildRenderQuery(request);

        // CRLF is encoded; no raw newline survives to split a request line/header.
        Assert.Contains("%0D%0A", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r", query);
        Assert.DoesNotContain("\n", query);
        // rc:Toolbar keeps its single, builder-set value.
        Assert.Single(Occurrences(query, "rc:Toolbar="));
        Assert.Contains("rc:Toolbar=true", query);
    }

    [Fact]
    public void BuildRenderQuery_ParameterKeyWithDelimiters_IsEncoded_CannotForgeDirectives()
    {
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Csv,
            new[] { new KeyValuePair<string, string>("a=b&rs:Format=PDF", "x") },
            ShowToolbar: false);

        string query = SsrsUrlBuilder.BuildRenderQuery(request);

        // The malicious key's delimiters are encoded, so it is one opaque key=value.
        Assert.Contains("a%3Db%26rs%3AFormat%3DPDF=x", query);
        // rs:Format stays the builder's CSV — the forged PDF did not take.
        Assert.Single(Occurrences(query, "rs:Format="));
        Assert.Contains("rs:Format=CSV", query);
        Assert.DoesNotContain("rs:Format=PDF", query);
    }

    [Fact]
    public async Task RoundTrip_ParameterValueWithDelimiters_EchoesLiteralValueThroughMock()
    {
        using var host = ReportingTestHost.Create();

        // /HR/Headcount's Department is a free-form string the mock echoes verbatim
        // into the rendered HTML. A value carrying & and = (and a forged rs:Format)
        // must come back as ONE literal string — proving it was not split into injected
        // query params — and the report must still render as HTML5, not the forged PDF.
        const string tricky = "Sales&rs:Format=PDF=evil";
        var result = await host.Renderer.RenderAsync(
            new ReportRequest("/HR/Headcount", ReportFormat.Html5, new[]
            {
                new KeyValuePair<string, string>("Department", tricky),
            }));

        // Format directive held: HTML came back, not the injected PDF.
        Assert.Equal("text/html", result.ContentType);

        // The literal delimiter-laden value round-tripped into a single table cell.
        // The mock HTML-encodes cell text, so the & shows as &amp;; the whole opaque
        // string is present, proving it was never split into separate query params.
        string html = System.Text.Encoding.UTF8.GetString(result.Content);
        string encodedCell = System.Net.WebUtility.HtmlEncode(tricky);
        Assert.Contains($"<td>{encodedCell}</td>", html);
    }

    private static IReadOnlyList<int> Occurrences(string haystack, string needle)
    {
        var hits = new List<int>();
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            hits.Add(index);
            index += needle.Length;
        }

        return hits;
    }
}
