using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Toast auto-dismiss, and the WCAG 2.2.1 pause/resume of the countdown.</summary>
public sealed class DxToastTests : TestContext
{
    [Fact]
    public async Task Toast_auto_dismisses_after_its_duration()
    {
        var svc = new ToastService();
        svc.Show("hi", durationMs: 30);
        Assert.Single(svc.Toasts);

        await Task.Delay(200);

        Assert.Empty(svc.Toasts);
    }

    [Fact]
    public async Task Pausing_holds_the_toast_then_resuming_dismisses_it()
    {
        var svc = new ToastService();
        svc.Show("read me", durationMs: 40);

        svc.PauseAll();
        await Task.Delay(200);
        Assert.Single(svc.Toasts);   // countdown paused — toast stays

        svc.ResumeAll();
        await Task.Delay(200);
        Assert.Empty(svc.Toasts);    // countdown restarted and elapsed
    }

    [Fact]
    public async Task Host_pauses_on_hover_and_resumes_on_leave()
    {
        var svc = new ToastService();
        Services.AddScoped(_ => svc);
        IRenderedComponent<DxToastHost> host = RenderComponent<DxToastHost>();

        svc.Show("hi", durationMs: 150);
        host.Find(".dx-toast-host").TriggerEvent("onmouseenter", new MouseEventArgs());   // pause
        await Task.Delay(400);
        Assert.Single(svc.Toasts);                  // hover held the toast

        host.Find(".dx-toast-host").TriggerEvent("onmouseleave", new MouseEventArgs());   // resume
        await Task.Delay(400);
        Assert.Empty(svc.Toasts);
    }
}
