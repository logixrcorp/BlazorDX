namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// Arithmetic over GF(256) with the QR-code primitive polynomial 0x11D and a
/// generator of 2. Precomputes exponent/log tables so multiply is a table lookup.
/// Pure and allocation-free at call time.
/// </summary>
internal static class GaloisField
{
    private const int Primitive = 0x11D;   // x^8 + x^4 + x^3 + x^2 + 1

    private static readonly int[] Exp = new int[512];
    private static readonly int[] Log = new int[256];

    static GaloisField()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = x;
            Log[x] = i;
            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= Primitive;
            }
        }

        // Duplicate the cycle so Exp[a+b] never needs a modulo at the call site.
        for (int i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    /// <summary>The field element α^<paramref name="power"/>.</summary>
    public static int Exponent(int power) => Exp[power % 255];

    /// <summary>Multiplies two field elements.</summary>
    public static int Multiply(int a, int b) =>
        a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];
}
