using BlazorDX.Primitives.Query;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The reflection-free query tree evaluator.</summary>
public sealed class QueryEvaluatorTests
{
    private static Func<string, string> Row(string name, int visits) => field => field switch
    {
        "Name" => name,
        "Visits" => visits.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static QueryCondition C(string field, QueryOperator op, string value) =>
        new() { Field = field, Operator = op, Value = value };

    [Fact]
    public void Empty_group_matches_everything()
    {
        Assert.True(QueryEvaluator.Evaluate(new QueryGroup(), Row("Ada", 5)));
    }

    [Theory]
    [InlineData(QueryOperator.Equals, "Ada", true)]
    [InlineData(QueryOperator.Equals, "Bo", false)]
    [InlineData(QueryOperator.NotEquals, "Bo", true)]
    [InlineData(QueryOperator.Contains, "d", true)]
    [InlineData(QueryOperator.StartsWith, "Ad", true)]
    [InlineData(QueryOperator.StartsWith, "da", false)]
    public void Text_operators(QueryOperator op, string value, bool expected)
    {
        QueryGroup q = new();
        q.Children.Add(C("Name", op, value));
        Assert.Equal(expected, QueryEvaluator.Evaluate(q, Row("Ada", 5)));
    }

    [Theory]
    [InlineData(QueryOperator.GreaterThan, "40", true)]
    [InlineData(QueryOperator.GreaterThan, "50", false)]
    [InlineData(QueryOperator.LessThan, "50", true)]
    public void Numeric_operators_parse_both_sides(QueryOperator op, string value, bool expected)
    {
        QueryGroup q = new();
        q.Children.Add(C("Visits", op, value));
        Assert.Equal(expected, QueryEvaluator.Evaluate(q, Row("Ada", 42)));
    }

    [Fact]
    public void And_requires_every_child()
    {
        QueryGroup q = new() { Combinator = QueryCombinator.And };
        q.Children.Add(C("Name", QueryOperator.StartsWith, "A"));
        q.Children.Add(C("Visits", QueryOperator.GreaterThan, "40"));

        Assert.True(QueryEvaluator.Evaluate(q, Row("Ada", 42)));
        Assert.False(QueryEvaluator.Evaluate(q, Row("Ada", 10)));   // fails the visits clause
    }

    [Fact]
    public void Or_requires_any_child()
    {
        QueryGroup q = new() { Combinator = QueryCombinator.Or };
        q.Children.Add(C("Name", QueryOperator.Equals, "Zed"));
        q.Children.Add(C("Visits", QueryOperator.GreaterThan, "40"));

        Assert.True(QueryEvaluator.Evaluate(q, Row("Ada", 42)));    // matches the visits clause
        Assert.False(QueryEvaluator.Evaluate(q, Row("Ada", 10)));
    }

    [Fact]
    public void Nested_groups_mix_logic()
    {
        // Name starts with A  AND  (Visits > 100  OR  Name = "Ada")
        QueryGroup root = new() { Combinator = QueryCombinator.And };
        root.Children.Add(C("Name", QueryOperator.StartsWith, "A"));
        QueryGroup inner = new() { Combinator = QueryCombinator.Or };
        inner.Children.Add(C("Visits", QueryOperator.GreaterThan, "100"));
        inner.Children.Add(C("Name", QueryOperator.Equals, "Ada"));
        root.Children.Add(inner);

        Assert.True(QueryEvaluator.Evaluate(root, Row("Ada", 5)));     // A* and (false or name=Ada)
        Assert.True(QueryEvaluator.Evaluate(root, Row("Abe", 150)));   // A* and (visits>100)
        Assert.False(QueryEvaluator.Evaluate(root, Row("Abe", 5)));    // A* but neither inner clause
        Assert.False(QueryEvaluator.Evaluate(root, Row("Bob", 200)));  // not A*
    }
}
