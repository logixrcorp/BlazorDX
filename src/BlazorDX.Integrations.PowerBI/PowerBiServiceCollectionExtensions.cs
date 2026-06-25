using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Registration for the Power BI embedding integration (ADR 0014, level 1: one DI
/// call). Wires a typed <see cref="HttpClient"/> through <see cref="IHttpClientFactory"/>
/// with its base address fixed to the configured API root + <c>/v1.0/myorg/</c>,
/// plus the scoped <see cref="IPowerBiEmbedService"/>. Services are scoped, never
/// Singleton, in keeping with the BlazorDX state-isolation rule. No Authorization
/// header is baked into the client here — it is applied per request from the
/// swappable <see cref="IPowerBiTokenProvider"/>, so the AAD token stays
/// server-side and out of any shared client state (ADR 0010).
/// </summary>
public static class PowerBiServiceCollectionExtensions
{
    /// <summary>The named <see cref="HttpClient"/> used for Power BI REST calls.</summary>
    public const string HttpClientName = "BlazorDX.PowerBI.Rest";

    /// <summary>
    /// Registers the Power BI services with the supplied options. Returns a
    /// <see cref="PowerBiBuilder"/> so a caller can supply the token provider
    /// (<c>.UseTokenProvider&lt;T&gt;()</c>, ADR 0014 level 4) before the container
    /// is built. A token provider MUST be registered for the embed flow to run.
    /// </summary>
    public static PowerBiBuilder AddBlazorDXPowerBi(
        this IServiceCollection services, Action<PowerBiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PowerBiOptions();
        configure(options);
        Validate(options);

        // A single resolved options instance is shared across scopes. It carries no
        // per-request state and (by design) no secrets, so sharing it is safe.
        services.TryAddSingleton(options);

        // The REST base address ends with '/v1.0/myorg/' (trailing slash) so the
        // service's relative 'groups/{id}/reports/{id}' paths resolve under it.
        var baseAddress = BuildApiBase(options.ApiBaseUrl);
        services.AddHttpClient(HttpClientName, client =>
        {
            client.BaseAddress = baseAddress;
        });

        // Scoped service resolving its named typed client from the factory: the
        // HttpMessageHandler lifetime is factory-managed while the service stays
        // scoped to the circuit/request.
        services.TryAddScoped<IPowerBiEmbedService>(sp =>
            new PowerBiEmbedService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                sp.GetRequiredService<IPowerBiTokenProvider>(),
                sp.GetRequiredService<PowerBiOptions>()));

        return new PowerBiBuilder(services);
    }

    private static void Validate(PowerBiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            throw new ArgumentException(
                $"{nameof(PowerBiOptions)}.{nameof(PowerBiOptions.ApiBaseUrl)} is required.",
                nameof(options));
        }

        if (!Uri.TryCreate(options.ApiBaseUrl.Trim(), UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                $"{nameof(PowerBiOptions)}.{nameof(PowerBiOptions.ApiBaseUrl)} must be an absolute URL.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.WorkspaceId))
        {
            throw new ArgumentException(
                $"{nameof(PowerBiOptions)}.{nameof(PowerBiOptions.WorkspaceId)} is required.",
                nameof(options));
        }
    }

    /// <summary>
    /// Normalizes the API base to <c>{root}/v1.0/myorg/</c> with a single trailing
    /// slash, so relative request paths resolve cleanly under the documented prefix.
    /// </summary>
    private static Uri BuildApiBase(string apiBaseUrl)
    {
        var root = apiBaseUrl.Trim().TrimEnd('/');
        return new Uri(root + "/v1.0/myorg/", UriKind.Absolute);
    }
}

/// <summary>
/// The progressive-disclosure seam (ADR 0014 level 4): lets a caller supply the
/// <see cref="IPowerBiTokenProvider"/> (the AAD token acquirer) after the one-line
/// registration, without the embed service above it changing.
/// </summary>
public sealed class PowerBiBuilder
{
    private readonly IServiceCollection _services;

    internal PowerBiBuilder(IServiceCollection services) => _services = services;

    /// <summary>The services being configured, for advanced wiring.</summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Registers <typeparamref name="TProvider"/> as the AAD token provider
    /// (scoped). This is the load-bearing swap point of ADR 0014: the host plugs in
    /// MSAL, a managed identity, or a cached acquirer here.
    /// </summary>
    public PowerBiBuilder UseTokenProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, IPowerBiTokenProvider
    {
        _services.RemoveAll<IPowerBiTokenProvider>();
        _services.AddScoped<IPowerBiTokenProvider, TProvider>();
        return this;
    }

    /// <summary>
    /// Registers a specific <see cref="IPowerBiTokenProvider"/> instance (singleton).
    /// Handy for the <see cref="StaticPowerBiTokenProvider"/> in tests/demos.
    /// </summary>
    public PowerBiBuilder UseTokenProvider(IPowerBiTokenProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _services.RemoveAll<IPowerBiTokenProvider>();
        _services.AddSingleton(provider);
        return this;
    }
}
