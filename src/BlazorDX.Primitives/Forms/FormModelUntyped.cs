namespace BlazorDX.Primitives.Forms;

/// <summary>
/// The non-generic face of <see cref="IFormModel{TModel}"/>. It exists so
/// infrastructure code (<c>DxForm</c>'s nested-field rendering, <c>FormTool</c>'s
/// schema builder and argument application) can recurse into a nested
/// <c>[DxFormModel]</c> type's own generated descriptor without being generic over
/// that type. A generated implementation stays fully typed internally (casts the
/// boxed <c>object model</c> back to its own model type) and only boxes at this
/// interface boundary — an ordinary upcast, not reflection.
///
/// Every "nested"/"array" member below has a default body so a scalar-only model —
/// generated or hand-written — is completely unaffected by this addition: it simply
/// never overrides them.
/// </summary>
public interface IFormModelUntyped
{
    /// <summary>Tool name for AI hosts (snake_case), from <c>[DxFormModel(Name=...)]</c> or the type name.</summary>
    string ToolName { get; }

    /// <summary>What the form/tool does, for the AI to decide when to call it.</summary>
    string? ToolDescription { get; }

    /// <summary>The fields, in declared order.</summary>
    IReadOnlyList<FormFieldInfo> Fields { get; }

    /// <summary>Reads a field as an invariant string (for binding / serialization).</summary>
    string GetString(object model, string field);

    /// <summary>Writes a field from an invariant string, parsing to the property's type. Bad input is ignored.</summary>
    void SetString(object model, string field, string value);

    /// <summary>Validates the model against the declared field constraints.</summary>
    IReadOnlyList<FormValidationError> Validate(object model);

    /// <summary>Reads an <see cref="FormFieldKind.Object"/> field's current nested instance (may be null).</summary>
    object? GetNestedInstance(object model, string field) => null;

    /// <summary>Attaches a nested instance to an <see cref="FormFieldKind.Object"/> field.</summary>
    void SetNestedInstance(object model, string field, object? instance) { }

    /// <summary>The generated descriptor for an <see cref="FormFieldKind.Object"/> field's nested type.</summary>
    IFormModelUntyped? GetNestedDescriptor(string field) => null;

    /// <summary>Constructs a new, blank instance for a currently-null <see cref="FormFieldKind.Object"/> field (<c>new TNested()</c>).</summary>
    object NewNestedInstance(string field) => throw new NotSupportedException();

    /// <summary>Reads an array-of-scalar field as its invariant-string elements.</summary>
    IReadOnlyList<string> GetArrayStrings(object model, string field) => Array.Empty<string>();

    /// <summary>Replaces an array-of-scalar field's elements.</summary>
    void SetArrayStrings(object model, string field, IReadOnlyList<string> items) { }

    /// <summary>Reads an array-of-nested-object field's current elements.</summary>
    IReadOnlyList<object> GetArrayInstances(object model, string field) => Array.Empty<object>();

    /// <summary>Replaces an array-of-nested-object field's elements.</summary>
    void SetArrayInstances(object model, string field, IReadOnlyList<object> items) { }

    /// <summary>The generated descriptor for an array-of-nested-object field's element type.</summary>
    IFormModelUntyped? GetArrayElementDescriptor(string field) => null;

    /// <summary>Constructs a new, blank element for an array field (nested: <c>new TElement()</c>; scalar: its empty invariant-string form).</summary>
    object NewArrayElement(string field) => throw new NotSupportedException();
}
