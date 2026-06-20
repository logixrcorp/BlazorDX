using System.Globalization;

namespace BlazorDX.Primitives.Query;

/// <summary>
/// Evaluates a <see cref="QueryNode"/> tree against a row, addressed only by a
/// field-name → cell-text resolver — so it is data-source agnostic and has no
/// reflection. Numeric operators parse both sides with the invariant culture;
/// an empty group matches everything.
/// </summary>
public static class QueryEvaluator
{
    /// <summary>Returns whether a row (via its <paramref name="cell"/> resolver) satisfies the tree.</summary>
    public static bool Evaluate(QueryNode node, Func<string, string> cell) => node switch
    {
        QueryGroup group => EvaluateGroup(group, cell),
        QueryCondition condition => EvaluateCondition(condition, cell),
        _ => true,
    };

    private static bool EvaluateGroup(QueryGroup group, Func<string, string> cell)
    {
        if (group.Children.Count == 0)
        {
            return true;
        }

        if (group.Combinator == QueryCombinator.And)
        {
            foreach (QueryNode child in group.Children)
            {
                if (!Evaluate(child, cell))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (QueryNode child in group.Children)
        {
            if (Evaluate(child, cell))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EvaluateCondition(QueryCondition condition, Func<string, string> cell)
    {
        string actual = cell(condition.Field) ?? string.Empty;
        string expected = condition.Value;

        return condition.Operator switch
        {
            QueryOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            QueryOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            QueryOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            QueryOperator.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            QueryOperator.GreaterThan => Compare(actual, expected) > 0,
            QueryOperator.LessThan => Compare(actual, expected) < 0,
            _ => true,
        };
    }

    // Numeric when both parse; otherwise an ordinal string comparison.
    private static int Compare(string a, string b)
    {
        if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out double da)
            && double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out double db))
        {
            return da.CompareTo(db);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
