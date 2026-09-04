using System.Threading;
using System.Threading.Tasks;
using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Diagnostics;
using BlazorDX.Primitives.Grid;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Diagnostics sink, DxErrorBoundary containment + reporting, and remote-grid OnDataError.</summary>
public sealed class ObservabilityTests : TestContext
{
    private sealed class RecordingDiagnostics : IDxDiagnostics
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Report(DiagnosticEvent diagnostic) => Events.Add(diagnostic);
    }

    // A child that throws during render when asked, to trip the boundary.
    private sealed class Boom : ComponentBase
    {
        [Parameter] public bool Throw { get; set; }
        protected override void OnParametersSet()
        {
            if (Throw)
            {
                throw new InvalidOperationException("kaboom");
            }
        }
    }

    private static RenderFragment ThrowingChild() => builder =>
    {
        builder.OpenComponent<Boom>(0);
        builder.AddComponentParameter(1, nameof(Boom.Throw), true);
        builder.CloseComponent();
    };

    public ObservabilityTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    // ---- Diagnostics sink ----

    [Fact]
    public void Delegate_sink_forwards_events_and_helpers_set_severity()
    {
        List<DiagnosticEvent> captured = new();
        IDxDiagnostics sink = new DelegateDxDiagnostics(captured.Add);

        sink.ReportError("src", "boom", new InvalidOperationException("x"));
        sink.ReportWarning("src", "careful");

        Assert.Equal(DiagnosticSeverity.Error, captured[0].Severity);
        Assert.NotNull(captured[0].Exception);
        Assert.Equal(DiagnosticSeverity.Warning, captured[1].Severity);
    }

    [Fact]
    public void TryReportError_is_a_noop_on_a_null_sink()
    {
        IDxDiagnostics? none = null;
        none.TryReportError("src", "ignored");   // must not throw
    }

    // ---- DxErrorBoundary ----

    [Fact]
    public void Error_boundary_catches_reports_and_shows_the_fallback()
    {
        RecordingDiagnostics diag = new();
        Services.AddScoped<IDxDiagnostics>(_ => diag);
        Exception? raised = null;

        IRenderedComponent<DxErrorBoundary> cut = RenderComponent<DxErrorBoundary>(p => p
            .Add(b => b.ChildContent, ThrowingChild())
            .Add(b => b.OnError, EventCallback.Factory.Create<Exception>(this, e => raised = e)));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".dx-errorboundary"));                 // fallback rendered
            Assert.Single(cut.FindAll(".dx-errorboundary-retry"));           // with a retry button
            Assert.Contains(diag.Events, e => e.Severity == DiagnosticSeverity.Error && e.Source == "DxErrorBoundary");
            Assert.NotNull(raised);                                          // OnError fired
        });
    }

    [Fact]
    public void Error_boundary_uses_a_custom_error_template_when_provided()
    {
        IRenderedComponent<DxErrorBoundary> cut = RenderComponent<DxErrorBoundary>(p => p
            .Add(b => b.ChildContent, ThrowingChild())
            .Add(b => b.ErrorContent, (RenderFragment<Exception>)(ex => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "my-error");
                builder.AddContent(2, ex.Message);
                builder.CloseElement();
            })));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".my-error"));
            Assert.Empty(cut.FindAll(".dx-errorboundary"));   // default fallback not used
            Assert.Contains("kaboom", cut.Markup);
        });
    }

    [Fact]
    public void Error_boundary_shows_detail_only_when_opted_in()
    {
        IRenderedComponent<DxErrorBoundary> cut = RenderComponent<DxErrorBoundary>(p => p
            .Add(b => b.ChildContent, ThrowingChild())
            .Add(b => b.ShowDetail, true));

        cut.WaitForAssertion(() => Assert.Contains("kaboom", cut.Find(".dx-errorboundary-detail").TextContent));
    }

    [Fact]
    public void Error_boundary_without_diagnostics_registered_does_not_throw()
    {
        // No IDxDiagnostics registered → optional resolution returns null → still works.
        IRenderedComponent<DxErrorBoundary> cut = RenderComponent<DxErrorBoundary>(p => p
            .Add(b => b.ChildContent, ThrowingChild()));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".dx-errorboundary")));
    }

    // ---- Remote grid OnDataError ----

    private sealed class FailingSource : IGridDataSource<WidgetRow>
    {
        public Task<GridDataPage<WidgetRow>> GetRowsAsync(GridDataRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("backend down");
    }

    [Fact]
    public void Remote_fetch_failure_raises_OnDataError_and_reports_without_crashing()
    {
        RecordingDiagnostics diag = new();
        Services.AddScoped<IDxDiagnostics>(_ => diag);
        Exception? raised = null;

        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(p => p
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.DataSource, new FailingSource())
            .Add(g => g.OnDataError, EventCallback.Factory.Create<Exception>(this, e => raised = e)));

        grid.WaitForAssertion(() =>
        {
            Assert.NotNull(raised);
            Assert.Contains(diag.Events, e => e.Source == "DxDataGrid.RemoteFetch");
            Assert.NotEmpty(grid.FindAll("[role=grid]"));   // grid still rendered, not crashed
        });
    }

    // ---- ILogger adapter ----

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Logs { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
            Logs.Add((level, ex));

        private sealed class Scope : IDisposable { public void Dispose() { } }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public RecordingLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    [Fact]
    public void Logger_adapter_maps_severity_to_log_level()
    {
        RecordingLoggerFactory factory = new();
        IDxDiagnostics diag = new LoggerDxDiagnostics(factory);

        diag.ReportError("src", "boom", new InvalidOperationException("x"));
        diag.ReportWarning("src", "careful");

        Assert.Equal(LogLevel.Error, factory.Logger.Logs[0].Level);
        Assert.NotNull(factory.Logger.Logs[0].Exception);
        Assert.Equal(LogLevel.Warning, factory.Logger.Logs[1].Level);
    }

    // ---- Encoder bad-input warnings ----

    [Fact]
    public void Barcode_reports_a_warning_when_the_value_cannot_be_encoded()
    {
        RecordingDiagnostics diag = new();
        Services.AddScoped<IDxDiagnostics>(_ => diag);

        // '€' is outside Code 128 Set B (printable ASCII) → the encoder throws.
        RenderComponent<DxBarcode>(p => p.Add(c => c.Value, "abc€"));

        Assert.Contains(diag.Events, e => e.Source == "DxBarcode" && e.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Ean13_reports_a_warning_for_non_numeric_input()
    {
        RecordingDiagnostics diag = new();
        Services.AddScoped<IDxDiagnostics>(_ => diag);

        RenderComponent<DxEan13>(p => p.Add(c => c.Value, "not-digits"));

        Assert.Contains(diag.Events, e => e.Source == "DxEan13" && e.Severity == DiagnosticSeverity.Warning);
    }

    // ---- Clipboard failure ----

    private sealed class FailingClipboardDom : IGridDomInterop
    {
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask<(double, double, double)> MeasureViewportAsync(string id) => ValueTask.FromResult<(double, double, double)>((0, 0, 0));
        public ValueTask<(double, double, double, double)> MeasureViewport2dAsync(string id) => ValueTask.FromResult<(double, double, double, double)>((0, 0, 0, 0));
        public ValueTask SubscribeScrollAsync(string id, Action onScroll) => ValueTask.CompletedTask;
        public ValueTask FocusFirstAsync(string id) => ValueTask.CompletedTask;
        public ValueTask DownloadTextAsync(string f, string m, string c) => ValueTask.CompletedTask;
        public ValueTask DownloadBytesAsync(string f, string m, byte[] c) => ValueTask.CompletedTask;
        public ValueTask<bool> WriteClipboardAsync(string text) => ValueTask.FromResult(false);   // permission denied
        public ValueTask ScrollToAsync(string id, double top) => ValueTask.CompletedTask;
        public ValueTask SuppressArrowKeysAsync(string id) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Clipboard_write_failure_is_reported()
    {
        RecordingDiagnostics diag = new();
        Services.AddScoped<IDxDiagnostics>(_ => diag);
        Services.AddScoped<IGridDomInterop>(_ => new FailingClipboardDom());   // overrides the Null dom

        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(p => p
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Items, new List<WidgetRow> { new() { Name = "A", Quantity = 1 } })
            .Add(g => g.ShowClipboard, true));

        grid.Find(".dx-grid-export").Click();   // the ⧉ Copy button

        grid.WaitForAssertion(() =>
            Assert.Contains(diag.Events, e => e.Source == "DxDataGrid.Clipboard" && e.Severity == DiagnosticSeverity.Error));
    }
}
