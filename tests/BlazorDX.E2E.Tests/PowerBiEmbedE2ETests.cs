using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// End-to-end coverage of the <c>/powerbi</c> page against the demo wired with the
/// in-process mock Power BI REST API plus the demo-only stub <c>window.powerbi</c>.
/// A passing run proves the WHOLE embed loop without a live Azure tenant:
/// the server minted an embed token from the mock's <c>GenerateToken</c>, returned
/// only the browser-bound config (embed URL + embed token) to the client, and our
/// <c>dx-powerbi.js</c> wrapper called <c>embed()</c> with it — captured by the stub
/// on <c>window.__lastEmbed</c>. The page also hydrates with NO console errors.
/// </summary>
[Collection("e2e")]
public sealed class PowerBiEmbedE2ETests(PlaywrightFixture fx)
{
    // The deterministic values the in-process mock returns for the demo report id.
    private const string DemoReportId = "11111111-1111-1111-1111-111111111111";
    private const string ExpectedToken = "FAKE-EMBED-TOKEN." + DemoReportId;

    [SkippableFact]
    public async Task Powerbi_page_hydrates_without_console_errors()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();

        List<string> errors = [];
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                errors.Add(msg.Text);
            }
        };

        await page.GotoAsync($"{fx.BaseUrl}/powerbi",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.DotNet", null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        // Give the WASM component time to fetch the config and call the wrapper.
        await page.WaitForFunctionAsync("() => !!window.__lastEmbed",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });

        Assert.True(errors.Count == 0, $"Console errors on /powerbi: {string.Join(" | ", errors)}");
    }

    [SkippableFact]
    public async Task Wrapper_embeds_with_the_embed_token_and_url_that_originated_from_the_mock()
    {
        Skip.IfNot(fx.Ready, fx.SkipReason);
        IPage page = await fx.NewPageAsync();

        await page.GotoAsync($"{fx.BaseUrl}/powerbi",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

        // Wait until the stub recorded the embed config the wrapper passed it. This
        // proves the loop ran: server got the embed token from the mock REST
        // GenerateToken -> returned config to the client -> wrapper called embed().
        await page.WaitForFunctionAsync("() => !!window.__lastEmbed",
            null, new PageWaitForFunctionOptions { Timeout = 45_000 });

        string json = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__lastEmbed)");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        string? accessToken = root.GetProperty("accessToken").GetString();
        string? embedUrl = root.GetProperty("embedUrl").GetString();
        string? id = root.GetProperty("id").GetString();
        int tokenType = root.GetProperty("tokenType").GetInt32();

        // The embed token is the deterministic one the mock's GenerateToken minted.
        Assert.Equal(ExpectedToken, accessToken);
        // The embed URL is the deterministic one the mock's GET report built.
        Assert.NotNull(embedUrl);
        Assert.Contains("reportEmbed", embedUrl);
        Assert.Contains(DemoReportId, embedUrl);
        // The report id round-tripped, and the token type is Embed (1).
        Assert.Equal(DemoReportId, id);
        Assert.Equal(1, tokenType);
    }
}
