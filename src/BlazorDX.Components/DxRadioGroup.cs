using BlazorDX.Primitives.Navigation;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A single-select radio group following the WAI-ARIA radio pattern: one tab stop,
/// arrow keys move and select (roving tabindex). Reuses the roving-tabindex
/// primitive. Two-way bind via <c>@bind-Value</c>. Styling is CSS-variable driven.
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public sealed class DxRadioGroup<TValue> : ComponentBase
{
    private readonly RovingTabIndex roving = new();
    private ElementReference[] itemElements = [];

    [Parameter] public IReadOnlyList<ListOption<TValue>> Items { get; set; } = [];

    [Parameter] public TValue? Value { get; set; }

    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Class { get; set; }

    private int SelectedIndex()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (EqualityComparer<TValue>.Default.Equals(Items[i].Value, Value))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnParametersSet()
    {
        roving.Configure(Items.Count, index => !Items[index].Disabled);
        int selected = SelectedIndex();
        roving.MoveTo(selected >= 0 ? selected : FirstEnabled()); // the tabbable radio
        if (itemElements.Length != Items.Count)
        {
            itemElements = new ElementReference[Items.Count];
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int selected = SelectedIndex();

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-radio-group {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "radiogroup");
        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(3, "aria-label", AriaLabel);
        }

        builder.AddAttribute(4, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));

        for (int i = 0; i < Items.Count; i++)
        {
            ListOption<TValue> option = Items[i];
            int index = i;
            bool isChecked = i == selected;
            bool tabbable = roving.IsActive(i) || (selected < 0 && i == FirstEnabled());

            string css = "dx-radio";
            if (isChecked)
            {
                css += " dx-radio-checked";
            }

            if (option.Disabled)
            {
                css += " dx-radio-disabled";
            }

            builder.OpenElement(5, "div");
            builder.SetKey(option);
            builder.AddAttribute(6, "class", css);
            builder.AddAttribute(7, "role", "radio");
            builder.AddAttribute(8, "aria-checked", isChecked ? "true" : "false");
            builder.AddAttribute(9, "tabindex", tabbable ? "0" : "-1");
            if (!option.Disabled)
            {
                builder.AddAttribute(10, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(index)));
            }

            builder.AddElementReferenceCapture(11, element => itemElements[index] = element);

            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "dx-radio-dot");
            builder.AddAttribute(14, "aria-hidden", "true");
            builder.CloseElement();

            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "class", "dx-radio-label");
            builder.AddContent(17, option.Text);
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown" or "ArrowRight": roving.MoveNext(); await SelectActiveAsync(); break;
            case "ArrowUp" or "ArrowLeft": roving.MovePrevious(); await SelectActiveAsync(); break;
            case "Home": roving.MoveFirst(); await SelectActiveAsync(); break;
            case "End": roving.MoveLast(); await SelectActiveAsync(); break;
        }
    }

    private async Task SelectActiveAsync()
    {
        int index = roving.ActiveIndex;
        if (index >= 0 && index < itemElements.Length)
        {
            try
            {
                await itemElements[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // not yet rendered
            }
        }

        await SelectAsync(index);
    }

    private async Task SelectAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        roving.MoveTo(index);
        if (ValueChanged.HasDelegate && !EqualityComparer<TValue>.Default.Equals(Items[index].Value, Value))
        {
            await ValueChanged.InvokeAsync(Items[index].Value);
        }
    }

    private int FirstEnabled()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (!Items[i].Disabled)
            {
                return i;
            }
        }

        return -1;
    }
}
