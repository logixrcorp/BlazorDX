using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorDX.SourceGen;

/// <summary>One <c>[DxField]</c> property discovered on a form model.</summary>
internal sealed record FormFieldDef(
    string PropertyName,
    string Label,
    string? Description,
    string Kind,              // FormFieldKind member name
    bool Required,
    int Order,
    double? Min,
    double? Max,
    int? MaxLength,
    string? Pattern,
    string? Placeholder,
    bool IsString,
    bool IsNullableValue,
    string UnderlyingFqn,     // non-nullable type FQN, for typed parse
    ImmutableArray<string> Choices,
    bool Sensitive,           // hidden from the AI tool surface (schema + ApplyArguments)
    string? DependsOn,        // PropertyName of the controlling field; null = always active
    string? DependsOnValue,
    string DependsOnOperator, // FormFieldDependsOnOperator member name
    // Object: the nested type's FQN. Array-of-nested: the ELEMENT type's FQN. Null
    // for every scalar field. For an Array field, null in BOTH this and
    // ArrayElementKind means the element type was neither [DxFormModel]-tagged nor a
    // recognized scalar -- FormModelDiagnostics reports DX2005 for that state.
    string? NestedTypeFqn = null,
    // Array-of-scalar only: the element's own scalar Kind (FormFieldKind member name).
    string? ArrayElementKind = null,
    // The FQN of the nested/element type's OWN generated "{Type}FormModel" descriptor
    // class (e.g. "global::Ns.AddressFormModel") -- set whenever NestedTypeFqn is.
    string? NestedDescriptorFqn = null);

/// <summary>Everything the form emitter needs about a <c>[DxFormModel]</c> type.</summary>
internal sealed record FormModelDef(
    string? Namespace,
    string TypeName,
    string Accessibility,
    string ToolName,
    string? ToolDescription,
    ImmutableArray<FormFieldDef> Fields,
    bool Validatable);   // implements IValidatableObject → run its cross-field Validate too

/// <summary>
/// Reads the <c>[DxFormModel]</c> / <c>[DxField]</c> attributes off a symbol — and, for
/// teams that already annotate their models, the standard
/// <c>System.ComponentModel.DataAnnotations</c> attributes (<c>[Required]</c>,
/// <c>[StringLength]</c>/<c>[MaxLength]</c>, <c>[Range]</c>, <c>[RegularExpression]</c>,
/// <c>[EmailAddress]</c>, <c>[Display]</c>/<c>[DisplayName]</c>, <c>[DataType]</c>). So an
/// existing DataAnnotations model becomes a BlazorDX form + AI tool with one class-level
/// attribute and zero reflection. A model implementing <c>IValidatableObject</c> also gets
/// its cross-field <c>Validate</c> run.
/// </summary>
internal static class FormModelAnalysis
{
    private const string FieldAttribute = "BlazorDX.Primitives.Forms.DxFieldAttribute";
    private const string AiHiddenAttribute = "BlazorDX.Primitives.Forms.AiHiddenAttribute";
    private const string ModelAttribute = "BlazorDX.Primitives.Forms.DxFormModelAttribute";

    private const string DaNs = "System.ComponentModel.DataAnnotations.";
    private const string RequiredAttr = DaNs + "RequiredAttribute";
    private const string StringLengthAttr = DaNs + "StringLengthAttribute";
    private const string MaxLengthAttr = DaNs + "MaxLengthAttribute";
    private const string RangeAttr = DaNs + "RangeAttribute";
    private const string RegexAttr = DaNs + "RegularExpressionAttribute";
    private const string EmailAttr = DaNs + "EmailAddressAttribute";
    private const string DisplayAttr = DaNs + "DisplayAttribute";
    private const string DataTypeAttr = DaNs + "DataTypeAttribute";
    private const string DisplayNameAttr = "System.ComponentModel.DisplayNameAttribute";
    private const string ValidatableInterface = "System.ComponentModel.DataAnnotations.IValidatableObject";

    // A pragmatic email shape — also flows into the AI tool's JSON-Schema "pattern".
    private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public static (FormModelDef Model, ImmutableArray<Diagnostic> Diagnostics) Build(
        INamedTypeSymbol type, AttributeData modelAttribute)
    {
        string? ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();
        string accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        string toolName = ReadNamedString(modelAttribute, "Name") ?? ToSnakeCase(type.Name);
        string? toolDescription = ReadNamedString(modelAttribute, "Description");
        bool validatable = type.AllInterfaces.Any(i => i.ToDisplayString() == ValidatableInterface);

        (ImmutableArray<FormFieldDef> fields0, ImmutableArray<(string PropertyName, Diagnostic Diagnostic)> shapeDiagnostics) = ReadFields(type);
        ImmutableArray<FormFieldDef> fields = fields0;

        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        HashSet<string> badShape = new();
        foreach ((string propertyName, Diagnostic diagnostic) in shapeDiagnostics)
        {
            // DX2008/DX2009: an Object/array-of-nested field referencing a type that
            // has no discovered fields, or no accessible parameterless constructor.
            diagnostics.Add(diagnostic);
            badShape.Add(propertyName);
        }

        // Validate DependsOn references (DX2001-DX2004, DX2007) and array-element
        // shape (DX2005) after the full field set is known — each rule needs to see
        // every field, not just the one being checked.
        HashSet<string> badDependsOn = new();
        foreach ((FormFieldDef field, DiagnosticDescriptor descriptor) in FormModelDiagnostics.Validate(fields))
        {
            Location location = type.GetMembers(field.PropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault()
                ?.Locations.FirstOrDefault() ?? Location.None;
            diagnostics.Add(Diagnostic.Create(descriptor, location, field.PropertyName, field.DependsOn));

            if (descriptor == FormModelDiagnostics.InvalidCollectionElement)
            {
                badShape.Add(field.PropertyName);
            }
            else
            {
                badDependsOn.Add(field.PropertyName);
            }
        }

        if (badShape.Count > 0)
        {
            // Best-effort: fall a malformed Object/Array field back to a plain Text
            // field so Emit() never has to special-case an invalid shape — the
            // diagnostic above is what the author sees, not a cascade of secondary
            // compiler errors from broken generated code.
            fields = fields
                .Select(f => badShape.Contains(f.PropertyName)
                    ? f with
                    {
                        Kind = "Text", NestedTypeFqn = null, ArrayElementKind = null, NestedDescriptorFqn = null,
                        UnderlyingFqn = "string", IsString = true, Choices = ImmutableArray<string>.Empty,
                    }
                    : f)
                .ToImmutableArray();
        }

        if (badDependsOn.Count > 0)
        {
            // Best-effort: strip the bad DependsOn so the emitted .g.cs stays syntactically
            // valid. The diagnostic above is what the author sees, not a cascade of
            // secondary compiler errors from broken generated code.
            fields = fields
                .Select(f => badDependsOn.Contains(f.PropertyName) ? f with { DependsOn = null } : f)
                .ToImmutableArray();
        }

        FormModelDef model = new(ns, type.Name, accessibility, toolName, toolDescription, fields, validatable);
        return (model, diagnostics.ToImmutable());
    }

    private static (ImmutableArray<FormFieldDef> Fields, ImmutableArray<(string PropertyName, Diagnostic Diagnostic)> ShapeDiagnostics) ReadFields(INamedTypeSymbol type)
    {
        ImmutableArray<FormFieldDef>.Builder builder = ImmutableArray.CreateBuilder<FormFieldDef>();
        ImmutableArray<(string PropertyName, Diagnostic Diagnostic)>.Builder shapeDiagnostics =
            ImmutableArray.CreateBuilder<(string, Diagnostic)>();
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IPropertySymbol property || property.SetMethod is null)
            {
                continue;
            }

            AttributeData? field = Find(property, FieldAttribute);

            // A property is a form field if it carries [DxField] or any recognized
            // DataAnnotations attribute — otherwise it's left out (explicit over implicit).
            if (field is null && !HasDataAnnotations(property))
            {
                continue;
            }

            string label = FieldLabel(field) ?? DisplayName(property) ?? property.Name;
            string? description = ReadNamedString(field, "Description") ?? DisplayProp(property, "Description");
            bool required = ReadNamedBool(field, "Required") || Has(property, RequiredAttr);
            int order = ReadNamedInt(field, "Order") ?? DisplayOrder(property) ?? 0;
            bool sensitive = ReadNamedBool(field, "Sensitive") || Has(property, AiHiddenAttribute);
            string? dependsOn = ReadNamedString(field, "DependsOn");
            string? dependsOnValue = ReadNamedString(field, "DependsOnValue");
            string dependsOnOperator = ReadNamedEnumMember(field, "DependsOnOperator") ?? "Equals";

            // ---- List<T>: array of nested [DxFormModel] objects, or array of scalars ----
            // Checked before Underlying()'s Nullable<T> unwrap below: List<T> is a
            // reference type and never goes through that path.
            if (TryGetListElementType(property.Type, out ITypeSymbol? elementType))
            {
                string elementFqn = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string? nestedFqn = null;
                string? arrayElementKind = null;
                ImmutableArray<string> arrayChoices = ImmutableArray<string>.Empty;

                string? nestedDescriptorFqn = null;
                if (HasAttribute(elementType, ModelAttribute))
                {
                    nestedFqn = elementFqn;
                    nestedDescriptorFqn = DescriptorFqn(elementType);
                    CheckNestedTargetShape(property, elementType, shapeDiagnostics);
                }
                else if (TryScalarKind(elementType, out string scalarKind, out ImmutableArray<string> scalarChoices))
                {
                    arrayElementKind = scalarKind;
                    arrayChoices = scalarChoices;
                }
                // else: element is neither [DxFormModel]-tagged nor a recognized scalar.
                // Both nestedFqn/arrayElementKind stay null -- FormModelDiagnostics
                // reports DX2005 for that state.

                builder.Add(new FormFieldDef(
                    property.Name, label, description, "Array", required, order,
                    null, null, null, null, null,
                    false, false, elementFqn, arrayChoices, sensitive,
                    dependsOn, dependsOnValue, dependsOnOperator,
                    NestedTypeFqn: nestedFqn, ArrayElementKind: arrayElementKind, NestedDescriptorFqn: nestedDescriptorFqn));
                continue;
            }

            // ---- Some other collection shape (T[]/IList<T>/etc.) -- always DX2005 ----
            if (IsOtherCollectionShape(property.Type))
            {
                builder.Add(new FormFieldDef(
                    property.Name, label, description, "Array", required, order,
                    null, null, null, null, null,
                    false, false, property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ImmutableArray<string>.Empty, sensitive,
                    dependsOn, dependsOnValue, dependsOnOperator,
                    NestedTypeFqn: null, ArrayElementKind: null));
                continue;
            }

            // ---- Nested [DxFormModel] object ----
            if (HasAttribute(property.Type, ModelAttribute))
            {
                string nestedFqn = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                CheckNestedTargetShape(property, property.Type, shapeDiagnostics);
                builder.Add(new FormFieldDef(
                    property.Name, label, description, "Object", required, order,
                    null, null, null, null, null,
                    false, false, nestedFqn, ImmutableArray<string>.Empty, sensitive,
                    dependsOn, dependsOnValue, dependsOnOperator,
                    NestedTypeFqn: nestedFqn, ArrayElementKind: null, NestedDescriptorFqn: DescriptorFqn(property.Type)));
                continue;
            }

            // ---- Scalar (unchanged) ----
            ITypeSymbol underlying = Underlying(property.Type, out bool isNullableValue);
            bool isString = property.Type.SpecialType == SpecialType.System_String;
            bool multiline = ReadNamedBool(field, "Multiline") || IsMultilineDataType(property);
            (double? rangeMin, double? rangeMax) = ReadRange(property);

            ImmutableArray<string> choices = underlying.TypeKind == TypeKind.Enum
                ? underlying.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToImmutableArray()
                : ImmutableArray<string>.Empty;

            builder.Add(new FormFieldDef(
                property.Name,
                label,
                description,
                Kind(underlying, isString, multiline),
                required,
                order,
                ReadNamedDouble(field, "Min") ?? rangeMin,
                ReadNamedDouble(field, "Max") ?? rangeMax,
                ReadNamedInt(field, "MaxLength") is { } ml and > 0 ? ml : ReadMaxLength(property),
                ReadNamedString(field, "Pattern") ?? ReadPattern(property),
                ReadNamedString(field, "Placeholder") ?? DisplayProp(property, "Prompt"),
                isString,
                isNullableValue,
                underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                choices,
                sensitive,
                dependsOn,
                dependsOnValue,
                dependsOnOperator));
        }

        builder.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        return (builder.ToImmutable(), shapeDiagnostics.ToImmutable());
    }

    // DX2008/DX2009 for an Object/array-of-nested field's referenced type. Both checks
    // are flat, single-level symbol scans (never recurse into the referenced type's
    // OWN Object/Array fields), so they stay safe even across a genuine reference
    // cycle — DX2006's whole-compilation pass is what catches cycles themselves.
    private static void CheckNestedTargetShape(
        IPropertySymbol property, ITypeSymbol target,
        ImmutableArray<(string PropertyName, Diagnostic Diagnostic)>.Builder shapeDiagnostics)
    {
        Location location = property.Locations.FirstOrDefault() ?? Location.None;

        if (!HasAnyFormField(target))
        {
            shapeDiagnostics.Add((property.Name,
                Diagnostic.Create(FormModelDiagnostics.ZeroFieldsTarget, location, property.Name, target.Name)));
        }

        if (!HasAccessibleParameterlessConstructor(target))
        {
            shapeDiagnostics.Add((property.Name,
                Diagnostic.Create(FormModelDiagnostics.NoParameterlessConstructor, location, property.Name, target.Name)));
        }
    }

    private static bool HasAnyFormField(ITypeSymbol type)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IPropertySymbol { SetMethod: not null } property
                && (Find(property, FieldAttribute) is not null || HasDataAnnotations(property)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAccessibleParameterlessConstructor(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        foreach (IMethodSymbol ctor in named.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    // ---- DataAnnotations readers ----

    private static bool HasDataAnnotations(IPropertySymbol property)
    {
        foreach (AttributeData attribute in property.GetAttributes())
        {
            string? name = attribute.AttributeClass?.ToDisplayString();
            if (name is RequiredAttr or StringLengthAttr or MaxLengthAttr or RangeAttr
                or RegexAttr or EmailAttr or DisplayAttr or DataTypeAttr or DisplayNameAttr)
            {
                return true;
            }
        }

        return false;
    }

    private static string? FieldLabel(AttributeData? field) =>
        field is { ConstructorArguments.Length: > 0 } && field.ConstructorArguments[0].Value is string l ? l : null;

    private static string? DisplayName(IPropertySymbol property)
    {
        AttributeData? display = Find(property, DisplayAttr);
        if (display is not null && ReadNamedString(display, "Name") is { } name)
        {
            return name;
        }

        AttributeData? displayName = Find(property, DisplayNameAttr);
        return displayName is { ConstructorArguments.Length: > 0 } && displayName.ConstructorArguments[0].Value is string d
            ? d
            : null;
    }

    private static string? DisplayProp(IPropertySymbol property, string named) =>
        Find(property, DisplayAttr) is { } display ? ReadNamedString(display, named) : null;

    private static int? DisplayOrder(IPropertySymbol property) =>
        Find(property, DisplayAttr) is { } display ? ReadNamedInt(display, "Order") : null;

    private static int? ReadMaxLength(IPropertySymbol property)
    {
        if (Find(property, StringLengthAttr) is { ConstructorArguments.Length: > 0 } sl
            && sl.ConstructorArguments[0].Value is int max and > 0)
        {
            return max;
        }

        if (Find(property, MaxLengthAttr) is { ConstructorArguments.Length: > 0 } ml
            && ml.ConstructorArguments[0].Value is int n and > 0)
        {
            return n;
        }

        return null;
    }

    private static string? ReadPattern(IPropertySymbol property)
    {
        if (Find(property, RegexAttr) is { ConstructorArguments.Length: > 0 } regex
            && regex.ConstructorArguments[0].Value is string pattern)
        {
            return pattern;
        }

        return Has(property, EmailAttr) ? EmailPattern : null;
    }

    private static (double? Min, double? Max) ReadRange(IPropertySymbol property)
    {
        AttributeData? range = Find(property, RangeAttr);
        if (range is { ConstructorArguments.Length: 2 }
            && AsDouble(range.ConstructorArguments[0]) is { } min
            && AsDouble(range.ConstructorArguments[1]) is { } max)
        {
            return (min, max);
        }

        return (null, null);   // the (Type, string, string) overload isn't a numeric range
    }

    private static double? AsDouble(TypedConstant value) => value.Value switch
    {
        int i => i,
        double d => d,
        long l => l,
        _ => null,
    };

    private static bool IsMultilineDataType(IPropertySymbol property)
    {
        // [DataType(DataType.MultilineText)] → MultilineText is enum member value 9.
        return Find(property, DataTypeAttr) is { ConstructorArguments.Length: > 0 } dt
            && dt.ConstructorArguments[0].Value is int kind && kind == 9;
    }

    private static bool Has(IPropertySymbol property, string fqn) => Find(property, fqn) is not null;

    private static string Kind(ITypeSymbol underlying, bool isString, bool multiline)
    {
        if (isString)
        {
            return multiline ? "Multiline" : "Text";
        }

        if (underlying.TypeKind == TypeKind.Enum)
        {
            return "Enum";
        }

        if (underlying.SpecialType == SpecialType.System_Boolean)
        {
            return "Bool";
        }

        if (IsInteger(underlying))
        {
            return "Integer";
        }

        if (IsFloating(underlying))
        {
            return "Number";
        }

        string name = underlying.ToDisplayString();
        if (name is "System.DateTime" or "System.DateOnly" or "System.DateTimeOffset")
        {
            return "Date";
        }

        return "Text";
    }

    private static bool IsInteger(ITypeSymbol t) => t.SpecialType
        is SpecialType.System_Byte or SpecialType.System_SByte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static bool IsFloating(ITypeSymbol t) => t.SpecialType
        is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    // Recognizes exactly List<T> (not T[]/IList<T>/ICollection<T>/etc. — see
    // IsOtherCollectionShape) so array fields support the simple "replace the whole
    // collection" mutation semantics ApplyArguments/rendering rely on.
    private static bool TryGetListElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol { Name: "List", TypeArguments.Length: 1 } named
            && named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    // A property typed as some other recognizable collection shape — reported as
    // DX2005 rather than silently misclassified as a scalar Text field.
    private static bool IsOtherCollectionShape(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        return type is INamedTypeSymbol { IsGenericType: true } named
            && named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
            && named.Name is "IList" or "ICollection" or "IReadOnlyList" or "IReadOnlyCollection"
                or "IEnumerable" or "ISet" or "HashSet" or "SortedSet" or "LinkedList" or "Queue" or "Stack";
    }

    // The FQN of a [DxFormModel] type's own generated descriptor class, matching
    // FormModelGenerator.Emit's naming convention ("{TypeName}FormModel" in the same
    // namespace). Computed from the symbol directly rather than string-split from a
    // FQN, since a nested/array-element type's own generator invocation runs
    // independently and this reference is resolved only at the compiler's final emit
    // pass (see the ADR/design notes) -- no dependency on that invocation's output.
    private static string DescriptorFqn(ITypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? $"global::{type.Name}FormModel"
            : $"global::{type.ContainingNamespace.ToDisplayString()}.{type.Name}FormModel";

    private static bool HasAttribute(ITypeSymbol type, string fqn)
    {
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == fqn)
            {
                return true;
            }
        }

        return false;
    }

    // Same recognized-scalar categories as Kind()/Underlying(), but strict: returns
    // false instead of defaulting to "Text" for anything unrecognized, since a List<T>
    // element with no clear scalar shape and no [DxFormModel] tag is a real error
    // (DX2005), not something to render as plain text.
    private static bool TryScalarKind(ITypeSymbol type, out string kind, out ImmutableArray<string> choices)
    {
        ITypeSymbol underlying = Underlying(type, out _);
        choices = ImmutableArray<string>.Empty;

        if (underlying.SpecialType == SpecialType.System_String)
        {
            kind = "Text";
            return true;
        }

        if (underlying.TypeKind == TypeKind.Enum)
        {
            kind = "Enum";
            choices = underlying.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToImmutableArray();
            return true;
        }

        if (underlying.SpecialType == SpecialType.System_Boolean)
        {
            kind = "Bool";
            return true;
        }

        if (IsInteger(underlying))
        {
            kind = "Integer";
            return true;
        }

        if (IsFloating(underlying))
        {
            kind = "Number";
            return true;
        }

        string name = underlying.ToDisplayString();
        if (name is "System.DateTime" or "System.DateOnly" or "System.DateTimeOffset")
        {
            kind = "Date";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type, out bool isNullableValue)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            isNullableValue = true;
            return nullable.TypeArguments[0];
        }

        isNullableValue = false;
        return type;
    }

    private static AttributeData? Find(ISymbol symbol, string fqn)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == fqn)
            {
                return attribute;
            }
        }

        return null;
    }

    private static string? ReadNamedString(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is string s)
            {
                return s;
            }
        }

        return null;
    }

    private static bool ReadNamedBool(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return false;
        }

        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is bool b)
            {
                return b;
            }
        }

        return false;
    }

    private static int? ReadNamedInt(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is int i)
            {
                return i;
            }
        }

        return null;
    }

    // An enum-typed named argument's TypedConstant.Value is the boxed underlying value
    // (e.g. an int for a plain enum) -- map it back to the member name by matching
    // against the enum type's own const fields, same technique ReadFields already uses
    // to extract an enum's Choices list.
    private static string? ReadNamedEnumMember(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key != name || arg.Value.Type is not { TypeKind: TypeKind.Enum } enumType)
            {
                continue;
            }

            foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst))
            {
                if (Equals(member.ConstantValue, arg.Value.Value))
                {
                    return member.Name;
                }
            }
        }

        return null;
    }

    private static double? ReadNamedDouble(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is double d && !double.IsNaN(d))
            {
                return d;
            }
        }

        return null;
    }

    private static string ToSnakeCase(string name)
    {
        System.Text.StringBuilder sb = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
