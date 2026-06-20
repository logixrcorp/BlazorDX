using Microsoft.Extensions.Logging;

namespace BlazorDX.Primitives.Diagnostics;

/// <summary>
/// An <see cref="IDxDiagnostics"/> that forwards every event to <c>ILogger</c> —
/// the zero-config way to send BlazorDX failures wherever the app already logs
/// (console, Application Insights, OpenTelemetry, Serilog, …). Register with the
/// no-argument <c>AddBlazorDXDiagnostics()</c>.
/// </summary>
public sealed class LoggerDxDiagnostics : IDxDiagnostics
{
    private readonly ILogger logger;

    public LoggerDxDiagnostics(ILoggerFactory loggerFactory) =>
        logger = loggerFactory.CreateLogger("BlazorDX");

    public void Report(DiagnosticEvent diagnostic)
    {
        LogLevel level = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => LogLevel.Error,
            DiagnosticSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        logger.Log(level, diagnostic.Exception, "[{Source}] {Message}", diagnostic.Source, diagnostic.Message);
    }
}
