using BlazorDX.Primitives.Barcodes;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Verifies the Code 128 encoder. The checksum is anchored to the published worked
/// example (PJJ123C with Start Code A → check symbol 54); correctness of the width
/// table is then confirmed by an independent decode round-trip and the known Start B
/// and Stop module patterns.
/// </summary>
public sealed class Code128EncoderTests
{
    [Fact]
    public void Checksum_matches_the_published_worked_example()
    {
        // PJJ123C in Code A: Start A(103), P(48), J(42), J(42), 1(17), 2(18), 3(19), C(35).
        // Weighted sum 878, mod 103 = 54.
        int[] symbols = [103, 48, 42, 42, 17, 18, 19, 35];
        Assert.Equal(54, Code128Encoder.Checksum(symbols));
    }

    [Theory]
    [InlineData("PJJ123C")]
    [InlineData("CODE128")]
    [InlineData("Hello, BlazorDX! 0123456789")]
    [InlineData("~ {|}")]   // edge of the printable ASCII range
    public void Encode_then_decode_round_trips(string text)
    {
        bool[] modules = Code128Encoder.Encode(text);
        Assert.Equal(text, Code128Encoder.Decode(modules));
    }

    [Fact]
    public void Symbol_starts_with_start_b_and_ends_with_the_stop_pattern()
    {
        bool[] modules = Code128Encoder.Encode("A");
        string bits = ToBits(modules);

        // Start Code B (value 104) module pattern and the 13-module Stop are fixed.
        Assert.StartsWith("11010010000", bits);          // Start B
        Assert.EndsWith("1100011101011", bits);          // Stop (2331112)
    }

    [Fact]
    public void Module_count_is_eleven_per_symbol_plus_the_stop()
    {
        // "PJJ123C": Start + 7 data + 1 check = 9 symbols × 11 + 13 (Stop) = 112.
        Assert.Equal((9 * 11) + 13, Code128Encoder.Encode("PJJ123C").Length);
    }

    [Fact]
    public void Rejects_characters_outside_set_b()
    {
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("tab\there"));
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
