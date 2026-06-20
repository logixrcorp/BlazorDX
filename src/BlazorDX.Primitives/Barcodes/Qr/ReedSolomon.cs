namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// Reed-Solomon error-correction codeword generation over <see cref="GaloisField"/>,
/// as used by QR codes. Pure and reflection-free.
/// </summary>
internal static class ReedSolomon
{
    /// <summary>
    /// The generator polynomial of the given degree: the product of
    /// (x − α^i) for i = 0 … degree−1, with coefficients as field elements.
    /// </summary>
    public static int[] GeneratorPolynomial(int degree)
    {
        int[] poly = [1];
        for (int i = 0; i < degree; i++)
        {
            poly = MultiplyPolynomials(poly, [1, GaloisField.Exponent(i)]);
        }

        return poly;
    }

    /// <summary>
    /// Computes <paramref name="ecCount"/> error-correction codewords for the data
    /// codewords via polynomial division — the remainder of data·x^ecCount ÷ generator.
    /// </summary>
    public static int[] Encode(IReadOnlyList<int> data, int ecCount)
    {
        int[] generator = GeneratorPolynomial(ecCount);
        int[] residue = new int[data.Count + ecCount];
        for (int i = 0; i < data.Count; i++)
        {
            residue[i] = data[i];
        }

        for (int i = 0; i < data.Count; i++)
        {
            int coefficient = residue[i];
            if (coefficient == 0)
            {
                continue;
            }

            for (int j = 0; j < generator.Length; j++)
            {
                residue[i + j] ^= GaloisField.Multiply(generator[j], coefficient);
            }
        }

        int[] ec = new int[ecCount];
        Array.Copy(residue, data.Count, ec, 0, ecCount);
        return ec;
    }

    private static int[] MultiplyPolynomials(int[] a, int[] b)
    {
        int[] product = new int[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                product[i + j] ^= GaloisField.Multiply(a[i], b[j]);
            }
        }

        return product;
    }
}
