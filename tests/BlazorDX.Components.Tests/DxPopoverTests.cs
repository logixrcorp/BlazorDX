using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Render contract for the styled popover. The no-op anchor/overlay bridges stand
/// in for the browser positioning and dismissal behaviors.
/// </summary>
public sealed class DxPopoverTests : TestContext
{
    public DxPopoverTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Renders_trigger_always_and_panel_only_when_open()
    {
        IRenderedComponent<DxPopover> popover = RenderComponent<DxPopover>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Trigger, (RenderFragment)(b => b.AddContent(0, "Trigger text")))
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "Panel body"))));

        string markup = popover.Markup;
        Assert.Contains("Trigger text", markup);
        Assert.Contains("aria-expanded=\"true\"", markup);
        Assert.Contains("role=\"dialog\"", markup);
        Assert.Contains("Panel body", markup);
    }

    [Fact]
    public void Hides_panel_and_marks_collapsed_when_closed()
    {
        IRenderedComponent<DxPopover> popover = RenderComponent<DxPopover>(parameters => parameters
            .Add(p => p.Open, false)
            .Add(p => p.Trigger, (RenderFragment)(b => b.AddContent(0, "Trigger text")))
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "Panel body"))));

        string markup = popover.Markup;
        Assert.Contains("Trigger text", markup);          // trigger always present
        Assert.Contains("aria-expanded=\"false\"", markup);
        Assert.DoesNotContain("Panel body", markup);       // panel absent when closed
    }
}
