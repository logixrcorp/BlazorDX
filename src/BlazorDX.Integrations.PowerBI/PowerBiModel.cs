namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Metadata for one Power BI report as returned by the REST
/// <c>GET groups/{groupId}/reports/{reportId}</c> endpoint: its id, display name,
/// the SDK <see cref="EmbedUrl"/> the browser navigates the iframe to, and the
/// dataset it is bound to. This carries no secret — it is safe to surface to the
/// browser. The embed <em>token</em> (the secret) lives only in
/// <see cref="PowerBiEmbedConfig"/>.
/// </summary>
/// <param name="Id">The report's GUID.</param>
/// <param name="Name">The report's display name.</param>
/// <param name="EmbedUrl">
/// The base URL the Power BI client SDK loads in the iframe. It does NOT contain
/// the embed token; the SDK supplies the token separately at load time.
/// </param>
/// <param name="DatasetId">The id of the dataset the report is bound to, when present.</param>
public sealed record PowerBiReportInfo(
    string Id,
    string Name,
    string EmbedUrl,
    string? DatasetId);

/// <summary>
/// The kind of token an embed config carries. Power BI's "embed for your
/// customers" flow uses an <see cref="Embed"/> token (minted server-side via
/// GenerateToken); the value mirrors the SDK's <c>tokenType</c> enum name.
/// </summary>
public enum PowerBiTokenType
{
    /// <summary>An embed token minted by GenerateToken (the app-owns-data flow).</summary>
    Embed,

    /// <summary>A raw Azure AD access token (the user-owns-data flow). Not minted here.</summary>
    Aad,
}

/// <summary>
/// Everything the browser-side Power BI client SDK needs to render one report:
/// the <see cref="EmbedUrl"/>, the short-lived <see cref="EmbedToken"/>, which
/// report it is for, the token type, and when the token expires. The host hands
/// this to the component (next turn); the embed token is the one secret here and
/// is produced server-side per request, never read from a URL.
/// </summary>
/// <param name="EmbedUrl">The report's embed URL (from <see cref="PowerBiReportInfo.EmbedUrl"/>).</param>
/// <param name="EmbedToken">The short-lived embed token minted by GenerateToken.</param>
/// <param name="ReportId">The report this config is for.</param>
/// <param name="TokenType">The token kind; always <see cref="PowerBiTokenType.Embed"/> here.</param>
/// <param name="Expiration">UTC instant at which <see cref="EmbedToken"/> stops being valid.</param>
public sealed record PowerBiEmbedConfig(
    string EmbedUrl,
    string EmbedToken,
    string ReportId,
    PowerBiTokenType TokenType,
    DateTimeOffset Expiration);
