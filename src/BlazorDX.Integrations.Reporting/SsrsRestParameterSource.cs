using System.Text.Json;

namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// The default <see cref="IReportParameterSource"/>: reads a report's parameter
/// metadata from the SSRS REST <c>parameterdefinitions</c> endpoint over the
/// injected REST <see cref="HttpClient"/>, deserializing the JSON only through the
/// source-generated <see cref="ReportingJsonContext"/> (ADR 0002, zero reflection).
/// </summary>
internal sealed class SsrsRestParameterSource : IReportParameterSource
{
    private readonly HttpClient _httpClient;

    public SsrsRestParameterSource(HttpClient httpClient) =>
        _httpClient = httpClient;

    public async Task<IReadOnlyList<ReportParameter>> GetParametersAsync(
        string reportPath, CancellationToken ct = default)
    {
        var relativePath = SsrsUrlBuilder.BuildParameterDefinitionsPath(reportPath);
        var relativeUri = new Uri(relativePath, UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(relativeUri, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReportRenderException(
                $"Could not reach the report server for parameters of '{reportPath}'.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(response, ct).ConfigureAwait(false);
                var code = SsrsErrorCodes.Extract(body);
                throw new ReportRenderException(
                    $"Reading parameters for '{reportPath}' failed ({(int)response.StatusCode}). " +
                    $"{SsrsErrorCodes.Summarize(body)}".TrimEnd())
                {
                    StatusCode = (int)response.StatusCode,
                    SsrsErrorCode = code,
                };
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);

            ParameterDefinitionsResponse? parsed;
            try
            {
                parsed = await JsonSerializer.DeserializeAsync(
                    stream, ReportingJsonContext.Default.ParameterDefinitionsResponse, ct)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new ReportRenderException(
                    $"The parameter response for '{reportPath}' was not valid JSON.", ex);
            }

            if (parsed?.Value is not { } definitions)
            {
                return Array.Empty<ReportParameter>();
            }

            var result = new List<ReportParameter>(definitions.Count);
            foreach (var dto in definitions)
            {
                result.Add(Map(dto));
            }

            return result;
        }
    }

    private static ReportParameter Map(ParameterDefinitionDto dto) => new(
        Name: dto.Name ?? string.Empty,
        Type: ParseType(dto.Type),
        Prompt: dto.Prompt,
        Nullable: dto.Nullable,
        MultiValue: dto.MultiValue,
        Required: dto.Required,
        DefaultValue: dto.DefaultValue,
        ValidValues: dto.ValidValues is { Count: > 0 } v ? v : null);

    /// <summary>
    /// Maps the wire type string to <see cref="ReportParameterDataType"/>. Unknown
    /// or absent types fall back to <see cref="ReportParameterDataType.String"/> so
    /// a new SSRS type never crashes the form.
    /// </summary>
    private static ReportParameterDataType ParseType(string? type) => type switch
    {
        "Integer" => ReportParameterDataType.Integer,
        "Boolean" => ReportParameterDataType.Boolean,
        "DateTime" => ReportParameterDataType.DateTime,
        "Float" => ReportParameterDataType.Float,
        _ => ReportParameterDataType.String,
    };

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
    }
}
