namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Off-browser no-op Power BI bridge (static SSR / Interactive Server prerender),
/// where there is no DOM and no Power BI SDK to call. Lets <c>DxPowerBiReport</c>
/// render its accessible container, loading, and error states without a runtime
/// dependency on the browser; the real embed happens only once the component is
/// interactive in WebAssembly and <see cref="PowerBiInterop"/> is resolved.
/// </summary>
public sealed class NullPowerBiInterop : IPowerBiInterop
{
    /// <inheritdoc />
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask EmbedAsync(string elementId, string configJson) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask UnmountAsync(string elementId) => ValueTask.CompletedTask;
}
