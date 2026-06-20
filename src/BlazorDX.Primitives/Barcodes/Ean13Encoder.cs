namespace BlazorDX.Primitives.Barcodes;

/// <summary>
/// Pure, reflection-free EAN-13 encoder. Turns a 12- or 13-digit string into the
/// 95-module bar pattern of the symbol (true = dark bar). The tables and structure
/// follow the EAN-13 / UPC standard exactly, so the output can be checked against
/// published reference symbols.
///
/// Structure: start guard <c>101</c> · six left digits (7 modules each, encoded
/// with L/G parity chosen by the first digit) · centre guard <c>01010</c> · six
/// right digits (7 modules each, R parity) · end guard <c>101</c> — 95 modules.
/// </summary>
public static class Ean13Encoder
{
    // L-codes (odd parity). Each is a 7-module pattern, MSB first, 1 = bar.
    private static readonly string[] L =
    [
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011",
    ];

    // G-codes (even parity) = the R-code read backwards.
    private static readonly string[] G =
    [
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111",
    ];

    // R-codes (right half) = the bitwise complement of the L-code.
    private static readonly string[] R =
    [
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100",
    ];

    // For each possible first digit, the L/G parity pattern of the six left digits.
    private static readonly string[] Parity =
    [
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL",
    ];

    /// <summary>
    /// Computes the EAN-13 check digit for the first 12 digits (positions weighted
    /// 1, 3, 1, 3, …). Returns a value 0–9.
    /// </summary>
    public static int CheckDigit(ReadOnlySpan<char> twelveDigits)
    {
        if (twelveDigits.Length != 12)
        {
            throw new ArgumentException("EAN-13 check digit needs the first 12 digits.", nameof(twelveDigits));
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = twelveDigits[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;   // positions 1,3,… weight 1; 2,4,… weight 3
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Normalises input to a full 13-digit code: validates digits, appends the
    /// check digit when given 12, and verifies it when given 13.
    /// </summary>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        foreach (char c in code)
        {
            if (c is < '0' or > '9')
            {
                throw new ArgumentException("EAN-13 accepts digits only.", nameof(code));
            }
        }

        return code.Length switch
        {
            12 => code + (char)('0' + CheckDigit(code)),
            13 => VerifyThirteen(code),
            _ => throw new ArgumentException("EAN-13 needs 12 or 13 digits.", nameof(code)),
        };
    }

    private static string VerifyThirteen(string code)
    {
        int expected = CheckDigit(code.AsSpan(0, 12));
        if (code[12] - '0' != expected)
        {
            throw new ArgumentException(
                $"EAN-13 check digit mismatch: expected {expected}, got {code[12]}.", nameof(code));
        }

        return code;
    }

    /// <summary>
    /// Encodes a 12- or 13-digit code into its 95-module bar pattern (true = bar).
    /// </summary>
    public static bool[] Encode(string code)
    {
        string digits = Normalize(code);
        bool[] modules = new bool[95];
        int index = 0;

        index = Append(modules, index, "101");   // start guard

        string parity = Parity[digits[0] - '0'];
        for (int i = 0; i < 6; i++)               // left half: digits 2..7
        {
            int digit = digits[i + 1] - '0';
            index = Append(modules, index, parity[i] == 'L' ? L[digit] : G[digit]);
        }

        index = Append(modules, index, "01010");  // centre guard

        for (int i = 0; i < 6; i++)               // right half: digits 8..13
        {
            int digit = digits[i + 7] - '0';
            index = Append(modules, index, R[digit]);
        }

        Append(modules, index, "101");            // end guard
        return modules;
    }

    private static int Append(bool[] modules, int index, string pattern)
    {
        foreach (char c in pattern)
        {
            modules[index++] = c == '1';
        }

        return index;
    }
}
