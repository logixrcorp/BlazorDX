using BlazorDX.Primitives.Diagnostics;

namespace BlazorDX.Demo.Client;

/// <summary>
/// A demo diagnostics sink that keeps the most recent events so a page can display them.
/// In a real app this delegate would forward to ILogger / Application Insights / OpenTelemetry.
/// </summary>
public sealed class DemoDiagnosticsLog : IDxDiagnostics
{
    public List<DiagnosticEvent> Events { get; } = new();

    public event Action? Changed;

    public void Report(DiagnosticEvent diagnostic)
    {
        Events.Insert(0, diagnostic);
        if (Events.Count > 20)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        Changed?.Invoke();
    }
}
