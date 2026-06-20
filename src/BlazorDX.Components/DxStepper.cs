using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>One step in a stepper: a title and the content shown when it is active.</summary>
public sealed record StepItem(string Title, RenderFragment Content);

/// <summary>
/// A horizontal stepper / wizard: numbered step indicators (completed / current /
/// upcoming), the current step's content, and Back / Next navigation. Steps can
/// also be clicked. The current index is two-way bindable. Styling is CSS-variable
/// driven (see dx-layout.css).
/// </summary>
public sealed class DxStepper : ComponentBase
{
    [Parameter] public IReadOnlyList<StepItem> Steps { get; set; } = [];

    /// <summary>The active step index (0-based). Two-way bindable.</summary>
    [Parameter] public int Current { get; set; }

    [Parameter] public EventCallback<int> CurrentChanged { get; set; }

    [Parameter] public string? Class { get; set; }

    private bool IsFirst => Current <= 0;

    private bool IsLast => Current >= Steps.Count - 1;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-stepper {Class}".TrimEnd());

        BuildIndicators(builder);

        // Current step's content.
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-stepper-content");
        if (Current >= 0 && Current < Steps.Count)
        {
            builder.AddContent(4, Steps[Current].Content);
        }

        builder.CloseElement();

        BuildActions(builder);

        builder.CloseElement();
    }

    private void BuildIndicators(RenderTreeBuilder builder)
    {
        builder.OpenElement(5, "ol");
        builder.AddAttribute(6, "class", "dx-stepper-steps");

        for (int i = 0; i < Steps.Count; i++)
        {
            int index = i;
            bool done = i < Current;
            bool current = i == Current;

            string state = done ? "dx-stepper-done" : current ? "dx-stepper-current" : "dx-stepper-todo";

            builder.OpenElement(7, "li");
            builder.SetKey(Steps[i]);
            builder.AddAttribute(8, "class", $"dx-stepper-step {state}");

            builder.OpenElement(9, "button");
            builder.AddAttribute(10, "type", "button");
            builder.AddAttribute(11, "class", "dx-stepper-trigger");
            if (current)
            {
                builder.AddAttribute(12, "aria-current", "step");
            }

            builder.AddAttribute(13, "onclick", EventCallback.Factory.Create(this, () => GoToAsync(index)));

            builder.OpenElement(14, "span");
            builder.AddAttribute(15, "class", "dx-stepper-marker");
            builder.AddContent(16, done ? "✓" : (i + 1).ToString());
            builder.CloseElement();

            builder.OpenElement(17, "span");
            builder.AddAttribute(18, "class", "dx-stepper-title");
            builder.AddContent(19, Steps[i].Title);
            builder.CloseElement();

            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildActions(RenderTreeBuilder builder)
    {
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dx-stepper-actions");

        builder.OpenElement(22, "button");
        builder.AddAttribute(23, "type", "button");
        builder.AddAttribute(24, "class", "dx-demo-button dx-stepper-back");
        builder.AddAttribute(25, "disabled", IsFirst);
        if (!IsFirst)
        {
            builder.AddAttribute(26, "onclick", EventCallback.Factory.Create(this, () => GoToAsync(Current - 1)));
        }

        builder.AddContent(27, "Back");
        builder.CloseElement();

        builder.OpenElement(28, "button");
        builder.AddAttribute(29, "type", "button");
        builder.AddAttribute(30, "class", "dx-demo-button dx-stepper-next");
        builder.AddAttribute(31, "disabled", IsLast);
        if (!IsLast)
        {
            builder.AddAttribute(32, "onclick", EventCallback.Factory.Create(this, () => GoToAsync(Current + 1)));
        }

        builder.AddContent(33, "Next");
        builder.CloseElement();

        builder.CloseElement();
    }

    private async Task GoToAsync(int index)
    {
        if (index >= 0 && index < Steps.Count && index != Current && CurrentChanged.HasDelegate)
        {
            await CurrentChanged.InvokeAsync(index);
        }
    }
}
