namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// Lays the QR module matrix: function patterns (finders, separators, timing,
/// alignment, dark module), the zig-zag data placement, mask selection by penalty,
/// and the BCH-coded format information. Pure and reflection-free. Versions 1–4.
/// </summary>
internal static class QrMatrix
{
    private const int FormatGenerator = 0x537;
    private const int FormatMaskXor = 0x5412;

    /// <summary>
    /// Builds the finished module grid (true = dark) and returns the mask it chose.
    /// </summary>
    public static (bool[,] Modules, int Mask) Build(int version, QrErrorLevel level, int[] codewords, int? forceMask)
    {
        int size = QrTables.SizeOf(version);
        bool[,] modules = new bool[size, size];
        bool[,] function = new bool[size, size];

        DrawFinder(modules, function, size, 3, 3);
        DrawFinder(modules, function, size, 3, size - 4);
        DrawFinder(modules, function, size, size - 4, 3);
        DrawTiming(modules, function, size);
        DrawAlignment(modules, function, version);
        ReserveFormat(function, size);

        PlaceData(modules, function, size, codewords);

        int mask = forceMask ?? SelectMask(modules, function, size, level);
        ApplyMask(modules, function, size, mask);
        DrawFormat(modules, size, level, mask);
        return (modules, mask);
    }

    // A finder pattern plus its light separator ring, centred at (centerRow, centerCol).
    // Modules at Chebyshev distance 0,1,3 are dark; 2 (inner ring) and 4 (separator)
    // are light. Out-of-bounds cells of the separator ring are simply skipped.
    private static void DrawFinder(bool[,] modules, bool[,] function, int size, int centerRow, int centerCol)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int r = centerRow + dy;
                int c = centerCol + dx;
                if (r < 0 || r >= size || c < 0 || c >= size)
                {
                    continue;
                }

                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                modules[r, c] = dist != 2 && dist != 4;
                function[r, c] = true;
            }
        }
    }

    // Timing patterns along row 6 and column 6, alternating dark at even coordinates.
    private static void DrawTiming(bool[,] modules, bool[,] function, int size)
    {
        for (int i = 0; i < size; i++)
        {
            if (!function[6, i])
            {
                modules[6, i] = i % 2 == 0;
                function[6, i] = true;
            }

            if (!function[i, 6])
            {
                modules[i, 6] = i % 2 == 0;
                function[i, 6] = true;
            }
        }
    }

    // Alignment patterns: a 5×5 box with a dark centre and dark border (Chebyshev
    // distance 0 and 2 dark; distance 1 light).
    private static void DrawAlignment(bool[,] modules, bool[,] function, int version)
    {
        foreach ((int row, int col) in QrTables.AlignmentPositions(version))
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    modules[row + dy, col + dx] = dist != 1;
                    function[row + dy, col + dx] = true;
                }
            }
        }
    }

    // Marks the format-information modules (two strips around the finders) and the
    // always-dark module as function so data placement skips them.
    private static void ReserveFormat(bool[,] function, int size)
    {
        for (int i = 0; i <= 8; i++)
        {
            function[8, i] = true;   // row 8, cols 0..8
            function[i, 8] = true;   // col 8, rows 0..8
        }

        for (int i = 0; i < 8; i++)
        {
            function[8, size - 1 - i] = true;   // row 8, right strip
            function[size - 1 - i, 8] = true;   // col 8, bottom strip (includes dark module)
        }
    }

    // Zig-zag placement of the codeword bitstream, two columns at a time from the
    // bottom-right, skipping the vertical timing column. Data modules past the end
    // of the stream stay light (remainder bits).
    private static void PlaceData(bool[,] modules, bool[,] function, int size, int[] codewords)
    {
        int bit = 0;
        int totalBits = codewords.Length * 8;

        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;   // skip the timing column
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

                    bool dark = false;
                    if (bit < totalBits)
                    {
                        dark = Bit(codewords[bit >> 3], 7 - (bit & 7));
                        bit++;
                    }

                    modules[row, col] = dark;
                }
            }
        }
    }

    private static int SelectMask(bool[,] modules, bool[,] function, int size, QrErrorLevel level)
    {
        int best = 0;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, function, size, mask);
            DrawFormat(modules, size, level, mask);
            int penalty = QrMask.Penalty(modules);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = mask;
            }

            ApplyMask(modules, function, size, mask);   // toggle back to unmasked
        }

        return best;
    }

    private static void ApplyMask(bool[,] modules, bool[,] function, int size, int mask)
    {
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!function[r, c] && QrMask.Flip(mask, r, c))
                {
                    modules[r, c] = !modules[r, c];
                }
            }
        }
    }

    private static void DrawFormat(bool[,] modules, int size, QrErrorLevel level, int mask)
    {
        int data = (QrTables.LevelBits(level) << 3) | mask;   // 5 bits
        int rem = data;
        for (int i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ (((rem >> 9) & 1) * FormatGenerator);
        }

        int bits = (((data << 10) | rem) ^ FormatMaskXor) & 0x7FFF;   // 15-bit BCH format string

        // First copy, around the top-left finder.
        for (int i = 0; i <= 5; i++)
        {
            modules[i, 8] = Bit(bits, i);
        }

        modules[7, 8] = Bit(bits, 6);
        modules[8, 8] = Bit(bits, 7);
        modules[8, 7] = Bit(bits, 8);
        for (int i = 9; i < 15; i++)
        {
            modules[8, 14 - i] = Bit(bits, i);
        }

        // Second copy, split across the top-right and bottom-left.
        for (int i = 0; i < 8; i++)
        {
            modules[size - 1 - i, 8] = Bit(bits, i);
        }

        for (int i = 8; i < 15; i++)
        {
            modules[8, size - 15 + i] = Bit(bits, i);
        }

        modules[size - 8, 8] = true;   // always-dark module
    }

    /// <summary>The 15-bit BCH format string for a (level, mask) — exposed for verification.</summary>
    public static int FormatBits(QrErrorLevel level, int mask)
    {
        int data = (QrTables.LevelBits(level) << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ (((rem >> 9) & 1) * FormatGenerator);
        }

        return (((data << 10) | rem) ^ FormatMaskXor) & 0x7FFF;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
}
