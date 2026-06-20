using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Renders and validates a <c>[DxFormModel]</c>-annotated model through its generated
/// <see cref="IFormModel{TModel}"/> descriptor — no reflection. With no
/// <see cref="ChildContent"/> it auto-renders every field; supply child content (e.g.
/// <see cref="DxFormSection"/>, <see cref="DxFormGrid"/>, <see cref="DxFormField"/>) to
/// lay them out yourself. Every layer is templatable via
/// <see cref="FieldTemplate"/>/<see cref="InputTemplate"/>/<see cref="LabelTemplate"/>.
/// The same descriptor powers the AI-tool projection (see <c>FormTool</c>), so this UI
/// and an AI tool call share one model and one set of validation rules.
/// </summary>
/// <typeparam name="TModel">The annotated model type.</typeparam>
public sealed class DxForm<TModel> : ComponentBase
{
    private readonly List<FormValidationError> errors = new();
    private FormContext? context;

    /// <summary>The model instance the form edits.</summary>
    [Parameter, EditorRequired] public TModel Model { get; set; } = default!;

    /// <summary>The generated descriptor for <typeparamref name="TModel"/> (e.g. <c>new MyModelFormModel()</c>).</summary>
    [Parameter, EditorRequired] public IFormModel<TModel> Descriptor { get; set; } = default!;

    /// <summary>Raised with the model when submitted and validation passes.</summary>
    [Parameter] public EventCallback<TModel> OnValidSubmit { get; set; }

    /// <summary>Raised with the errors when submitted and validation fails.</summary>
    [Parameter] public EventCallback<IReadOnlyList<FormValidationError>> OnInvalidSubmit { get; set; }

    /// <summary>Manual layout. When null, all fields auto-render in declared order.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Overrides the entire per-field row (label + input + errors).</summary>
    [Parameter] public RenderFragment<FormFieldRenderContext>? FieldTemplate { get; set; }

    /// <summary>Overrides just the input control for every field.</summary>
    [Parameter] public RenderFragment<FormFieldRenderContext>? InputTemplate { get; set; }

    /// <summary>Overrides the label for every field.</summary>
    [Parameter] public RenderFragment<FormFieldInfo>? LabelTemplate { get; set; }

    /// <summary>Re-validate after every field change (default: validate on submit only).</summary>
    [Parameter] public bool ValidateOnChange { get; set; }

    /// <summary>Show the built-in submit button.</summary>
    [Parameter] public bool ShowSubmit { get; set; } = true;

    /// <summary>Submit button text.</summary>
    [Parameter] public string SubmitText { get; set; } = "Submit";

    /// <summary>Extra CSS classes appended to the form element.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void OnParametersSet()
    {
        // Stable context: the closures read the current Model/errors at call time.
        context ??= new FormContext
        {
            Receiver = this,
            Fields = Descriptor.Fields,
            Get = name => Descriptor.GetString(Model, name),
            SetAsync = SetFieldAsync,
            ErrorsFor = MessagesFor,
            FieldTemplate = FieldTemplate,
            InputTemplate = InputTemplate,
            LabelTemplate = LabelTemplate,
        };
    }

    private Task SetFieldAsync(string name, string value)
    {
        Descriptor.SetString(Model, name, value);
        if (ValidateOnChange)
        {
            Revalidate();
        }

        context?.RaiseChanged();   // refresh manually-placed DxFormFields
        return Task.CompletedTask;
    }

    /// <summary>
    /// Re-reads the model into the rendered fields. Call this after the model is changed
    /// from outside the form — e.g. when an AI tool call fills it via <c>FormTool</c>.
    /// </summary>
    public void Refresh()
    {
        Revalidate();
        context?.RaiseChanged();
        StateHasChanged();
    }

    private IReadOnlyList<string> MessagesFor(string name)
    {
        List<string> messages = new();
        foreach (FormValidationError error in errors)
        {
            if (error.Field == name)
            {
                messages.Add(error.Message);
            }
        }

        return messages;
    }

    private void Revalidate()
    {
        errors.Clear();
        errors.AddRange(Descriptor.Validate(Model));
    }

    private async Task SubmitAsync()
    {
        Revalidate();
        if (errors.Count == 0)
        {
            await OnValidSubmit.InvokeAsync(Model);
        }
        else
        {
            await OnInvalidSubmit.InvokeAsync(errors);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "form");
        builder.AddAttribute(1, "class", $"dx-form {Class}".TrimEnd());
        builder.AddAttribute(2, "onsubmit", EventCallback.Factory.Create(this, SubmitAsync));
        builder.AddEventPreventDefaultAttribute(3, "onsubmit", true);

        builder.OpenComponent<CascadingValue<FormContext>>(4);
        builder.AddComponentParameter(5, "Value", context);
        builder.AddComponentParameter(6, "IsFixed", true);
        builder.AddComponentParameter(7, "ChildContent", (RenderFragment)RenderBody);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderBody(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(0, ChildContent);
        }
        else
        {
            int region = 0;
            foreach (FormFieldInfo field in Descriptor.Fields)
            {
                builder.OpenRegion(region++);   // isolate each iteration's sequence space
                FormFieldRenderer.Render(builder, context!, field);
                builder.CloseRegion();
            }
        }

        if (ShowSubmit)
        {
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", "dx-form-actions");
            builder.OpenElement(12, "button");
            builder.AddAttribute(13, "type", "submit");
            builder.AddAttribute(14, "class", "dx-btn-primary");
            builder.AddContent(15, SubmitText);
            builder.CloseElement();
            builder.CloseElement();
        }
    }
}
