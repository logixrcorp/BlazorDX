using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders a keyboard shortcut as a sequence of styled <c>&lt;kbd&gt;</c> keys —
/// e.g. <c>Combo="Ctrl+Shift+P"</c> becomes Ctrl ＋ Shift ＋ P. A leaf component
/// (no primitive); styling is CSS-variable driven (see dx-display.css). Modifier
/// and special-key names are prettified for display, and the whole combo carries
/// a readable <c>aria-label</c> so screen readers announce it as words.
/// </summary>
public sealed class DxKbd : ComponentBase
{
    /// <summary>The shortcut, e.g. "Ctrl+K" or "Ctrl+Shift+P" (parts split on "+").</summary>
    [Parameter] public string Combo { get; set; } = string.Empty;

    /// <summary>Extra CSS classes appended to the combo wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string[] tokens = Combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-kbd-combo {Class}".TrimEnd());
        if (tokens.Length > 0)
        {
            builder.AddAttribute(2, "role", "img");
            builder.AddAttribute(3, "aria-label", string.Join(" plus ", tokens.Select(Spoken)));
        }

        int seq = 4;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                builder.OpenElement(seq++, "span");
                builder.AddAttribute(seq++, "class", "dx-kbd-plus");
                builder.AddAttribute(seq++, "aria-hidden", "true");
                builder.AddContent(seq++, "+");
                builder.CloseElement();
            }

            builder.OpenElement(seq++, "kbd");
            builder.AddAttribute(seq++, "class", "dx-kbd");
            builder.AddAttribute(seq++, "aria-hidden", "true");
            builder.AddContent(seq++, Display(tokens[i]));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // Short, glanceable label for the key cap.
    private static string Display(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Ctrl",
        "cmd" or "command" or "meta" or "win" => "⌘",
        "alt" or "option" or "opt" => "Alt",
        "shift" => "Shift",
        "enter" or "return" => "Enter",
        "esc" or "escape" => "Esc",
        "space" or "spacebar" => "Space",
        "tab" => "Tab",
        "del" or "delete" => "Del",
        "backspace" => "⌫",
        "up" or "arrowup" => "↑",
        "down" or "arrowdown" => "↓",
        "left" or "arrowleft" => "←",
        "right" or "arrowright" => "→",
        _ => token.Length == 1 ? token.ToUpperInvariant() : Capitalize(token),
    };

    // Spoken form for the aria-label, so symbols are announced as words.
    private static string Spoken(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Control",
        "cmd" or "command" or "meta" or "win" => "Command",
        "alt" or "option" or "opt" => "Alt",
        "shift" => "Shift",
        "up" or "arrowup" => "Up arrow",
        "down" or "arrowdown" => "Down arrow",
        "left" or "arrowleft" => "Left arrow",
        "right" or "arrowright" => "Right arrow",
        "backspace" => "Backspace",
        _ => token.Length == 1 ? token.ToUpperInvariant() : Capitalize(token),
    };

    private static string Capitalize(string token) =>
        char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
}
