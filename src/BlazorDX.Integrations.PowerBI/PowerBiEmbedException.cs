namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Raised when building an embed config fails in a way the service can describe:
/// a non-success HTTP status from the Power BI REST API (401 from a rejected AAD
/// token, 404 for an unknown report, or any other failure), or a response body
/// that cannot be parsed into the documented shape. The service maps these to this
/// exception rather than letting a raw <see cref="HttpRequestException"/> or parse
/// error escape, so the host (and the component next turn) can show a clear
/// message instead of crashing.
/// </summary>
public sealed class PowerBiEmbedException : Exception
{
    public PowerBiEmbedException(string message)
        : base(message)
    {
    }

    public PowerBiEmbedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The HTTP status code of the failing response, when the failure came from a
    /// transport-level response. <c>null</c> for non-HTTP failures (e.g. a parse,
    /// or the token provider throwing).
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Which step of the two-call embed flow failed: resolving the report
    /// (<c>GET .../reports/{id}</c>) or minting the token (<c>POST .../GenerateToken</c>).
    /// Helps a caller tell "report not found" from "token mint refused".
    /// </summary>
    public PowerBiEmbedStage Stage { get; init; }
}

/// <summary>The step of the embed flow an error occurred in.</summary>
public enum PowerBiEmbedStage
{
    /// <summary>The failure happened before or outside a specific REST call (e.g. the token provider).</summary>
    None,

    /// <summary>The <c>GET groups/{groupId}/reports/{reportId}</c> report lookup.</summary>
    ResolveReport,

    /// <summary>The <c>POST .../GenerateToken</c> embed-token mint.</summary>
    GenerateToken,
}
