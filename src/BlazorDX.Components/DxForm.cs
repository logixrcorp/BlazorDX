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
///
/// A thin typed wrapper: the actual rendering/validation lives in
/// <see cref="DxFormBody"/> (over <see cref="IFormModelUntyped"/>/<c>object</c>, not
/// generic) — this class exists to give callers a strongly-typed <see cref="Model"/>/
/// <see cref="Descriptor"/>/<see cref="OnValidSubmit"/> surface and to render the
/// actual <c>&lt;form&gt;</c> element. A nested Object/Array field opens
/// <see cref="DxFormBody"/> directly instead, since dynamically opening
/// <c>DxForm&lt;TNested&gt;</c> for a runtime-only-known <c>TNested</c> would need
/// <c>Type.MakeGenericType</c> — incompatible with Native AOT.
/// </summary>
/// <typeparam name="TModel">The annotated model type.</typeparam>
public sealed class DxForm<TModel> : ComponentBase
{
    private DxFormBody? bodyRef;

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
    [Parameter] public string? SubmitText { get; set; }

    /// <summary>Extra CSS classes appended to the form element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Re-reads the model into the rendered fields. Call this after the model is changed
    /// from outside the form — e.g. when an AI tool call fills it via <c>FormTool</c>.
    /// </summary>
    public void Refresh() => bodyRef?.Refresh();

    private Task SubmitAsync() => bodyRef?.SubmitAsync() ?? Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "form");
        builder.AddAttribute(1, "class", $"dx-form {Class}".TrimEnd());
        builder.AddAttribute(2, "onsubmit", EventCallback.Factory.Create(this, SubmitAsync));
        builder.AddEventPreventDefaultAttribute(3, "onsubmit", true);

        builder.OpenComponent<DxFormBody>(4);
        builder.AddComponentParameter(5, "Model", (object)Model!);
        builder.AddComponentParameter(6, "Descriptor", Descriptor);
        builder.AddComponentParameter(7, "OnValidSubmit",
            EventCallback.Factory.Create<object>(this, m => OnValidSubmit.InvokeAsync((TModel)m)));
        builder.AddComponentParameter(8, "OnInvalidSubmit", OnInvalidSubmit);
        builder.AddComponentParameter(9, "ChildContent", ChildContent);
        builder.AddComponentParameter(10, "FieldTemplate", FieldTemplate);
        builder.AddComponentParameter(11, "InputTemplate", InputTemplate);
        builder.AddComponentParameter(12, "LabelTemplate", LabelTemplate);
        builder.AddComponentParameter(13, "ValidateOnChange", ValidateOnChange);
        builder.AddComponentParameter(14, "ShowSubmit", ShowSubmit);
        builder.AddComponentParameter(15, "SubmitText", SubmitText);
        builder.AddComponentReferenceCapture(16, r => bodyRef = (DxFormBody)r);
        builder.CloseComponent();

        builder.CloseElement();
    }
}
