using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Toast auto-dismiss, and the WCAG 2.2.1 pause/resume of the countdown.</summary>
/// <remarks>
/// These ran against the wall clock: start a 30ms toast, sleep 200ms, assert it is gone. That is a
/// test which passes on an idle machine and fails on a loaded CI runner, and this one did — it was
/// the suite's longest-standing flake. The countdown now runs on an injected
/// <see cref="ManualTimeProvider"/>, so a test states the elapsed time instead of waiting for it
/// and the sleeps are gone. Total sleep across this file went from 1.2 seconds to none.
/// </remarks>
public sealed class DxToastTests : TestContext
{
    private static ManualTimeProvider NewClock() => new(DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Completes on the service's next change notification. Dismissal runs on a continuation, so
    /// advancing the clock and asserting immediately would race the thread pool — but this awaits
    /// the removal itself rather than a duration, which is the difference that matters. The
    /// timeout is only a backstop, so a genuine failure reports as one instead of hanging.
    /// </summary>
    private static Task NextChangeAsync(ToastService service)
    {
        TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler()
        {
            service.OnChange -= Handler;
            signal.TrySetResult();
        }

        service.OnChange += Handler;
        return signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Toast_auto_dismisses_after_its_duration()
    {
        ManualTimeProvider clock = NewClock();
        ToastService svc = new(clock);
        svc.Show("hi", durationMs: 30);
        Assert.Single(svc.Toasts);

        Task dismissed = NextChangeAsync(svc);
        clock.Advance(TimeSpan.FromMilliseconds(30));
        await dismissed;

        Assert.Empty(svc.Toasts);
    }

    [Fact]
    public async Task A_toast_survives_right_up_to_its_duration()
    {
        // The other half of the contract, which the sleeping version could not express: a 30ms
        // toast must still be there at 29ms. Asserting only that it eventually disappears would
        // pass just as well against a service that dismissed instantly.
        ManualTimeProvider clock = NewClock();
        ToastService svc = new(clock);
        svc.Show("hi", durationMs: 30);

        clock.Advance(TimeSpan.FromMilliseconds(29));
        Assert.Single(svc.Toasts);

        Task dismissed = NextChangeAsync(svc);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await dismissed;

        Assert.Empty(svc.Toasts);
    }

    [Fact]
    public async Task Pausing_holds_the_toast_then_resuming_dismisses_it()
    {
        ManualTimeProvider clock = NewClock();
        ToastService svc = new(clock);
        svc.Show("read me", durationMs: 40);

        svc.PauseAll();
        clock.Advance(TimeSpan.FromMinutes(5));         // however long: a paused toast stays
        Assert.Single(svc.Toasts);

        svc.ResumeAll();
        Task dismissed = NextChangeAsync(svc);
        clock.Advance(TimeSpan.FromMilliseconds(40));   // resume restarts a full countdown
        await dismissed;

        Assert.Empty(svc.Toasts);
    }

    [Fact]
    public async Task Host_pauses_on_hover_and_resumes_on_leave()
    {
        ManualTimeProvider clock = NewClock();
        ToastService svc = new(clock);
        Services.AddScoped(_ => svc);
        IRenderedComponent<DxToastHost> host = RenderComponent<DxToastHost>();

        svc.Show("hi", durationMs: 150);
        host.Find(".dx-toast-host").TriggerEvent("onmouseenter", new MouseEventArgs());   // pause
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(svc.Toasts);                  // hover held the toast

        host.Find(".dx-toast-host").TriggerEvent("onmouseleave", new MouseEventArgs());   // resume
        Task dismissed = NextChangeAsync(svc);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await dismissed;

        Assert.Empty(svc.Toasts);
    }
}
