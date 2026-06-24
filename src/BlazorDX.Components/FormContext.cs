using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

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
