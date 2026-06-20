using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + ARIA for the edge-anchored sheet (no-op browser bridges).</summary>
public sealed class DxSheetTests : TestContext
{
    public DxSheetTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Renders_panel_with_dialog_role_and_side_class_when_open()
    {
        IRenderedComponent<DxSheet> sheet = RenderComponent<DxSheet>(parameters => parameters
            .Add(s => s.Open, true)
            .Add(s => s.Side, "left")
            .Add(s => s.AriaLabel, "Settings panel")
            .Add(s => s.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body"))));

        string markup = sheet.Markup;
        Assert.Contains("role=\"dialog\"", markup);
        Assert.Contains("aria-modal=\"true\"", markup);
        Assert.Contains("dx-sheet-panel-left", markup);
        Assert.Contains("aria-label=\"Settings panel\"", markup);
        Assert.Contains("dx-sheet-backdrop", markup);
        Assert.Contains("Body", markup);
    }

    [Fact]
    public void Renders_nothing_when_closed()
    {
        IRenderedComponent<DxSheet> sheet = RenderComponent<DxSheet>(parameters => parameters
            .Add(s => s.Open, false)
            .Add(s => s.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body"))));

        Assert.DoesNotContain("dx-sheet-panel", sheet.Markup);
    }

    [Fact]
    public void Defaults_to_right_side()
    {
        IRenderedComponent<DxSheet> sheet = RenderComponent<DxSheet>(parameters => parameters
            .Add(s => s.Open, true)
            .Add(s => s.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body"))));

        Assert.Contains("dx-sheet-panel-right", sheet.Markup);
    }
}
