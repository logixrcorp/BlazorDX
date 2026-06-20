using System.Globalization;
using BlazorDX.Primitives.Barcodes;
using BlazorDX.Primitives.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders an EAN-13 barcode as crisp SVG rectangles. The bar pattern comes from
/// <see cref="Ean13Encoder"/> (pure, reflection-free); this component only draws
/// it. Colours use CSS variables so the symbol can be themed, though scanners need
/// dark-on-light contrast.
/// </summary>
public sealed class DxEan13 : ComponentBase
{
    /// <summary>The 12- or 13-digit code. With 12 digits the check digit is appended.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = string.Empty;

    /// <summary>Pixel width of a single module (narrowest bar). Larger = bigger symbol.</summary>
    [Parameter] public double ModuleWidth { get; set; } = 2;

    /// <summary>Height of the bars in pixels (excludes the digit caption).</summary>
    [Parameter] public double BarHeight { get; set; } = 70;

    /// <summary>Whether to print the human-readable digits beneath the bars.</summary>
    [Parameter] public bool ShowText { get; set; } = true;

    /// <summary>Extra CSS classes for the root <c>svg</c>.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    // Quiet zone (light margin) in modules on each side — required for scanning.
    private const int QuietModules = 10;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool[] modules;
        string digits;
        try
        {
            digits = Ean13Encoder.Normalize(Value);
            modules = Ean13Encoder.Encode(digits);
        }
        catch (ArgumentException ex)
        {
            (Services.GetService(typeof(IDxDiagnostics)) as IDxDiagnostics)
                .TryReportWarning("DxEan13", $"Could not encode value: {ex.Message}");
            BuildError(builder);
            return;
        }

        double textHeight = ShowText ? 18 : 0;
        double width = (modules.Length + (QuietModules * 2)) * ModuleWidth;
        double height = BarHeight + textHeight;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-barcode {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {width} {height}"));
        builder.AddAttribute(4, "width", Inv($"{width}"));
        builder.AddAttribute(5, "height", Inv($"{height}"));
        builder.AddAttribute(6, "role", "img");
        builder.AddAttribute(7, "aria-label", $"EAN-13 barcode {digits}");

        // Light background (the quiet zone plus gaps).
        builder.OpenElement(8, "rect");
        builder.AddAttribute(9, "x", "0");
        builder.AddAttribute(10, "y", "0");
        builder.AddAttribute(11, "width", Inv($"{width}"));
        builder.AddAttribute(12, "height", Inv($"{height}"));
        builder.AddAttribute(13, "fill", "var(--dx-barcode-bg, #ffffff)");
        builder.CloseElement();

        // Merge consecutive dark modules into a single rect for a compact DOM.
        int run = 0;
        for (int i = 0; i <= modules.Length; i++)
        {
            bool dark = i < modules.Length && modules[i];
            if (dark)
            {
                run++;
                continue;
            }

            if (run > 0)
            {
                double x = (QuietModules + i - run) * ModuleWidth;
                builder.OpenElement(14, "rect");
                builder.AddAttribute(15, "x", Inv($"{x}"));
                builder.AddAttribute(16, "y", "0");
                builder.AddAttribute(17, "width", Inv($"{run * ModuleWidth}"));
                builder.AddAttribute(18, "height", Inv($"{BarHeight}"));
                builder.AddAttribute(19, "fill", "var(--dx-barcode-fg, #000000)");
                builder.CloseElement();
                run = 0;
            }
        }

        if (ShowText)
        {
            builder.OpenElement(20, "text");
            builder.AddAttribute(21, "x", Inv($"{width / 2}"));
            builder.AddAttribute(22, "y", Inv($"{height - 4}"));
            builder.AddAttribute(23, "text-anchor", "middle");
            builder.AddAttribute(24, "font-family", "monospace");
            builder.AddAttribute(25, "font-size", "14");
            builder.AddAttribute(26, "letter-spacing", "2");
            builder.AddAttribute(27, "fill", "var(--dx-barcode-fg, #000000)");
            builder.AddContent(28, digits);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildError(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "span");
        builder.AddAttribute(41, "class", "dx-barcode-error");
        builder.AddContent(42, $"Invalid EAN-13: \"{Value}\"");
        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
