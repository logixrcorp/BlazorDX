using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Combo normalization and the match -> action dispatch path.</summary>
public sealed class DxHotkeysTests : TestContext
{
    // Captures the .NET match callback + the registered bindings so a test can
    // simulate a keypress without a real DOM listener.
    private sealed class FakeHotkeys : IHotkeyInterop
    {
        public Action<string>? OnMatch { get; private set; }
        public string[] Bindings { get; private set; } = [];

        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask SubscribeAsync(Action<string> onMatch) { OnMatch = onMatch; return ValueTask.CompletedTask; }
        public ValueTask SetBindingsAsync(string[] combos) { Bindings = combos; return ValueTask.CompletedTask; }
        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("Ctrl+K", "ctrl+k")]
    [InlineData("ctrl+k", "ctrl+k")]
    [InlineData("K+Ctrl", "ctrl+k")]            // order-insensitive
    [InlineData("⌘+K", "ctrl+k")]          // Cmd folds into ctrl
    [InlineData("Cmd+K", "ctrl+k")]
    [InlineData("Ctrl + Shift + P", "ctrl+shift+p")]
    [InlineData("Alt+Shift+Ctrl+S", "ctrl+alt+shift+s")]
    public void Normalizes_combos_to_the_js_form(string input, string expected)
    {
        Assert.Equal(expected, DxHotkeys.Normalize(input));
    }

    [Fact]
    public void Renders_no_markup()
    {
        Services.AddScoped<IHotkeyInterop>(_ => new FakeHotkeys());
        IRenderedComponent<DxHotkeys> hk = RenderComponent<DxHotkeys>();
        Assert.Equal(string.Empty, hk.Markup.Trim());
    }

    [Fact]
    public void Registers_normalized_bindings()
    {
        FakeHotkeys fake = new();
        Services.AddScoped<IHotkeyInterop>(_ => fake);

        RenderComponent<DxHotkeys>(parameters => parameters.Add(h => h.Bindings, new[]
        {
            new Hotkey("Ctrl+K", EventCallback.Factory.Create(this, () => { })),
            new Hotkey("Ctrl+Shift+P", EventCallback.Factory.Create(this, () => { })),
        }));

        Assert.Contains("ctrl+k", fake.Bindings);
        Assert.Contains("ctrl+shift+p", fake.Bindings);
    }

    [Fact]
    public void Pressing_a_registered_combo_runs_its_action()
    {
        FakeHotkeys fake = new();
        Services.AddScoped<IHotkeyInterop>(_ => fake);

        int count = 0;
        RenderComponent<DxHotkeys>(parameters => parameters.Add(h => h.Bindings, new[]
        {
            new Hotkey("Ctrl+B", EventCallback.Factory.Create(this, () => count++)),
        }));

        // Simulate the JS listener firing for the matched combo.
        Assert.NotNull(fake.OnMatch);
        fake.OnMatch!("ctrl+b");

        Assert.Equal(1, count);
    }

    [Fact]
    public void Unknown_combo_does_nothing()
    {
        FakeHotkeys fake = new();
        Services.AddScoped<IHotkeyInterop>(_ => fake);

        int count = 0;
        RenderComponent<DxHotkeys>(parameters => parameters.Add(h => h.Bindings, new[]
        {
            new Hotkey("Ctrl+B", EventCallback.Factory.Create(this, () => count++)),
        }));

        fake.OnMatch!("ctrl+z");   // not registered
        Assert.Equal(0, count);
    }
}
