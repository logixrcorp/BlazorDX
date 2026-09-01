using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// <see cref="DxAlert"/>'s <c>Dismiss</c> aria-label is injected via
/// <see cref="IStringLocalizer{DxAlert}"/> (ADR 0016's localization spike) -- every test that
/// renders the component needs one registered (Blazor resolves <c>[Inject]</c> properties
/// unconditionally at activation, whether or not the component logic ends up reading them),
/// so each test explicitly registers either the real localizer (<see cref="Services"/>'s
/// <c>AddLocalization()</c>, resolving <c>DxAlert.resx</c>'s real values) or
/// <see cref="FakeStringLocalizer{T}"/> (a sentinel, for the one test that needs to prove the
/// string is actually wired through the localizer rather than still hardcoded).
/// </summary>
public sealed class DxAlertTests : TestContext
{
    [Fact]
    public void Renders_severity_class_role_and_title()
    {
        Services.AddLocalization();
        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "error")
            .Add(a => a.Title, "Build failed"));

        IElement root = alert.Find("div.dx-alert");
        Assert.Contains("dx-alert-error", root.ClassName);
        Assert.Equal("alert", root.GetAttribute("role"));
        Assert.Contains("Build failed", alert.Markup);
    }

    [Fact]
    public void Dismiss_button_hidden_unless_Dismissible()
    {
        Services.AddLocalization();
        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "info"));

        Assert.Empty(alert.FindAll("button.dx-alert-close"));
    }

    [Fact]
    public void Dismiss_aria_label_is_wired_through_the_localizer_not_hardcoded()
    {
        // The real English resource value is also "Dismiss" -- asserting that string would
        // pass whether or not the component actually calls the localizer. The sentinel is
        // the only assertion that distinguishes "wired to IStringLocalizer<DxAlert>" from
        // "still a hardcoded literal that happens to match."
        Services.AddSingleton<IStringLocalizer<DxAlert>>(new FakeStringLocalizer<DxAlert>());

        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "error")
            .Add(a => a.Dismissible, true)
            .Add(a => a.OnDismiss, EventCallback.Factory.Create(this, () => { })));

        IElement button = alert.Find("button.dx-alert-close");
        Assert.Equal("§§DISMISS§§", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void Dismiss_falls_back_to_the_invariant_resource_when_no_translation_is_registered()
    {
        // The real IStringLocalizerFactory (not a fake) -- proves DxAlert.resx's own
        // invariant-culture value round-trips through the actual resource pipeline,
        // not just that a registered fake happens to return "Dismiss".
        Services.AddLocalization();

        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "error")
            .Add(a => a.Dismissible, true)
            .Add(a => a.OnDismiss, EventCallback.Factory.Create(this, () => { })));

        IElement button = alert.Find("button.dx-alert-close");
        Assert.Equal("Dismiss", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void OnDismiss_fires_on_click()
    {
        Services.AddLocalization();
        bool dismissed = false;
        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "warning")
            .Add(a => a.Dismissible, true)
            .Add(a => a.OnDismiss, EventCallback.Factory.Create(this, () => dismissed = true)));

        alert.Find("button.dx-alert-close").Click();
        Assert.True(dismissed);
    }
}
