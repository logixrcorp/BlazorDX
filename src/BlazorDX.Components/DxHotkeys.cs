using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A global keyboard shortcut and the action it runs.</summary>
/// <param name="Combo">Key combo, e.g. "Ctrl+K" or "Ctrl+Shift+P" (case- and order-insensitive).</param>
/// <param name="Invoke">Invoked when the combo is pressed; an <see cref="EventCallback"/> so the host re-renders.</param>
/// <param name="Description">Optional human-readable label (for a future cheatsheet).</param>
public readonly record struct Hotkey(string Combo, EventCallback Invoke, string? Description = null);

/// <summary>
/// Registers global keyboard shortcuts. Place once (e.g. in a layout); pressing a
/// registered combo runs its action and suppresses the browser default. Matching
/// happens in JS so the default can be prevented synchronously. Renders nothing.
/// </summary>
public sealed class DxHotkeys : ComponentBase, IAsyncDisposable
{
    private Dictionary<string, Hotkey> map = new();
    private bool subscribed;

    [Parameter] public IReadOnlyList<Hotkey> Bindings { get; set; } = [];

    [Inject] private IHotkeyInterop Interop { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        map = new Dictionary<string, Hotkey>(Bindings.Count);
        foreach (Hotkey binding in Bindings)
        {
            map[Normalize(binding.Combo)] = binding;
        }

        if (subscribed)
        {
            await Interop.SetBindingsAsync(map.Keys.ToArray());
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Behavior-only: no markup.
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The injected bridge is the no-op variant off-browser, so subscribing
        // unconditionally is safe and keeps the component testable.
        if (firstRender)
        {
            subscribed = true;
            await Interop.SubscribeAsync(OnMatch);
            await Interop.SetBindingsAsync(map.Keys.ToArray());
        }
    }

    // Invoked from JS with the matched combo; marshal back onto the render loop.
    private void OnMatch(string combo) => _ = InvokeAsync(async () =>
    {
        if (map.TryGetValue(combo, out Hotkey binding) && binding.Invoke.HasDelegate)
        {
            await binding.Invoke.InvokeAsync();
        }
    });

    /// <summary>
    /// Normalizes a combo to the JS form: lowercase, modifiers in ctrl/alt/shift
    /// order, Cmd/Meta folded into Ctrl. So "⌘K", "Ctrl+k", and "k+ctrl" all match.
    /// </summary>
    public static string Normalize(string combo)
    {
        bool ctrl = false, alt = false, shift = false;
        string key = string.Empty;

        foreach (string raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control" or "cmd" or "command" or "meta" or "⌘": ctrl = true; break;
                case "alt" or "option" or "opt": alt = true; break;
                case "shift": shift = true; break;
                default: key = raw.ToLowerInvariant(); break;
            }
        }

        List<string> parts = new(4);
        if (ctrl)
        {
            parts.Add("ctrl");
        }

        if (alt)
        {
            parts.Add("alt");
        }

        if (shift)
        {
            parts.Add("shift");
        }

        if (key.Length > 0)
        {
            parts.Add(key);
        }

        return string.Join('+', parts);
    }

    public async ValueTask DisposeAsync()
    {
        if (subscribed)
        {
            await Interop.UnsubscribeAsync();
        }
    }
}
