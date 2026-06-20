using BlazorDX.Components;
using BlazorDX.Primitives.Query;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The visual query builder edits its bound tree in place.</summary>
public sealed class DxQueryBuilderTests : TestContext
{
    private static IReadOnlyList<QueryField> Fields() =>
    [
        new QueryField("Name"),
        new QueryField("Visits", IsNumeric: true),
    ];

    private IRenderedComponent<DxQueryBuilder> Render(QueryGroup root) =>
        RenderComponent<DxQueryBuilder>(parameters => parameters
            .Add(q => q.Fields, Fields())
            .Add(q => q.Root, root));

    [Fact]
    public void Add_condition_appends_to_the_tree_and_renders_a_row()
    {
        QueryGroup root = new();
        IRenderedComponent<DxQueryBuilder> qb = Render(root);

        qb.FindAll(".dx-qb-add")[0].Click();   // "+ Condition"

        Assert.Single(root.Children);
        Assert.IsType<QueryCondition>(root.Children[0]);
        Assert.Single(qb.FindAll(".dx-qb-row"));
        // The new condition defaults to the first field.
        Assert.Equal("Name", ((QueryCondition)root.Children[0]).Field);
    }

    [Fact]
    public void Editing_a_condition_updates_the_model()
    {
        QueryGroup root = new();
        root.Children.Add(new QueryCondition { Field = "Name", Operator = QueryOperator.Contains });
        IRenderedComponent<DxQueryBuilder> qb = Render(root);

        qb.Find(".dx-qb-op").Change("GreaterThan");
        qb.Find(".dx-qb-value").Input("100");

        var condition = (QueryCondition)root.Children[0];
        Assert.Equal(QueryOperator.GreaterThan, condition.Operator);
        Assert.Equal("100", condition.Value);
    }

    [Fact]
    public void Combinator_toggle_switches_and_or()
    {
        QueryGroup root = new();
        IRenderedComponent<DxQueryBuilder> qb = Render(root);

        Assert.Equal(QueryCombinator.And, root.Combinator);
        qb.FindAll(".dx-qb-comb")[1].Click();   // "OR"
        Assert.Equal(QueryCombinator.Or, root.Combinator);
    }

    [Fact]
    public void Add_group_nests_a_child_group()
    {
        QueryGroup root = new();
        IRenderedComponent<DxQueryBuilder> qb = Render(root);

        qb.FindAll(".dx-qb-add")[1].Click();   // "+ Group"

        Assert.Single(root.Children);
        Assert.IsType<QueryGroup>(root.Children[0]);
        Assert.Equal(2, qb.FindAll(".dx-qb-group").Count);   // root + nested
    }

    [Fact]
    public void Remove_deletes_the_node()
    {
        QueryGroup root = new();
        root.Children.Add(new QueryCondition { Field = "Name" });
        IRenderedComponent<DxQueryBuilder> qb = Render(root);

        qb.Find(".dx-qb-remove").Click();

        Assert.Empty(root.Children);
        Assert.Empty(qb.FindAll(".dx-qb-row"));
    }
}
