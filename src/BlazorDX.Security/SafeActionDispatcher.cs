namespace BlazorDX.Security;

/// <summary>
/// Dispatches asynchronous work such that only the most recent call can affect
/// the UI. Each dispatch cancels the previous one, so a slow earlier response
/// can never overwrite state produced by a newer request — the out-of-order
/// race that silently corrupts manually-managed Blazor loading state.
/// </summary>
/// <remarks>Register one per component (scoped/owned), never as a Singleton.</remarks>
public sealed class SafeActionDispatcher : IDisposable
{
    private CancellationTokenSource? activeSource;

    /// <summary>
    /// Runs <paramref name="action"/> under a fresh cancellation token, cancelling
    /// any in-flight previous action. If this action is itself superseded, its
    /// cancellation is swallowed so callers need no special handling.
    /// </summary>
    public async Task DispatchAsync(Func<CancellationToken, Task> action)
    {
        CancellationTokenSource source = StartNew();
        try
        {
            await action(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // Superseded by a newer dispatch; the newer one owns the UI now.
        }
    }

    /// <summary>
    /// As <see cref="DispatchAsync(Func{CancellationToken, Task})"/>, but applies a
    /// result via <paramref name="onResult"/> only if this action was not superseded.
    /// </summary>
    public async Task DispatchAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        Action<TResult> onResult)
    {
        CancellationTokenSource source = StartNew();
        try
        {
            TResult result = await action(source.Token);
            if (!source.IsCancellationRequested)
            {
                onResult(result);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // Superseded; drop the stale result.
        }
    }

    public void Dispose()
    {
        activeSource?.Cancel();
        activeSource?.Dispose();
        activeSource = null;
    }

    private CancellationTokenSource StartNew()
    {
        activeSource?.Cancel();
        activeSource?.Dispose();
        CancellationTokenSource source = new();
        activeSource = source;
        return source;
    }
}
