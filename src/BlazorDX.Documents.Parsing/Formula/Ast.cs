namespace BlazorDX.Documents.Formula;

/// <summary>
/// Binary operators in precedence-bearing form. The parser maps tokens to these;
/// the evaluator switches on them. Precedence and associativity are decided by the
/// parser, so this enum carries no precedence of its own.
/// </summary>
public enum BinaryOperator
{
    /// <summary>Addition (<c>+</c>).</summary>
    Add,

    /// <summary>Subtraction (<c>-</c>).</summary>
    Subtract,

    /// <summary>Multiplication (<c>*</c>).</summary>
    Multiply,

    /// <summary>Division (<c>/</c>).</summary>
    Divide,

    /// <summary>Exponentiation (<c>^</c>), right-associative.</summary>
    Power,

    /// <summary>Text concatenation (<c>&amp;</c>).</summary>
    Concat,

    /// <summary>Equality (<c>=</c>).</summary>
    Equal,

    /// <summary>Inequality (<c>&lt;&gt;</c>).</summary>
    NotEqual,

    /// <summary>Less-than (<c>&lt;</c>).</summary>
    LessThan,

    /// <summary>Greater-than (<c>&gt;</c>).</summary>
    GreaterThan,

    /// <summary>Less-than-or-equal (<c>&lt;=</c>).</summary>
    LessThanOrEqual,

    /// <summary>Greater-than-or-equal (<c>&gt;=</c>).</summary>
    GreaterThanOrEqual,
}

/// <summary>Base type for every node in a parsed formula expression tree.</summary>
public abstract class ExpressionNode
{
    private protected ExpressionNode()
    {
    }
}

/// <summary>A literal number, e.g. <c>3.14</c>.</summary>
public sealed class NumberNode : ExpressionNode
{
    /// <summary>Creates a number literal node.</summary>
    public NumberNode(double value) => Value = value;

    /// <summary>The literal value.</summary>
    public double Value { get; }
}

/// <summary>A literal double-quoted string, e.g. <c>"hello"</c>.</summary>
public sealed class TextNode : ExpressionNode
{
    /// <summary>Creates a text literal node.</summary>
    public TextNode(string value) => Value = value;

    /// <summary>The literal text (quotes removed, doubled quotes unescaped).</summary>
    public string Value { get; }
}

/// <summary>A literal boolean keyword, <c>TRUE</c> or <c>FALSE</c>.</summary>
public sealed class BooleanNode : ExpressionNode
{
    /// <summary>Creates a boolean literal node.</summary>
    public BooleanNode(bool value) => Value = value;

    /// <summary>The literal value.</summary>
    public bool Value { get; }
}

/// <summary>A single cell reference, e.g. <c>A1</c> or <c>$B$2</c>.</summary>
public sealed class CellReferenceNode : ExpressionNode
{
    /// <summary>Creates a cell-reference node.</summary>
    public CellReferenceNode(CellAddress address) => Address = address;

    /// <summary>The referenced address.</summary>
    public CellAddress Address { get; }
}

/// <summary>A rectangular range reference, e.g. <c>A1:B3</c>.</summary>
public sealed class RangeNode : ExpressionNode
{
    /// <summary>Creates a range node.</summary>
    public RangeNode(CellRange range) => Range = range;

    /// <summary>The referenced range.</summary>
    public CellRange Range { get; }
}

/// <summary>Unary negation, <c>-x</c>.</summary>
public sealed class UnaryMinusNode : ExpressionNode
{
    /// <summary>Creates a unary-minus node.</summary>
    public UnaryMinusNode(ExpressionNode operand) => Operand = operand;

    /// <summary>The negated operand.</summary>
    public ExpressionNode Operand { get; }
}

/// <summary>A binary operation, e.g. <c>a + b</c>.</summary>
public sealed class BinaryNode : ExpressionNode
{
    /// <summary>Creates a binary-operation node.</summary>
    public BinaryNode(BinaryOperator op, ExpressionNode left, ExpressionNode right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }

    /// <summary>The operator.</summary>
    public BinaryOperator Operator { get; }

    /// <summary>The left operand.</summary>
    public ExpressionNode Left { get; }

    /// <summary>The right operand.</summary>
    public ExpressionNode Right { get; }
}

/// <summary>A function call, e.g. <c>SUM(A1:A3, 5)</c>.</summary>
public sealed class FunctionCallNode : ExpressionNode
{
    /// <summary>Creates a function-call node.</summary>
    public FunctionCallNode(string name, IReadOnlyList<ExpressionNode> arguments)
    {
        Name = name;
        Arguments = arguments;
    }

    /// <summary>The upper-cased function name.</summary>
    public string Name { get; }

    /// <summary>The argument expressions, in source order.</summary>
    public IReadOnlyList<ExpressionNode> Arguments { get; }
}
