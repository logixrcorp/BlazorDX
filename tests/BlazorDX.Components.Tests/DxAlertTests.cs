using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// <see cref="DxAlert"/>'s <c>Dismiss</c> aria-label goes through <c>DxStrings</c>, which
/// resolves <see cref="IStringLocalizer{T}"/> <i>optionally</i> (ADR 0021): with none registered
/// the English text at the call site renders, so a test only registers a localizer when it is
/// asserting something about localization. The three that do cover the three states that can
/// differ -- no localizer at all, a registered localizer (sentinel, proving the string is really
/// routed through it), and the real resource pipeline.
/// </summary>
public sealed class DxAlertTests : TestContext
{
    [Fact]
    public void Renders_severity_class_role_and_title()
    {
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
        // The English fallback is also "Dismiss" -- asserting that string would pass whether or
        // not the component actually calls the localizer. The sentinel is the only assertion that
        // distinguishes "routed through the localizer" from "still a hardcoded literal that
        // happens to match" -- the exact confusion ADR 0016 hit during Phase 0.
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
    public void Dismiss_renders_English_when_no_localizer_is_registered_at_all()
    {
        // The reason DxStrings exists. A consumer who never calls AddLocalization() must get
        // English, not an activation exception -- rendering this component with an empty service
        // collection is the whole assertion. Before ADR 0021 this threw.
        IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
            .Add(a => a.Severity, "error")
            .Add(a => a.Dismissible, true)
            .Add(a => a.OnDismiss, EventCallback.Factory.Create(this, () => { })));

        Assert.Equal("Dismiss", alert.Find("button.dx-alert-close").GetAttribute("aria-label"));
    }

    [Fact]
    public void Dismiss_resolves_the_French_resource_under_a_French_UI_culture()
    {
        // Nothing else in the suite sets a culture, so DxAlert.fr.resx was only ever validated by
        // the AOT publish job noticing a satellite assembly. This is the assertion that the
        // fr -> invariant fallback chain actually resolves a translated value.
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fr");
            Services.AddLocalization();

            IRenderedComponent<DxAlert> alert = RenderComponent<DxAlert>(p => p
                .Add(a => a.Severity, "error")
                .Add(a => a.Dismissible, true)
                .Add(a => a.OnDismiss, EventCallback.Factory.Create(this, () => { })));

            Assert.Equal("Ignorer", alert.Find("button.dx-alert-close").GetAttribute("aria-label"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
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
