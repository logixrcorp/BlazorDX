using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Non-generic marker a nested <c>DxForm&lt;TNested&gt;</c> implements so an owning
/// form can propagate <c>Refresh()</c> into it without knowing <c>TNested</c> at its
/// own compile time — an ordinary interface dispatch, not reflection or <c>dynamic</c>.
/// </summary>
internal interface IRefreshableForm
{
    void Refresh();
}

/// <summary>What a field template receives: the field metadata, its current value, a
/// callback to change it, and any validation errors against it.</summary>
public sealed class FormFieldRenderContext
{
    public required FormFieldInfo Field { get; init; }
    public required string Value { get; init; }
    public required EventCallback<string> ValueChanged { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

/// <summary>
/// Cascaded by <see cref="DxForm{TModel}"/> so child components (<see cref="DxFormField"/>,
/// containers) can render fields without knowing the model's type. It exposes string
/// get/set over the generated descriptor plus any author-supplied templates.
/// </summary>
public sealed class FormContext
{
    public required object Receiver { get; init; }

    /// <summary>Unique-per-form id stem, used to build stable element ids (e.g. error
    /// regions for <c>aria-describedby</c>). Set by <see cref="DxForm{TModel}"/>.</summary>
    public required string IdPrefix { get; init; }

    public required IReadOnlyList<FormFieldInfo> Fields { get; init; }
    public required Func<string, string> Get { get; init; }
    public required Func<string, string, Task> SetAsync { get; init; }
    public required Func<string, IReadOnlyList<string>> ErrorsFor { get; init; }

    /// <summary>Whether a (possibly conditional) field is currently active — see
    /// <see cref="FormFieldActivity"/>. Always true for an unconditional field.</summary>
    public required Func<FormFieldInfo, bool> IsActive { get; init; }

    // ---- Nested (Object-kind) field access ----
    // Closures over the owning DxForm<TModel>'s Descriptor/Model, boxed at this
    // untyped boundary the same way IFormModelUntyped itself is.
    public required Func<string, object?> GetNested { get; init; }
    public required Action<string, object> SetNested { get; init; }
    public required Func<string, IFormModelUntyped?> NestedDescriptorFor { get; init; }
    public required Func<string, object> NewNestedInstance { get; init; }

    // ---- Array-kind field access ----
    public required Func<string, IReadOnlyList<string>> GetArrayStrings { get; init; }
    public required Func<string, IReadOnlyList<string>, Task> SetArrayStringsAsync { get; init; }
    public required Func<string, IReadOnlyList<object>> GetArrayInstances { get; init; }
    public required Func<string, IReadOnlyList<object>, Task> SetArrayInstancesAsync { get; init; }
    public required Func<string, IFormModelUntyped?> ArrayElementDescriptorFor { get; init; }
    public required Func<string, object> NewArrayElement { get; init; }

    /// <summary>
    /// Captures a nested <c>DxForm&lt;TNested&gt;</c>'s component reference so the
    /// owning form can propagate <c>Refresh()</c> into it — keyed by field name and,
    /// for an array-of-nested row, its index (-1 for a singular Object field).
    /// </summary>
    public required Action<string, int, object> CaptureNestedRef { get; init; }

    public RenderFragment<FormFieldRenderContext>? FieldTemplate { get; init; }
    public RenderFragment<FormFieldRenderContext>? InputTemplate { get; init; }
    public RenderFragment<FormFieldInfo>? LabelTemplate { get; init; }

    /// <summary>Raised when any field value or the validation state changes, so manually
    /// laid-out <see cref="DxFormField"/>s re-render even when the model is mutated elsewhere
    /// (e.g. an AI tool call).</summary>
    public event Action? Changed;

    internal void RaiseChanged() => Changed?.Invoke();

    public FormFieldInfo? Find(string name)
    {
        foreach (FormFieldInfo field in Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        return null;
    }
}

/// <summary>
/// Renders a single field — label, the input appropriate to its
/// <see cref="FormFieldKind"/>, and validation errors — honoring any
/// <see cref="FormContext.FieldTemplate"/>/<see cref="FormContext.InputTemplate"/>/
/// <see cref="FormContext.LabelTemplate"/>. Shared by the auto-render loop and
/// <see cref="DxFormField"/>.
/// </summary>
internal static class FormFieldRenderer
{
    public static void Render(RenderTreeBuilder b, FormContext ctx, FormFieldInfo field)
    {
        if (field.Kind == FormFieldKind.Object)
        {
            RenderNestedObject(b, ctx, field);
            return;
        }

        if (field.Kind == FormFieldKind.Array)
        {
            RenderArray(b, ctx, field);
            return;
        }

        string value = ctx.Get(field.Name);
        IReadOnlyList<string> errors = ctx.ErrorsFor(field.Name);
        // Stable id for the error region so the input can point at it via
        // aria-describedby; null when valid so the attributes are omitted (WCAG 3.3.1).
        string? errorId = errors.Count > 0 ? $"{ctx.IdPrefix}-err-{field.Name}" : null;
        EventCallback<string> changed =
            EventCallback.Factory.Create<string>(ctx.Receiver, v => ctx.SetAsync(field.Name, v));

        if (ctx.FieldTemplate is not null)
        {
            b.AddContent(0, ctx.FieldTemplate(new FormFieldRenderContext
            {
                Field = field, Value = value, ValueChanged = changed, Errors = errors,
            }));
            return;
        }

        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", errors.Count > 0 ? "dx-field dx-field-invalid" : "dx-field");

        // Label
        if (ctx.LabelTemplate is not null)
        {
            b.AddContent(2, ctx.LabelTemplate(field));
        }
        else
        {
            b.OpenElement(3, "label");
            b.AddAttribute(4, "class", "dx-field-label");
            b.AddContent(5, field.Label);
            if (field.Required)
            {
                b.OpenElement(6, "span");
                b.AddAttribute(7, "class", "dx-field-req");
                b.AddAttribute(8, "aria-hidden", "true");
                b.AddContent(9, " *");
                b.CloseElement();
            }

            b.CloseElement();
        }

        // Input
        if (ctx.InputTemplate is not null)
        {
            b.AddContent(20, ctx.InputTemplate(new FormFieldRenderContext
            {
                Field = field, Value = value, ValueChanged = changed, Errors = errors,
            }));
        }
        else
        {
            RenderInput(b, ctx.Receiver, field, value, changed, errorId);
        }

        // Errors: one alert region carrying the id referenced by aria-describedby,
        // so a screen reader reads the message(s) when focus returns to the field.
        if (errorId is not null)
        {
            b.OpenElement(70, "div");
            b.AddAttribute(71, "id", errorId);
            b.AddAttribute(72, "role", "alert");
            for (int i = 0; i < errors.Count; i++)
            {
                b.OpenElement(73, "span");
                b.SetKey(i);
                b.AddAttribute(74, "class", "dx-field-error");
                b.AddContent(75, errors[i]);
                b.CloseElement();
            }

            b.CloseElement();
        }

        b.CloseElement();
    }

    // An Object-kind field: a real DxFormSection wrapping a dynamically-opened
    // DxForm<TNested> (builder.OpenComponent(int, Type) is Blazor's own supported
    // dynamic-component API — it never inspects TNested's members, so this doesn't
    // touch ADR 0002's zero-reflection identity, which is about BlazorDX's own
    // model-binding layer, not Blazor's built-in component-parameter wiring that
    // every BlazorDX component already relies on). A currently-null nested property
    // is materialized (new TNested(), guaranteed constructible by DX2009) and
    // attached immediately, so the freshly-rendered sub-form edits the real instance.
    private static void RenderNestedObject(RenderTreeBuilder b, FormContext ctx, FormFieldInfo field)
    {
        IReadOnlyList<string> errors = ctx.ErrorsFor(field.Name);

        object? instance = ctx.GetNested(field.Name);
        if (instance is null)
        {
            instance = ctx.NewNestedInstance(field.Name);
            ctx.SetNested(field.Name, instance);
        }

        IFormModelUntyped? descriptor = ctx.NestedDescriptorFor(field.Name);
        Type formType = typeof(DxForm<>).MakeGenericType(instance.GetType());

        b.OpenComponent<DxFormSection>(0);
        b.AddComponentParameter(1, "Title", field.Label);
        b.AddComponentParameter(2, "ChildContent", (RenderFragment)(b2 =>
        {
            b2.OpenComponent(0, formType);
            b2.AddComponentParameter(1, "Model", instance);
            b2.AddComponentParameter(2, "Descriptor", descriptor);
            b2.AddComponentParameter(3, "ShowSubmit", false);
            b2.AddComponentParameter(5, "IsNestedForm", true);
            b2.AddComponentReferenceCapture(4, r => ctx.CaptureNestedRef(field.Name, -1, r));
            b2.CloseComponent();

            // The nested sub-form validates and shows its own field-level errors
            // independently (its own Revalidate/MessagesFor) — this region only ever
            // carries the OUTER field's own top-level message ("Location is
            // required."), never a "Location.Street"-prefixed one.
            if (errors.Count > 0)
            {
                b2.OpenElement(10, "div");
                b2.AddAttribute(11, "role", "alert");
                b2.AddAttribute(12, "class", "dx-field-error");
                for (int i = 0; i < errors.Count; i++)
                {
                    b2.OpenElement(13, "span");
                    b2.SetKey(i);
                    b2.AddContent(14, errors[i]);
                    b2.CloseElement();
                }

                b2.CloseElement();
            }
        }));
        b.CloseComponent();
    }

    // An Array-kind field: a DxFieldList<TItem> — TItem = string for array-of-scalar,
    // or the nested model type for array-of-nested-object (whose rows are literally
    // the Object-kind path above, repeated per element — no third rendering mechanism).
    private static void RenderArray(RenderTreeBuilder b, FormContext ctx, FormFieldInfo field)
    {
        IReadOnlyList<string> errors = ctx.ErrorsFor(field.Name);

        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", errors.Count > 0 ? "dx-field dx-field-invalid" : "dx-field");

        b.OpenElement(2, "span");
        b.AddAttribute(3, "class", "dx-field-label");
        b.AddContent(4, field.Label);
        b.CloseElement();

        if (field.NestedType is not null)
        {
            RenderNestedArray(b, ctx, field);
        }
        else
        {
            RenderScalarArray(b, ctx, field);
        }

        if (errors.Count > 0)
        {
            b.OpenElement(30, "div");
            b.AddAttribute(31, "role", "alert");
            b.AddAttribute(32, "class", "dx-field-error");
            for (int i = 0; i < errors.Count; i++)
            {
                b.OpenElement(33, "span");
                b.SetKey(i);
                b.AddContent(34, errors[i]);
                b.CloseElement();
            }

            b.CloseElement();
        }

        b.CloseElement();
    }

    private static void RenderScalarArray(RenderTreeBuilder b, FormContext ctx, FormFieldInfo field)
    {
        IReadOnlyList<string> items = ctx.GetArrayStrings(field.Name);
        FormFieldKind elementKind = field.ArrayElementKind ?? FormFieldKind.Text;

        b.OpenComponent<DxFieldList<string>>(0);
        b.AddComponentParameter(1, "Items", items);
        b.AddComponentParameter(2, "ItemsChanged", EventCallback.Factory.Create<IReadOnlyList<string>>(
            ctx.Receiver, updated => ctx.SetArrayStringsAsync(field.Name, updated)));
        b.AddComponentParameter(3, "NewItem", (Func<string>)(() => (string)ctx.NewArrayElement(field.Name)));
        b.AddComponentParameter(4, "ItemTemplate", (RenderFragment<CollectionItemContext<string>>)(itemCtx => inner =>
        {
            RenderElementInput(
                inner, ctx.Receiver, elementKind, itemCtx.Item,
                EventCallback.Factory.Create<string>(ctx.Receiver, v => itemCtx.SetItemAsync(v)),
                field.Choices, $"{field.Label} item {itemCtx.Index + 1}");
        }));
        b.CloseComponent();
    }

    private static void RenderNestedArray(RenderTreeBuilder b, FormContext ctx, FormFieldInfo field)
    {
        IReadOnlyList<object> items = ctx.GetArrayInstances(field.Name);
        Type formType = typeof(DxForm<>).MakeGenericType(field.NestedType!);
        IFormModelUntyped? elementDescriptor = ctx.ArrayElementDescriptorFor(field.Name);

        b.OpenComponent<DxFieldList<object>>(0);
        b.AddComponentParameter(1, "Items", items);
        b.AddComponentParameter(2, "ItemsChanged", EventCallback.Factory.Create<IReadOnlyList<object>>(
            ctx.Receiver, updated => ctx.SetArrayInstancesAsync(field.Name, updated)));
        b.AddComponentParameter(3, "NewItem", (Func<object>)(() => ctx.NewArrayElement(field.Name)));
        b.AddComponentParameter(4, "ItemTemplate", (RenderFragment<CollectionItemContext<object>>)(itemCtx => inner =>
        {
            inner.OpenComponent(0, formType);
            inner.AddComponentParameter(1, "Model", itemCtx.Item);
            inner.AddComponentParameter(2, "Descriptor", elementDescriptor);
            inner.AddComponentParameter(3, "ShowSubmit", false);
            inner.AddComponentParameter(5, "IsNestedForm", true);
            inner.AddComponentReferenceCapture(4, r => ctx.CaptureNestedRef(field.Name, itemCtx.Index, r));
            inner.CloseComponent();
        }));
        b.CloseComponent();
    }

    // A lighter-weight sibling of RenderInput for an array row: an array element has
    // no Placeholder/MaxLength/Pattern/Min/Max metadata of its own in this v1 pass
    // (per-item constraint validation is a stated scope cut — see ADR 0019), so this
    // deliberately doesn't reuse RenderInput/AddCommon's full FormFieldInfo-shaped
    // logic; it renders just the control appropriate to the element's own Kind.
    private static void RenderElementInput(
        RenderTreeBuilder b, object receiver, FormFieldKind kind, string value, EventCallback<string> changed,
        IReadOnlyList<string>? choices, string ariaLabel)
    {
        EventCallback<ChangeEventArgs> onText = EventCallback.Factory.Create<ChangeEventArgs>(
            receiver, e => changed.InvokeAsync(e.Value as string ?? string.Empty));

        switch (kind)
        {
            case FormFieldKind.Multiline:
                b.OpenElement(0, "textarea");
                b.AddAttribute(1, "class", "dx-input dx-textarea");
                b.AddAttribute(2, "rows", "2");
                b.AddAttribute(3, "aria-label", ariaLabel);
                b.AddAttribute(4, "value", value);
                b.AddAttribute(5, "oninput", onText);
                b.CloseElement();
                break;

            case FormFieldKind.Bool:
                b.OpenElement(0, "input");
                b.AddAttribute(1, "class", "dx-checkbox");
                b.AddAttribute(2, "type", "checkbox");
                b.AddAttribute(3, "aria-label", ariaLabel);
                b.AddAttribute(4, "checked", value is "true" or "True");
                b.AddAttribute(5, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                    receiver, e => changed.InvokeAsync(e.Value is true ? "true" : "false")));
                b.CloseElement();
                break;

            case FormFieldKind.Enum:
                b.OpenElement(0, "select");
                b.AddAttribute(1, "class", "dx-input dx-select-native");
                b.AddAttribute(2, "value", value);
                b.AddAttribute(3, "onchange", onText);
                b.AddAttribute(4, "aria-label", ariaLabel);
                if (choices is not null)
                {
                    for (int i = 0; i < choices.Count; i++)
                    {
                        b.OpenElement(5, "option");
                        b.SetKey(choices[i]);
                        b.AddAttribute(6, "value", choices[i]);
                        b.AddContent(7, choices[i]);
                        b.CloseElement();
                    }
                }

                b.CloseElement();
                break;

            case FormFieldKind.Integer:
            case FormFieldKind.Number:
                b.OpenElement(0, "input");
                b.AddAttribute(1, "class", "dx-input");
                b.AddAttribute(2, "type", "number");
                b.AddAttribute(3, "step", kind == FormFieldKind.Integer ? "1" : "any");
                b.AddAttribute(4, "aria-label", ariaLabel);
                b.AddAttribute(5, "value", value);
                b.AddAttribute(6, "oninput", onText);
                b.CloseElement();
                break;

            case FormFieldKind.Date:
                b.OpenElement(0, "input");
                b.AddAttribute(1, "class", "dx-input");
                b.AddAttribute(2, "type", "date");
                b.AddAttribute(3, "aria-label", ariaLabel);
                b.AddAttribute(4, "value", value);
                b.AddAttribute(5, "oninput", onText);
                b.CloseElement();
                break;

            default:
                b.OpenElement(0, "input");
                b.AddAttribute(1, "class", "dx-input");
                b.AddAttribute(2, "type", "text");
                b.AddAttribute(3, "aria-label", ariaLabel);
                b.AddAttribute(4, "value", value);
                b.AddAttribute(5, "oninput", onText);
                b.CloseElement();
                break;
        }
    }

    // Marks an input as invalid and points it at its error region. A no-op when valid.
    private static void AddValidationState(RenderTreeBuilder b, string? errorId)
    {
        if (errorId is null)
        {
            return;
        }

        b.AddAttribute(80, "aria-invalid", "true");
        b.AddAttribute(81, "aria-describedby", errorId);
    }

    private static void RenderInput(
        RenderTreeBuilder b, object receiver, FormFieldInfo field, string value, EventCallback<string> changed,
        string? errorId)
    {
        EventCallback<ChangeEventArgs> onText = EventCallback.Factory.Create<ChangeEventArgs>(
            receiver, e => changed.InvokeAsync(e.Value as string ?? string.Empty));

        switch (field.Kind)
        {
            case FormFieldKind.Multiline:
                b.OpenElement(30, "textarea");
                b.AddAttribute(31, "class", "dx-input dx-textarea");
                b.AddAttribute(32, "rows", "3");
                AddCommon(b, field, errorId);
                b.AddAttribute(38, "value", value);
                b.AddAttribute(39, "oninput", onText);
                b.CloseElement();
                break;

            case FormFieldKind.Bool:
                b.OpenElement(30, "input");
                b.AddAttribute(31, "class", "dx-checkbox");
                b.AddAttribute(32, "type", "checkbox");
                b.AddAttribute(40, "aria-label", field.Label);
                b.AddAttribute(33, "checked", value is "true" or "True");
                b.AddAttribute(34, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                    receiver, e => changed.InvokeAsync(e.Value is true ? "true" : "false")));
                AddValidationState(b, errorId);
                b.CloseElement();
                break;

            case FormFieldKind.Enum:
                b.OpenElement(30, "select");
                b.AddAttribute(31, "class", "dx-input dx-select-native");
                b.AddAttribute(32, "value", value);
                b.AddAttribute(33, "onchange", onText);
                b.AddAttribute(37, "aria-label", field.Label);
                AddValidationState(b, errorId);
                if (field.Choices is { } choices)
                {
                    for (int i = 0; i < choices.Count; i++)
                    {
                        b.OpenElement(34, "option");
                        b.SetKey(choices[i]);
                        b.AddAttribute(35, "value", choices[i]);
                        b.AddContent(36, choices[i]);
                        b.CloseElement();
                    }
                }

                b.CloseElement();
                break;

            case FormFieldKind.Integer:
            case FormFieldKind.Number:
                b.OpenElement(30, "input");
                b.AddAttribute(31, "class", "dx-input");
                b.AddAttribute(32, "type", "number");
                b.AddAttribute(33, "step", field.Kind == FormFieldKind.Integer ? "1" : "any");
                if (field.Min is { } min)
                {
                    b.AddAttribute(34, "min", min);
                }

                if (field.Max is { } max)
                {
                    b.AddAttribute(35, "max", max);
                }

                AddCommon(b, field, errorId);
                b.AddAttribute(38, "value", value);
                b.AddAttribute(39, "oninput", onText);
                b.CloseElement();
                break;

            case FormFieldKind.Date:
                b.OpenElement(30, "input");
                b.AddAttribute(31, "class", "dx-input");
                b.AddAttribute(32, "type", "date");
                b.AddAttribute(40, "aria-label", field.Label);
                AddValidationState(b, errorId);
                b.AddAttribute(38, "value", value);
                b.AddAttribute(39, "oninput", onText);
                b.CloseElement();
                break;

            default:
                b.OpenElement(30, "input");
                b.AddAttribute(31, "class", "dx-input");
                b.AddAttribute(32, "type", "text");
                AddCommon(b, field, errorId);
                b.AddAttribute(38, "value", value);
                b.AddAttribute(39, "oninput", onText);
                b.CloseElement();
                break;
        }
    }

    private static void AddCommon(RenderTreeBuilder b, FormFieldInfo field, string? errorId)
    {
        // The visible <label> is not associated by id, so give the control its own accessible
        // name. Without this, screen readers (and axe) see an unlabeled input.
        b.AddAttribute(40, "aria-label", field.Label);
        if (!string.IsNullOrEmpty(field.Placeholder))
        {
            b.AddAttribute(36, "placeholder", field.Placeholder);
        }

        if (field.MaxLength is { } maxLength)
        {
            b.AddAttribute(37, "maxlength", maxLength);
        }

        AddValidationState(b, errorId);
    }
}
