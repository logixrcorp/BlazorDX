using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>The lexical category of a <see cref="Token"/>.</summary>
public enum TokenKind
{
    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A double-quoted string literal (value already unescaped).</summary>
    Text,

    /// <summary>The keyword <c>TRUE</c>.</summary>
    True,

    /// <summary>The keyword <c>FALSE</c>.</summary>
    False,

    /// <summary>An A1 cell reference (e.g. <c>A1</c>, <c>$B$2</c>).</summary>
    CellRef,

    /// <summary>An identifier immediately followed by <c>(</c> — a function name.</summary>
    Function,

    /// <summary>An operator such as <c>+</c>, <c>&lt;=</c>, <c>&amp;</c>.</summary>
    Operator,

    /// <summary>A left parenthesis.</summary>
    LeftParen,

    /// <summary>A right parenthesis.</summary>
    RightParen,

    /// <summary>A range colon <c>:</c>.</summary>
    Colon,

    /// <summary>An argument-separator comma.</summary>
    Comma,

    /// <summary>End of input.</summary>
    End,
}

/// <summary>A single lexical token with its source text and category.</summary>
public readonly struct Token
{
    /// <summary>Creates a token.</summary>
    public Token(TokenKind kind, string text, double number = 0d)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    /// <summary>The token category.</summary>
    public TokenKind Kind { get; }

    /// <summary>The raw or decoded text (decoded string for <see cref="TokenKind.Text"/>).</summary>
    public string Text { get; }

    /// <summary>The parsed value for <see cref="TokenKind.Number"/> tokens.</summary>
    public double Number { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{Text}";
}

/// <summary>
/// Raised when the tokenizer hits a character it cannot classify (e.g. an
/// unterminated string). The parser turns this into a <c>#NAME?</c>/<c>#VALUE!</c>
/// value so a bad formula never escapes the engine as an exception.
/// </summary>
public sealed class FormulaSyntaxException : Exception
{
    /// <summary>Creates the exception with a human-readable message.</summary>
    public FormulaSyntaxException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A hand-rolled lexer that turns a formula string into a flat token list. It is
/// allocation-light and culture-invariant: numbers parse with <c>.</c> as the
/// decimal point regardless of the host locale. A leading <c>=</c> is tolerated and
/// skipped so both <c>=A1+1</c> and <c>A1+1</c> tokenize identically.
/// </summary>
public static class Tokenizer
{
    /// <summary>Tokenizes <paramref name="formula"/> into a list ending with an End token.</summary>
    public static IReadOnlyList<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        if (formula is null)
        {
            tokens.Add(new Token(TokenKind.End, string.Empty));
            return tokens;
        }

        int i = 0;
        int n = formula.Length;

        // A leading '=' marks a formula; skip it.
        while (i < n && char.IsWhiteSpace(formula[i]))
        {
            i++;
        }

        if (i < n && formula[i] == '=')
        {
            i++;
        }

        while (i < n)
        {
            char c = formula[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '"')
            {
                tokens.Add(ReadString(formula, ref i));
                continue;
            }

            if (IsDigit(c) || (c == '.' && i + 1 < n && IsDigit(formula[i + 1])))
            {
                tokens.Add(ReadNumber(formula, ref i));
                continue;
            }

            if (IsIdentStart(c) || c == '$')
            {
                tokens.Add(ReadIdentifierOrRef(formula, ref i));
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LeftParen, "("));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RightParen, ")"));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ","));
                    i++;
                    continue;
                case ':':
                    tokens.Add(new Token(TokenKind.Colon, ":"));
                    i++;
                    continue;
            }

            tokens.Add(ReadOperator(formula, ref i));
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private static Token ReadString(string s, ref int i)
    {
        // Opening quote already at s[i].
        int n = s.Length;
        i++; // consume opening quote
        var sb = new System.Text.StringBuilder();
        while (i < n)
        {
            char c = s[i];
            if (c == '"')
            {
                if (i + 1 < n && s[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                i++; // consume closing quote
                return new Token(TokenKind.Text, sb.ToString());
            }

            sb.Append(c);
            i++;
        }

        throw new FormulaSyntaxException("Unterminated string literal.");
    }

    private static Token ReadNumber(string s, ref int i)
    {
        int n = s.Length;
        int start = i;
        bool seenDot = false;
        bool seenExp = false;

        while (i < n)
        {
            char c = s[i];
            if (IsDigit(c))
            {
                i++;
            }
            else if (c == '.' && !seenDot && !seenExp)
            {
                seenDot = true;
                i++;
            }
            else if ((c == 'e' || c == 'E') && !seenExp && i > start)
            {
                seenExp = true;
                i++;
                if (i < n && (s[i] == '+' || s[i] == '-'))
                {
                    i++;
                }
            }
            else
            {
                break;
            }
        }

        string text = s[start..i];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormulaSyntaxException($"Invalid number literal '{text}'.");
        }

        return new Token(TokenKind.Number, text, value);
    }

    private static Token ReadIdentifierOrRef(string s, ref int i)
    {
        int n = s.Length;
        int start = i;

        // Consume a run that may contain $, letters and digits — enough to cover
        // both cell references ($A$1) and function/keyword identifiers (SUM, TRUE).
        while (i < n && (IsIdentPart(s[i]) || s[i] == '$'))
        {
            i++;
        }

        string raw = s[start..i];

        // A '(' immediately after (whitespace allowed) means this is a function name.
        int peek = i;
        while (peek < n && char.IsWhiteSpace(s[peek]))
        {
            peek++;
        }

        bool followedByParen = peek < n && s[peek] == '(';

        if (followedByParen)
        {
            return new Token(TokenKind.Function, raw.ToUpperInvariant());
        }

        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new Token(TokenKind.True, "TRUE");
        }

        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new Token(TokenKind.False, "FALSE");
        }

        if (CellAddress.TryParse(raw, out _))
        {
            return new Token(TokenKind.CellRef, raw);
        }

        // An unknown bare identifier (a name): surface it so the parser can emit #NAME?.
        return new Token(TokenKind.Operator, raw);
    }

    private static Token ReadOperator(string s, ref int i)
    {
        int n = s.Length;
        char c = s[i];

        // Two-character comparison operators first.
        if (c == '<' && i + 1 < n && s[i + 1] == '>')
        {
            i += 2;
            return new Token(TokenKind.Operator, "<>");
        }

        if (c == '<' && i + 1 < n && s[i + 1] == '=')
        {
            i += 2;
            return new Token(TokenKind.Operator, "<=");
        }

        if (c == '>' && i + 1 < n && s[i + 1] == '=')
        {
            i += 2;
            return new Token(TokenKind.Operator, ">=");
        }

        switch (c)
        {
            case '+':
            case '-':
            case '*':
            case '/':
            case '^':
            case '&':
            case '=':
            case '<':
            case '>':
                i++;
                return new Token(TokenKind.Operator, c.ToString());
            default:
                throw new FormulaSyntaxException($"Unexpected character '{c}'.");
        }
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsIdentStart(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || IsDigit(c) || c == '.';
}
