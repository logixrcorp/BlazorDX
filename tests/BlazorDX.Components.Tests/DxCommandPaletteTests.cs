using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + filter/run behavior for the command palette (no-op overlay bridge).</summary>
public sealed class DxCommandPaletteTests : TestContext
{
    private string ran = "(none)";

    private IReadOnlyList<Command> BuildCommands() =>
    [
        new Command("New document", () => ran = "New document"),
        new Command("Open settings", () => ran = "Open settings"),
        new Command("Toggle dark mode", () => ran = "Toggle dark mode"),
    ];

    public DxCommandPaletteTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
    }

    [Fact]
    public void Renders_nothing_when_closed()
    {
        IRenderedComponent<DxCommandPalette> palette = RenderComponent<DxCommandPalette>(parameters => parameters
            .Add(p => p.Commands, BuildCommands())
            .Add(p => p.Open, false));

        Assert.Empty(palette.FindAll("[role=option]"));
    }

    [Fact]
    public void Lists_all_commands_when_opened()
    {
        IRenderedComponent<DxCommandPalette> palette = RenderComponent<DxCommandPalette>(parameters => parameters
            .Add(p => p.Commands, BuildCommands())
            .Add(p => p.Open, true));

        Assert.Equal(3, palette.FindAll("[role=option]").Count);
        Assert.Equal("dialog", palette.Find("[aria-modal=true]").GetAttribute("role"));
    }

    [Fact]
    public void Typing_filters_the_commands()
    {
        IRenderedComponent<DxCommandPalette> palette = RenderComponent<DxCommandPalette>(parameters => parameters
            .Add(p => p.Commands, BuildCommands())
            .Add(p => p.Open, true));

        palette.Find(".dx-cmdk-input").Input("set");

        var options = palette.FindAll("[role=option]");
        Assert.Single(options);
        Assert.Contains("Open settings", palette.Markup);
    }

    [Fact]
    public void Enter_runs_the_active_command_and_closes()
    {
        bool open = true;
        IRenderedComponent<DxCommandPalette> palette = RenderComponent<DxCommandPalette>(parameters => parameters
            .Add(p => p.Commands, BuildCommands())
            .Add(p => p.Open, open)
            .Add(p => p.OpenChanged, value => open = value));

        // Filter to "Toggle", then Enter runs the single match.
        palette.Find(".dx-cmdk-input").Input("toggle");
        palette.Find(".dx-cmdk-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Toggle dark mode", ran);
        Assert.False(open); // closed after running
    }
}
