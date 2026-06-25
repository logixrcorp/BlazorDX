using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// Registration for the SSRS reporting integration (ADR 0014, level 1: one DI
/// call). Wires two typed <see cref="HttpClient"/> instances through
/// <see cref="IHttpClientFactory"/> — one for URL-Access render, one for the REST
/// parameter API — with the base address and any Basic-auth header applied from
/// <see cref="ReportingOptions"/>, plus the scoped <see cref="IReportRenderer"/>
/// and <see cref="IReportParameterSource"/>. Services are scoped, never Singleton,
/// in keeping with the BlazorDX state-isolation rule; credentials stay server-side.
/// </summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>The named <see cref="HttpClient"/> used for URL-Access render requests.</summary>
    public const string RenderClientName = "BlazorDX.Reporting.UrlAccess";

    /// <summary>The named <see cref="HttpClient"/> used for REST parameter metadata.</summary>
    public const string RestClientName = "BlazorDX.Reporting.Rest";

    /// <summary>
    /// Registers the reporting services with the supplied options. Returns a
    /// <see cref="ReportingBuilder"/> so a caller can swap the renderer
    /// (<c>.UseRenderer&lt;T&gt;()</c>, ADR 0014 level 4) before the container is built.
    /// </summary>
    public static ReportingBuilder AddBlazorDXReporting(
        this IServiceCollection services, Action<ReportingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ReportingOptions();
        configure(options);
        Validate(options);

        // A single resolved options instance is shared by both clients. It carries
        // no per-request state, so sharing it across scopes is safe.
        services.TryAddSingleton(options);

        var authHeader = BuildAuthHeader(options);

        // URL-Access: the render query begins with '?', so the base address must
        // keep its exact path (e.g. '/ReportServer') with NO trailing slash, or the
        // '?' would resolve against a '/ReportServer/' the endpoint does not serve.
        services.AddHttpClient(RenderClientName, client =>
        {
            client.BaseAddress = new Uri(options.ServerUrl.Trim(), UriKind.Absolute);
            ApplyAuth(client, authHeader);
        });

        // REST: requests are relative paths under the API root, so the base address
        // must end with '/' for relative resolution to keep the '/reports' segment.
        services.AddHttpClient(RestClientName, client =>
        {
            client.BaseAddress = EnsureTrailingSlash(options.RestUrl ?? options.ServerUrl);
            ApplyAuth(client, authHeader);
        });

        // Scoped renderer/source: each resolves its named typed client from the
        // factory, so the HttpMessageHandler lifetime is managed by the factory
        // while the service itself stays scoped to the circuit/request.
        services.TryAddScoped<IReportRenderer>(sp =>
            new SsrsUrlAccessReportClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(RenderClientName)));

        services.TryAddScoped<IReportParameterSource>(sp =>
            new SsrsRestParameterSource(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(RestClientName)));

        return new ReportingBuilder(services);
    }

    private static void Validate(ReportingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            throw new ArgumentException(
                $"{nameof(ReportingOptions)}.{nameof(ReportingOptions.ServerUrl)} is required.",
                nameof(options));
        }

        if (options.CredentialMode == ReportCredentialMode.Basic &&
            string.IsNullOrEmpty(options.Username))
        {
            throw new ArgumentException(
                "Basic credential mode requires a username.", nameof(options));
        }
    }

    /// <summary>
    /// Builds the Basic <see cref="AuthenticationHeaderValue"/> when the options ask
    /// for it; otherwise <c>null</c>. The service-account mode is a placeholder
    /// (handler wired with the viewer), so it attaches no header here.
    /// </summary>
    private static AuthenticationHeaderValue? BuildAuthHeader(ReportingOptions options)
    {
        if (options.CredentialMode != ReportCredentialMode.Basic)
        {
            return null;
        }

        var raw = $"{options.Username}:{options.Password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private static void ApplyAuth(HttpClient client, AuthenticationHeaderValue? header)
    {
        if (header is not null)
        {
            client.DefaultRequestHeaders.Authorization = header;
        }
    }

    /// <summary>
    /// Normalizes a base URL to end with <c>/</c> so a relative request URI
    /// resolves against the full path (e.g. <c>.../reports/</c> keeps the
    /// <c>reports</c> segment). For the URL-Access client the trailing slash is
    /// harmless: the render query begins with <c>?</c>.
    /// </summary>
    private static Uri EnsureTrailingSlash(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        return new Uri(trimmed, UriKind.Absolute);
    }
}

/// <summary>
/// The progressive-disclosure seam (ADR 0014 level 4): lets a caller replace the
/// default <see cref="IReportRenderer"/> with their own implementation after the
/// one-line registration, without touching the viewer above it.
/// </summary>
public sealed class ReportingBuilder
{
    private readonly IServiceCollection _services;

    internal ReportingBuilder(IServiceCollection services) => _services = services;

    /// <summary>The services being configured, for advanced wiring.</summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Replaces the renderer with <typeparamref name="TRenderer"/> (scoped). Use
    /// this to swap URL-Access for the REST execution API, in-process RDLC, or a
    /// test fake — the load-bearing swap point of ADR 0014.
    /// </summary>
    public ReportingBuilder UseRenderer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRenderer>()
        where TRenderer : class, IReportRenderer
    {
        _services.RemoveAll<IReportRenderer>();
        _services.AddScoped<IReportRenderer, TRenderer>();
        return this;
    }

    /// <summary>Replaces the parameter source with <typeparamref name="TSource"/> (scoped).</summary>
    public ReportingBuilder UseParameterSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>()
        where TSource : class, IReportParameterSource
    {
        _services.RemoveAll<IReportParameterSource>();
        _services.AddScoped<IReportParameterSource, TSource>();
        return this;
    }
}
