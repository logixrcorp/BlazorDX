using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// Wire shape of the SSRS REST <c>parameterdefinitions</c> response, e.g.
/// <c>{ "report": "/Sales/Monthly", "title": "Monthly Sales", "value": [ … ] }</c>.
/// Deserialized only through the source-generated <see cref="ReportingJsonContext"/>
/// (ADR 0002: zero reflection, trim/AOT-clean) — never via a reflection-based
/// <c>JsonSerializer.Deserialize&lt;T&gt;</c> overload.
/// </summary>
internal sealed class ParameterDefinitionsResponse
{
    [JsonPropertyName("report")]
    public string? Report { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("value")]
    public List<ParameterDefinitionDto>? Value { get; init; }
}

/// <summary>One parameter definition as it appears on the wire.</summary>
internal sealed class ParameterDefinitionDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("nullable")]
    public bool Nullable { get; init; }

    [JsonPropertyName("multiValue")]
    public bool MultiValue { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; init; }

    [JsonPropertyName("validValues")]
    public List<string>? ValidValues { get; init; }
}

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for the reporting wire
/// types. Case-insensitive property matching guards against minor casing drift
/// between SSRS builds without resorting to reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ParameterDefinitionsResponse))]
internal sealed partial class ReportingJsonContext : JsonSerializerContext;
