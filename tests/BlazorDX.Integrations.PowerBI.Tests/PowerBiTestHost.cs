using BlazorDX.Integrations.PowerBI;
using BlazorDX.MockReportServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// Spins up the real mock Power BI REST server in-memory and wires the production
/// <c>AddBlazorDXPowerBi</c> DI graph against it, so <see cref="IPowerBiEmbedService"/>
/// is exercised over real HTTP through <see cref="IHttpClientFactory"/> — not a
/// hand-built client. The factory's in-memory handler is injected as the named
/// client's primary handler, so request building, the bearer pass-through, status
/// mapping, and source-gen JSON parsing all run exactly as in production.
/// </summary>
internal sealed class PowerBiTestHost : IDisposable
{
    /// <summary>The workspace id the mock and the configured options agree on.</summary>
    public const string WorkspaceId = "ws-0000";

    /// <summary>A report id the mock recognises.</summary>
    public const string KnownReportId = "11111111-1111-1111-1111-111111111111";

    /// <summary>The fixed AAD token the configured <see cref="StaticPowerBiTokenProvider"/> returns.</summary>
    public const string AadToken = "fake-aad-token";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    private PowerBiTestHost(WebApplicationFactory<Program> factory, ServiceProvider provider)
    {
        _factory = factory;
        _provider = provider;
        _scope = provider.CreateScope();
    }

    public IPowerBiEmbedService EmbedService =>
        _scope.ServiceProvider.GetRequiredService<IPowerBiEmbedService>();

    /// <summary>
    /// A raw <see cref="HttpClient"/> that hits the mock directly (no bearer header,
    /// no embed service), so a test can prove the mock's own auth/404 behaviour
    /// independently of the service that normally supplies the token.
    /// </summary>
    public HttpClient RawMockClient => _factory.CreateClient();

    /// <summary>
    /// Builds a host wiring the embed service against the in-memory mock with a
    /// <see cref="StaticPowerBiTokenProvider"/> supplying <see cref="AadToken"/>.
    /// </summary>
    public static PowerBiTestHost Create()
    {
        var factory = new WebApplicationFactory<Program>();
        var primaryHandler = factory.Server.CreateHandler();

        var services = new ServiceCollection();
        var builder = services.AddBlazorDXPowerBi(options =>
        {
            options.ApiBaseUrl = "http://localhost";
            options.WorkspaceId = WorkspaceId;
        });
        builder.UseTokenProvider(new StaticPowerBiTokenProvider(AadToken));

        // Route the named client's transport through the in-memory test handler.
        services.AddHttpClient(PowerBiServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        _ = builder;
        var provider = services.BuildServiceProvider();
        return new PowerBiTestHost(factory, provider);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _factory.Dispose();
    }
}
