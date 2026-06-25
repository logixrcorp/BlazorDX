using BlazorDX.Integrations.Reporting;
using BlazorDX.MockReportServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// Spins up the real mock SSRS server in-memory and wires the production
/// <c>AddBlazorDXReporting</c> DI graph against it, so the client and parameter
/// source are exercised over real HTTP through <see cref="IHttpClientFactory"/> —
/// not a hand-built client. The factory's in-memory handler is injected as each
/// named client's primary handler, so request building, status mapping, and JSON
/// parsing all run exactly as they would in production.
/// </summary>
internal sealed class ReportingTestHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    private ReportingTestHost(
        WebApplicationFactory<Program> factory, ServiceProvider provider)
    {
        _factory = factory;
        _provider = provider;
        _scope = provider.CreateScope();
    }

    public IReportRenderer Renderer =>
        _scope.ServiceProvider.GetRequiredService<IReportRenderer>();

    public IReportParameterSource ParameterSource =>
        _scope.ServiceProvider.GetRequiredService<IReportParameterSource>();

    /// <summary>
    /// Builds a host. <paramref name="configure"/> runs against the
    /// <see cref="ReportingOptions"/> so a test can set credentials. The mock's
    /// base URLs are fixed placeholders; the in-memory handler ignores the host and
    /// routes by path.
    /// </summary>
    // The standalone mock Program reads MOCKRS_AUTH from the process environment at
    // host-build time. That is process-global, so building two hosts concurrently
    // could let one test's auth setting leak into another's host. We serialize the
    // set-env / build / clear-env window behind this lock so each host captures the
    // intended setting regardless of test parallelism.
    private static readonly object HostBuildLock = new();

    public static ReportingTestHost Create(
        bool authEnabled = false, Action<ReportingOptions>? configure = null)
    {
        WebApplicationFactory<Program> factory;
        HttpMessageHandler primaryHandler;
        lock (HostBuildLock)
        {
            SetAuthEnv(authEnabled);
            try
            {
                factory = new WebApplicationFactory<Program>();
                // Force the host to build (reading the env) before we clear it.
                primaryHandler = factory.Server.CreateHandler();
            }
            finally
            {
                SetAuthEnv(false);
            }
        }

        var services = new ServiceCollection();
        var builder = services.AddBlazorDXReporting(options =>
        {
            options.ServerUrl = "http://localhost/ReportServer";
            options.RestUrl = "http://localhost/reports";
            configure?.Invoke(options);
        });

        // Route both named clients' transport through the in-memory test handler.
        services.AddHttpClient(ReportingServiceCollectionExtensions.RenderClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        services.AddHttpClient(ReportingServiceCollectionExtensions.RestClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        _ = builder;
        var provider = services.BuildServiceProvider();
        return new ReportingTestHost(factory, provider);
    }

    private static void SetAuthEnv(bool enabled)
    {
        Environment.SetEnvironmentVariable("MOCKRS_AUTH", enabled ? "true" : null);
        Environment.SetEnvironmentVariable("MOCKRS_USER", enabled ? "report" : null);
        Environment.SetEnvironmentVariable("MOCKRS_PASS", enabled ? "viewer" : null);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _factory.Dispose();
    }
}
