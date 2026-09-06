namespace BlazorDX.Components.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taking a dependency on Microsoft.Extensions.TimeProvider.Testing: the
/// only thing the suite needs is <c>Task.Delay(delay, provider, token)</c> completing on demand,
/// and that reduces to CreateTimer plus a clock. Timers are fired in due-time order so a test that
/// advances past several at once sees them complete in the order real time would have produced.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<FakeTimer> timers = [];
    private readonly Lock gate = new();
    private DateTimeOffset now;

    public ManualTimeProvider(DateTimeOffset start) => now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        FakeTimer timer = new(this, callback, state, period);
        timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
        lock (gate)
        {
            timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that becomes due.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = now + by;
        while (true)
        {
            FakeTimer? next;
            lock (gate)
            {
                next = timers.Where(t => t.DueAt is { } due && due <= target)
                             .OrderBy(t => t.DueAt!.Value)
                             .FirstOrDefault();
            }

            if (next is null)
            {
                break;
            }

            now = next.DueAt!.Value;
            next.DueAt = next.Period == Timeout.InfiniteTimeSpan ? null : now + next.Period;
            next.Fire();
        }

        now = target;
    }

    private void Remove(FakeTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private sealed class FakeTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan period) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; private set; } = period;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            Period = period;
            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
