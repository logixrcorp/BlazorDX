using System.Text;

namespace BlazorDX.Primitives.Barcodes;

/// <summary>
/// Pure, reflection-free Code 128 encoder (Code Set B — all printable ASCII 32–126).
/// Produces the module pattern (true = bar) of the full symbol: Start B · data ·
/// weighted modulo-103 check symbol · Stop. The width table is the published Code 128
/// table, so output can be checked against reference symbols; an independent
/// <see cref="Decode"/> round-trips the pattern back to text.
/// </summary>
public static class Code128Encoder
{
    // Module widths for symbol values 0..105 (6 elements: bar,space,bar,space,bar,space,
    // each summing to 11 modules). Value 106 (Stop) is handled separately.
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232",
    ];

    private const string StopWidths = "2331112";   // 7 elements / 13 modules
    private const int StartB = 104;
    private const int Stop = 106;

    /// <summary>The lowest / highest ASCII codes Code Set B represents.</summary>
    private const int MinChar = 32;
    private const int MaxChar = 126;

    /// <summary>
    /// The symbol values for the text, including the leading Start B but excluding
    /// the check symbol and Stop. Each printable ASCII char maps to value (char − 32).
    /// </summary>
    public static int[] SymbolValues(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int[] values = new int[text.Length + 1];
        values[0] = StartB;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < MinChar || c > MaxChar)
            {
                throw new ArgumentException(
                    $"Code 128 Set B supports ASCII 32–126; '{c}' (U+{(int)c:X4}) is out of range.", nameof(text));
            }

            values[i + 1] = c - MinChar;
        }

        return values;
    }

    /// <summary>
    /// The weighted modulo-103 check symbol: the start value (weight 1) plus each data
    /// value times its 1-based position, mod 103. Matches the published worked example
    /// (PJJ123C with Start A → 54).
    /// </summary>
    public static int Checksum(IReadOnlyList<int> symbolsIncludingStart)
    {
        ArgumentNullException.ThrowIfNull(symbolsIncludingStart);
        long sum = symbolsIncludingStart[0];   // start code, weight 1
        for (int i = 1; i < symbolsIncludingStart.Count; i++)
        {
            sum += (long)symbolsIncludingStart[i] * i;
        }

        return (int)(sum % 103);
    }

    /// <summary>Encodes text into the full Code 128B module pattern (true = bar).</summary>
    public static bool[] Encode(string text)
    {
        int[] values = SymbolValues(text);
        int check = Checksum(values);

        List<bool> modules = new();
        foreach (int value in values)
        {
            AppendWidths(modules, Patterns[value]);
        }

        AppendWidths(modules, Patterns[check]);
        AppendWidths(modules, StopWidths);   // value 106 (Stop), 13 modules
        return modules.ToArray();
    }

    // Expands a width string ("212222") into modules, starting with a bar and
    // alternating bar/space; widths are counts of consecutive same-colour modules.
    private static void AppendWidths(List<bool> modules, string widths)
    {
        bool bar = true;
        foreach (char w in widths)
        {
            int count = w - '0';
            for (int i = 0; i < count; i++)
            {
                modules.Add(bar);
            }

            bar = !bar;
        }
    }

    /// <summary>
    /// Independent decoder: reads a Code 128B module pattern back to its text,
    /// verifying the check symbol. Used to round-trip-verify <see cref="Encode"/>.
    /// </summary>
    public static string Decode(bool[] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        // Reverse lookup: width string → value. Built once per call (small table).
        Dictionary<string, int> byPattern = new(Patterns.Length);
        for (int value = 0; value < Patterns.Length; value++)
        {
            byPattern[Patterns[value]] = value;
        }

        // The symbol is N × 11-module data symbols followed by the 13-module Stop.
        if (modules.Length < 11 + 13 || (modules.Length - 13) % 11 != 0)
        {
            throw new ArgumentException("Not a well-formed Code 128 module pattern.", nameof(modules));
        }

        int symbolCount = (modules.Length - 13) / 11;
        List<int> values = new(symbolCount);
        for (int s = 0; s < symbolCount; s++)
        {
            string widths = WidthsOf(modules, s * 11, 11);
            if (!byPattern.TryGetValue(widths, out int value))
            {
                throw new ArgumentException($"Unknown Code 128 symbol '{widths}'.", nameof(modules));
            }

            values.Add(value);
        }

        if (values.Count < 2 || values[0] != StartB)
        {
            throw new ArgumentException("Code 128 pattern does not start with Start Code B.", nameof(modules));
        }

        int check = values[^1];
        int expected = Checksum(values.GetRange(0, values.Count - 1));
        if (check != expected)
        {
            throw new ArgumentException($"Code 128 check symbol mismatch: expected {expected}, got {check}.", nameof(modules));
        }

        StringBuilder text = new();
        for (int i = 1; i < values.Count - 1; i++)   // skip start and the trailing check symbol
        {
            text.Append((char)(values[i] + MinChar));
        }

        return text.ToString();
    }

    // Run-length-encodes a window of modules into a width string (counts of
    // consecutive same-colour modules), e.g. 11010010000 → "211214".
    private static string WidthsOf(bool[] modules, int start, int length)
    {
        StringBuilder widths = new();
        int run = 1;
        for (int i = start + 1; i < start + length; i++)
        {
            if (modules[i] == modules[i - 1])
            {
                run++;
            }
            else
            {
                widths.Append((char)('0' + run));
                run = 1;
            }
        }

        widths.Append((char)('0' + run));
        return widths.ToString();
    }
}
