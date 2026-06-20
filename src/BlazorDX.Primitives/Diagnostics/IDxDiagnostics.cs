namespace BlazorDX.Primitives.Diagnostics;

/// <summary>How serious a <see cref="DiagnosticEvent"/> is.</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One diagnostic emitted by a BlazorDX component: what happened, where, and the
/// exception if any. Components report these instead of silently swallowing failures,
/// so apps get a single place to log/observe them.
/// </summary>
public sealed record DiagnosticEvent(
    DiagnosticSeverity Severity,
    string Source,
    string Message,
    Exception? Exception = null);

/// <summary>
/// The observability sink BlazorDX reports failures to. Register one (e.g. wiring it to
/// <c>ILogger</c>, Application Insights, or OpenTelemetry) via
/// <c>AddBlazorDXDiagnostics</c>. It is resolved <em>optionally</em> — if none is
/// registered, components keep their resilient behavior and report nowhere.
/// </summary>
public interface IDxDiagnostics
{
    void Report(DiagnosticEvent diagnostic);
}

/// <summary>Convenience reporting helpers.</summary>
public static class DxDiagnosticsExtensions
{
    public static void ReportError(this IDxDiagnostics diagnostics, string source, string message, Exception? exception = null) =>
        diagnostics.Report(new DiagnosticEvent(DiagnosticSeverity.Error, source, message, exception));

    public static void ReportWarning(this IDxDiagnostics diagnostics, string source, string message) =>
        diagnostics.Report(new DiagnosticEvent(DiagnosticSeverity.Warning, source, message));

    public static void ReportInfo(this IDxDiagnostics diagnostics, string source, string message) =>
        diagnostics.Report(new DiagnosticEvent(DiagnosticSeverity.Info, source, message));

    /// <summary>Reports an error to <paramref name="diagnostics"/> when it is non-null; a no-op otherwise.</summary>
    public static void TryReportError(this IDxDiagnostics? diagnostics, string source, string message, Exception? exception = null) =>
        diagnostics?.Report(new DiagnosticEvent(DiagnosticSeverity.Error, source, message, exception));

    /// <summary>Reports a warning to <paramref name="diagnostics"/> when it is non-null; a no-op otherwise.</summary>
    public static void TryReportWarning(this IDxDiagnostics? diagnostics, string source, string message) =>
        diagnostics?.Report(new DiagnosticEvent(DiagnosticSeverity.Warning, source, message));

    /// <summary>Reports an informational event (e.g. an AI tool-call audit) when non-null; a no-op otherwise.</summary>
    public static void TryReportInfo(this IDxDiagnostics? diagnostics, string source, string message) =>
        diagnostics?.Report(new DiagnosticEvent(DiagnosticSeverity.Info, source, message));
}

/// <summary>A sink that does nothing — the default when no diagnostics are registered.</summary>
public sealed class NullDxDiagnostics : IDxDiagnostics
{
    public static readonly NullDxDiagnostics Instance = new();

    public void Report(DiagnosticEvent diagnostic)
    {
        // Intentionally empty.
    }
}

/// <summary>A sink that forwards every event to a delegate — the easy way to wire any logger.</summary>
public sealed class DelegateDxDiagnostics : IDxDiagnostics
{
    private readonly Action<DiagnosticEvent> sink;

    public DelegateDxDiagnostics(Action<DiagnosticEvent> sink) => this.sink = sink;

    public void Report(DiagnosticEvent diagnostic) => sink(diagnostic);
}
