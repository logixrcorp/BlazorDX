using BlazorDX.Primitives.Barcodes;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Verifies the EAN-13 encoder against the standard. The full-symbol assertion uses
/// the canonical published example for 5901234123457 (the Wikipedia EAN-13 sample),
/// so this is an independent check, not a round-trip against our own expectations.
/// </summary>
public sealed class Ean13EncoderTests
{
    [Fact]
    public void Computes_the_published_check_digit()
    {
        // 5901234123457: the documented check digit is 7.
        Assert.Equal(7, Ean13Encoder.CheckDigit("590123412345"));
        Assert.Equal("5901234123457", Ean13Encoder.Normalize("590123412345"));
    }

    [Fact]
    public void Accepts_a_valid_13_digit_code_and_rejects_a_bad_check_digit()
    {
        Assert.Equal("5901234123457", Ean13Encoder.Normalize("5901234123457"));
        Assert.Throws<ArgumentException>(() => Ean13Encoder.Normalize("5901234123450"));
    }

    [Fact]
    public void Rejects_non_digits_and_wrong_lengths()
    {
        Assert.Throws<ArgumentException>(() => Ean13Encoder.Normalize("59012345234X"));
        Assert.Throws<ArgumentException>(() => Ean13Encoder.Normalize("12345"));
    }

    [Fact]
    public void Encodes_the_canonical_symbol_for_5901234123457()
    {
        // Published 95-module pattern (start 101 · 6 L/G digits · centre 01010 ·
        // 6 R digits · end 101) for first digit 5 (parity LGGLLG).
        const string expected =
            "101"
            + "0001011"  // 9 L
            + "0100111"  // 0 G
            + "0110011"  // 1 G
            + "0010011"  // 2 L
            + "0111101"  // 3 L
            + "0011101"  // 4 G
            + "01010"
            + "1100110"  // 1 R
            + "1101100"  // 2 R
            + "1000010"  // 3 R
            + "1011100"  // 4 R
            + "1001110"  // 5 R
            + "1000100"  // 7 R
            + "101";

        bool[] modules = Ean13Encoder.Encode("5901234123457");
        Assert.Equal(95, modules.Length);
        Assert.Equal(expected, ToBits(modules));
    }

    [Fact]
    public void Guard_patterns_sit_at_the_symbol_edges_and_centre()
    {
        string bits = ToBits(Ean13Encoder.Encode("4006381333931"));
        Assert.StartsWith("101", bits);
        Assert.EndsWith("101", bits);
        Assert.Equal("01010", bits.Substring(45, 5));   // centre guard at modules 45..49
    }

    private static string ToBits(bool[] modules)
    {
        char[] chars = new char[modules.Length];
        for (int i = 0; i < modules.Length; i++)
        {
            chars[i] = modules[i] ? '1' : '0';
        }

        return new string(chars);
    }
}
