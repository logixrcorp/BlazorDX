namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// A finished QR symbol: its module grid plus the parameters chosen to build it.
/// </summary>
/// <param name="Version">QR version (1–4); the side length is 17 + 4·version.</param>
/// <param name="Level">The error-correction level used.</param>
/// <param name="Mode">The data-encoding mode chosen for the payload.</param>
/// <param name="Mask">The data-mask pattern (0–7) the encoder selected.</param>
/// <param name="Modules">The square grid of modules; <c>true</c> = dark.</param>
public sealed record QrSymbol(int Version, QrErrorLevel Level, QrMode Mode, int Mask, bool[,] Modules)
{
    /// <summary>Side length of the symbol in modules.</summary>
    public int Size => Modules.GetLength(0);

    /// <summary>Whether the module at (row, col) is dark.</summary>
    public bool IsDark(int row, int col) => Modules[row, col];
}

/// <summary>
/// Pure, reflection-free QR encoder (versions 1–4; numeric, alphanumeric, and byte
/// modes; all four error-correction levels). Heavy lifting — Galois-field
/// Reed-Solomon, bitstream assembly, module placement, masking — lives in the
/// <c>Qr</c> internals; this is the public entry point.
/// </summary>
public static class QrCode
{
    /// <summary>
    /// Encodes <paramref name="text"/> into a QR symbol at the given error-correction
    /// level, picking the most compact mode and smallest fitting version (1–4).
    /// </summary>
    /// <param name="forceMask">
    /// Forces a specific data mask (0–7) instead of penalty-based selection. Intended
    /// for verification against fixed reference symbols; leave null in normal use.
    /// </param>
    public static QrSymbol Encode(string text, QrErrorLevel level = QrErrorLevel.Medium, int? forceMask = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        QrMode mode = QrEncoder.SelectMode(text);
        int version = QrEncoder.SelectVersion(text, mode, level);
        int[] data = QrEncoder.BuildDataCodewords(text, mode, version, level);
        int[] codewords = QrEncoder.InterleaveWithEc(data, version, level);

        (bool[,] modules, int mask) = QrMatrix.Build(version, level, codewords, forceMask);
        return new QrSymbol(version, level, mode, mask, modules);
    }
}
