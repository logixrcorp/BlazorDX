using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Toast service/host, alert, progress, spinner.</summary>
public sealed class DxFeedbackTests : TestContext
{
    [Fact]
    public void Toast_service_adds_and_removes_with_change_events()
    {
        ToastService service = new();
        int changes = 0;
        service.OnChange += () => changes++;

        service.Show("hello", "success", durationMs: 100000); // long, so it stays
        Assert.Single(service.Toasts);
        Assert.Equal("success", service.Toasts[0].Severity);

        service.Remove(service.Toasts[0].Id);
        Assert.Empty(service.Toasts);
        Assert.Equal(2, changes); // add + remove
    }

    [Fact]
    public void ToastHost_renders_active_toasts_and_dismisses()
    {
        ToastService service = new();
        Services.AddSingleton(service); // test-only; real app registers scoped
        service.Show("on screen", "info", durationMs: 100000);

        IRenderedComponent<DxToastHost> host = RenderComponent<DxToastHost>();
        Assert.Contains("on screen", host.Markup);

        host.Find(".dx-toast-close").Click();
        Assert.Empty(host.FindAll(".dx-toast"));
    }

    [Fact]
    public void Alert_uses_alert_role_for_error_and_renders_title()
    {
        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(parameters => parameters
            .Add(a => a.Severity, "error")
            .Add(a => a.Title, "Boom")
            .Add(a => a.ChildContent, (RenderFragment)(b => b.AddContent(0, "It broke"))));

        Assert.Equal("alert", alert.Find(".dx-alert").GetAttribute("role"));
        Assert.Contains("Boom", alert.Markup);
        Assert.Contains("It broke", alert.Markup);
    }

    [Fact]
    public void Progress_reports_value_and_indeterminate()
    {
        IRenderedComponent<DxProgress> determinate = RenderComponent<DxProgress>(p => p.Add(x => x.Value, 65));
        Assert.Equal("65", determinate.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
        Assert.Contains("width:65%", determinate.Find(".dx-progress-fill").GetAttribute("style"));

        IRenderedComponent<DxProgress> indeterminate = RenderComponent<DxProgress>();
        Assert.False(indeterminate.Find("[role=progressbar]").HasAttribute("aria-valuenow"));
        Assert.Contains("dx-progress-indeterminate", indeterminate.Find(".dx-progress-fill").GetAttribute("class"));
    }

    [Fact]
    public void Spinner_has_status_role()
    {
        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>(p => p.Add(s => s.Label, "Working"));
        var el = spinner.Find(".dx-spinner");
        Assert.Equal("status", el.GetAttribute("role"));
        Assert.Equal("Working", el.GetAttribute("aria-label"));
    }
}
