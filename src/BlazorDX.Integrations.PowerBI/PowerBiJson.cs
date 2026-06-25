using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Wire shape of the Power BI REST <c>GET groups/{groupId}/reports/{reportId}</c>
/// response, e.g.
/// <c>{ "id": "...", "name": "Sales", "embedUrl": "https://app.powerbi.com/reportEmbed?...", "datasetId": "..." }</c>.
/// Deserialized only through the source-generated <see cref="PowerBiJsonContext"/>
/// (ADR 0002: zero reflection, trim/AOT-clean) — never via a reflection-based
/// <c>JsonSerializer.Deserialize&lt;T&gt;</c> overload.
/// </summary>
internal sealed class PowerBiReportResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("embedUrl")]
    public string? EmbedUrl { get; init; }

    [JsonPropertyName("datasetId")]
    public string? DatasetId { get; init; }
}

/// <summary>
/// Wire shape of the Power BI REST <c>POST .../GenerateToken</c> response, e.g.
/// <c>{ "token": "H4sI...", "tokenId": "...", "expiration": "2026-06-25T12:00:00Z" }</c>.
/// </summary>
internal sealed class GenerateTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("tokenId")]
    public string? TokenId { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }
}

/// <summary>
/// Request body for <c>POST .../GenerateToken</c>: the documented contract is
/// <c>{ "accessLevel": "View" }</c>. Serialized through the source-gen context so
/// the body is produced without reflection too.
/// </summary>
internal sealed class GenerateTokenRequest
{
    [JsonPropertyName("accessLevel")]
    public string AccessLevel { get; init; } = "View";
}

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for the Power BI wire
/// types. Case-insensitive property matching guards against minor casing drift in
/// the REST surface without resorting to reflection (ADR 0002).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PowerBiReportResponse))]
[JsonSerializable(typeof(GenerateTokenResponse))]
[JsonSerializable(typeof(GenerateTokenRequest))]
internal sealed partial class PowerBiJsonContext : JsonSerializerContext;
