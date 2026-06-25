namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// The render seam (ADR 0014, level 4): the one moving part a senior developer
/// swaps to change <em>how</em> a report is produced — URL-Access against SSRS,
/// the SSRS REST execution API, RDLC in-process, or a fake in tests — without the
/// viewer above it changing. Registered scoped; the default is
/// <see cref="SsrsUrlAccessReportClient"/>.
/// </summary>
public interface IReportRenderer
{
    /// <summary>Renders <paramref name="request"/> to bytes + content type.</summary>
    /// <exception cref="ReportRenderException">
    /// Thrown when the server reports a failure (non-success status or an SSRS
    /// error code). Never throws a raw transport exception out.
    /// </exception>
    Task<RenderedReport> RenderAsync(ReportRequest request, CancellationToken ct = default);
}

/// <summary>
/// The parameter-metadata seam: reads a report's declared parameters so a viewer
/// can build a parameter-entry form. Backed by the SSRS REST
/// <c>parameterdefinitions</c> endpoint via <see cref="SsrsRestParameterSource"/>.
/// </summary>
public interface IReportParameterSource
{
    /// <summary>Reads the declared parameters for the report at <paramref name="reportPath"/>.</summary>
    /// <exception cref="ReportRenderException">
    /// Thrown when the lookup fails (unknown report, transport error, or a body
    /// that cannot be parsed into the metadata model).
    /// </exception>
    Task<IReadOnlyList<ReportParameter>> GetParametersAsync(
        string reportPath, CancellationToken ct = default);
}
