using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// The actual field-rendering/validation engine behind <see cref="DxForm{TModel}"/> —
/// over <see cref="IFormModelUntyped"/>/<c>object</c> rather than a generic
/// <c>TModel</c>. Not public API: <c>DxForm&lt;TModel&gt;</c> is a thin typed wrapper
/// around one of these (boxing <c>Model</c>/passing <c>Descriptor</c>, which already
/// implements <see cref="IFormModelUntyped"/>), and it's also what a nested Object/
/// Array field opens directly for its sub-form.
///
/// This split exists specifically so nested rendering never needs
/// <c>Type.MakeGenericType</c> to open a <c>DxForm&lt;TNested&gt;</c> for a
/// runtime-only-known <c>TNested</c> — that API is incompatible with Native AOT (this
/// repo publishes and smoke-tests an AOT build in CI). <c>DxFormBody</c> is not
/// generic, so opening one for any nested model is an ordinary
/// <c>builder.OpenComponent&lt;DxFormBody&gt;(...)</c> — ordinary, ahead-of-time-
/// compilable component instantiation. It also renders no <c>&lt;form&gt;</c> element
/// of its own, so a nested sub-form can never produce the invalid nested-<c>&lt;form&gt;</c>
/// HTML a generic-wrapper approach risked.
/// </summary>
internal sealed class DxFormBody : ComponentBase
{
    private readonly List<FormValidationError> errors = new();

    // Per-form id prefix so each field's error region has a unique, stable id to
    // wire `aria-describedby` against (WCAG 3.3.1 Error Identification).
    private readonly string idPrefix = $"dx-form-{Guid.NewGuid():N}";
    private FormContext? context;

    // Nested/array-of-nested DxFormBody references, keyed by field name and (for an
    // array row) its index — -1 for a singular Object field. Lets Refresh()/
    // SubmitAsync propagate into them; no interface needed since the concrete type is
    // always DxFormBody, generic or not.
    private readonly Dictionary<(string Field, int Index), DxFormBody> nestedFormRefs = new();

    [Parameter, EditorRequired] public object Model { get; set; } = default!;

    [Parameter, EditorRequired] public IFormModelUntyped Descriptor { get; set; } = default!;

    [Parameter] public EventCallback<object> OnValidSubmit { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<FormValidationError>> OnInvalidSubmit { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public RenderFragment<FormFieldRenderContext>? FieldTemplate { get; set; }

    [Parameter] public RenderFragment<FormFieldRenderContext>? InputTemplate { get; set; }

    [Parameter] public RenderFragment<FormFieldInfo>? LabelTemplate { get; set; }

    [Parameter] public bool ValidateOnChange { get; set; }

    [Parameter] public bool ShowSubmit { get; set; } = true;

    [Parameter] public string SubmitText { get; set; } = "Submit";

    protected override void OnParametersSet()
    {
        // Stable context: the closures read the current Model/errors at call time.
        context ??= new FormContext
        {
            Receiver = this,
            IdPrefix = idPrefix,
            Fields = Descriptor.Fields,
            Get = name => Descriptor.GetString(Model, name),
            SetAsync = SetFieldAsync,
            ErrorsFor = MessagesFor,
            IsActive = field => FormFieldActivity.IsActive(Descriptor, Model, field),
            GetNested = name => Descriptor.GetNestedInstance(Model, name),
            SetNested = (name, instance) => Descriptor.SetNestedInstance(Model, name, instance),
            NestedDescriptorFor = name => Descriptor.GetNestedDescriptor(name),
            NewNestedInstance = name => Descriptor.NewNestedInstance(name),
            GetArrayStrings = name => Descriptor.GetArrayStrings(Model, name),
            SetArrayStringsAsync = (name, items) => SetArrayAsync(() => Descriptor.SetArrayStrings(Model, name, items)),
            GetArrayInstances = name => Descriptor.GetArrayInstances(Model, name),
            SetArrayInstancesAsync = (name, items) => SetArrayAsync(() => Descriptor.SetArrayInstances(Model, name, items)),
            ArrayElementDescriptorFor = name => Descriptor.GetArrayElementDescriptor(name),
            NewArrayElement = name => Descriptor.NewArrayElement(name),
            CaptureNestedRef = (field, index, componentRef) => nestedFormRefs[(field, index)] = (DxFormBody)componentRef,
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

    // Shared by the array-field setters wired into FormContext: mutate the array
    // through the descriptor, then the same re-render posture SetFieldAsync already
    // uses. Re-render of the DxFieldList/this body itself is automatic — both are
    // driven by EventCallbacks bound to `this`, which Blazor re-renders after
    // InvokeAsync completes (the same mechanism SetFieldAsync already relies on).
    private Task SetArrayAsync(Action mutate)
    {
        mutate();
        if (ValidateOnChange)
        {
            Revalidate();
        }

        context?.RaiseChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Re-reads the model into the rendered fields. Called by <see cref="DxForm{TModel}.Refresh"/>
    /// after the model is changed from outside the form — e.g. an AI tool call filling it via
    /// <c>FormTool</c> — and propagated into every nested sub-form too.
    /// </summary>
    public void Refresh()
    {
        Revalidate();
        context?.RaiseChanged();
        PropagateRefreshToNestedForms();
        StateHasChanged();
    }

    // Propagates into every currently-captured nested/array-of-nested DxFormBody so it
    // re-validates and re-renders too — needed because a nested sub-form keeps its OWN
    // independent errors list (see FormContext.RenderNestedObject's comment), which
    // nothing else would refresh.
    private void PropagateRefreshToNestedForms()
    {
        foreach (DxFormBody nested in nestedFormRefs.Values)
        {
            nested.Refresh();
        }
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

    /// <summary>Called by <see cref="DxForm{TModel}"/>'s own <c>onsubmit</c> handler.</summary>
    public async Task SubmitAsync()
    {
        Revalidate();
        PropagateRefreshToNestedForms();
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
        builder.OpenComponent<CascadingValue<FormContext>>(0);
        builder.AddComponentParameter(1, "Value", context);
        builder.AddComponentParameter(2, "IsFixed", true);
        builder.AddComponentParameter(3, "ChildContent", (RenderFragment)RenderBody);
        builder.CloseComponent();
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
                // The region is opened (and its sequence number consumed) for every field
                // regardless of active state, so a field's diffing identity doesn't shift
                // whenever an *earlier* field's active-state toggles.
                builder.OpenRegion(region++);
                if (context!.IsActive(field))
                {
                    FormFieldRenderer.Render(builder, context!, field);
                }

                builder.CloseRegion();
            }
        }

        if (ShowSubmit)
        {
            // type="submit" bubbles to the nearest ancestor <form> — always the real
            // <form> DxForm<TModel> renders around this body (this button only ever
            // renders when ShowSubmit is true, which a nested sub-form never passes).
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
