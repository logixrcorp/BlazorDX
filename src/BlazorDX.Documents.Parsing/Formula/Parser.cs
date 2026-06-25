namespace BlazorDX.Documents.Formula;

/// <summary>
/// A precedence-climbing (Pratt-style) recursive-descent parser that turns a token
/// stream into an <see cref="ExpressionNode"/> tree.
/// </summary>
/// <remarks>
/// Operator precedence, lowest to highest:
/// comparisons (<c>= &lt;&gt; &lt; &gt; &lt;= &gt;=</c>) &lt; concat (<c>&amp;</c>)
/// &lt; add/subtract (<c>+ -</c>) &lt; multiply/divide (<c>* /</c>) &lt; unary minus
/// &lt; power (<c>^</c>, right-associative). Parentheses override everything.
/// A bare unknown identifier and a colon outside a range both raise
/// <see cref="FormulaSyntaxException"/>, which the engine converts into a
/// <c>#NAME?</c> value.
/// </remarks>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    private Parser(IReadOnlyList<Token> tokens) => _tokens = tokens;

    /// <summary>Parses a full formula string into an expression tree.</summary>
    public static ExpressionNode Parse(string formula)
    {
        IReadOnlyList<Token> tokens = Tokenizer.Tokenize(formula);
        var parser = new Parser(tokens);
        ExpressionNode node = parser.ParseExpression(0);
        if (parser.Current.Kind != TokenKind.End)
        {
            throw new FormulaSyntaxException($"Unexpected token '{parser.Current.Text}'.");
        }

        return node;
    }

    private Token Current => _tokens[_pos];

    private Token Advance() => _tokens[_pos++];

    // Binding powers for binary operators. Higher binds tighter. Power is handled
    // separately in the unary/primary path so it can be right-associative and sit
    // above unary minus.
    private static int BinaryPrecedence(in Token token)
    {
        if (token.Kind != TokenKind.Operator)
        {
            return -1;
        }

        return token.Text switch
        {
            "=" or "<>" or "<" or ">" or "<=" or ">=" => 1,
            "&" => 2,
            "+" or "-" => 3,
            "*" or "/" => 4,
            _ => -1,
        };
    }

    private static BinaryOperator MapBinary(string op) => op switch
    {
        "+" => BinaryOperator.Add,
        "-" => BinaryOperator.Subtract,
        "*" => BinaryOperator.Multiply,
        "/" => BinaryOperator.Divide,
        "&" => BinaryOperator.Concat,
        "=" => BinaryOperator.Equal,
        "<>" => BinaryOperator.NotEqual,
        "<" => BinaryOperator.LessThan,
        ">" => BinaryOperator.GreaterThan,
        "<=" => BinaryOperator.LessThanOrEqual,
        ">=" => BinaryOperator.GreaterThanOrEqual,
        _ => throw new FormulaSyntaxException($"Unknown operator '{op}'."),
    };

    // Precedence-climbing for the left-associative binary tier.
    private ExpressionNode ParseExpression(int minPrecedence)
    {
        ExpressionNode left = ParseUnary();

        while (true)
        {
            Token op = Current;
            int prec = BinaryPrecedence(op);
            if (prec < 0 || prec < minPrecedence)
            {
                break;
            }

            Advance();
            // All these operators are left-associative: parse the right side at a
            // higher minimum so equal-precedence operators bind leftward.
            ExpressionNode right = ParseExpression(prec + 1);
            left = new BinaryNode(MapBinary(op.Text), left, right);
        }

        return left;
    }

    // Unary minus binds tighter than the binary tier but looser than power, so
    // -2^2 parses as -(2^2). A unary plus is accepted and discarded.
    private ExpressionNode ParseUnary()
    {
        if (Current.Kind == TokenKind.Operator && Current.Text == "-")
        {
            Advance();
            return new UnaryMinusNode(ParseUnary());
        }

        if (Current.Kind == TokenKind.Operator && Current.Text == "+")
        {
            Advance();
            return ParseUnary();
        }

        return ParsePower();
    }

    // Power is right-associative and binds tighter than unary minus on its right
    // operand: 2^3^2 == 2^(3^2). The right operand is parsed via ParseUnary so that
    // 2^-3 is accepted.
    private ExpressionNode ParsePower()
    {
        ExpressionNode baseExpr = ParsePrimary();
        if (Current.Kind == TokenKind.Operator && Current.Text == "^")
        {
            Advance();
            ExpressionNode exponent = ParseUnary();
            return new BinaryNode(BinaryOperator.Power, baseExpr, exponent);
        }

        return baseExpr;
    }

    private ExpressionNode ParsePrimary()
    {
        Token token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Advance();
                return new NumberNode(token.Number);

            case TokenKind.Text:
                Advance();
                return new TextNode(token.Text);

            case TokenKind.True:
                Advance();
                return new BooleanNode(true);

            case TokenKind.False:
                Advance();
                return new BooleanNode(false);

            case TokenKind.LeftParen:
                Advance();
                ExpressionNode inner = ParseExpression(0);
                Expect(TokenKind.RightParen, ")");
                return inner;

            case TokenKind.CellRef:
                return ParseReference();

            case TokenKind.Function:
                return ParseFunctionCall();

            case TokenKind.Operator:
                // A bare identifier the tokenizer could not classify (an unknown
                // name) lands here. Surface it as a name error.
                throw new FormulaSyntaxException($"Unknown name '{token.Text}'.");

            default:
                throw new FormulaSyntaxException($"Unexpected token '{token.Text}'.");
        }
    }

    private ExpressionNode ParseReference()
    {
        Token first = Advance();
        if (!CellAddress.TryParse(first.Text, out CellAddress start))
        {
            throw new FormulaSyntaxException($"Invalid cell reference '{first.Text}'.");
        }

        if (Current.Kind == TokenKind.Colon)
        {
            Advance();
            if (Current.Kind != TokenKind.CellRef)
            {
                throw new FormulaSyntaxException("Expected a cell reference after ':'.");
            }

            Token second = Advance();
            if (!CellAddress.TryParse(second.Text, out CellAddress end))
            {
                throw new FormulaSyntaxException($"Invalid cell reference '{second.Text}'.");
            }

            return new RangeNode(new CellRange(start, end));
        }

        return new CellReferenceNode(start);
    }

    private ExpressionNode ParseFunctionCall()
    {
        Token nameToken = Advance();
        Expect(TokenKind.LeftParen, "(");

        var args = new List<ExpressionNode>();
        if (Current.Kind != TokenKind.RightParen)
        {
            args.Add(ParseExpression(0));
            while (Current.Kind == TokenKind.Comma)
            {
                Advance();
                args.Add(ParseExpression(0));
            }
        }

        Expect(TokenKind.RightParen, ")");
        return new FunctionCallNode(nameToken.Text, args);
    }

    private void Expect(TokenKind kind, string display)
    {
        if (Current.Kind != kind)
        {
            throw new FormulaSyntaxException($"Expected '{display}' but found '{Current.Text}'.");
        }

        Advance();
    }
}
