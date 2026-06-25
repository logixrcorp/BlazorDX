using BlazorDX.Integrations.PowerBI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// bUnit coverage of <see cref="DxPowerBiReport"/>: it renders a labeled, accessible
/// container and a loading state, calls the interop's EmbedAsync with the
/// browser-bound config (embed URL + embed token) once it has one, and is a clean
/// no-op off browser where the <see cref="NullPowerBiInterop"/> is used.
/// </summary>
public sealed class DxPowerBiReportTests : TestContext
{
    private sealed class RecordingInterop : IPowerBiInterop
    {
        public List<(string ElementId, string ConfigJson)> Embeds { get; } = new();
        public List<string> Unmounts { get; } = new();

        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

        public ValueTask EmbedAsync(string elementId, string configJson)
        {
            Embeds.Add((elementId, configJson));
            return ValueTask.CompletedTask;
        }

        public ValueTask UnmountAsync(string elementId)
        {
            Unmounts.Add(elementId);
            return ValueTask.CompletedTask;
        }
    }

    private static readonly PowerBiEmbedConfig SampleConfig = new(
        EmbedUrl: "https://app.powerbi.com/reportEmbed?reportId=abc&groupId=ws",
        EmbedToken: "FAKE-EMBED-TOKEN.abc",
        ReportId: "abc",
        TokenType: PowerBiTokenType.Embed,
        Expiration: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Renders_a_labeled_application_container()
    {
        Services.AddScoped<IPowerBiInterop>(_ => new RecordingInterop());

        IRenderedComponent<DxPowerBiReport> cut = RenderComponent<DxPowerBiReport>(p => p
            .Add(c => c.EmbedConfig, SampleConfig)
            .Add(c => c.Label, "Quarterly Revenue"));

        var frame = cut.Find(".dx-powerbi-frame");
        Assert.Equal("application", frame.GetAttribute("role"));
        Assert.Equal("Quarterly Revenue", frame.GetAttribute("aria-label"));
        // The container has a stable id the JS wrapper targets.
        Assert.False(string.IsNullOrEmpty(frame.GetAttribute("id")));
    }

    [Fact]
    public void Container_falls_back_to_a_generic_accessible_name_when_label_is_blank()
    {
        Services.AddScoped<IPowerBiInterop>(_ => new RecordingInterop());

        IRenderedComponent<DxPowerBiReport> cut = RenderComponent<DxPowerBiReport>(p => p
            .Add(c => c.EmbedConfig, SampleConfig)
            .Add(c => c.Label, "   "));

        Assert.Equal("Power BI report", cut.Find(".dx-powerbi-frame").GetAttribute("aria-label"));
    }

    [Fact]
    public void Shows_an_accessible_loading_state_before_the_embed_completes()
    {
        // An interop whose EmbedAsync never completes leaves the component in the
        // loading state, so we can observe it.
        Services.AddScoped<IPowerBiInterop>(_ => new StallingInterop());

        IRenderedComponent<DxPowerBiReport> cut = RenderComponent<DxPowerBiReport>(p => p
            .Add(c => c.EmbedConfig, SampleConfig));

        var loading = cut.Find(".dx-powerbi-loading");
        Assert.Equal("status", loading.GetAttribute("role"));
        Assert.Equal("polite", loading.GetAttribute("aria-live"));
        Assert.Contains("Loading", loading.TextContent);
    }

    private sealed class StallingInterop : IPowerBiInterop
    {
        private readonly TaskCompletionSource _never = new();
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask EmbedAsync(string elementId, string configJson) => new(_never.Task);
        public ValueTask UnmountAsync(string elementId) => ValueTask.CompletedTask;
    }

    [Fact]
    public void Calls_EmbedAsync_with_the_browser_bound_config()
    {
        var interop = new RecordingInterop();
        Services.AddScoped<IPowerBiInterop>(_ => interop);

        RenderComponent<DxPowerBiReport>(p => p.Add(c => c.EmbedConfig, SampleConfig));

        var embed = Assert.Single(interop.Embeds);
        // The embed token + URL (browser-bound by design) reached the wrapper.
        Assert.Contains("FAKE-EMBED-TOKEN.abc", embed.ConfigJson);
        Assert.Contains("reportEmbed", embed.ConfigJson);
        // No AAD token is ever serialized into the config that crosses to the client.
        Assert.DoesNotContain("aad", embed.ConfigJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Off_browser_the_null_interop_is_a_no_op_and_does_not_throw()
    {
        // The exact implementation registered off-browser (static SSR / Server
        // prerender). Rendering + the first-render embed attempt + dispose must all
        // be silent no-ops.
        Services.AddScoped<IPowerBiInterop, NullPowerBiInterop>();

        IRenderedComponent<DxPowerBiReport> cut = RenderComponent<DxPowerBiReport>(p => p
            .Add(c => c.EmbedConfig, SampleConfig));

        // Container still renders so the SSR markup is sound.
        Assert.NotNull(cut.Find(".dx-powerbi-frame"));
        // Disposal (UnmountAsync) is also a no-op and must not throw.
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_unmounts_the_embed()
    {
        var interop = new RecordingInterop();
        Services.AddScoped<IPowerBiInterop>(_ => interop);

        IRenderedComponent<DxPowerBiReport> cut = RenderComponent<DxPowerBiReport>(p => p
            .Add(c => c.EmbedConfig, SampleConfig));
        string id = cut.Find(".dx-powerbi-frame").GetAttribute("id")!;

        await cut.Instance.DisposeAsync();

        Assert.Contains(id, interop.Unmounts);
    }
}
