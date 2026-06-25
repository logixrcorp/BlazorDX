namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// The Azure AD seam (ADR 0014, level 4): the one moving part a host swaps to
/// decide <em>how</em> an Azure AD bearer token is obtained for the Power BI REST
/// API — a service principal via MSAL, a managed identity, a cached token, or a
/// fixed token in tests. This package deliberately does NOT depend on MSAL: token
/// acquisition is the host's concern. <see cref="PowerBiEmbedService"/> consumes
/// whatever bearer token this returns and applies it server-side as the
/// <c>Authorization</c> header; the token never reaches the browser.
/// </summary>
public interface IPowerBiTokenProvider
{
    /// <summary>
    /// Returns an Azure AD bearer token (the raw token value, without the
    /// <c>Bearer </c> scheme prefix) for the Power BI service.
    /// </summary>
    /// <exception cref="PowerBiEmbedException">
    /// Implementations should surface acquisition failures as this exception so the
    /// embed flow fails with a clear message rather than a raw provider error.
    /// </exception>
    Task<string> GetAadTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// A trivial <see cref="IPowerBiTokenProvider"/> that returns a fixed token. For
/// tests and demos only: it lets the embed flow run against the mock (or a real
/// API with a token you already minted) without wiring MSAL. A production host
/// replaces this via <c>.UseTokenProvider&lt;T&gt;()</c> with a real acquirer.
/// </summary>
public sealed class StaticPowerBiTokenProvider : IPowerBiTokenProvider
{
    private readonly string _token;

    /// <summary>Creates a provider that always returns <paramref name="token"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null or blank.</exception>
    public StaticPowerBiTokenProvider(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A non-empty token is required.", nameof(token));
        }

        _token = token;
    }

    /// <inheritdoc />
    public Task<string> GetAadTokenAsync(CancellationToken ct = default) =>
        Task.FromResult(_token);
}
