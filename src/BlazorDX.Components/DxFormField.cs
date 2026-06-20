using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders one field of the enclosing <see cref="DxForm{TModel}"/> by name. Use it
/// inside a form's child content to lay fields out manually — across
/// <see cref="DxFormSection"/>s, a <see cref="DxFormGrid"/>, or any markup — while
/// keeping the generated binding, validation, and templates.
/// </summary>
public sealed class DxFormField : ComponentBase, IDisposable
{
    /// <summary>The model property name to render.</summary>
    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;

    [CascadingParameter] private FormContext? Form { get; set; }

    private FormContext? subscribed;

    protected override void OnParametersSet()
    {
        // Re-render this field whenever the form's value/validation state changes,
        // including model mutations that bypass this field's own input (e.g. an AI fill).
        if (!ReferenceEquals(subscribed, Form))
        {
            if (subscribed is not null)
            {
                subscribed.Changed -= OnFormChanged;
            }

            subscribed = Form;
            if (subscribed is not null)
            {
                subscribed.Changed += OnFormChanged;
            }
        }
    }

    private void OnFormChanged() => InvokeAsync(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        FormFieldInfo? field = Form?.Find(Name);
        if (Form is null || field is null)
        {
            return;
        }

        FormFieldRenderer.Render(builder, Form, field);
    }

    public void Dispose()
    {
        if (subscribed is not null)
        {
            subscribed.Changed -= OnFormChanged;
        }
    }
}
