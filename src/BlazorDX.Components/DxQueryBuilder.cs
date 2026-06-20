using BlazorDX.Primitives.Query;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A visual query builder: nestable AND/OR groups of field-operator-value
/// conditions over a declared field list. Edits the bound <see cref="Root"/> tree
/// in place; evaluate it with <c>QueryEvaluator</c>. Styling is token-driven (see
/// dx-querybuilder.css).
/// </summary>
public sealed class DxQueryBuilder : ComponentBase
{
    private static readonly (QueryOperator Op, string Label)[] Operators =
    [
        (QueryOperator.Equals, "="),
        (QueryOperator.NotEquals, "≠"),
        (QueryOperator.Contains, "contains"),
        (QueryOperator.StartsWith, "starts with"),
        (QueryOperator.GreaterThan, ">"),
        (QueryOperator.LessThan, "<"),
    ];

    [Parameter, EditorRequired] public IReadOnlyList<QueryField> Fields { get; set; } = [];

    [Parameter, EditorRequired] public QueryGroup Root { get; set; } = new();

    /// <summary>Raised whenever the query tree changes.</summary>
    [Parameter] public EventCallback OnChange { get; set; }

    [Parameter] public string? Class { get; set; }

    private string DefaultField => Fields.Count > 0 ? Fields[0].Name : string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-qb {Class}".TrimEnd());
        BuildGroup(builder, Root);
        builder.CloseElement();
    }

    private void BuildGroup(RenderTreeBuilder builder, QueryGroup group)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-qb-group");

        // Combinator toggle.
        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", "dx-qb-combinator");
        CombinatorButton(builder, 6, group, QueryCombinator.And, "AND");
        CombinatorButton(builder, 10, group, QueryCombinator.Or, "OR");
        builder.CloseElement();

        for (int i = 0; i < group.Children.Count; i++)
        {
            QueryNode child = group.Children[i];
            builder.OpenElement(14, "div");
            builder.SetKey(child);
            builder.AddAttribute(15, "class", "dx-qb-child");

            if (child is QueryGroup nested)
            {
                BuildGroup(builder, nested);
            }
            else if (child is QueryCondition condition)
            {
                BuildCondition(builder, condition);
            }

            builder.OpenElement(16, "button");
            builder.AddAttribute(17, "type", "button");
            builder.AddAttribute(18, "class", "dx-qb-remove");
            builder.AddAttribute(19, "aria-label", "Remove");
            builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, () => Remove(group, child)));
            builder.AddContent(21, "✕");
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.OpenElement(22, "div");
        builder.AddAttribute(23, "class", "dx-qb-actions");
        ActionButton(builder, 24, "+ Condition", () => AddCondition(group));
        ActionButton(builder, 28, "+ Group", () => AddGroup(group));
        builder.CloseElement();

        builder.CloseElement();
    }

    private void BuildCondition(RenderTreeBuilder builder, QueryCondition condition)
    {
        builder.OpenElement(32, "div");
        builder.AddAttribute(33, "class", "dx-qb-row");

        // Field select.
        builder.OpenElement(34, "select");
        builder.AddAttribute(35, "class", "dx-qb-field");
        builder.AddAttribute(36, "aria-label", "Field");
        builder.AddAttribute(37, "value", condition.Field);
        builder.AddAttribute(38, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this,
            e => { condition.Field = e.Value as string ?? string.Empty; Changed(); }));
        foreach (QueryField field in Fields)
        {
            builder.OpenElement(39, "option");
            builder.SetKey(field.Name);
            builder.AddAttribute(40, "value", field.Name);
            builder.AddContent(41, field.Name);
            builder.CloseElement();
        }

        builder.CloseElement();

        // Operator select.
        builder.OpenElement(42, "select");
        builder.AddAttribute(43, "class", "dx-qb-op");
        builder.AddAttribute(44, "aria-label", "Operator");
        builder.AddAttribute(45, "value", condition.Operator.ToString());
        builder.AddAttribute(46, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this,
            e => { condition.Operator = Enum.Parse<QueryOperator>(e.Value as string ?? "Contains"); Changed(); }));
        foreach ((QueryOperator op, string label) in Operators)
        {
            builder.OpenElement(47, "option");
            builder.SetKey(op);
            builder.AddAttribute(48, "value", op.ToString());
            builder.AddContent(49, label);
            builder.CloseElement();
        }

        builder.CloseElement();

        // Value input.
        builder.OpenElement(50, "input");
        builder.AddAttribute(51, "class", "dx-qb-value");
        builder.AddAttribute(52, "type", "text");
        builder.AddAttribute(53, "aria-label", "Value");
        builder.AddAttribute(54, "value", condition.Value);
        builder.AddAttribute(55, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this,
            e => { condition.Value = e.Value as string ?? string.Empty; Changed(); }));
        builder.CloseElement();

        builder.CloseElement();
    }

    private void CombinatorButton(RenderTreeBuilder builder, int seq, QueryGroup group, QueryCombinator value, string label)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", group.Combinator == value ? "dx-qb-comb dx-qb-comb-active" : "dx-qb-comb");
        builder.AddAttribute(seq + 3, "aria-pressed", group.Combinator == value ? "true" : "false");
        builder.AddAttribute(seq + 4, "onclick", EventCallback.Factory.Create(this, () => { group.Combinator = value; Changed(); }));
        builder.AddContent(seq + 5, label);
        builder.CloseElement();
    }

    private void ActionButton(RenderTreeBuilder builder, int seq, string label, Action onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-qb-add");
        builder.AddAttribute(seq + 3, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 4, label);
        builder.CloseElement();
    }

    private void AddCondition(QueryGroup group)
    {
        group.Children.Add(new QueryCondition { Field = DefaultField });
        Changed();
    }

    private void AddGroup(QueryGroup group)
    {
        group.Children.Add(new QueryGroup());
        Changed();
    }

    private void Remove(QueryGroup parent, QueryNode child)
    {
        parent.Children.Remove(child);
        Changed();
    }

    private void Changed()
    {
        StateHasChanged();
        _ = OnChange.InvokeAsync();
    }
}
