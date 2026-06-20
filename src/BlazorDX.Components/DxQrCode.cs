using System.Globalization;
using BlazorDX.Primitives.Barcodes.Qr;
using BlazorDX.Primitives.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders a QR code as SVG. The module matrix comes from <see cref="QrCode"/>
/// (pure, reflection-free; versions 1–4, all EC levels); this component only draws
/// it, with a standard quiet zone. Colours use CSS variables, though scanners need
/// dark-on-light contrast.
/// </summary>
public sealed class DxQrCode : ComponentBase
{
    /// <summary>The payload to encode.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = string.Empty;

    /// <summary>Error-correction level (higher tolerates more damage but holds less data).</summary>
    [Parameter] public QrErrorLevel Level { get; set; } = QrErrorLevel.Medium;

    /// <summary>Pixel size of a single module.</summary>
    [Parameter] public double ModuleSize { get; set; } = 6;

    /// <summary>Extra CSS classes for the root <c>svg</c>.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    // The standard QR quiet zone is four modules on every side.
    private const int Quiet = 4;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        QrSymbol qr;
        try
        {
            qr = QrCode.Encode(Value, Level);
        }
        catch (ArgumentException ex)
        {
            (Services.GetService(typeof(IDxDiagnostics)) as IDxDiagnostics)
                .TryReportWarning("DxQrCode", $"Could not encode value: {ex.Message}");
            BuildError(builder);
            return;
        }

        int dimension = qr.Size + (Quiet * 2);
        double pixels = dimension * ModuleSize;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-qrcode {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {dimension} {dimension}"));
        builder.AddAttribute(4, "width", Inv($"{pixels}"));
        builder.AddAttribute(5, "height", Inv($"{pixels}"));
        builder.AddAttribute(6, "role", "img");
        builder.AddAttribute(7, "aria-label", $"QR code: {Value}");
        builder.AddAttribute(8, "shape-rendering", "crispEdges");

        // Light background, including the quiet zone (sized in module units).
        builder.OpenElement(9, "rect");
        builder.AddAttribute(10, "x", "0");
        builder.AddAttribute(11, "y", "0");
        builder.AddAttribute(12, "width", Inv($"{dimension}"));
        builder.AddAttribute(13, "height", Inv($"{dimension}"));
        builder.AddAttribute(14, "fill", "var(--dx-qr-bg, #ffffff)");
        builder.CloseElement();

        // One rect per dark module, offset by the quiet zone.
        for (int row = 0; row < qr.Size; row++)
        {
            for (int col = 0; col < qr.Size; col++)
            {
                if (!qr.IsDark(row, col))
                {
                    continue;
                }

                builder.OpenElement(15, "rect");
                builder.AddAttribute(16, "x", Inv($"{col + Quiet}"));
                builder.AddAttribute(17, "y", Inv($"{row + Quiet}"));
                builder.AddAttribute(18, "width", "1");
                builder.AddAttribute(19, "height", "1");
                builder.AddAttribute(20, "fill", "var(--dx-qr-fg, #000000)");
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    private void BuildError(RenderTreeBuilder builder)
    {
        builder.OpenElement(30, "span");
        builder.AddAttribute(31, "class", "dx-barcode-error");
        builder.AddContent(32, $"Cannot encode as QR (1–4): \"{Value}\"");
        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
