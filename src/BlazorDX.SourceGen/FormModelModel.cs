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
    bool Sensitive);          // hidden from the AI tool surface (schema + ApplyArguments)

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

    public static FormModelDef Build(INamedTypeSymbol type, AttributeData modelAttribute)
    {
        string? ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();
        string accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        string toolName = ReadNamedString(modelAttribute, "Name") ?? ToSnakeCase(type.Name);
        string? toolDescription = ReadNamedString(modelAttribute, "Description");
        bool validatable = type.AllInterfaces.Any(i => i.ToDisplayString() == ValidatableInterface);

        return new FormModelDef(
            ns, type.Name, accessibility, toolName, toolDescription, ReadFields(type), validatable);
    }

    private static ImmutableArray<FormFieldDef> ReadFields(INamedTypeSymbol type)
    {
        ImmutableArray<FormFieldDef>.Builder builder = ImmutableArray.CreateBuilder<FormFieldDef>();
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

            ITypeSymbol underlying = Underlying(property.Type, out bool isNullableValue);
            bool isString = property.Type.SpecialType == SpecialType.System_String;

            string label = FieldLabel(field) ?? DisplayName(property) ?? property.Name;
            bool multiline = ReadNamedBool(field, "Multiline") || IsMultilineDataType(property);
            (double? rangeMin, double? rangeMax) = ReadRange(property);

            ImmutableArray<string> choices = underlying.TypeKind == TypeKind.Enum
                ? underlying.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToImmutableArray()
                : ImmutableArray<string>.Empty;

            builder.Add(new FormFieldDef(
                property.Name,
                label,
                ReadNamedString(field, "Description") ?? DisplayProp(property, "Description"),
                Kind(underlying, isString, multiline),
                ReadNamedBool(field, "Required") || Has(property, RequiredAttr),
                ReadNamedInt(field, "Order") ?? DisplayOrder(property) ?? 0,
                ReadNamedDouble(field, "Min") ?? rangeMin,
                ReadNamedDouble(field, "Max") ?? rangeMax,
                ReadNamedInt(field, "MaxLength") is { } ml and > 0 ? ml : ReadMaxLength(property),
                ReadNamedString(field, "Pattern") ?? ReadPattern(property),
                ReadNamedString(field, "Placeholder") ?? DisplayProp(property, "Prompt"),
                isString,
                isNullableValue,
                underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                choices,
                ReadNamedBool(field, "Sensitive") || Has(property, AiHiddenAttribute)));
        }

        builder.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        return builder.ToImmutable();
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
