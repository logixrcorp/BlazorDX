using Microsoft.Playwright;
using Xunit;

namespace BlazorDX.E2E.Tests;

/// <summary>
/// Shared Playwright browser for the E2E suite. Reads the target from
/// <c>BLAZORDX_BASEURL</c> (default <c>http://localhost:5296</c>) and the browser
/// engine from <c>BLAZORDX_BROWSER</c> (<c>chromium</c> | <c>firefox</c> | <c>webkit</c>).
/// If the server is unreachable or the browser binaries are missing, <see cref="Ready"/>
/// is false and tests skip with <see cref="SkipReason"/> rather than failing.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("BLAZORDX_BASEURL")?.TrimEnd('/') ?? "http://localhost:5296";

    public bool Ready { get; private set; }
    public string SkipReason { get; private set; } = "E2E disabled";

    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task InitializeAsync()
    {
        if (!await ServerIsReachableAsync())
        {
            SkipReason = $"No demo server reachable at {BaseUrl}. " +
                "Start it (e.g. `dotnet run --project samples/BlazorDX.Demo/BlazorDX.Demo`) and re-run.";
            return;
        }

        try
        {
            playwright = await Playwright.CreateAsync();
            IBrowserType engine = (Environment.GetEnvironmentVariable("BLAZORDX_BROWSER") ?? "chromium") switch
            {
                "firefox" => playwright.Firefox,
                "webkit" => playwright.Webkit,
                _ => playwright.Chromium,
            };
            browser = await engine.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            Ready = true;
        }
        catch (Exception ex)
        {
            // Most commonly: browsers not installed (run `playwright.ps1 install`).
            SkipReason = $"Playwright could not launch a browser: {ex.Message}";
        }
    }

    /// <summary>Opens a fresh, isolated page (its own browser context).</summary>
    public async Task<IPage> NewPageAsync()
    {
        IBrowserContext context = await browser!.NewContextAsync();
        return await context.NewPageAsync();
    }

    private async Task<bool> ServerIsReachableAsync()
    {
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };
            HttpResponseMessage res = await http.GetAsync(BaseUrl);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();
    }
}

/// <summary>Binds all E2E test classes to one shared browser instance.</summary>
[CollectionDefinition("e2e")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture>;
