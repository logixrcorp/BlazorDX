using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A text input that formats its value against a fixed mask as the user types.
/// Mask tokens: <c>0</c> = digit, <c>A</c> = letter, <c>*</c> = letter or digit; any
/// other character is a literal that is inserted automatically. A leaf component;
/// the masking is pure and reflection-free (see <see cref="ApplyMask"/>).
/// </summary>
public sealed class DxMaskedTextBox : ComponentBase
{
    /// <summary>The formatted value (two-way bindable via <c>@bind-Value</c>).</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Raised with the re-masked value as the user types.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The mask pattern, e.g. <c>(000) 000-0000</c>.</summary>
    [Parameter, EditorRequired] public string Mask { get; set; } = string.Empty;

    /// <summary>Placeholder shown when empty.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Optional field label.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Disables the input.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Accessible label (falls back to <see cref="Label"/>).</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the field.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", $"dx-field {Class}".TrimEnd());

        if (Label is not null)
        {
            builder.OpenElement(2, "span");
            builder.AddAttribute(3, "class", "dx-field-label");
            builder.AddContent(4, Label);
            builder.CloseElement();
        }

        builder.OpenElement(5, "input");
        builder.AddAttribute(6, "class", "dx-input");
        builder.AddAttribute(7, "type", "text");
        builder.AddAttribute(8, "value", Value);
        builder.AddAttribute(9, "placeholder", Placeholder ?? Mask);
        builder.AddAttribute(10, "disabled", Disabled);
        builder.AddAttribute(11, "aria-label", AriaLabel ?? Label);
        builder.AddAttribute(12, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.CloseElement();

        builder.CloseElement();
    }

    private async Task OnInput(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        string masked = ApplyMask(Mask, args.Value?.ToString() ?? string.Empty);
        if (masked != Value && ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(masked);
        }
    }

    /// <summary>
    /// Formats <paramref name="raw"/> input against <paramref name="mask"/>. Significant
    /// characters are placed into the token positions; literals are inserted as needed
    /// and any literals the user typed are skipped. Trailing literals (with no further
    /// input) are omitted. Reflection-free and pure — safe to unit test directly.
    /// </summary>
    public static string ApplyMask(string mask, string raw)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(raw);

        StringBuilder result = new();
        StringBuilder pendingLiterals = new();
        int rawIndex = 0;

        foreach (char token in mask)
        {
            if (IsToken(token))
            {
                // Skip raw characters that don't satisfy this token (e.g. literals).
                while (rawIndex < raw.Length && !Matches(token, raw[rawIndex]))
                {
                    rawIndex++;
                }

                if (rawIndex >= raw.Length)
                {
                    break;   // no more input; drop any pending trailing literals
                }

                result.Append(pendingLiterals);
                pendingLiterals.Clear();
                result.Append(raw[rawIndex]);
                rawIndex++;
            }
            else
            {
                pendingLiterals.Append(token);
                // If the user typed this literal explicitly, consume it.
                if (rawIndex < raw.Length && raw[rawIndex] == token)
                {
                    rawIndex++;
                }
            }
        }

        return result.ToString();
    }

    private static bool IsToken(char c) => c is '0' or 'A' or '*';

    private static bool Matches(char token, char c) => token switch
    {
        '0' => char.IsDigit(c),
        'A' => char.IsLetter(c),
        '*' => char.IsLetterOrDigit(c),
        _ => false,
    };
}
