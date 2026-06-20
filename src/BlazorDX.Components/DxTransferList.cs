using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A dual list box: move items between an "available" and a "selected" list. It is
/// a pure composition of two <see cref="DxListbox{TValue}"/> (multi-select) plus
/// move buttons — no new behavior, demonstrating how components assemble from the
/// shared engine. <c>Value</c> is the set of items currently in the selected list.
/// </summary>
public sealed class DxTransferList : ComponentBase
{
    private IReadOnlyCollection<string> sourceChecked = [];
    private IReadOnlyCollection<string> targetChecked = [];

    [Parameter] public IReadOnlyList<string> Items { get; set; } = [];

    /// <summary>The items currently in the "selected" list. Two-way bindable.</summary>
    [Parameter] public IReadOnlyCollection<string> Value { get; set; } = [];

    [Parameter] public EventCallback<IReadOnlyCollection<string>> ValueChanged { get; set; }

    [Parameter] public string AvailableLabel { get; set; } = "Available";

    [Parameter] public string SelectedLabel { get; set; } = "Selected";

    private bool InTarget(string item) => Value.Contains(item);

    private IReadOnlyList<ListOption<string>> SourceOptions =>
        Items.Where(item => !InTarget(item)).Select(item => new ListOption<string>(item, item)).ToList();

    private IReadOnlyList<ListOption<string>> TargetOptions =>
        Items.Where(InTarget).Select(item => new ListOption<string>(item, item)).ToList();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-transfer");

        BuildPane(builder, 2, AvailableLabel, SourceOptions, sourceChecked,
            EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, values => sourceChecked = values));

        BuildButtons(builder);

        BuildPane(builder, 3, SelectedLabel, TargetOptions, targetChecked,
            EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, values => targetChecked = values));

        builder.CloseElement();
    }

    private static void BuildPane(
        RenderTreeBuilder builder,
        int sequence,
        string label,
        IReadOnlyList<ListOption<string>> options,
        IReadOnlyCollection<string> selected,
        EventCallback<IReadOnlyCollection<string>> selectedChanged)
    {
        builder.OpenElement(sequence, "div");
        builder.AddAttribute(sequence + 1, "class", "dx-transfer-pane");

        builder.OpenElement(sequence + 2, "div");
        builder.AddAttribute(sequence + 3, "class", "dx-transfer-label");
        builder.AddContent(sequence + 4, label);
        builder.CloseElement();

        builder.OpenComponent<DxListbox<string>>(sequence + 5);
        builder.AddComponentParameter(sequence + 6, "Items", options);
        builder.AddComponentParameter(sequence + 7, "Multiple", true);
        builder.AddComponentParameter(sequence + 8, "Values", selected);
        builder.AddComponentParameter(sequence + 9, "ValuesChanged", selectedChanged);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void BuildButtons(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "div");
        builder.AddAttribute(41, "class", "dx-transfer-buttons");

        Button(builder, 42, "Move all to selected", "»", MoveAllRightAsync);
        Button(builder, 43, "Move selected to selected", "›", MoveSelectedRightAsync);
        Button(builder, 44, "Remove selected", "‹", MoveSelectedLeftAsync);
        Button(builder, 45, "Remove all", "«", MoveAllLeftAsync);

        builder.CloseElement();
    }

    private void Button(RenderTreeBuilder builder, int sequence, string label, string glyph, Func<Task> onClick)
    {
        builder.OpenElement(sequence, "button");
        builder.AddAttribute(sequence + 100, "type", "button");
        builder.AddAttribute(sequence + 200, "class", "dx-transfer-button");
        builder.AddAttribute(sequence + 300, "aria-label", label);
        builder.AddAttribute(sequence + 400, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(sequence + 500, glyph);
        builder.CloseElement();
    }

    private Task MoveSelectedRightAsync()
    {
        List<string> target = Items.Where(item => InTarget(item) || sourceChecked.Contains(item)).ToList();
        sourceChecked = [];
        return EmitAsync(target);
    }

    private Task MoveSelectedLeftAsync()
    {
        List<string> target = Items.Where(item => InTarget(item) && !targetChecked.Contains(item)).ToList();
        targetChecked = [];
        return EmitAsync(target);
    }

    private Task MoveAllRightAsync()
    {
        sourceChecked = [];
        return EmitAsync(Items.ToList());
    }

    private Task MoveAllLeftAsync()
    {
        targetChecked = [];
        return EmitAsync([]);
    }

    private async Task EmitAsync(IReadOnlyCollection<string> target)
    {
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(target);
        }
    }
}
