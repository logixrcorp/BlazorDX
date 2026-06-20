using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An on-screen QWERTY keyboard for touch, kiosk, and accessibility text entry.
/// Keys edit a two-way-bound <see cref="Value"/>; Shift toggles letter case and the
/// number row's symbols, Backspace deletes, Space inserts a space, and Enter raises
/// <see cref="OnEnter"/> (it does not insert a newline). A leaf component; styling is
/// CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxVirtualKeyboard : ComponentBase
{
    private static readonly string[] Rows =
    [
        "1234567890",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    ];

    // Shifted faces for the number row; letters just upper-case.
    private const string Digits = "1234567890";
    private const string Symbols = "!@#$%^&*()";

    private bool shift;

    /// <summary>The text being edited. Two-way bindable via <c>@bind-Value</c>.</summary>
    [Parameter] public string Value { get; set; } = string.Empty;

    /// <summary>Raised whenever <see cref="Value"/> changes from a key press.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Raised when the Enter key is pressed (e.g. to submit).</summary>
    [Parameter] public EventCallback OnEnter { get; set; }

    /// <summary>Extra CSS classes appended to the keyboard container.</summary>
    [Parameter] public string? Class { get; set; }

    private Task AppendAsync(char c) => ValueChanged.InvokeAsync(Value + c);

    private Task BackspaceAsync() =>
        Value.Length == 0 ? Task.CompletedTask : ValueChanged.InvokeAsync(Value[..^1]);

    private void ToggleShift() => shift = !shift;

    private char Face(char key)
    {
        if (char.IsLetter(key))
        {
            return shift ? char.ToUpperInvariant(key) : key;
        }

        int digit = Digits.IndexOf(key);
        return shift && digit >= 0 ? Symbols[digit] : key;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-vkeyboard {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "group");
        builder.AddAttribute(3, "aria-label", "On-screen keyboard");

        int seq = 4;
        foreach (string row in Rows)
        {
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "dx-vkey-row");
            foreach (char key in row)
            {
                char face = Face(key);
                builder.OpenElement(seq++, "button");
                builder.AddAttribute(seq++, "type", "button");
                builder.AddAttribute(seq++, "class", "dx-vkey");
                builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => AppendAsync(face)));
                builder.AddContent(seq++, face.ToString());
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        // Control row: Shift, Space, Backspace, Enter.
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "dx-vkey-row");

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", shift ? "dx-vkey dx-vkey-mod dx-vkey-active" : "dx-vkey dx-vkey-mod");
        builder.AddAttribute(seq++, "aria-pressed", shift ? "true" : "false");
        builder.AddAttribute(seq++, "aria-label", "Shift");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, ToggleShift));
        builder.AddContent(seq++, "⇧ Shift");
        builder.CloseElement();

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "dx-vkey dx-vkey-space");
        builder.AddAttribute(seq++, "aria-label", "Space");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => AppendAsync(' ')));
        builder.AddContent(seq++, "Space");
        builder.CloseElement();

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "dx-vkey dx-vkey-mod");
        builder.AddAttribute(seq++, "aria-label", "Backspace");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, BackspaceAsync));
        builder.AddContent(seq++, "⌫");
        builder.CloseElement();

        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "dx-vkey dx-vkey-mod");
        builder.AddAttribute(seq++, "aria-label", "Enter");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => OnEnter.InvokeAsync()));
        builder.AddContent(seq++, "↵ Enter");
        builder.CloseElement();

        builder.CloseElement();   // control row
        builder.CloseElement();   // keyboard
    }
}
