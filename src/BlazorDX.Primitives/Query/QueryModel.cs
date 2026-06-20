namespace BlazorDX.Primitives.Query;

/// <summary>How a group combines its children.</summary>
public enum QueryCombinator
{
    And,
    Or,
}

/// <summary>The comparison a condition applies.</summary>
public enum QueryOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    GreaterThan,
    LessThan,
}

/// <summary>A field the query can target.</summary>
/// <param name="Name">Field name (matches the cell resolver key).</param>
/// <param name="IsNumeric">Whether to compare numerically for &gt;/&lt;.</param>
public readonly record struct QueryField(string Name, bool IsNumeric = false);

/// <summary>Base type for a node in a query tree (a condition or a group).</summary>
public abstract class QueryNode
{
}

/// <summary>A leaf comparison: field OP value.</summary>
public sealed class QueryCondition : QueryNode
{
    public string Field { get; set; } = string.Empty;

    public QueryOperator Operator { get; set; } = QueryOperator.Contains;

    public string Value { get; set; } = string.Empty;
}

/// <summary>A combinator over child nodes, enabling nested logic.</summary>
public sealed class QueryGroup : QueryNode
{
    public QueryCombinator Combinator { get; set; } = QueryCombinator.And;

    public List<QueryNode> Children { get; } = new();
}
