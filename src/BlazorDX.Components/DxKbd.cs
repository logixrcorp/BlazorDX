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

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxKbd>? s;

    private DxStrings<DxKbd> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string[] tokens = Combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-kbd-combo {Class}".TrimEnd());
        if (tokens.Length > 0)
        {
            builder.AddAttribute(2, "role", "img");
            // The joiner is spoken text, not punctuation: a screen reader reads it aloud between
            // every pair of keys, so it has to translate along with the key names themselves.
            builder.AddAttribute(3, "aria-label", string.Join(S["ComboJoiner", " plus "], tokens.Select(Spoken)));
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

    // Short, glanceable label for the key cap. Instance, not static, because the arms now read
    // through the localizer -- and each arm is a literal S["Key", "English"] pair so the English
    // stays visible here and LocalizedStringConsistencyTests can still check it against the .resx.
    //
    // The purely symbolic arms (⌘ ⌫ ↑ ↓ ← →) are deliberately NOT localized: they carry no
    // language, and DX1003 ignores letter-free literals for exactly this reason.
    private string Display(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => S["KeyCapControl", "Ctrl"],
        "cmd" or "command" or "meta" or "win" => "⌘",
        "alt" or "option" or "opt" => S["KeyCapAlt", "Alt"],
        "shift" => S["KeyCapShift", "Shift"],
        "enter" or "return" => S["KeyCapEnter", "Enter"],
        "esc" or "escape" => S["KeyCapEscape", "Esc"],
        "space" or "spacebar" => S["KeyCapSpace", "Space"],
        "tab" => S["KeyCapTab", "Tab"],
        "del" or "delete" => S["KeyCapDelete", "Del"],
        "backspace" => "⌫",
        "up" or "arrowup" => "↑",
        "down" or "arrowdown" => "↓",
        "left" or "arrowleft" => "←",
        "right" or "arrowright" => "→",
        _ => token.Length == 1 ? token.ToUpperInvariant() : Capitalize(token),
    };

    // Spoken form for the aria-label, so symbols are announced as words. Separate keys from
    // Display's: "Ctrl" and "Control" are the same key in English but need not be in every
    // language, and a screen reader saying the abbreviation would be a regression.
    private string Spoken(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => S["SpokenControl", "Control"],
        "cmd" or "command" or "meta" or "win" => S["SpokenCommand", "Command"],
        "alt" or "option" or "opt" => S["SpokenAlt", "Alt"],
        "shift" => S["SpokenShift", "Shift"],
        "up" or "arrowup" => S["SpokenUpArrow", "Up arrow"],
        "down" or "arrowdown" => S["SpokenDownArrow", "Down arrow"],
        "left" or "arrowleft" => S["SpokenLeftArrow", "Left arrow"],
        "right" or "arrowright" => S["SpokenRightArrow", "Right arrow"],
        "backspace" => S["SpokenBackspace", "Backspace"],
        _ => token.Length == 1 ? token.ToUpperInvariant() : Capitalize(token),
    };

    private static string Capitalize(string token) =>
        char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
}
