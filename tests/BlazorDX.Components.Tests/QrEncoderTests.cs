using BlazorDX.Primitives.Barcodes.Qr;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Anchors the QR data path to the published "HELLO WORLD" version 1, level M
/// worked example: the assembled data codewords must match exactly, which verifies
/// mode selection, the character-count indicator, alphanumeric packing, the
/// terminator, and pad codewords together.
/// </summary>
public sealed class QrEncoderTests
{
    [Fact]
    public void Selects_alphanumeric_mode_and_version_1_for_hello_world()
    {
        QrMode mode = QrEncoder.SelectMode("HELLO WORLD");
        Assert.Equal(QrMode.Alphanumeric, mode);
        Assert.Equal(1, QrEncoder.SelectVersion("HELLO WORLD", mode, QrErrorLevel.Medium));
    }

    [Fact]
    public void Builds_the_published_data_codewords_for_hello_world_1M()
    {
        int[] expected =
        [
            32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17,
        ];

        int[] actual = QrEncoder.BuildDataCodewords("HELLO WORLD", QrMode.Alphanumeric, 1, QrErrorLevel.Medium);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Selects_byte_mode_for_lowercase_and_numeric_for_digits()
    {
        Assert.Equal(QrMode.Byte, QrEncoder.SelectMode("hello"));
        Assert.Equal(QrMode.Numeric, QrEncoder.SelectMode("12345"));
        Assert.Equal(QrMode.Alphanumeric, QrEncoder.SelectMode("ABC-123"));
    }

    [Fact]
    public void Single_block_interleave_is_data_then_ec()
    {
        // Version 1-M is a single block, so the final stream is just data ++ EC.
        int[] data = QrEncoder.BuildDataCodewords("HELLO WORLD", QrMode.Alphanumeric, 1, QrErrorLevel.Medium);
        int[] interleaved = QrEncoder.InterleaveWithEc(data, 1, QrErrorLevel.Medium);

        Assert.Equal(26, interleaved.Length);   // 16 data + 10 EC = v1 total
        Assert.Equal(data, interleaved[..16]);
        Assert.Equal(new[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }, interleaved[16..]);
    }
}
