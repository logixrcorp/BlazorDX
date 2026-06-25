using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// End-to-end coverage of the <c>/reports</c> page against the mock SSRS server
/// mounted in-process: the embed iframe points at the URL-Access render endpoint,
/// the embedded frame actually loads, and submitting the parameter form renders
/// report output that reflects the chosen parameter.
/// </summary>
[Collection("e2e")]
public sealed class ReportViewerE2ETests(PlaywrightFixture fx)
{
    [SkippableFact]
    public async Task Reports_page_loads_with_an_embed_iframe_on_the_render_endpoint()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/reports",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        ILocator frame = page.Locator("iframe.dx-report-frame");
        Assert.True(await frame.CountAsync() > 0, "Embed iframe is present.");

        string? src = await frame.First.GetAttributeAsync("src");
        Assert.NotNull(src);
        Assert.Contains("/ReportServer?", src);
        Assert.Contains("rs:Command=Render", src);

        // A non-empty accessible name (WCAG 4.1.2).
        string? title = await frame.First.GetAttributeAsync("title");
        Assert.False(string.IsNullOrWhiteSpace(title));
    }

    [SkippableFact]
    public async Task Embed_iframe_actually_renders_the_mock_report()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();

        // Capture the URL-Access render request the iframe issues to prove the embed
        // wired to the server endpoint and got a successful HTML response back.
        Task<IResponse> renderResponse = page.WaitForResponseAsync(
            r => r.Url.Contains("/ReportServer") && r.Url.Contains("rs:Command=Render"),
            new PageWaitForResponseOptions { Timeout = 45_000 });

        await page.GotoAsync($"{fx.BaseUrl}/reports",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        // The iframe is lazy-loaded; scroll it into view so the browser fetches it.
        await page.Locator("iframe.dx-report-frame").ScrollIntoViewIfNeededAsync();

        IResponse response = await renderResponse;
        // The iframe issued the URL-Access render request and the mock answered with a
        // successful HTML document (the body of an iframe navigation isn't retained by
        // the protocol, so we assert on the response status + the carried parameter).
        Assert.True(response.Ok, $"The embedded render request returned {response.Status}.");
        Assert.Contains("rs:Format=HTML5", response.Url);
        Assert.Contains("Region=West", response.Url);
    }

    [SkippableFact]
    public async Task Submitting_the_parameter_form_renders_output_reflecting_the_parameter()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/reports",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        // The render-mode form: pick a non-default Region and run it.
        await page.SelectOptionAsync("#dx-report select[name='Region']", "East");
        await page.FillAsync("#dx-report input[name='Year']", "2024");
        await page.ClickAsync("#dx-report button.dx-report-run");

        // HTMX swaps the viewer fragment in place; wait for the rendered output.
        ILocator output = page.Locator("#dx-report .dx-report-output");
        await output.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // The output region reflects the chosen Region (the mock builds rows per region).
        await Assertions.Expect(output).ToContainTextAsync("East", new() { Timeout = 15_000 });
        // No error panel.
        Assert.Equal(0, await page.Locator("#dx-report .dx-report-error").CountAsync());
    }
}
