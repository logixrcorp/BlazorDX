using BlazorDX.Primitives.Barcodes.Qr;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Verifies QR matrix construction. The data and EC codewords are already anchored
/// to a published example (see <see cref="QrReedSolomonTests"/> / <see cref="QrEncoderTests"/>);
/// here we check the structure (size, finder patterns, timing, dark module), the
/// format-information BCH string against the published value, and — most strongly —
/// an independent readback that reverses the zig-zag and data mask to recover the
/// exact codewords that were placed.
/// </summary>
public sealed class QrMatrixTests
{
    [Fact]
    public void Format_bch_matches_the_published_value_for_medium_mask0()
    {
        // The format string for EC level M, mask 0 is 0x5412 (binary 101010000010010).
        Assert.Equal(0x5412, QrMatrix.FormatBits(QrErrorLevel.Medium, 0));
    }

    [Fact]
    public void Hello_world_is_version_1_with_correct_function_patterns()
    {
        QrSymbol qr = QrCode.Encode("HELLO WORLD", QrErrorLevel.Medium);

        Assert.Equal(1, qr.Version);
        Assert.Equal(21, qr.Size);
        Assert.InRange(qr.Mask, 0, 7);

        // Three finder patterns: dark centre, light ring, dark border.
        AssertFinder(qr, 0, 0);
        AssertFinder(qr, 0, qr.Size - 7);
        AssertFinder(qr, qr.Size - 7, 0);

        // Timing patterns alternate, dark at even coordinates.
        for (int i = 8; i <= qr.Size - 9; i++)
        {
            Assert.Equal(i % 2 == 0, qr.IsDark(6, i));
            Assert.Equal(i % 2 == 0, qr.IsDark(i, 6));
        }

        // The always-dark module.
        Assert.True(qr.IsDark(qr.Size - 8, 8));
    }

    [Fact]
    public void Placed_modules_read_back_to_the_exact_codewords()
    {
        QrSymbol qr = QrCode.Encode("HELLO WORLD", QrErrorLevel.Medium);

        int[] data = QrEncoder.BuildDataCodewords("HELLO WORLD", QrMode.Alphanumeric, 1, QrErrorLevel.Medium);
        int[] expected = QrEncoder.InterleaveWithEc(data, 1, QrErrorLevel.Medium);

        // Recover the mask from the format information, then reverse the placement.
        int recoveredMask = ReadMask(qr);
        Assert.Equal(qr.Mask, recoveredMask);

        int[] readBack = ReadCodewords(qr, recoveredMask, expected.Length);
        Assert.Equal(expected, readBack);
    }

    [Theory]
    [InlineData("https://anthropic.com", QrErrorLevel.Medium)]
    [InlineData("ABC-123 / 456", QrErrorLevel.Quartile)]
    [InlineData("0123456789", QrErrorLevel.High)]
    public void Arbitrary_payloads_round_trip_through_the_matrix(string text, QrErrorLevel level)
    {
        QrSymbol qr = QrCode.Encode(text, level);

        QrMode mode = QrEncoder.SelectMode(text);
        int[] data = QrEncoder.BuildDataCodewords(text, mode, qr.Version, level);
        int[] expected = QrEncoder.InterleaveWithEc(data, qr.Version, level);

        int[] readBack = ReadCodewords(qr, ReadMask(qr), expected.Length);
        Assert.Equal(expected, readBack);
    }

    // ---- Independent reader (mirrors the spec placement to invert it) ----

    private static void AssertFinder(QrSymbol qr, int top, int left)
    {
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                bool border = r == 0 || r == 6 || c == 0 || c == 6;
                bool centre = r >= 2 && r <= 4 && c >= 2 && c <= 4;
                Assert.Equal(border || centre, qr.IsDark(top + r, left + c));
            }
        }
    }

    private static int ReadMask(QrSymbol qr)
    {
        // Read the 15 format bits from the first copy around the top-left finder.
        int bits = 0;
        for (int i = 0; i <= 5; i++)
        {
            bits |= (qr.IsDark(i, 8) ? 1 : 0) << i;
        }

        bits |= (qr.IsDark(7, 8) ? 1 : 0) << 6;
        bits |= (qr.IsDark(8, 8) ? 1 : 0) << 7;
        bits |= (qr.IsDark(8, 7) ? 1 : 0) << 8;
        for (int i = 9; i < 15; i++)
        {
            bits |= (qr.IsDark(8, 14 - i) ? 1 : 0) << i;
        }

        for (int mask = 0; mask < 8; mask++)
        {
            if (QrMatrix.FormatBits(qr.Level, mask) == bits)
            {
                return mask;
            }
        }

        throw new Xunit.Sdk.XunitException($"Format bits {bits:X} did not match any mask for level {qr.Level}.");
    }

    private static int[] ReadCodewords(QrSymbol qr, int mask, int count)
    {
        int size = qr.Size;
        bool[,] function = BuildFunctionMap(size, qr.Version);

        List<bool> bits = new();
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int col = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int row = upward ? size - 1 - vert : vert;
                    if (function[row, col])
                    {
                        continue;
                    }

                    bool value = qr.IsDark(row, col);
                    if (QrMask.Flip(mask, row, col))
                    {
                        value = !value;   // undo the data mask
                    }

                    bits.Add(value);
                }
            }
        }

        int[] codewords = new int[count];
        for (int i = 0; i < count; i++)
        {
            int b = 0;
            for (int k = 0; k < 8; k++)
            {
                b = (b << 1) | (bits[(i * 8) + k] ? 1 : 0);
            }

            codewords[i] = b;
        }

        return codewords;
    }

    // Reconstructs which modules are function modules, independent of the encoder.
    private static bool[,] BuildFunctionMap(int size, int version)
    {
        bool[,] fn = new bool[size, size];

        void Finder(int cr, int cc)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int r = cr + dy;
                    int c = cc + dx;
                    if (r >= 0 && r < size && c >= 0 && c < size)
                    {
                        fn[r, c] = true;
                    }
                }
            }
        }

        Finder(3, 3);
        Finder(3, size - 4);
        Finder(size - 4, 3);

        for (int i = 0; i < size; i++)
        {
            fn[6, i] = true;
            fn[i, 6] = true;
        }

        foreach ((int row, int col) in QrTables.AlignmentPositions(version))
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    fn[row + dy, col + dx] = true;
                }
            }
        }

        for (int i = 0; i <= 8; i++)
        {
            fn[8, i] = true;
            fn[i, 8] = true;
        }

        for (int i = 0; i < 8; i++)
        {
            fn[8, size - 1 - i] = true;
            fn[size - 1 - i, 8] = true;
        }

        return fn;
    }
}
