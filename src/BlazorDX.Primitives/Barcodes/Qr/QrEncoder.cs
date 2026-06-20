using System.Text;

namespace BlazorDX.Primitives.Barcodes.Qr;

/// <summary>
/// Builds the QR data codeword stream: picks a mode and version, encodes the
/// payload bitstream with terminator and padding, splits it into Reed-Solomon
/// blocks, and interleaves data and EC codewords per the standard. Pure and
/// reflection-free. Supports versions 1–4.
/// </summary>
internal static class QrEncoder
{
    /// <summary>Picks the most compact mode that can represent the text.</summary>
    public static QrMode SelectMode(string text)
    {
        if (QrTables.IsNumeric(text))
        {
            return QrMode.Numeric;
        }

        return QrTables.IsAlphanumeric(text) ? QrMode.Alphanumeric : QrMode.Byte;
    }

    /// <summary>The number of payload data bits the text needs in the given mode.</summary>
    private static int DataBitLength(string text, QrMode mode) => mode switch
    {
        QrMode.Numeric => (text.Length / 3 * 10) + (text.Length % 3) switch
        {
            2 => 7,
            1 => 4,
            _ => 0,
        },
        QrMode.Alphanumeric => (text.Length / 2 * 11) + (text.Length % 2 * 6),
        QrMode.Byte => Encoding.UTF8.GetByteCount(text) * 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The smallest supported version that fits the payload at the given level.</summary>
    public static int SelectVersion(string text, QrMode mode, QrErrorLevel level)
    {
        int needed = 4 + QrTables.CharCountBits(mode) + DataBitLength(text, mode);
        for (int version = QrTables.MinVersion; version <= QrTables.MaxVersion; version++)
        {
            if (QrTables.DataCodewordCount(version, level) * 8 >= needed)
            {
                return version;
            }
        }

        throw new ArgumentException(
            "Payload too large for the supported QR versions (1–4). Shorten the text or lower the EC level.", nameof(text));
    }

    /// <summary>
    /// Encodes the text into the full set of data codewords (concatenated across all
    /// blocks), including mode, character count, terminator, and pad codewords.
    /// </summary>
    public static int[] BuildDataCodewords(string text, QrMode mode, int version, QrErrorLevel level)
    {
        int capacityBytes = QrTables.DataCodewordCount(version, level);
        BitWriter bits = new();

        bits.Write(QrTables.ModeIndicator(mode), 4);
        bits.Write(CharCount(text, mode), QrTables.CharCountBits(mode));
        WriteData(bits, text, mode);

        // Terminator: up to four 0 bits, but never past capacity.
        int capacityBits = capacityBytes * 8;
        int terminator = Math.Min(4, capacityBits - bits.Length);
        bits.Write(0, terminator);

        // Pad to a byte boundary, then alternate the two standard pad codewords.
        bits.PadToByte();
        byte[] padBytes = [0xEC, 0x11];
        int pad = 0;
        while (bits.Length / 8 < capacityBytes)
        {
            bits.Write(padBytes[pad % 2], 8);
            pad++;
        }

        return bits.ToCodewords();
    }

    private static int CharCount(string text, QrMode mode) =>
        mode == QrMode.Byte ? Encoding.UTF8.GetByteCount(text) : text.Length;

    private static void WriteData(BitWriter bits, string text, QrMode mode)
    {
        switch (mode)
        {
            case QrMode.Numeric:
                WriteNumeric(bits, text);
                break;
            case QrMode.Alphanumeric:
                WriteAlphanumeric(bits, text);
                break;
            case QrMode.Byte:
                foreach (byte b in Encoding.UTF8.GetBytes(text))
                {
                    bits.Write(b, 8);
                }

                break;
        }
    }

    private static void WriteNumeric(BitWriter bits, string text)
    {
        int i = 0;
        for (; i + 3 <= text.Length; i += 3)
        {
            bits.Write(int.Parse(text.Substring(i, 3)), 10);
        }

        int remaining = text.Length - i;
        if (remaining == 2)
        {
            bits.Write(int.Parse(text.Substring(i, 2)), 7);
        }
        else if (remaining == 1)
        {
            bits.Write(text[i] - '0', 4);
        }
    }

    private static void WriteAlphanumeric(BitWriter bits, string text)
    {
        int i = 0;
        for (; i + 2 <= text.Length; i += 2)
        {
            int pair = (QrTables.AlphanumericValue(text[i]) * 45) + QrTables.AlphanumericValue(text[i + 1]);
            bits.Write(pair, 11);
        }

        if (i < text.Length)
        {
            bits.Write(QrTables.AlphanumericValue(text[i]), 6);
        }
    }

    /// <summary>
    /// Splits data codewords into blocks, computes EC codewords for each, then
    /// interleaves data then EC column-by-column per the standard, producing the
    /// final codeword stream laid into the matrix.
    /// </summary>
    public static int[] InterleaveWithEc(int[] dataCodewords, int version, QrErrorLevel level)
    {
        IReadOnlyList<(int Data, int Ec)> layout = QrTables.Blocks(version, level);

        List<int[]> dataBlocks = new();
        List<int[]> ecBlocks = new();
        int offset = 0;
        foreach ((int dataCount, int ecCount) in layout)
        {
            int[] block = new int[dataCount];
            Array.Copy(dataCodewords, offset, block, 0, dataCount);
            offset += dataCount;

            dataBlocks.Add(block);
            ecBlocks.Add(ReedSolomon.Encode(block, ecCount));
        }

        List<int> result = new();

        int maxData = 0;
        foreach (int[] block in dataBlocks)
        {
            maxData = Math.Max(maxData, block.Length);
        }

        for (int col = 0; col < maxData; col++)
        {
            foreach (int[] block in dataBlocks)
            {
                if (col < block.Length)
                {
                    result.Add(block[col]);
                }
            }
        }

        int ecLen = ecBlocks[0].Length;   // constant per version/level in this range
        for (int col = 0; col < ecLen; col++)
        {
            foreach (int[] block in ecBlocks)
            {
                result.Add(block[col]);
            }
        }

        return result.ToArray();
    }

    // Computes the EC codewords for a single block (exposed for verification).
    public static int[] BlockEc(int[] dataBlock, int ecCount) => ReedSolomon.Encode(dataBlock, ecCount);

    /// <summary>A minimal MSB-first bit accumulator used to build the codeword stream.</summary>
    private sealed class BitWriter
    {
        private readonly List<bool> bits = new();

        public int Length => bits.Count;

        public void Write(int value, int bitCount)
        {
            for (int i = bitCount - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) == 1);
            }
        }

        public void PadToByte()
        {
            while (bits.Count % 8 != 0)
            {
                bits.Add(false);
            }
        }

        public int[] ToCodewords()
        {
            int[] codewords = new int[bits.Count / 8];
            for (int i = 0; i < codewords.Length; i++)
            {
                int value = 0;
                for (int b = 0; b < 8; b++)
                {
                    value = (value << 1) | (bits[(i * 8) + b] ? 1 : 0);
                }

                codewords[i] = value;
            }

            return codewords;
        }
    }
}
