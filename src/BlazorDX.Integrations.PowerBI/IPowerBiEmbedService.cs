namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// The server-side embed seam: given a report id, produces a
/// <see cref="PowerBiEmbedConfig"/> (embed URL + short-lived embed token) the
/// browser-side Power BI SDK can render. All Azure AD credentials and the embed
/// token are minted and held server-side; the token is returned to the caller
/// (the host endpoint/component) but never placed in a URL. Registered scoped; the
/// default is <see cref="PowerBiEmbedService"/>.
/// </summary>
public interface IPowerBiEmbedService
{
    /// <summary>
    /// Resolves the report's embed URL and mints a View-level embed token for it,
    /// combining them into a <see cref="PowerBiEmbedConfig"/>.
    /// </summary>
    /// <param name="reportId">The report GUID within the configured workspace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="PowerBiEmbedException">
    /// Thrown when the report lookup or token mint fails (unknown report, rejected
    /// AAD token, transport error, or an unparseable body). Never throws a raw
    /// transport exception out.
    /// </exception>
    Task<PowerBiEmbedConfig> CreateEmbedConfigAsync(string reportId, CancellationToken ct = default);

    /// <summary>
    /// Reads a report's metadata (id, name, embed URL, dataset) without minting a
    /// token. Useful for listing/inspection.
    /// </summary>
    /// <exception cref="PowerBiEmbedException">Thrown on lookup failure, as above.</exception>
    Task<PowerBiReportInfo> GetReportAsync(string reportId, CancellationToken ct = default);
}
