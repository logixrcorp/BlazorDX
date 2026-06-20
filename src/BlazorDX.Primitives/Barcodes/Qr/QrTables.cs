namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>Error-correction level for a QR code (ascending recovery capacity).</summary>
public enum QrErrorLevel
{
    /// <summary>~7% recovery.</summary>
    Low,

    /// <summary>~15% recovery.</summary>
    Medium,

    /// <summary>~25% recovery.</summary>
    Quartile,

    /// <summary>~30% recovery.</summary>
    High,
}

/// <summary>The data-encoding mode chosen for the payload.</summary>
public enum QrMode
{
    /// <summary>Digits 0–9 only.</summary>
    Numeric,

    /// <summary>0–9, A–Z, and nine symbols (space $ % * + - . / :).</summary>
    Alphanumeric,

    /// <summary>Arbitrary bytes (ISO-8859-1 / UTF-8).</summary>
    Byte,
}

/// <summary>
/// Static QR specification tables for versions 1–4 (the range this encoder
/// supports): error-correction block structure, alignment-pattern centres, mode
/// indicators, and character-count widths. Values are from the QR standard.
/// </summary>
internal static class QrTables
{
    /// <summary>The supported version range (inclusive).</summary>
    public const int MinVersion = 1;
    public const int MaxVersion = 4;

    /// <summary>Module count along one side of a version's symbol.</summary>
    public static int SizeOf(int version) => 17 + (version * 4);

    /// <summary>The 4-bit mode indicator.</summary>
    public static int ModeIndicator(QrMode mode) => mode switch
    {
        QrMode.Numeric => 0b0001,
        QrMode.Alphanumeric => 0b0010,
        QrMode.Byte => 0b0100,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The character-count indicator width (bits) for versions 1–9.</summary>
    public static int CharCountBits(QrMode mode) => mode switch
    {
        QrMode.Numeric => 10,
        QrMode.Alphanumeric => 9,
        QrMode.Byte => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The 2-bit error-correction level field used in the format string.</summary>
    public static int LevelBits(QrErrorLevel level) => level switch
    {
        QrErrorLevel.Low => 0b01,
        QrErrorLevel.Medium => 0b00,
        QrErrorLevel.Quartile => 0b11,
        QrErrorLevel.High => 0b10,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// The error-correction block layout for a (version, level): one entry per block,
    /// each giving (data codewords, EC codewords). Sourced from the QR EC table.
    /// </summary>
    public static IReadOnlyList<(int Data, int Ec)> Blocks(int version, QrErrorLevel level)
    {
        (int data, int ec, int count, int data2, int count2) spec = (version, level) switch
        {
            (1, QrErrorLevel.Low) => (19, 7, 1, 0, 0),
            (1, QrErrorLevel.Medium) => (16, 10, 1, 0, 0),
            (1, QrErrorLevel.Quartile) => (13, 13, 1, 0, 0),
            (1, QrErrorLevel.High) => (9, 17, 1, 0, 0),

            (2, QrErrorLevel.Low) => (34, 10, 1, 0, 0),
            (2, QrErrorLevel.Medium) => (28, 16, 1, 0, 0),
            (2, QrErrorLevel.Quartile) => (22, 22, 1, 0, 0),
            (2, QrErrorLevel.High) => (16, 28, 1, 0, 0),

            (3, QrErrorLevel.Low) => (55, 15, 1, 0, 0),
            (3, QrErrorLevel.Medium) => (44, 26, 1, 0, 0),
            (3, QrErrorLevel.Quartile) => (17, 18, 2, 0, 0),
            (3, QrErrorLevel.High) => (13, 22, 2, 0, 0),

            (4, QrErrorLevel.Low) => (80, 20, 1, 0, 0),
            (4, QrErrorLevel.Medium) => (32, 18, 2, 0, 0),
            (4, QrErrorLevel.Quartile) => (24, 26, 2, 0, 0),
            (4, QrErrorLevel.High) => (9, 16, 4, 0, 0),

            _ => throw new NotSupportedException($"QR version {version} is outside the supported range 1–4."),
        };

        List<(int, int)> blocks = new();
        for (int i = 0; i < spec.count; i++)
        {
            blocks.Add((spec.data, spec.ec));
        }

        for (int i = 0; i < spec.count2; i++)
        {
            blocks.Add((spec.data2, spec.ec));
        }

        return blocks;
    }

    /// <summary>Total data codewords available for a (version, level).</summary>
    public static int DataCodewordCount(int version, QrErrorLevel level)
    {
        int total = 0;
        foreach ((int data, int _) in Blocks(version, level))
        {
            total += data;
        }

        return total;
    }

    // Alignment-pattern centre coordinates per version (versions 2–6 share a single
    // pair; version 1 has none).
    private static readonly Dictionary<int, int[]> AlignmentCenters = new()
    {
        [1] = [],
        [2] = [6, 18],
        [3] = [6, 22],
        [4] = [6, 26],
    };

    /// <summary>
    /// The (row, col) centres of the alignment patterns for a version, excluding the
    /// three that would collide with the finder patterns at the corners.
    /// </summary>
    public static IEnumerable<(int Row, int Col)> AlignmentPositions(int version)
    {
        int[] centers = AlignmentCenters[version];
        int last = centers.Length - 1;
        for (int i = 0; i < centers.Length; i++)
        {
            for (int j = 0; j < centers.Length; j++)
            {
                // Skip the three corners occupied by finder patterns.
                bool topLeft = i == 0 && j == 0;
                bool topRight = i == 0 && j == last;
                bool bottomLeft = i == last && j == 0;
                if (topLeft || topRight || bottomLeft)
                {
                    continue;
                }

                yield return (centers[i], centers[j]);
            }
        }
    }

    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    /// <summary>The alphanumeric value of a character, or −1 if not representable.</summary>
    public static int AlphanumericValue(char c) => AlphanumericChars.IndexOf(c);

    /// <summary>Whether every character in the text is alphanumeric-encodable.</summary>
    public static bool IsAlphanumeric(string text)
    {
        foreach (char c in text)
        {
            if (AlphanumericChars.IndexOf(c) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every character is a digit.</summary>
    public static bool IsNumeric(string text)
    {
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
