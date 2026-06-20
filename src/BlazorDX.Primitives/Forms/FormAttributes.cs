namespace BlazorDX.Primitives.Forms;

/// <summary>
/// Marks a class as a BlazorDX form model. The <c>BlazorDX.SourceGen</c> generator
/// emits a strongly-typed <see cref="IFormModel{TModel}"/> for it — field metadata,
/// validation, and zero-reflection get/set — which both <c>DxForm</c> renders and
/// <see cref="FormTool"/> projects into an AI tool (JSON-Schema) definition. One
/// model, two faces: a UI a person fills, and a tool an AI calls.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DxFormModelAttribute : Attribute
{
    /// <summary>Tool name exposed to an AI host (defaults to the type name). Use snake_case for function-calling hosts.</summary>
    public string? Name { get; set; }

    /// <summary>What the form/tool does — shown to the AI so it knows when to call it.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Declares a property as a form field. The label, description, and constraints feed
/// both the rendered input (and its validation) and the generated AI tool schema —
/// the <see cref="Description"/> in particular is what an AI reads to fill the field.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DxFieldAttribute : Attribute
{
    public DxFieldAttribute(string? label = null)
    {
        Label = label;
    }

    /// <summary>Human label (defaults to the property name).</summary>
    public string? Label { get; }

    /// <summary>Field description — also the AI-facing parameter description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether a value is required.</summary>
    public bool Required { get; set; }

    /// <summary>Display/declaration order; lower comes first.</summary>
    public int Order { get; set; }

    /// <summary>Minimum for numeric fields (NaN = unset).</summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>Maximum for numeric fields (NaN = unset).</summary>
    public double Max { get; set; } = double.NaN;

    /// <summary>Maximum length for text fields (0 = unset).</summary>
    public int MaxLength { get; set; }

    /// <summary>Validation/format regex for text fields.</summary>
    public string? Pattern { get; set; }

    /// <summary>Placeholder text for the rendered input.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Render as a multi-line text area.</summary>
    public bool Multiline { get; set; }

    /// <summary>
    /// Hide this field from the AI tool surface: it is omitted from the generated JSON-Schema
    /// and <see cref="FormTool.ApplyArguments{TModel}"/> refuses to set it from AI arguments —
    /// yet a human still sees and edits it in <c>DxForm</c>. Use for PII / secrets the model
    /// should neither see nor write. Equivalent to applying <see cref="AiHiddenAttribute"/>.
    /// </summary>
    public bool Sensitive { get; set; }
}

/// <summary>
/// Marks a form-field property as off-limits to AI: excluded from the generated tool schema
/// and never settable via <see cref="FormTool.ApplyArguments{TModel}"/>, while remaining
/// fully editable by a human in <c>DxForm</c>. The same effect as <c>[DxField(Sensitive = true)]</c>,
/// but it composes with plain DataAnnotations-annotated models too.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiHiddenAttribute : Attribute
{
}
