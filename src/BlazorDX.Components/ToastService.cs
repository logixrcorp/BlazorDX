namespace BlazorDX.Components;

/// <summary>A transient notification.</summary>
/// <param name="Id">Unique id (used as the render key).</param>
/// <param name="Message">Text shown to the user.</param>
/// <param name="Severity">info / success / warning / error.</param>
public sealed record Toast(string Id, string Message, string Severity);

/// <summary>
/// Queues transient toast notifications and auto-dismisses them. Registered with a
/// scoped lifetime (never Singleton — that would share notifications across users on
/// Blazor Server, per the BlazorDX state-isolation rule). A <c>DxToastHost</c>
/// subscribes to <see cref="OnChange"/> to render the active toasts.
///
/// Auto-dismiss can be paused (<see cref="PauseAll"/>) and resumed (<see cref="ResumeAll"/>)
/// so a hovered or keyboard-focused toast is not whisked away mid-read — the host wires
/// these to pointer/focus events for WCAG 2.2.1 (Timing Adjustable).
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> toasts = new();
    private readonly Dictionary<string, int> durations = new();
    private readonly Dictionary<string, CancellationTokenSource> timers = new();
    private readonly TimeProvider time;

    /// <summary>Creates a service that dismisses toasts against the system clock.</summary>
    public ToastService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates a service that dismisses toasts against <paramref name="timeProvider"/>.
    /// </summary>
    /// <remarks>
    /// The seam exists for tests. A countdown measured against the wall clock can only be
    /// asserted by sleeping for longer than it and hoping, which is a test that passes on an idle
    /// machine and fails on a loaded CI runner — as this suite's did. Nothing in the library
    /// passes anything but <see cref="TimeProvider.System"/>.
    /// </remarks>
    /// <param name="timeProvider">Clock used for the auto-dismiss countdown.</param>
    public ToastService(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        time = timeProvider;
    }

    public IReadOnlyList<Toast> Toasts => toasts;

    /// <summary>Raised whenever the set of active toasts changes.</summary>
    public event Action? OnChange;

    /// <summary>
    /// Shows a toast and removes it automatically after <paramref name="durationMs"/>.
    /// A non-positive duration makes the toast sticky (dismissed only by the user).
    /// </summary>
    public void Show(string message, string severity = "info", int durationMs = 4000)
    {
        Toast toast = new(Guid.NewGuid().ToString("N"), message, severity);
        toasts.Add(toast);
        durations[toast.Id] = durationMs;
        OnChange?.Invoke();
        Schedule(toast.Id, durationMs);
    }

    public void Remove(string id)
    {
        CancelTimer(id);
        durations.Remove(id);
        if (toasts.RemoveAll(t => t.Id == id) > 0)
        {
            OnChange?.Invoke();
        }
    }

    /// <summary>Pauses every auto-dismiss countdown (e.g. while the toast region is hovered
    /// or focused), so a timed notification cannot disappear before it is read.</summary>
    public void PauseAll()
    {
        foreach (string id in timers.Keys.ToArray())
        {
            CancelTimer(id);
        }
    }

    /// <summary>Restarts auto-dismiss for every visible toast (e.g. when the pointer or focus
    /// leaves the region). Each toast gets a fresh countdown.</summary>
    public void ResumeAll()
    {
        foreach (Toast toast in toasts)
        {
            if (!timers.ContainsKey(toast.Id) && durations.TryGetValue(toast.Id, out int ms))
            {
                Schedule(toast.Id, ms);
            }
        }
    }

    private void Schedule(string id, int durationMs)
    {
        if (durationMs <= 0)
        {
            return;   // sticky: no auto-dismiss
        }

        CancellationTokenSource cts = new();
        timers[id] = cts;
        _ = DismissAfterAsync(id, durationMs, cts.Token);
    }

    private void CancelTimer(string id)
    {
        if (timers.Remove(id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task DismissAfterAsync(string id, int durationMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(durationMs), time, token);
        }
        catch (OperationCanceledException)
        {
            return;   // paused or removed before the countdown elapsed
        }

        Remove(id);
    }
}
