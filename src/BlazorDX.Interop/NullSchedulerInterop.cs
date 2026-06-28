namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="ISchedulerInterop"/>. There is no DOM and
/// no pointer to watch outside WebAssembly, so registration is a no-op. The scheduler still renders
/// statically and stays fully usable by keyboard and click; drag is a progressive enhancement.
/// </summary>
public sealed class NullSchedulerInterop : ISchedulerInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask RegisterTimeGridAsync(
        string gridId,
        int dayCount,
        int startHour,
        int endHour,
        int hourHeight,
        Action<SchedulerDragResult> onDrag) => ValueTask.CompletedTask;

    public ValueTask UnregisterAsync(string gridId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
