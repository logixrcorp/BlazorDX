namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// QR data-mask conditions and the four penalty rules used to choose the mask that
/// scans most reliably. Pure and reflection-free.
/// </summary>
internal static class QrMask
{
    /// <summary>Whether mask pattern <paramref name="mask"/> (0–7) flips module (row, col).</summary>
    public static bool Flip(int mask, int row, int col) => mask switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => ((row / 2) + (col / 3)) % 2 == 0,
        5 => ((row * col) % 2) + ((row * col) % 3) == 0,
        6 => (((row * col) % 2) + ((row * col) % 3)) % 2 == 0,
        7 => (((row + col) % 2) + ((row * col) % 3)) % 2 == 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mask)),
    };

    /// <summary>The total penalty score of a finished module grid (lower is better).</summary>
    public static int Penalty(bool[,] m)
    {
        int size = m.GetLength(0);
        return RunsPenalty(m, size)
            + BlocksPenalty(m, size)
            + FinderLikePenalty(m, size)
            + BalancePenalty(m, size);
    }

    // Rule 1: runs of five or more same-colour modules in a row or column.
    private static int RunsPenalty(bool[,] m, int size)
    {
        int penalty = 0;
        for (int a = 0; a < size; a++)
        {
            penalty += LineRuns(m, size, a, horizontal: true);
            penalty += LineRuns(m, size, a, horizontal: false);
        }

        return penalty;
    }

    private static int LineRuns(bool[,] m, int size, int line, bool horizontal)
    {
        int penalty = 0;
        int run = 1;
        bool previous = horizontal ? m[line, 0] : m[0, line];
        for (int i = 1; i < size; i++)
        {
            bool current = horizontal ? m[line, i] : m[i, line];
            if (current == previous)
            {
                run++;
            }
            else
            {
                if (run >= 5)
                {
                    penalty += 3 + (run - 5);
                }

                run = 1;
                previous = current;
            }
        }

        if (run >= 5)
        {
            penalty += 3 + (run - 5);
        }

        return penalty;
    }

    // Rule 2: each 2×2 block of one colour scores 3.
    private static int BlocksPenalty(bool[,] m, int size)
    {
        int penalty = 0;
        for (int r = 0; r < size - 1; r++)
        {
            for (int c = 0; c < size - 1; c++)
            {
                bool v = m[r, c];
                if (v == m[r, c + 1] && v == m[r + 1, c] && v == m[r + 1, c + 1])
                {
                    penalty += 3;
                }
            }
        }

        return penalty;
    }

    // Rule 3: the finder-like 1011101-0000 pattern (and its reverse) in any line.
    private static readonly bool[] FinderPattern =
        [true, false, true, true, true, false, true, false, false, false, false];

    private static int FinderLikePenalty(bool[,] m, int size)
    {
        int penalty = 0;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c <= size - 11; c++)
            {
                if (MatchesFinder(m, r, c, horizontal: true))
                {
                    penalty += 40;
                }
            }
        }

        for (int c = 0; c < size; c++)
        {
            for (int r = 0; r <= size - 11; r++)
            {
                if (MatchesFinder(m, r, c, horizontal: false))
                {
                    penalty += 40;
                }
            }
        }

        return penalty;
    }

    private static bool MatchesFinder(bool[,] m, int row, int col, bool horizontal)
    {
        bool forward = true;
        bool backward = true;
        for (int k = 0; k < 11; k++)
        {
            bool value = horizontal ? m[row, col + k] : m[row + k, col];
            forward &= value == FinderPattern[k];
            backward &= value == FinderPattern[10 - k];
        }

        return forward || backward;
    }

    // Rule 4: deviation of the dark-module proportion from 50%.
    private static int BalancePenalty(bool[,] m, int size)
    {
        int dark = 0;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (m[r, c])
                {
                    dark++;
                }
            }
        }

        int total = size * size;
        int ratio = dark * 100 / total;
        int low = ratio / 5 * 5;
        int high = low + 5;
        int deviation = Math.Min(Math.Abs(low - 50), Math.Abs(high - 50));
        return deviation / 5 * 10;
    }
}
