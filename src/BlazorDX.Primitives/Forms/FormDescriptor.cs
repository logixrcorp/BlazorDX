namespace BlazorDX.Primitives.Forms;

/// <summary>How a field is edited and typed — drives both the rendered input and the JSON-Schema type.</summary>
public enum FormFieldKind
{
    /// <summary>Single-line text.</summary>
    Text,

    /// <summary>Multi-line text.</summary>
    Multiline,

    /// <summary>Whole number.</summary>
    Integer,

    /// <summary>Decimal number.</summary>
    Number,

    /// <summary>Boolean (checkbox / true|false).</summary>
    Bool,

    /// <summary>Date (ISO-8601 string).</summary>
    Date,

    /// <summary>One of a fixed set (<see cref="FormFieldInfo.Choices"/>).</summary>
    Enum,

    /// <summary>A nested <c>[DxFormModel]</c>-tagged object (<see cref="FormFieldInfo.NestedType"/>).</summary>
    Object,

    /// <summary>A <c>List&lt;T&gt;</c> of scalars (<see cref="FormFieldInfo.ArrayElementKind"/>) or of a nested <c>[DxFormModel]</c>-tagged type (<see cref="FormFieldInfo.NestedType"/>).</summary>
    Array,
}

/// <summary>
/// How a conditional field's <see cref="FormFieldInfo.DependsOn"/> value is compared
/// against <see cref="FormFieldInfo.DependsOnValue"/> to decide whether the field is
/// currently active. Comparison is always over the invariant-string form
/// <see cref="IFormModel{TModel}.GetString"/> already produces for every field kind
/// (bool → "True"/"False", enum → member name, ...) — no per-kind comparison logic.
/// </summary>
public enum FormFieldDependsOnOperator
{
    /// <summary>The dependency's current value equals <see cref="FormFieldInfo.DependsOnValue"/> (case-insensitive).</summary>
    Equals,

    /// <summary>The dependency's current value does not equal <see cref="FormFieldInfo.DependsOnValue"/> (case-insensitive).</summary>
    NotEquals,

    /// <summary>The dependency's current value is non-empty/non-whitespace. <see cref="FormFieldInfo.DependsOnValue"/> is ignored.</summary>
    NotEmpty,
}

/// <summary>
/// Static metadata for one field — everything <c>DxForm</c> needs to render and
/// validate it, and everything <see cref="FormTool"/> needs to describe it to an AI.
/// Generated, never reflected.
/// </summary>
public sealed record FormFieldInfo(
    string Name,
    string Label,
    string? Description,
    FormFieldKind Kind,
    bool Required,
    double? Min,
    double? Max,
    int? MaxLength,
    string? Pattern,
    string? Placeholder,
    IReadOnlyList<string>? Choices,
    bool Sensitive = false,   // hidden from the AI tool surface (schema + ApplyArguments), still human-editable
    // Conditional fields: this field renders / validates / is AI-settable only while
    // FormFieldActivity.IsActive(model, instance, this) holds. DependsOn is null for
    // every unconditional field (the common case) — see docs/adr/0018-conditional-form-fields.md.
    string? DependsOn = null,
    string? DependsOnValue = null,
    FormFieldDependsOnOperator DependsOnOperator = FormFieldDependsOnOperator.Equals,
    // Object: the nested type. Array-of-nested: the ELEMENT type. Null for every
    // scalar field and for an array-of-scalar field.
    Type? NestedType = null,
    // Array-of-scalar only: the element's own scalar Kind. Null otherwise. Choices is
    // reused (not duplicated) for an array-of-enum's element choices.
    FormFieldKind? ArrayElementKind = null);

/// <summary>A single validation failure: which field, and why.</summary>
public sealed record FormValidationError(string Field, string Message);

/// <summary>
/// A reflection-free description of a <c>[DxFormModel]</c> type, emitted by
/// <c>BlazorDX.SourceGen</c>. It is the single source of truth shared by the rendered
/// form and the AI-tool projection (<see cref="FormTool"/>).
/// </summary>
/// <typeparam name="TModel">The annotated model type.</typeparam>
public interface IFormModel<TModel> : IFormModelUntyped
{
    /// <summary>Tool name for AI hosts (snake_case), from <c>[DxFormModel(Name=...)]</c> or the type name.</summary>
    string ToolName { get; }

    /// <summary>What the form/tool does, for the AI to decide when to call it.</summary>
    string? ToolDescription { get; }

    /// <summary>The fields, in declared order.</summary>
    IReadOnlyList<FormFieldInfo> Fields { get; }

    /// <summary>Reads a field as an invariant string (for binding / serialization).</summary>
    string GetString(TModel model, string field);

    /// <summary>Writes a field from an invariant string, parsing to the property's type. Bad input is ignored.</summary>
    void SetString(TModel model, string field, string value);

    /// <summary>Validates the model against the declared field constraints.</summary>
    IReadOnlyList<FormValidationError> Validate(TModel model);
}
