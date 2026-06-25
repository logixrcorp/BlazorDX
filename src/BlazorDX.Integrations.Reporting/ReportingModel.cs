namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// The SSRS <c>rs:Format</c> values this client requests. Each maps to a single
/// <c>rs:Format=</c> token on the URL-Access URL and to the content type SSRS
/// returns for it.
/// </summary>
public enum ReportFormat
{
    /// <summary>Accessible HTML, requested as <c>rs:Format=HTML5</c>.</summary>
    Html5,

    /// <summary>A PDF document, requested as <c>rs:Format=PDF</c>.</summary>
    Pdf,

    /// <summary>Comma-separated values, requested as <c>rs:Format=CSV</c>.</summary>
    Csv,

    /// <summary>A rendered image (PNG), requested as <c>rs:Format=IMAGE</c>.</summary>
    Image,
}

/// <summary>
/// A request to render one report. Parameters are an <em>ordered multi-map</em>
/// (<see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey,TValue}"/>) so a
/// multi-value SSRS parameter can repeat the same key, and so the URL is built in
/// a deterministic order.
/// </summary>
/// <param name="ReportPath">
/// The catalog path of the report, e.g. <c>/Sales/Monthly</c>. Leading slash is
/// optional; it is normalized when the URL is built.
/// </param>
/// <param name="Format">The output format to request.</param>
/// <param name="Parameters">
/// Report parameter values as ordered key/value pairs. Repeat a key to pass a
/// multi-value parameter. <c>null</c> is treated as an empty set.
/// </param>
/// <param name="ShowToolbar">
/// Maps to <c>rc:Toolbar</c>. Only meaningful for HTML output; ignored by SSRS
/// for binary formats.
/// </param>
public sealed record ReportRequest(
    string ReportPath,
    ReportFormat Format = ReportFormat.Html5,
    IEnumerable<KeyValuePair<string, string>>? Parameters = null,
    bool ShowToolbar = true);

/// <summary>
/// The bytes returned by a render, with the content type SSRS reported and the
/// format that was requested. The bytes are handed to the viewer untouched; the
/// caller decides whether to stream, embed, or offer them as a download.
/// </summary>
/// <param name="Content">The raw rendered bytes.</param>
/// <param name="ContentType">The MIME type from the response (e.g. <c>text/html</c>).</param>
/// <param name="Format">The format that was requested.</param>
public sealed record RenderedReport(byte[] Content, string ContentType, ReportFormat Format);

/// <summary>
/// The SSRS data type of a parameter, mirroring the REST
/// <c>parameterdefinitions</c> surface (<c>String</c>, <c>Integer</c>,
/// <c>Boolean</c>, <c>DateTime</c>, <c>Float</c>). Unknown values map to
/// <see cref="String"/> so an unexpected type never crashes parsing.
/// </summary>
public enum ReportParameterDataType
{
    String,
    Integer,
    Boolean,
    DateTime,
    Float,
}

/// <summary>
/// Metadata for one report parameter, shaped after SSRS parameter definitions so
/// a viewer can build a parameter-entry form: name, type, prompt, whether it is
/// nullable / multi-value / required, an optional default, and an optional closed
/// list of valid values.
/// </summary>
/// <param name="Name">Internal parameter name (the URL query key).</param>
/// <param name="Type">SSRS data type.</param>
/// <param name="Prompt">Human-facing label SSRS would show.</param>
/// <param name="Nullable">Whether the parameter accepts a null/blank value.</param>
/// <param name="MultiValue">Whether the parameter accepts repeated values.</param>
/// <param name="Required">Whether SSRS requires a value (no default, not nullable).</param>
/// <param name="DefaultValue">Default applied when omitted, or <c>null</c>.</param>
/// <param name="ValidValues">Closed list of allowed values, or <c>null</c> if open.</param>
public sealed record ReportParameter(
    string Name,
    ReportParameterDataType Type,
    string? Prompt,
    bool Nullable,
    bool MultiValue,
    bool Required,
    string? DefaultValue,
    IReadOnlyList<string>? ValidValues);
