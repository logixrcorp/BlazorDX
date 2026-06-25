namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// Raised when a report render or parameter lookup fails in a way the client can
/// describe: a non-success HTTP status, or an SSRS <c>rs*</c> error code in the
/// body (e.g. <c>rsItemNotFound</c>, <c>rsParameterError</c>). The client maps
/// these to this exception rather than letting a raw <see cref="HttpRequestException"/>
/// or parse error escape, so the viewer can show a clear message.
/// </summary>
public sealed class ReportRenderException : Exception
{
    public ReportRenderException(string message)
        : base(message)
    {
    }

    public ReportRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The HTTP status code of the failing response, when the failure came from a
    /// transport-level response. <c>null</c> for non-HTTP failures (e.g. a parse).
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// The SSRS error code parsed from the response body, when present
    /// (e.g. <c>rsItemNotFound</c>). <c>null</c> when no code could be identified.
    /// </summary>
    public string? SsrsErrorCode { get; init; }
}
