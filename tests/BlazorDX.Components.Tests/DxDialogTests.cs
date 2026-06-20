using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Renders the styled dialog through bUnit. The no-op overlay bridge stands in for
/// the browser focus-trap/dismiss behaviors; these tests cover the render contract
/// (mount when open, ARIA, nothing when closed).
/// </summary>
public sealed class DxDialogTests : TestContext
{
    public DxDialogTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
    }

    [Fact]
    public void Renders_panel_with_dialog_semantics_when_open()
    {
        IRenderedComponent<DxDialog> dialog = RenderComponent<DxDialog>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.AriaLabel, "Example")
            .Add(d => d.ChildContent, (RenderFragment)(builder => builder.AddContent(0, "Body text"))));

        string markup = dialog.Markup;
        Assert.Contains("role=\"dialog\"", markup);
        Assert.Contains("aria-modal=\"true\"", markup);
        Assert.Contains("aria-label=\"Example\"", markup);
        Assert.Contains("Body text", markup);
    }

    [Fact]
    public void Renders_nothing_when_closed()
    {
        IRenderedComponent<DxDialog> dialog = RenderComponent<DxDialog>(parameters => parameters
            .Add(d => d.Open, false)
            .Add(d => d.ChildContent, (RenderFragment)(builder => builder.AddContent(0, "Hidden"))));

        Assert.DoesNotContain("role=\"dialog\"", dialog.Markup);
        Assert.DoesNotContain("Hidden", dialog.Markup);
    }
}
