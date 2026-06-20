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
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> toasts = new();

    public IReadOnlyList<Toast> Toasts => toasts;

    /// <summary>Raised whenever the set of active toasts changes.</summary>
    public event Action? OnChange;

    /// <summary>Shows a toast and removes it automatically after <paramref name="durationMs"/>.</summary>
    public void Show(string message, string severity = "info", int durationMs = 4000)
    {
        Toast toast = new(Guid.NewGuid().ToString("N"), message, severity);
        toasts.Add(toast);
        OnChange?.Invoke();
        _ = DismissAfterAsync(toast.Id, durationMs);
    }

    public void Remove(string id)
    {
        if (toasts.RemoveAll(t => t.Id == id) > 0)
        {
            OnChange?.Invoke();
        }
    }

    private async Task DismissAfterAsync(string id, int durationMs)
    {
        await Task.Delay(durationMs);
        Remove(id);
    }
}
