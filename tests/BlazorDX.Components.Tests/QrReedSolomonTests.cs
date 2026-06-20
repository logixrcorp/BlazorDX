using BlazorDX.Primitives.Barcodes.Qr;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Anchors the QR Reed-Solomon core to a published worked example: the message
/// "HELLO WORLD" at version 1, level M (Thonky tutorial). The 16 data codewords
/// must yield exactly these 10 error-correction codewords.
/// </summary>
public sealed class QrReedSolomonTests
{
    [Fact]
    public void Produces_the_published_error_correction_codewords()
    {
        int[] data =
        [
            32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17,
        ];

        int[] expectedEc = [196, 35, 39, 119, 235, 215, 231, 226, 93, 23];

        Assert.Equal(expectedEc, ReedSolomon.Encode(data, 10));
    }

    [Fact]
    public void Generator_polynomial_is_monic_with_the_right_degree()
    {
        int[] generator = ReedSolomon.GeneratorPolynomial(10);
        Assert.Equal(11, generator.Length);   // degree 10 → 11 coefficients
        Assert.Equal(1, generator[0]);         // leading coefficient is 1
    }
}
