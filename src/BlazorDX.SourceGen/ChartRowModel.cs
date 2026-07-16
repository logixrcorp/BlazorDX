using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace BlazorDX.SourceGen;

/// <summary>
/// One <c>[ChartValue]</c> mapping discovered on a <c>[ChartRow]</c> type: which
/// <see cref="global::BlazorDX.Components.ChartPoint"/> field a property feeds.
/// </summary>
/// <param name="Field">The target field's name — matches a <c>ChartField</c> enum member exactly.</param>
/// <param name="PropertyName">The source property on the row type.</param>
internal sealed record ChartFieldMapping(string Field, string PropertyName);

/// <summary>Everything the emitter needs about a <c>[ChartRow]</c> type.</summary>
internal sealed record ChartRowModel(
    string? Namespace,
    string TypeName,
    string Accessibility,
    ImmutableArray<ChartFieldMapping> Mappings);

/// <summary>Helpers for reading the row/value attributes off a symbol.</summary>
internal static class ChartRowAnalysis
{
    private const string ValueAttribute = "BlazorDX.Components.ChartValueAttribute";

    // ChartPoint.X/Y/Y2/Y3/Y4 require a numeric-convertible source property; Category/Series/Color
    // accept any type (stringified via Convert.ToString at the call site).
    private static readonly string[] NumericFields = { "X", "Y", "Y2", "Y3", "Y4" };

    public static ChartRowModel Build(INamedTypeSymbol rowType)
    {
        string? containingNamespace = rowType.ContainingNamespace.IsGlobalNamespace
            ? null
            : rowType.ContainingNamespace.ToDisplayString();
        string accessibility = rowType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        return new ChartRowModel(containingNamespace, rowType.Name, accessibility, ReadMappings(rowType));
    }

    private static ImmutableArray<ChartFieldMapping> ReadMappings(INamedTypeSymbol rowType)
    {
        // A repeated [ChartValue] for the same field overwrites the earlier one (declaration
        // order), so the result is always at most one mapping per ChartPoint field.
        Dictionary<string, ChartFieldMapping> byField = new(StringComparer.Ordinal);

        foreach (ISymbol member in rowType.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            AttributeData? attribute = FindValueAttribute(property);
            if (attribute is null)
            {
                continue;
            }

            string? field = ReadField(attribute);
            if (field is null)
            {
                continue;
            }

            // A numeric field on a non-numeric property is silently not mapped (the ChartPoint
            // field keeps its default) — the same degrade-gracefully policy [GridColumn] uses for
            // a non-numeric column, rather than a hard compile error.
            bool numericRequired = Array.IndexOf(NumericFields, field) >= 0;
            if (numericRequired && !IsNumeric(property.Type))
            {
                continue;
            }

            byField[field] = new ChartFieldMapping(field, property.Name);
        }

        return byField.Values.ToImmutableArray();
    }

    private static AttributeData? FindValueAttribute(IPropertySymbol property)
    {
        foreach (AttributeData attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ValueAttribute)
            {
                return attribute;
            }
        }

        return null;
    }

    // The [ChartValue(ChartField.X)] constructor argument is a boxed enum-underlying int; resolve
    // it back to the member's name via the ChartField symbol itself (robust to the enum's
    // declaration order — no hardcoded ordinal table to keep in sync).
    private static string? ReadField(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        TypedConstant argument = attribute.ConstructorArguments[0];
        if (argument.Value is not int ordinal || argument.Type is not { TypeKind: TypeKind.Enum } enumType)
        {
            return null;
        }

        foreach (ISymbol member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field
                && field.ConstantValue is int value
                && value == ordinal)
            {
                return field.Name;
            }
        }

        return null;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsNumeric(ITypeSymbol type)
    {
        type = Underlying(type);

        return type.SpecialType
            is SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal;
    }
}
