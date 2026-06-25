using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BlazorDX.MockReportServer.Tests;

/// <summary>
/// Integration tests that boot the mock SSRS server in-memory via
/// <see cref="WebApplicationFactory{Program}"/> and assert it is faithful to the
/// documented SSRS URL Access protocol the real client will be written against.
/// </summary>
public sealed class UrlAccessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UrlAccessTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient();

    [Fact]
    public async Task Render_Html5_ReturnsAccessibleHtmlWithTitleAndParams()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West&Year=2026");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Monthly Sales</h1>", html);
        Assert.Contains("West", html);
        Assert.Contains("2026", html);
        // Accessible table: scoped column headers.
        Assert.Contains("<th scope=\"col\">Region</th>", html);
        Assert.Contains("<td>", html);
    }

    [Fact]
    public async Task Render_Html5_ToolbarTrueIncludesToolbar_FalseOmitsIt()
    {
        var client = Client();

        var withToolbar = await client.GetStringAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&rc:Toolbar=true&Region=West");
        Assert.Contains("ssrs-toolbar", withToolbar);

        var withoutToolbar = await client.GetStringAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&rc:Toolbar=false&Region=West");
        Assert.DoesNotContain("ssrs-toolbar", withoutToolbar);
    }

    [Fact]
    public async Task Render_Pdf_ReturnsValidPdfBytes()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=PDF&Region=East");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var header = Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);

        var tail = Encoding.Latin1.GetString(bytes);
        Assert.Contains("%%EOF", tail);
        Assert.Contains("xref", tail);
    }

    [Fact]
    public async Task Render_Csv_ReturnsCsvContentTypeWithHeaderRow()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=CSV&Region=North");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Region,Year,Month,Revenue", csv);
        Assert.Contains("North", csv);
    }

    [Fact]
    public async Task Render_Image_ReturnsValidPng()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/HR/Headcount&rs:Command=Render&rs:Format=IMAGE&Department=Sales");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(bytes.Length > signature.Length);
        Assert.Equal(signature, bytes[..signature.Length]);
    }

    [Fact]
    public async Task Render_MultiValueParameter_EchoesEveryValue()
    {
        var client = Client();
        var html = await client.GetStringAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West&Region=East");

        Assert.Contains("West", html);
        Assert.Contains("East", html);
        // Both regions produce table rows, so both appear in <td> cells.
        Assert.Contains("<td>West</td>", html);
        Assert.Contains("<td>East</td>", html);
    }

    [Fact]
    public async Task Render_OmittedOptionalParameter_AppliesDefault()
    {
        var client = Client();
        // Year is omitted; its default (2026) should appear.
        var html = await client.GetStringAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West");

        Assert.Contains("2026", html);
    }

    [Fact]
    public async Task Render_UrlEncodedReportPath_Works()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?%2fSales%2fMonthly&rs:Command=Render&rs:Format=HTML5&Region=South");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Monthly Sales</h1>", html);
        Assert.Contains("South", html);
    }

    [Fact]
    public async Task Render_UnknownReportPath_ReturnsItemNotFound()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Nope/DoesNotExist&rs:Command=Render&rs:Format=HTML5");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsItemNotFound", body);
    }

    [Fact]
    public async Task Render_MissingRequiredParameter_ReturnsParameterError()
    {
        var client = Client();
        // Region is required (no default, not nullable) and is omitted here.
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsParameterError", body);
        Assert.Contains("Region", body);
    }

    [Fact]
    public async Task Render_UnknownFormat_ReturnsFormatError()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=WORD&Region=West");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsUnknownFormat", body);
    }

    [Fact]
    public async Task Render_InvalidValidValue_ReturnsParameterError()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=Mars");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsParameterError", body);
    }

    [Fact]
    public async Task ListChildren_ReturnsCatalogItemsUnderFolder()
    {
        var client = Client();
        var json = await client.GetStringAsync("/ReportServer?/Sales&rs:Command=ListChildren");

        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("path").GetString())
            .ToList();

        Assert.Contains("/Sales/Monthly", paths);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsCommandError()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Frobnicate");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsUnknownCommand", body);
    }

    [Fact]
    public async Task ParameterDefinitions_RestShape_ReturnsExpectedMetadata()
    {
        var client = Client();
        var json = await client.GetStringAsync(
            "/reports/api/v2.0/reports(/Sales/Monthly)/parameterdefinitions");

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");

        var region = value.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "Region");

        Assert.Equal("String", region.GetProperty("type").GetString());
        Assert.True(region.GetProperty("required").GetBoolean());
        Assert.True(region.GetProperty("multiValue").GetBoolean());

        var validValues = region.GetProperty("validValues").EnumerateArray()
            .Select(v => v.GetString())
            .ToList();
        Assert.Contains("West", validValues);
        Assert.Contains("South", validValues);

        var year = value.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "Year");
        Assert.Equal("Integer", year.GetProperty("type").GetString());
        Assert.Equal("2026", year.GetProperty("defaultValue").GetString());
        Assert.False(year.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task ParameterDefinitions_FriendlyAlias_Works()
    {
        var client = Client();
        var json = await client.GetStringAsync("/mock/parameters?report=/HR/Headcount");

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        var dept = value.EnumerateArray().Single();
        Assert.Equal("Department", dept.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ParameterDefinitions_UnknownReport_ReturnsItemNotFound()
    {
        var client = Client();
        var response = await client.GetAsync("/mock/parameters?report=/Nope/Missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rsItemNotFound", body);
    }

    [Fact]
    public async Task Auth_Disabled_ByDefault_AllowsAnonymous()
    {
        var client = Client();
        var response = await client.GetAsync(
            "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Auth_Enabled_Returns401WithoutCredentials_And200WithValidCredentials()
    {
        // Boot a second instance with Basic auth turned on via the env var the
        // standalone Program reads. The env var must be set before the host is
        // built, so we create a fresh factory here rather than reusing the fixture.
        Environment.SetEnvironmentVariable("MOCKRS_AUTH", "true");
        Environment.SetEnvironmentVariable("MOCKRS_USER", "report");
        Environment.SetEnvironmentVariable("MOCKRS_PASS", "viewer");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();

            var noCreds = await client.GetAsync(
                "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West");
            Assert.Equal(HttpStatusCode.Unauthorized, noCreds.StatusCode);
            Assert.True(noCreds.Headers.WwwAuthenticate.Count > 0);

            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes("report:viewer"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
            var withCreds = await client.GetAsync(
                "/ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West");
            Assert.Equal(HttpStatusCode.OK, withCreds.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOCKRS_AUTH", null);
            Environment.SetEnvironmentVariable("MOCKRS_USER", null);
            Environment.SetEnvironmentVariable("MOCKRS_PASS", null);
        }
    }
}
