using System.Text.Json.Serialization;

namespace BlazorDX.Conduit;

/// <summary>
/// Response body for a successful <c>McpProxyEndpoint</c> delivery: how many live SSE
/// connections the payload was fanned out to, and the message id that was delivered.
/// </summary>
internal sealed class McpProxyAcceptedResponse
{
    [JsonPropertyName("delivered")]
    public int Delivered { get; init; }

    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = string.Empty;
}

/// <summary>Response body for a rejected <c>McpProxyEndpoint</c> delivery (missing/invalid request).</summary>
internal sealed class McpProxyErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for the Conduit's own wire types
/// (ADR 0002: zero reflection, trim/AOT-clean — this library is built with
/// <c>IsAotCompatible</c>, so <c>Results.Json</c> must be called with a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> from here, never the
/// reflection-based overload).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(McpProxyAcceptedResponse))]
[JsonSerializable(typeof(McpProxyErrorResponse))]
internal sealed partial class ConduitJsonContext : JsonSerializerContext;
