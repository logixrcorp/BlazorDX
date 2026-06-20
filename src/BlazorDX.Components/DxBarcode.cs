using System.Globalization;
using BlazorDX.Primitives.Barcodes;
using BlazorDX.Primitives.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders a Code 128 (Set B) barcode as SVG rectangles. The module pattern comes
/// from <see cref="Code128Encoder"/> (pure, reflection-free); this component only
/// draws it. Suitable for alphanumeric payloads (SKUs, tracking ids, URLs).
/// </summary>
public sealed class DxBarcode : ComponentBase
{
    /// <summary>The text to encode (printable ASCII 32–126).</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = string.Empty;

    /// <summary>Pixel width of a single module (narrowest bar).</summary>
    [Parameter] public double ModuleWidth { get; set; } = 2;

    /// <summary>Height of the bars in pixels (excludes the caption).</summary>
    [Parameter] public double BarHeight { get; set; } = 70;

    /// <summary>Whether to print the encoded text beneath the bars.</summary>
    [Parameter] public bool ShowText { get; set; } = true;

    /// <summary>Extra CSS classes for the root <c>svg</c>.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    // Quiet zone in modules; Code 128 requires at least 10 on each side.
    private const int QuietModules = 10;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool[] modules;
        try
        {
            modules = Code128Encoder.Encode(Value);
        }
        catch (ArgumentException ex)
        {
            (Services.GetService(typeof(IDxDiagnostics)) as IDxDiagnostics)
                .TryReportWarning("DxBarcode", $"Could not encode value: {ex.Message}");
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
        builder.AddAttribute(7, "aria-label", $"Code 128 barcode: {Value}");

        builder.OpenElement(8, "rect");
        builder.AddAttribute(9, "x", "0");
        builder.AddAttribute(10, "y", "0");
        builder.AddAttribute(11, "width", Inv($"{width}"));
        builder.AddAttribute(12, "height", Inv($"{height}"));
        builder.AddAttribute(13, "fill", "var(--dx-barcode-bg, #ffffff)");
        builder.CloseElement();

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
            builder.AddAttribute(25, "font-size", "13");
            builder.AddAttribute(26, "fill", "var(--dx-barcode-fg, #000000)");
            builder.AddContent(27, Value);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildError(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "span");
        builder.AddAttribute(41, "class", "dx-barcode-error");
        builder.AddContent(42, $"Cannot encode as Code 128: \"{Value}\"");
        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
